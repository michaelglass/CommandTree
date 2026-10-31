module CommandTree.Tests.SourceRevisionStampTests

open System
open System.IO
open Xunit
open Swensen.Unquote
open CommandTree

let private repositoryRoot =
    let rec findRoot (directory: DirectoryInfo) =
        if File.Exists(Path.Combine(directory.FullName, "mise.toml")) then
            directory.FullName
        elif isNull directory.Parent then
            failwith "Could not find repository root containing mise.toml"
        else
            findRoot directory.Parent

    findRoot (DirectoryInfo AppContext.BaseDirectory)

let private targetsFile =
    Path.Combine(repositoryRoot, "src", "CommandTree", "build", "CommandTree.targets")

/// A jj repository in a temp directory, isolated from the user's jj configuration.
/// The stamp target runs jj itself, and inherits this environment through MSBuild.
type private JjRepo() =
    let root =
        Path.Combine(Path.GetTempPath(), "commandtree-stamp-" + Guid.NewGuid().ToString "N")

    let config = Path.Combine(root, "jj-config.toml")
    let repo = Path.Combine(root, "repo")

    let env =
        [
            "JJ_CONFIG", config
            "JJ_USER", "Stamp Test"
            "JJ_EMAIL", "stamp@example.invalid"
            "MSBUILDDISABLENODEREUSE", "1"
        ]

    do
        Directory.CreateDirectory repo |> ignore
        File.WriteAllText(config, "")

    member _.Path = repo

    /// Scratch space beside the repository, outside what jj snapshots.
    member _.Root = root

    /// Runs from inside the repository, as a developer would.
    member _.Run (command: string) (args: string list) =
        let result =
            Process.runSilentWith
                (SilentRun.defaults |> SilentRun.withEnv env |> SilentRun.inDirectory repo)
                command
                args

        // dotnet reports build errors on stdout, so a failure carries both streams.
        if result.ExitCode <> 0 then
            failwith $"%s{command} %A{args} exited %d{result.ExitCode}: %s{result.Stderr}\n%s{result.Stdout}"

        result.Stdout

    member this.Jj(args: string list) = this.Run "jj" ([ "-R"; repo ] @ args)

    member this.CommitId(revision: string) =
        this.Jj [ "log"; "--no-graph"; "-r"; revision; "-T"; "commit_id" ]

    /// The `SourceRevisionId` the stamp target resolves for a project in this repo.
    member this.Stamp() =
        let project = Path.Combine(repo, "Stamp.proj")

        this.Run
            "dotnet"
            [
                "msbuild"
                project
                "-nologo"
                "-nodeReuse:false"
                "-t:CommandTreeStampSourceRevision"
                "-getProperty:SourceRevisionId"
            ]

    interface IDisposable with
        member _.Dispose() =
            if Directory.Exists root then
                Directory.Delete(root, true)

/// A repository whose working-copy commit holds only the stamp project, committed
/// as `base`.
let private jjRepoWithProject () =
    let repo = new JjRepo()
    repo.Run "jj" [ "git"; "init"; repo.Path ] |> ignore

    File.WriteAllText(
        Path.Combine(repo.Path, "Stamp.proj"),
        $"<Project><Import Project=\"%s{targetsFile}\" /></Project>"
    )

    repo.Jj [ "commit"; "-m"; "base" ] |> ignore
    repo

let private jjAvailable () =
    try
        let code, _, _ = Process.runSilent "jj" [ "--version" ]
        code = 0
    with _ ->
        false

/// Skips a jj test on a machine without jj, but FAILS it in CI: CI installs a pinned
/// jj (scripts/install-jj.sh), so a missing one there means the workflow stopped
/// testing the jj arm, and a skip would hide that.
let private requireJj () =
    if not (jjAvailable ()) then
        if String.IsNullOrEmpty(Environment.GetEnvironmentVariable "CI") then
            Assert.Skip "jj is not installed; the jj arm of the stamp target is untested here"
        else
            failwith "CI is set but jj is not on PATH: scripts/install-jj.sh did not run or did not install it"

[<Fact>]
let ``under jj, edits to the working copy do not move the stamped revision`` () =
    requireJj ()

    use repo = jjRepoWithProject ()
    let parent = repo.CommitId "@-"

    File.WriteAllText(Path.Combine(repo.Path, "Edit.txt"), "first")
    let afterFirstEdit = repo.Stamp()

    File.WriteAllText(Path.Combine(repo.Path, "Edit.txt"), "second")
    let afterSecondEdit = repo.Stamp()

    test <@ afterFirstEdit = $"%s{parent}.dirty" @>
    test <@ afterSecondEdit = afterFirstEdit @>

[<Fact>]
let ``under jj, an unchanged working copy stamps the commit it sits on, not dirty`` () =
    requireJj ()

    use repo = jjRepoWithProject ()

    test <@ repo.Stamp() = repo.CommitId "@-" @>

