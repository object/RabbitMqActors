#load ".fake/build.fsx/intellisense.fsx"
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

Target.initEnvironment ()

Target.create "Clean" (fun _ ->
    !! "**/bin"
    ++ "**/obj"
    |> Shell.cleanDirs
)

Target.create "Build" (fun _ ->
    // !! "**/*.*proj"
    // |> Seq.iter (DotNet.build id)
    DotNet.build id "."
)

Target.create "Test" (fun _ ->
    // !! "**/*.*proj"
    // |> Seq.iter (DotNet.test id)
    DotNet.test id "."
)


"Clean"
  ==> "Build"
  ==> "Test"

Target.runOrDefault "Build"
