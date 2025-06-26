module Disassembly
(*
This module is partially a translation of Andrey Shchekin code for https://sharplab.io/
The original code is under BSD-2 license and can be found on github:
https://github.com/ashmind/SharpLab


Copyright (c) 2016-2017, Andrey Shchekin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*)
open System
open Iced.Intel
open System.Diagnostics
open System.Runtime.CompilerServices
open Microsoft.Diagnostics.Runtime
open System.Reflection
open System.Runtime.Loader
open System.IO
open ICSharpCode.Decompiler

type Platform =
    | X86
    | X64

type Language =
    | Asm
    | IL
let private (|CI|_|) (x: string) (y: string) =
    if String.Equals(x,y,StringComparison.OrdinalIgnoreCase) then
        Some()
    else
        None

module Platform =
    let bitness = function
        | X86 -> 32
        | X64 -> 64

    let parse (input: string) =
        match input with
        | CI "x86" -> Ok X86
        | CI "x64" -> Ok X64
        | _ -> Error $"Unknown platform '{input}'"

module Language =
    let parse input =
        match input with
        | CI "asm" -> Ok Asm
        | CI "il" -> Ok IL
        | _ -> Error $"Unknown language '{input}'"

/// Assembly resolver/loader
type CustomAssemblyLoadContext(shouldShareAssembly: AssemblyName -> bool) =
    inherit AssemblyLoadContext(isCollectible = true)

    override this.Load(assemblyName: AssemblyName) =
        let name = if isNull assemblyName.Name then "" else assemblyName.Name
        if (name = "netstandard" || name = "mscorlib" || name.StartsWith("System.") || shouldShareAssembly(assemblyName)) then
            Assembly.Load(assemblyName);
        else
            base.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, assemblyName.Name + ".dll"));

    interface IDisposable with
        member this.Dispose() = base.Unload()

let formatterOptions = FormatterOptions(
                            HexPrefix = "0x",
                            HexSuffix = null,
                            UppercaseHex = false,
                            SpaceAfterOperandSeparator = true)
        


// disassemble a single method
let disassembleConcreteMethod (runtime: ClrRuntime) (mthinfo: MethodBase) platform showOpcodes (writer: TextWriter) =

    runtime.FlushCachedData()
    let h = mthinfo.MethodHandle
    let prepareSucceeded =
        try
            RuntimeHelpers.PrepareMethod(h)
            true
        with
        | ex -> 
            writer.WriteLine $";Failed to prepare: %s{mthinfo.DeclaringType.FullName}%s{ mthinfo.Name}"
            false


    if prepareSucceeded then
        // get a byte array from jitted memory region
        let getBytes (regions: HotColdRegions) =
            let span = ReadOnlySpan<byte>((nativeint regions.HotStart).ToPointer(), int regions.HotSize)
            span.ToArray()

        // try to find method runtime info
        let clrmth = runtime.GetMethodByHandle(uint64 (h.Value.ToInt64()))
       

        if not (isNull clrmth) then

            if clrmth.HotColdInfo.HotSize > 0u && clrmth.HotColdInfo.HotStart <> UInt64.MaxValue then
                // method has been jitted, emit it
                writer.WriteLine $""
                writer.WriteLine $";%s{clrmth.Signature}"
                writer.Flush()

                let bytes = getBytes clrmth.HotColdInfo
                let address = clrmth.HotColdInfo.HotStart

                let decoder = Decoder.Create(Platform.bitness platform,bytes)
                decoder.IP <- address
                let formatter =
                        IntelFormatter(formatterOptions,
                                        { new ISymbolResolver with 
                                            member _.TryGetSymbol(inst, _,_,addr, _, result) =
                                                if addr >= address && addr < address + uint64 clrmth.HotColdInfo.HotSize then
                                                    // symbol is in method scope, emit label
                                                    result <- SymbolResult(addr, $"L%04x{addr-address}")
                                                    true
                                                else
                                                    // symbol is out of scope
                                                    // try to find called method
                                                    let callmth = runtime.GetMethodByInstructionPointer(addr)
                                                    if isNull callmth then
                                                        // there is no method, just emit address
                                                        result <- SymbolResult()
                                                        false
                                                    else
                                                        // a method was found, emit the method signature
                                                        result <- SymbolResult(addr, callmth.Signature)
                                                        true
                                             })

                let out = StringOutput()

                // render instructions
                for inst in decoder do
                    formatter.Format(&inst, out)
                    let instRelativeIp = int <| inst.IP - address
                    if showOpcodes then
                        let inst_opcode_hex = bytes[instRelativeIp..instRelativeIp + inst.Length - 1]
                                                |> Array.map (fun b -> sprintf "%02x" b) 
                                                |> String.concat ""
                        writer.WriteLine $"L%04x{instRelativeIp}: %45s{inst_opcode_hex}  %s{out.ToStringAndReset()}"
                    else
                        writer.WriteLine $"L%04x{instRelativeIp}: %s{out.ToStringAndReset()}"
                    writer.Flush()


