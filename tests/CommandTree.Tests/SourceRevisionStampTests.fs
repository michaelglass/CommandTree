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
        [ "JJ_CONFIG", config
          "JJ_USER", "Stamp Test"
          "JJ_EMAIL", "stamp@example.invalid"
          "MSBUILDDISABLENODEREUSE", "1" ]

    do
        Directory.CreateDirectory repo |> ignore
        File.WriteAllText(config, "")

    member _.Path = repo

    member _.Run (command: string) (args: string list) =
        let code, stdout, stderr = Process.runSilentWithEnv command args env

        if code <> 0 then
            failwith $"%s{command} %A{args} exited %d{code}: %s{stderr}"

        stdout.Trim()

    member this.Jj(args: string list) = this.Run "jj" ([ "-R"; repo ] @ args)

    member this.CommitId(revision: string) =
        this.Jj [ "log"; "--no-graph"; "-r"; revision; "-T"; "commit_id" ]

    /// The `SourceRevisionId` the stamp target resolves for a project in this repo.
    member this.Stamp() =
        let project = Path.Combine(repo, "Stamp.proj")

        this.Run
            "dotnet"
            [ "msbuild"
              project
              "-nologo"
              "-nodeReuse:false"
              "-t:CommandTreeStampSourceRevision"
              "-getProperty:SourceRevisionId" ]

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

[<Fact>]
let ``under jj, edits to the working copy do not move the stamped revision`` () =
    if not (jjAvailable ()) then
        Assert.Skip "jj is not installed; the jj arm of the stamp target is untested here"

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
    if not (jjAvailable ()) then
        Assert.Skip "jj is not installed; the jj arm of the stamp target is untested here"

    use repo = jjRepoWithProject ()

    test <@ repo.Stamp() = repo.CommitId "@-" @>

[<Fact>]
let ``under jj, a merge working copy is never stamped with its parents' ids run together`` () =
    if not (jjAvailable ()) then
        Assert.Skip "jj is not installed; the jj arm of the stamp target is untested here"

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
