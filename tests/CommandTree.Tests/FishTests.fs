module CommandTree.Tests.FishTests

open System
open System.IO
open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Test types
// =============================================================================

type SubCmd =
    | [<Cmd("Build the project")>] Build
    | [<Cmd("Run tests"); CmdDefault>] Test
    | [<Cmd("Deploy to env"); CmdCompletion("dev", "staging", "prod")>] Deploy of target: string

type SimpleCmd =
    | [<Cmd("Manage sub-things")>] Sub of SubCmd
    | [<Cmd("Show help")>] Help

// =============================================================================
// generateContent — pure function, fixture/snapshot test
// =============================================================================

[<Fact>]
let ``generateContent produces expected fish completion script`` () =
    let tree = CommandReflection.fromUnion<SimpleCmd> "Simple CLI"
    let content = FishCompletions.generateContent tree "my-app"

    test <@ content.Contains("# Fish completions for my-app") @>
    test <@ content.Contains("# Generated automatically from CommandTree") @>
    test <@ content.Contains("complete -c my-app -f") @>

    // Top-level commands
    test <@ content.Contains("__fish_use_subcommand") @>
    test <@ content.Contains("-a \"sub\"") @>
    test <@ content.Contains("-a \"help\"") @>

    // Subcommands under "sub"
    test <@ content.Contains("__fish_seen_subcommand_from sub") @>
    test <@ content.Contains("-a \"build\"") @>
    test <@ content.Contains("-a \"test\"") @>
    test <@ content.Contains("-a \"deploy\"") @>

    // Deploy argument completions
    test <@ content.Contains("-a \"dev\"") @>
    test <@ content.Contains("-a \"staging\"") @>
    test <@ content.Contains("-a \"prod\"") @>

[<Fact>]
let ``generateContent full snapshot`` () =
    let tree = CommandReflection.fromUnion<SimpleCmd> "Simple CLI"
    let content = FishCompletions.generateContent tree "my-app"

    let lines = content.Split('\n')

    // Verify structure: header, blank, disable, blank, completions
    test <@ lines.[0] = "# Fish completions for my-app" @>
    test <@ lines.[1] = "# Generated automatically from CommandTree" @>
    test <@ lines.[2] = "" @>
    test <@ lines.[3] = "# Disable file completions for command" @>
    test <@ lines.[4] = "complete -c my-app -f" @>
    test <@ lines.[5] = "" @>
    test <@ lines.[6] = "# Commands, subcommands, and argument completions" @>

    // Rest is the fishCompletions output
    let completionLines =
        lines |> Array.skip 7 |> Array.filter (fun l -> l.Trim() <> "")

    test <@ completionLines.Length > 0 @>

// =============================================================================
// generateContent with different command shapes
// =============================================================================

type FlatCmd =
    | [<Cmd("Do alpha")>] Alpha
    | [<Cmd("Do beta")>] Beta of name: string
    | [<Cmd("Do gamma"); CmdFileCompletion>] Gamma of path: string

[<Fact>]
let ``generateContent handles flat commands with file completion`` () =
    let tree = CommandReflection.fromUnion<FlatCmd> "Flat CLI"
    let content = FishCompletions.generateContent tree "flat-app"

    test <@ content.Contains("complete -c flat-app -f") @>
    test <@ content.Contains("-a \"alpha\"") @>
    test <@ content.Contains("-a \"beta\"") @>
    test <@ content.Contains("-a \"gamma\"") @>
    test <@ content.Contains("-F") @>

// =============================================================================
// writeToFile — writes to filesystem (uses real home dir)
// =============================================================================