let outputGenericMethod (runtime: ClrRuntime) (mthinfo: MethodBase) (writer: TextWriter)  =
    
    let h = mthinfo.MethodHandle
    let clrmth = runtime.GetMethodByHandle(uint64 (h.Value.ToInt64()))

    writer.WriteLine $""
    match clrmth with
    | null -> ()
    | _ -> writer.WriteLine $"%s{clrmth.Signature}"
    writer.WriteLine $"; generic method cannot be jitted. provide explicit types"
    writer.Flush()

let disassembleMethod runtime (mthinfo: MethodBase) platform showOpcodes writer =
    if not mthinfo.IsGenericMethodDefinition && not mthinfo.DeclaringType.IsGenericTypeDefinition then
        disassembleConcreteMethod runtime mthinfo platform showOpcodes writer
    else
        outputGenericMethod runtime mthinfo writer

/// Attach to current process and execute given function with the runtime information
let withRuntime f =
    use dt = DataTarget.AttachToProcess(Process.GetCurrentProcess().Id, false)
    dt.SetSymbolPath("https://msdl.microsoft.com/download/symbols")
    use runtime = dt.ClrVersions.[0].CreateRuntime()
    f runtime
    

// disassemble assembly as jitted x86/x64 to text writer
let disassemble asmPath (writer: TextWriter) platform showOpcodes =

    // attach to self
    withRuntime (fun runtime ->
        use ctx = new CustomAssemblyLoadContext(fun _ -> true)

        // load the assembly
        let asm = ctx.LoadFromAssemblyPath(asmPath)



        // find all methods of given type
        let getAllMethods (ty: Type) =
            [ yield! ty.GetConstructors() |> Seq.cast<MethodBase> 
              yield! ty.GetMethods() |> Seq.cast<MethodBase> ]

        // walk assembly types and nested types
        for ty in asm.GetTypes() do

            for mth in getAllMethods ty do
                if mth.DeclaringType <> typeof<obj> then
                    disassembleMethod runtime mth platform showOpcodes writer
                        

            for sty in ty.GetNestedTypes() do
                for mth in getAllMethods sty do
                    if mth.DeclaringType <> typeof<obj> then
                        disassembleMethod runtime mth platform showOpcodes writer
    )

/// disassemble assembly as IL to text writer
let ildasm asmPath writer =
    use pe = new Metadata.PEFile(asmPath)
    use cts = new Threading.CancellationTokenSource()
    let disass = Disassembler.ReflectionDisassembler(PlainTextOutput(writer) :> ITextOutput,cts.Token)
    disass.WriteModuleContents(pe)

/// disassemble assembly to writer
let decompile asmPath writer language platform showOpcodes =
    match language with
    | Asm -> disassemble asmPath writer platform showOpcodes
    | IL -> ildasm asmPath writer

/// disassemble assembly to file
let decompileToFile asmPath outPath language platform showOpcodes =
    use w = File.CreateText(outPath)
    decompile asmPath w language platform showOpcodes


/// disassemble assembly to console
let decompileToConsole asmPath language platform showOpcodes =
    use s = Console.OpenStandardOutput()
    use w = new IO.StreamWriter(s)
    decompile asmPath w language platform showOpcodes
