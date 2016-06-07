// include Fake libs
#r "./packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.Testing
open System.IO

// Directories
let curDir = Directory.GetCurrentDirectory()
let appDir = "build/src/"

// Filesets
let appReferences = !! "RabbitMqActors/**/*.fsproj"

// Properties
let buildProps = [("SolutionDir", curDir)]

// Targets
Target "Clean" (fun _ ->
  CleanDirs [appDir]
)

Target "Build" (fun _ ->
  // compile all projects below app/
  MSBuildDebug appDir "Build" appReferences
  |> Log "AppBuild-Output: "
)

// Build order
"Clean"
  ==> "Build"

// start build
RunTargetOrDefault "Build"
