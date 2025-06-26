// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp
open System
open Fargo
open FileSystem
open Disassembly


type Command =
    { Source: string 
      Console: bool
      Output: string option
      Hex: bool
      Watch: bool
      Platform: Disassembly.Platform
      Language: Disassembly.Language
    }

let cmdLine =
    fargo {
        let! watch = flag "watch" "w" "Run in watch mode"
        and! console = flag "console" "c" "Output to console"
        and! output = opt "output" "o" "output-path" "Specify the output file"
        and! hex = flag "hex" "x" "Show instruction opcodes"
        and! platform = 
            optc "platform" "p" "x64|x86" "Specity the platform for disassembly (x64/x86)"
                (Completer.choices ["x64";"x86"])
            |> optParse Disassembly.Platform.parse
            |> defaultValue (if Environment.Is64BitProcess then X64 else X86)
        and! language =
            optc "language" "l" "asm|il" "specify the output language"
                (Completer.choices ["asm";"il"])
            |> optParse Disassembly.Language.parse
            |> defaultValue Asm
        and! source = arg "source" "The source fsx or dotnet assembly file" |> reqArg
        return {
            Source = source
            Console = console
            Output = output
            Hex = hex
            Watch = watch
            Platform = platform
            Language = language
        }
    }

/// Command line options
//type Cmd =
//    | [<Mandatory; MainCommand; AltCommandLine("-s")>]Source of string
//    | [<AltCommandLine("-c")>] Console
//    | [<AltCommandLine("-o")>] Output of string
//    | [<AltCommandLine("-x")>] Hex
//    | [<AltCommandLine("-w")>] Watch
//    | [<AltCommandLine("-p")>] Platform of Disassembly.Platform
//    | [<AltCommandLine("-l")>] Language of Disassembly.Language
    

//    interface Argu.IArgParserTemplate with
//        member this.Usage =
//            match this with
//            | Source _ -> "the source fsx or dotnet assembly file"
//            | Console -> "output to console"
//            | Output _ -> "specifiy the output file" 
//            | Hex -> "show instruction opcodes"
//            | Watch -> "run in watch mode"
//            | Platform _ -> "specify the platform for disassembly"
//            | Language _ -> "specify the output language (asm/il)"

/// source type
type Source =
    | Script of string
    | Assembly of string

/// indicates wheter source should be compiled, or is already
let shouldCompile = function
    | Script _ -> true
    | Assembly _ -> false


let help = """fasmi                               F# -> ASM disassembler
----------------------------------------------------------
copyright D-EDGE 2021
Inspired from https://sharplab.io/ code by Andrey Shchekin
----------------------------------------------------------
"""

/// get the process name for argu help
// it tries to determine if it's running as a dotnet tool or directly as a program
let getProcessName() =
    let name = IO.Path.GetFileNameWithoutExtension (Diagnostics.Process.GetCurrentProcess().MainModule.FileName )
    if String.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase) then
        "dotnet fasmi"
    else
        "fasmi"

[<EntryPoint>]
let main argv =


    Run.run "fasmi" cmdLine argv <| fun ct cmd ->
        task {

            // build full source path
            let source = 
                let path = cmd.Source
                let src =
                    if IO.Path.IsPathRooted path then
                        path |> IO.Path.GetFullPath
                    else
                        Environment.CurrentDirectory </> path |> IO.Path.GetFullPath

                // determine source type
                if IO.Path.GetExtension(src) =  ".dll" then
                    Assembly src
                else
                    Script src

            // get target platform
            let platform = cmd.Platform

            // get target language
            let language = cmd.Language

            // get the output file path depending on argument/target 
            let out =
                match cmd.Output with
                | Some out -> out
                | None ->
                    let ext = 
                        match language with      
                        | Asm -> ".asm"
                        | IL -> ".il"
                    match source with
                    | Script src 
                    | Assembly src -> dir src </> filename src + ext


            // ensure compilation directory exists if needed
            let binDir =
                match source with
                | Script src
                | Assembly src -> dir src </> "bin"

            if shouldCompile source then
                ensuredir binDir

            // assembly path (to build, or passed as source)
            let asmPath =
                match source with
                | Script src -> binDir </> filename src + ".dll"
                | Assembly src -> src


            // log function
            // when outputing to console, no log is output to
            // only write the disassembly result
            let logf fmt =  
                if cmd.Console then
                    Printf.kprintf (fun _ -> ()) fmt
                else
                    Printf.kprintf (printfn "%s") fmt

            logf $"%s{help}"

            // the core function to run disassembly
            let run() =

                // compile if needed
                match source with
                | Script src ->
                    logf $"Source: %s{src}"
                    logf "Compilation" 
                    Compilation.compile src asmPath
                | Assembly src ->
                    logf $"Source: %s{src}"


                logf "Disassembly"

                let showOpcodes = cmd.Hex

                // disassemble
                if cmd.Console then
                    Disassembly.decompileToConsole asmPath language platform showOpcodes
                else
                    Disassembly.decompileToFile asmPath out language platform showOpcodes

            if cmd.Watch then
                // run in watch mode
                
                // first run
                run()
                let dir, filter =
                    match source with
                    | Script src
                    | Assembly src -> dir src, filenameExt src

                // prepare watcher
                use watcher = new IO.FileSystemWatcher(dir, filter, EnableRaisingEvents = true)

                watcher.Changed |> Event.add (fun _ -> run())

                let signal = new System.Threading.Tasks.TaskCompletionSource()

                // wait for Ctrl+C
                Console.CancelKeyPress |> Event.add (fun _ -> signal.SetResult() |> ignore)

                do! signal.Task 
            else
                // run once
                run()

            return 0
    }