[<Fact>]
let ``writeToFile creates fish completion file`` () =
    let tree = CommandReflection.fromUnion<SimpleCmd> "Simple CLI"

    // writeToFile targets the real ~/.config/fish/completions/, so use a unique
    // name and delete it afterwards.
    let uniqueName = $"fish-test-{Guid.NewGuid():N}"

    try
        let (_output, _) =
            UITests.captureStdout (fun () -> FishCompletions.writeToFile tree uniqueName)

        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

        let completionsFile =
            Path.Combine(home, ".config", "fish", "completions", $"%s{uniqueName}.fish")

        test <@ File.Exists(completionsFile) @>
        let content = File.ReadAllText(completionsFile)
        test <@ content.Contains($"# Fish completions for %s{uniqueName}") @>
        test <@ content.Contains($"complete -c %s{uniqueName} -f") @>

        File.Delete(completionsFile)
    with ex ->
        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

        let completionsFile =
            Path.Combine(home, ".config", "fish", "completions", $"%s{uniqueName}.fish")

        if File.Exists(completionsFile) then
            File.Delete(completionsFile)

        reraise ()

// =============================================================================
// generateContent cmdName variations
// =============================================================================

[<Fact>]
let ``generateContent uses cmdName in complete commands`` () =
    let tree = CommandReflection.fromUnion<FlatCmd> "Test"
    let content1 = FishCompletions.generateContent tree "my-tool"
    let content2 = FishCompletions.generateContent tree "other-tool"

    test <@ content1.Contains("complete -c my-tool") @>
    test <@ not (content1.Contains("complete -c other-tool")) @>
    test <@ content2.Contains("complete -c other-tool") @>
    test <@ not (content2.Contains("complete -c my-tool")) @>

// =============================================================================
// hookScript — the conf.d hook `installHook` writes
//
// The hook runs on every `cd`. It used to run the CLI's `fish generate`
// synchronously on every change of directory into the project, so a CLI that
// builds itself before running held the prompt for as long as that build took.
// It now regenerates only when the completions are missing or older than an
// input, and never in the foreground.
// =============================================================================

