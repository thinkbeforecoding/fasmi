module Compilation
open System
open FileSystem
open FSharp.Compiler.CodeAnalysis

// the Assembly attribute to build output as net5.0

let netAttr =
    #if NET6_0
    """
namespace Microsoft.BuildSettings
[<System.Runtime.Versioning.TargetFrameworkAttribute(".NETCoreApp,Version=v6.0", FrameworkDisplayName="")>]
do ()
"""

#else
    """
namespace Microsoft.BuildSettings
[<System.Runtime.Versioning.TargetFrameworkAttribute(".NETCoreApp,Version=v5.0", FrameworkDisplayName="")>]
do ()
"""

#endif

let netAttrName = "Net50AssemblyAttr.fs"

// check the net5.0 assembly attribute file exists or create it
let ensureNet5Attr asmPath =
    let filePath = dir asmPath </> netAttrName
    if not (IO.File.Exists filePath) then
        IO.File.WriteAllText(filePath, netAttr)
    filePath


/// compile given script as an assembly
let compile (path: string) (asmPath: string) = 
    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    // find netx.0 assembly path
    let version = System.Environment.Version
    let netPath = 
        let  runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
        // IO.Path.GetFullPath(runtimeDir </> $"../../../packs/Microsoft.NETCore.App.Ref/{version}.0.0/ref/net{version}.0/")
        IO.Path.GetFullPath(runtimeDir </> $"../../../packs/Microsoft.NETCore.App.Ref/{version}/ref/net{version.Major}.{version.Minor}/")
        
    let attrfile = ensureNet5Attr asmPath
    
    let diag,_ = 
        checker.Compile([| "fsc.exe"
                           "-o"; asmPath;
                           "-a"; path
                           "-a"; attrfile
                           "--debug:portable"
                           "--noframework"
                           "--targetprofile:netcore"
                           "--langversion:preview"
                           "--define:NET"
                           "--define:NETCOREAPP"
                           for i in 5 .. version.Major do
                               "--define:NET{i}_0_OR_GREATER"
                           "--define:NET{i}_0"
                           "--define:NETCOREAPP1_0_OR_GREATER"
                           "--define:NETCOREAPP1_1_OR_GREATER"
                           "--define:NETCOREAPP2_0_OR_GREATER"
                           "--define:NETCOREAPP2_1_OR_GREATER"
                           "--define:NETCOREAPP2_2_OR_GREATER"
                           "--define:NETCOREAPP3_0_OR_GREATER"
                           "--define:NETCOREAPP3_1_OR_GREATER"
                           "--optimize+"
                           for f in IO.Directory.EnumerateFiles(netPath,"*.dll") do
                                $"-r:{f}"
                            |])
        |> Async.RunSynchronously

    // output compilatoin errors
    for d in diag do
        printfn $"{d}"
