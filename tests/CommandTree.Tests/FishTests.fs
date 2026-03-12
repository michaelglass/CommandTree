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

    // Header
    test <@ content.Contains("# Fish completions for my-app") @>
    test <@ content.Contains("# Generated automatically from CommandTree") @>

    // Disable file completions
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

    // Split into lines for readable assertion
    let lines = content.Split('\n')

    // Verify structure: header, blank, disable, blank, completions
    test <@ lines.[0] = "# Fish completions for my-app" @>
    test <@ lines.[1] = "# Generated automatically from CommandTree" @>
    test <@ lines.[2] = "" @>
    test <@ lines.[3] = "# Disable file completions for command" @>
    test <@ lines.[4] = "complete -c my-app -f" @>
    test <@ lines.[5] = "" @>
    test <@ lines.[6] = "# Commands, subcommands, and argument completions" @>

    // Rest is the fishCompletions output — verify it's non-empty
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
    // File completion flag for gamma
    test <@ content.Contains("-F") @>

// =============================================================================
// writeToFile — writes to filesystem (uses real home dir)
// =============================================================================

[<Fact>]
let ``writeToFile creates fish completion file`` () =
    let tree = CommandReflection.fromUnion<SimpleCmd> "Simple CLI"

    // writeToFile writes to ~/.config/fish/completions/
    // We call it with a unique name and clean up after
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

        // Clean up
        File.Delete(completionsFile)
    with ex ->
        // Clean up on failure too
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