let private fishPath () =
    (Environment.GetEnvironmentVariable "PATH"
     |> Option.ofObj
     |> Option.defaultValue "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun directory -> Path.Combine(directory, "fish"))
    |> Array.tryFind File.Exists

/// Run `script` under `fish --no-config` with HOME pointed at `home`; the exit
/// code, stdout and whether it finished inside `timeout`.
let private runFish (home: string) (script: string) (timeout: TimeSpan) =
    let fish =
        match fishPath () with
        | Some fish -> fish
        | None -> failwith "fish is not on PATH"

    let psi = Diagnostics.ProcessStartInfo(fish)
    psi.ArgumentList.Add "--no-config"
    psi.ArgumentList.Add "-c"
    psi.ArgumentList.Add script
    psi.Environment["HOME"] <- home
    psi.RedirectStandardOutput <- true
    psi.UseShellExecute <- false
    use proc = Diagnostics.Process.Start psi
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let finished = proc.WaitForExit(int timeout.TotalMilliseconds)

    if not finished then
        proc.Kill true

    finished, (if finished then proc.ExitCode else -1), (if finished then stdout.Result else "")

let private withProject (assertion: string -> string -> unit) =
    let id = Guid.NewGuid().ToString "N"
    let root = Path.Combine(Path.GetTempPath(), $"commandtree-fish-hook-%s{id}")

    let home = Path.Combine(root, "home")
    let project = Path.Combine(root, "project")

    Directory.CreateDirectory(Path.Combine(home, ".config", "fish", "completions"))
    |> ignore

    Directory.CreateDirectory(Path.Combine(project, "src")) |> ignore

    try
        assertion home project
    finally
        Directory.Delete(root, true)

let private touch (path: string) (writtenAt: DateTime) =
    if not (File.Exists path) then
        File.WriteAllText(path, "")

    File.SetLastWriteTimeUtc(path, writtenAt)

let private completionsIn (home: string) =
    Path.Combine(home, ".config", "fish", "completions", "tool.fish")

[<Fact>]
let ``hookScript names the CLI and every watched input, and backgrounds the regeneration`` () =
    let script =
        FishCompletions.hookScript "build.fsx" [ "src/Commands/*.fs"; "obj/Tool.dll" ]

    test <@ script.Contains "--on-variable PWD" @>
    test <@ script.Contains "\"$root/build.fsx\"" @>
    test <@ script.Contains "$root/src/Commands/*.fs" @>
    test <@ script.Contains "$root/obj/Tool.dll" @>
    test <@ script.Contains "-nt" @>
    test <@ script.Contains "fish generate" @>
    test <@ script.Contains "disown" @>
    // The dot in the CLI name never reaches a fish function name.
    test <@ script.Contains "function __build_fsx_update_completions" @>

[<Fact>]
let ``hookScript refuses a watched pattern fish would not read as one literal glob`` () =
    for pattern in
        [
            "src/My Commands/*.fs"
            "$HOME/x"
            "a;b"
            "\"q\""
            "/abs/path"
            ""
            "../outside"
        ] do
        raises<ArgumentException> <@ FishCompletions.hookScript "tool" [ pattern ] @>

[<Fact>]
let ``the hook judges completions stale when missing or older than an input, fresh otherwise`` () =
    Assert.SkipWhen((fishPath ()).IsNone, "fish is not on PATH")

    withProject (fun home project ->
        let hook = Path.Combine(home, "hook.fish")
        File.WriteAllText(hook, FishCompletions.hookScript "tool" [ "src/*.fs" ])
        let anHourAgo = DateTime.UtcNow.AddHours -1.0
        touch (Path.Combine(project, "tool")) anHourAgo
        touch (Path.Combine(project, "src", "Commands.fs")) anHourAgo

        let stale () =
            let finished, code, _ =
                runFish
                    home
                    $"source '%s{hook}'; __tool_completions_stale '%s{project}' '%s{completionsIn home}'"
                    (TimeSpan.FromSeconds 30.0)

            test <@ finished @>
            code = 0

        test <@ stale () @> // no completions file at all

        touch (completionsIn home) DateTime.UtcNow
        test <@ not (stale ()) @> // newer than the CLI and every watched input

        touch (Path.Combine(project, "src", "Commands.fs")) (DateTime.UtcNow.AddMinutes 1.0)
        test <@ stale () @> // a watched input changed after generation

        touch (completionsIn home) (DateTime.UtcNow.AddMinutes 2.0)
        touch (Path.Combine(project, "tool")) (DateTime.UtcNow.AddMinutes 3.0)
        test <@ stale () @>) // the CLI itself changed after generation

[<Fact>]
let ``entering a project with stale completions returns before the generator finishes`` () =
    Assert.SkipWhen((fishPath ()).IsNone, "fish is not on PATH")

    withProject (fun home project ->
        let hook = Path.Combine(home, "hook.fish")
        File.WriteAllText(hook, FishCompletions.hookScript "tool" [])
        let release = Path.Combine(home, "release")
        let log = Path.Combine(home, "generated")
        let tool = Path.Combine(project, "tool")

        // A generator that cannot finish until the test releases it: a hook that
        // waited for it would hold `cd` past the 30 s bound below, which kills fish.
        File.WriteAllText(
            tool,
            "#!/bin/sh\n"
            + $"i=0; while [ ! -f '%s{release}' ] && [ $i -lt 600 ]; do sleep 0.1; i=$((i+1)); done\n"
            + $"echo \"$*\" > '%s{log}'\n"
            + $"echo 'complete -c tool -f' > '%s{completionsIn home}'\n"
        )

        File.SetUnixFileMode(tool, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)

        let finished, code, stdout =
            runFish home $"source '%s{hook}'; cd '%s{project}'; echo entered" (TimeSpan.FromSeconds 30.0)

        test <@ finished && code = 0 && stdout.Trim() = "entered" @>
        test <@ not (File.Exists log) @> // fish returned while the generator still waited

        File.WriteAllText(release, "")
        let deadline = DateTime.UtcNow.AddSeconds 60.0

        while not (File.Exists log) && DateTime.UtcNow < deadline do
            Threading.Thread.Sleep 100

        test <@ File.Exists log @>
        test <@ (File.ReadAllText log).Trim() = "fish generate" @>)
