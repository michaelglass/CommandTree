module CommandTree.Tests.RepositoryPolicyTests

open System
open System.IO
open System.Xml.Linq
open Xunit
open Swensen.Unquote

let private repositoryRoot =
    let rec findRoot (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "mise.toml")) then
            directory.FullName
        elif isNull directory.Parent then
            failwith "Could not find repository root containing mise.toml"
        else
            findRoot directory.Parent

    findRoot (DirectoryInfo AppContext.BaseDirectory)

[<Fact>]
let ``check verifies cross-platform coverage floors without auto-ratcheting host-specific branches`` () =
    let mise = File.ReadAllText(Path.Combine(repositoryRoot, "mise.toml"))

    let checkTask =
        mise.Substring(mise.IndexOf("[tasks.check]", StringComparison.Ordinal))

    let checkTask =
        checkTask.Substring(0, checkTask.IndexOf("[tasks.ci]", StringComparison.Ordinal))

    test <@ checkTask.Contains "coverage-check" @>
    test <@ not (checkTask.Contains "coverage-ratchet") @>

[<Fact>]
let ``the library publishes a declared FSharp.Core floor rather than the building SDK's`` () =
    let project =
        XDocument.Load(Path.Combine(repositoryRoot, "src", "CommandTree", "CommandTree.fsproj"))

    let elements name =
        project.Descendants(XName.Get name) |> List.ofSeq

    let implicitDisabled =
        elements "DisableImplicitFSharpCoreReference"
        |> List.exists (fun element -> element.Value.Trim() = "true")

    let fsharpCoreVersions =
        elements "PackageReference"
        |> List.filter (fun element -> element.Attribute(XName.Get "Include") |> isNull |> not)
        |> List.filter (fun element -> element.Attribute(XName.Get "Include").Value = "FSharp.Core")
        |> List.map (fun element -> element.Attribute(XName.Get "Version").Value)

    test <@ implicitDisabled @>
    test <@ fsharpCoreVersions = [ "10.1.301" ] @>
