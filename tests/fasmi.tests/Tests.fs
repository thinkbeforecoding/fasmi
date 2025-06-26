module Tests

open System
open System.Runtime.InteropServices
open FileSystem
open Xunit

// generated asm is platform sensitive due to differences in call conventions
let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

// normalize line endings for string comparions
let normalizeLineEnds (s: string) =
    if isWindows then
        s
    else
        s.Replace("\r\n", "\n") 

module Assert =
    let EqualString(x, y) = Assert.Equal(normalizeLineEnds x, normalizeLineEnds y)


// disassemble en single method from the fasmi.tests.source project
// notice that this project is *ALWAYS* compiled in release to produce
// the same optimized IL.
let disassembleFromSourceProject methodName =
    use ctx = new Disassembly.CustomAssemblyLoadContext(fun _ -> true)
    let asm = ctx.LoadFromAssemblyPath(Environment.CurrentDirectory </> "fasmi.tests.source.dll")

    let mth = asm.GetType("Source").GetMethod(methodName)
    if isNull mth then
        failwith $"Function '%s{methodName}' not found"

    Disassembly.withRuntime (fun runtime ->
        use writer = new IO.StringWriter()
        Disassembly.disassembleMethod runtime mth Disassembly.Platform.X64 false writer
        writer.Flush()
        writer.ToString())


[<Fact>]
let ``check basic 'inc' method disassembly`` () =
    let output = disassembleFromSourceProject "inc"
    
    let expected = 
        if isWindows then
            // on windows, the argument is in rcx
            """
;Source.inc(Int32)
L0000: lea eax, [rcx+1]
L0003: ret
"""
        else
            // on linux, the argument is in rdi
            """
;Source.inc(Int32)
L0000: lea eax, [rdi+1]
L0003: ret
"""

    Assert.EqualString(expected, output)

[<Fact>]
let ``jump label should be generated`` () =
    let output = disassembleFromSourceProject "jump"
    let lines = (normalizeLineEnds output).Split(Environment.NewLine)
    // we look only at the conditional jump 'jl' line
    // others are different due call conventions
    let jumpLine =
        lines
        |> Array.find (fun l -> l.Contains "jl")
    
    // the full label can be different because emited ASM has different size
    // depending on the platform, so we check only the first chars.
    // we also omit checking the label at the start of the line
    let expected = "jl short L00"
    Assert.Contains(expected, jumpLine)


[<Fact>]
let ``when calling function, call must contain function name`` () =
    let output = disassembleFromSourceProject "toString"

    let lines = (normalizeLineEnds output).Split(Environment.NewLine)
    let callLine = 
        lines
        |> Array.find (fun l -> l.Contains "call")
 
    // we omit checking the label at the start of the line
    // because it can be different on different platforms
    let expected = "call qword ptr [0x"
    Assert.Contains(expected, callLine)