[<Fact>]
let ``under jj, a merge working copy is never stamped with its parents' ids run together`` () =
    requireJj ()

    use repo = jjRepoWithProject ()
    let baseId = repo.CommitId "@-"

    let commitOnBase (file: string) =
        repo.Jj [ "new"; baseId ] |> ignore
        File.WriteAllText(Path.Combine(repo.Path, file), file)
        repo.Jj [ "commit"; "-m"; file ] |> ignore
        repo.CommitId "@-"

    let first = commitOnBase "first.txt"
    let second = commitOnBase "second.txt"
    repo.Jj [ "new"; first; second ] |> ignore

    let stamp = repo.Stamp()

    // jj's answer names two commits, so the git fallback supplies the revision: the
    // colocated repository's HEAD, which jj points at the first parent.
    test <@ stamp.StartsWith first @>
    test <@ not (stamp.Contains second) @>

/// The configuration this test assembly was built in (…/bin/<configuration>/<tfm>/), so the
/// package below can be packed from the CommandTree build that is already on disk.
let private buildConfiguration =
    DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent.Name

let private fixtureFile (repo: JjRepo) (relativePath: string) (content: string) =
    let path = Path.Combine(repo.Path, relativePath)
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, content)

/// An F# project for the consumer fixture. FSharp.Core comes from CommandTree's own
/// dependency, so restore needs only the packed package and what the user cache holds.
let private fixtureProject (references: string) =
    $"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>
    <!-- As in a repository without a git directory: the SDK's SourceLink would
         otherwise set SourceRevisionId from the colocated git HEAD first. -->
    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
  </PropertyGroup>
  <ItemGroup><Compile Include="Code.fs" /></ItemGroup>
  <ItemGroup>%s{references}</ItemGroup>
</Project>"""

[<Fact>]
let ``a project referencing a stamped one does not recompile when the working copy turns dirty`` () =
    requireJj ()

    use repo = new JjRepo()
    repo.Run "jj" [ "git"; "init"; repo.Path ] |> ignore

    let feed = Path.Combine(repo.Root, "feed")
    let packages = Path.Combine(repo.Root, "packages")
    let version = "0.0.0-stamptest"

    // The real package layout: whether a consumer imports the stamp target is decided
    // by where the package puts it, so pack rather than import the file directly.
    repo.Run
        "dotnet"
        [
            "pack"
            Path.Combine(repositoryRoot, "src", "CommandTree", "CommandTree.fsproj")
            "--no-build"
            "-c"
            buildConfiguration
            "-o"
            feed
            $"-p:Version=%s{version}"
            // A local pack would otherwise append the source ref to the version.
            "-p:RefStampReleaseBuild=true"
            "-nodeReuse:false"
        ]
    |> ignore

    let userPackages =
        match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
        | null
        | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget", "packages")
        | path -> path

    // An isolated package folder keeps the throwaway version out of the user's cache;
    // the user's cache is a fallback so FSharp.Core resolves without a download.
    fixtureFile
        repo
        "NuGet.Config"
        $"""<configuration>
  <config><add key="globalPackagesFolder" value="%s{packages}" /></config>
  <packageSources>
    <clear />
    <add key="stamp-test" value="%s{feed}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <fallbackPackageFolders><add key="user" value="%s{userPackages}" /></fallbackPackageFolders>
</configuration>"""

    fixtureFile repo ".gitignore" "bin/\nobj/\n"

    fixtureFile
        repo
        "Cli/Cli.fsproj"
        (fixtureProject $"""<PackageReference Include="CommandTree" Version="[%s{version}]" />""")

    fixtureFile repo "Cli/Code.fs" "module Cli\nlet answer = 42\n"

    fixtureFile
        repo
        "Dependent/Dependent.fsproj"
        (fixtureProject """<ProjectReference Include="../Cli/Cli.fsproj" />""")

    fixtureFile repo "Dependent/Code.fs" "module Dependent\nlet answer = Cli.answer\n"
    repo.Jj [ "commit"; "-m"; "base" ] |> ignore

    // Relative to the repository, which Run uses as the working directory. The temp
    // directory can sit behind a symlink (/tmp is /private/tmp on macOS); given the
    // linked absolute path, restore records the referenced project under its other
    // spelling and loses the reference, and with it CommandTree's transitive targets.
    let build () =
        repo.Run
            "dotnet"
            [
                "build"
                Path.Combine("Dependent", "Dependent.fsproj")
                "-nologo"
                "-nodeReuse:false"
            ]
        |> ignore

    let intermediate project file =
        Path.Combine(repo.Path, project, "obj", "Debug", "net10.0", file)

    build ()

    let dependentCompiledClean =
        File.GetLastWriteTimeUtc(intermediate "Dependent" "Dependent.dll")

    File.WriteAllText(Path.Combine(repo.Path, "Edit.txt"), "an edit that is no compile input")
    build ()

    let cliStamp = File.ReadAllText(intermediate "Cli" "Cli.AssemblyInfo.fs")

    let dependentCompiledDirty =
        File.GetLastWriteTimeUtc(intermediate "Dependent" "Dependent.dll")

    // The flip happened and the CLI that asked for the stamp carries it ...
    test <@ cliStamp.Contains $"""+%s{repo.CommitId "@-"}.dirty""" @>
    // ... but its dependent was not recompiled for it.
    test <@ dependentCompiledDirty = dependentCompiledClean @>
