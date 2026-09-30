module CommandTree.Tests.HelpTests

open Xunit
open Swensen.Unquote
open CommandTree
open CommandTree.Tests.TestHelpers

type AnnotatedCommand =
    | [<Cmd("Ratchet coverage"); CmdArg("Path to config", Default = "cfg.json")>] Ratchet of config: string option
    | [<Cmd("Merge two files");
        CmdArg("Baseline XML");
        CmdArg("Current XML", FieldIndex = 1);
        CmdArg("Output path", FieldIndex = 2);
        CmdExample("old.xml new.xml out.xml", "a.xml b.xml merged.xml")>] Merge of
        baseline: string *
        current: string *
        output: string

type OptionalDescCommand =
    | [<Cmd(Name = "fmt")>] Format // description derived from case name
    | Deploy // no attribute at all

type DefaultOnlyCommand = | [<Cmd("Build"); CmdArg(Default = "Release")>] Build of config: string option

type MergeArgs =
    {
        [<CmdArg("Baseline XML file")>]
        Baseline: string
        [<CmdArg("Current XML file")>]
        Current: string
        [<CmdArg("Output path", Default = "merged.xml")>]
        Output: string option
    }

type RecordArgCommand =
    | [<Cmd("Merge using record"); CmdExample("old.xml new.xml", "a.xml b.xml out.xml")>] Merge of MergeArgs

type DocFlag =
    | [<CmdFlag(Description = "Skip the actual operation")>] DryRun
    | [<CmdFlag(Name = "out", Description = "Output file path", Repeatable = true)>] Output of string

type FlagDescCommand = | [<Cmd("Generate docs")>] Generate of DocFlag list

let annotatedRatchet =
    CommandReflection.fromUnion<AnnotatedCommand> "Test" |> getLeaf <| "ratchet"

let annotatedMerge =
    CommandReflection.fromUnion<AnnotatedCommand> "Test" |> getLeaf <| "merge"

let recordMerge =
    CommandReflection.fromUnion<RecordArgCommand> "Test" |> getLeaf <| "merge"

[<Fact>]
let ``CmdArg on case populates ArgInfo Description`` () =
    test <@ annotatedRatchet.Args.[0].Description = Some "Path to config" @>

[<Fact>]
let ``CmdArg on case populates ArgInfo Default`` () =
    test <@ annotatedRatchet.Args.[0].Default = Some "cfg.json" @>

[<Fact>]
let ``CmdArg FieldIndex targets correct field`` () =
    test <@ annotatedMerge.Args.[0].Description = Some "Baseline XML" @>
    test <@ annotatedMerge.Args.[1].Description = Some "Current XML" @>
    test <@ annotatedMerge.Args.[2].Description = Some "Output path" @>

[<Fact>]
let ``CmdArg field with no FieldIndex match gives None`` () =
    let leaf =
        CommandReflection.fromUnion<OptionalDescCommand> "Test" |> getLeaf <| "deploy"
    // Deploy has no CmdArg attributes, so Args is empty
    test <@ List.isEmpty leaf.Args @>

[<Fact>]
let ``CmdExample with multiple values in one attribute`` () =
    test <@ annotatedMerge.Examples = [ "old.xml new.xml out.xml"; "a.xml b.xml merged.xml" ] @>

[<Fact>]
let ``Cmd without description derives from case name`` () =
    let leaf =
        CommandReflection.fromUnion<OptionalDescCommand> "Test" |> getLeaf <| "fmt"

    test <@ leaf.Name = "fmt" @>
    test <@ leaf.Description = "Format" @>

[<Fact>]
let ``CmdArg with only Default gives None Description`` () =
    let leaf =
        CommandReflection.fromUnion<DefaultOnlyCommand> "Test" |> getLeaf <| "build"

    test <@ leaf.Args.[0].Description = None @>
    test <@ leaf.Args.[0].Default = Some "Release" @>

[<Fact>]
let ``CmdArg on record field populates Description`` () =
    test <@ recordMerge.Args.[0].Description = Some "Baseline XML file" @>
    test <@ recordMerge.Args.[1].Description = Some "Current XML file" @>

[<Fact>]
let ``CmdArg on record field populates Default`` () =
    test <@ recordMerge.Args.[2].Default = Some "merged.xml" @>

[<Fact>]
let ``CmdExample on record command populates Examples`` () =
    test <@ recordMerge.Examples = [ "old.xml new.xml"; "a.xml b.xml out.xml" ] @>

[<Fact>]
let ``CmdFlag Description overrides derived description`` () =
    let leaf =
        CommandReflection.fromUnion<FlagDescCommand> "Test" |> getLeaf <| "generate"

    let dryRunFlag = leaf.Flags |> List.find (fun fi -> fi.LongName = "dry-run")
    let outFlag = leaf.Flags |> List.find (fun fi -> fi.LongName = "out")
    test <@ dryRunFlag.Description = "Skip the actual operation" @>
    test <@ outFlag.Description = "Output file path" @>

[<Fact>]
let ``help identifies a repeatable flag`` () =
    let tree = CommandReflection.fromUnion<FlagDescCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "generate" ] "test"
    test <@ helpText.Contains("Output file path (repeatable)") @>

[<Fact>]
let ``help includes Arguments section when args have descriptions`` () =
    let helpText = CommandTree.help (CommandTree.Leaf annotatedMerge) [] "mycli"
    test <@ helpText.Contains("Arguments:") @>
    test <@ helpText.Contains("Baseline XML") @>

[<Fact>]
let ``help includes default in Arguments section`` () =
    let helpText = CommandTree.help (CommandTree.Leaf annotatedRatchet) [] "mycli"
    test <@ helpText.Contains("(default: cfg.json)") @>

[<Fact>]
let ``help includes Examples section`` () =
    let helpText = CommandTree.help (CommandTree.Leaf annotatedMerge) [] "mycli"
    test <@ helpText.Contains("Examples:") @>
    test <@ helpText.Contains("old.xml new.xml out.xml") @>

/// The example line for a leaf should render the command path (prefix + leaf name)
/// exactly once. Regression for leaf-name duplication in examples.
let private exampleLines (helpText: string) =
    helpText.Split('\n')
    |> Array.filter (fun l -> l.Contains("old.xml new.xml out.xml"))

[<Fact>]
let ``help example renders leaf name exactly once (path = [])`` () =
    let helpText = CommandTree.help (CommandTree.Leaf annotatedMerge) [] "mycli"
    let lines = exampleLines helpText
    test <@ lines.Length = 1 @>
    // leaf name "merge" must appear exactly once on the example line
    let line = lines.[0]

    let occurrences =
        (line.Split([| "merge" |], System.StringSplitOptions.None)).Length - 1

    test <@ occurrences = 1 @>
    test <@ line.Trim() = "mycli merge old.xml new.xml out.xml" @>

[<Fact>]
let ``helpForPath example renders leaf name exactly once`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "merge" ] "mycli"
    let lines = exampleLines helpText
    test <@ lines.Length = 1 @>
    let line = lines.[0]

    let occurrences =
        (line.Split([| "merge" |], System.StringSplitOptions.None)).Length - 1

    test <@ occurrences = 1 @>
    test <@ line.Trim() = "mycli merge old.xml new.xml out.xml" @>

// =============================================================================
// Multi-line descriptions: the caller writes flush-left prose, and each help
// surface lays it out. Table rows hang continuation lines under their own
// description column; the per-command block keeps them flush-left.
// =============================================================================

[<Literal>]
let private ThreeLineDescription =
    "Confirm the gate verdict\nfailed here, not selected by check\nre-run it to see why"

type LayoutFlag = | [<CmdFlag(Description = "Skip the actual operation\nand print what would run")>] DryRun

type LayoutToolsCommand =
    | [<CmdDefault; Cmd(ThreeLineDescription)>] Confirm
    | [<Cmd("Show status")>] Status

type LayoutCommand =
    | [<Cmd(ThreeLineDescription)>] Confirm
    | [<Cmd("Merge\nthe inputs"); CmdArg("Baseline XML\nfrom main")>] Merge of baseline: string * current: string
    | [<Cmd("Deploy")>] Deploy of LayoutFlag list
    | [<Cmd("Nested tools")>] Tools of LayoutToolsCommand

let private layoutTree = CommandReflection.fromUnion<LayoutCommand> "Layout"

[<Fact>]
let ``group listing hangs multi-line descriptions under the description column`` () =
    let expected =
        "Usage: fshw <command>\n"
        + "\n"
        + "Layout\n"
        + "\n"
        + "Commands:\n"
        + "  confirm          Confirm the gate verdict\n"
        + "                   failed here, not selected by check\n"
        + "                   re-run it to see why\n"
        + "  merge <baseline> <current> Merge\n"
        + "                             the inputs\n"
        + "  deploy           Deploy\n"
        + "  tools            Nested tools"

    test <@ CommandTree.help layoutTree [] "fshw" = expected @>

[<Fact>]
let ``nested group listing puts the default marker on the first description line`` () =
    let expected =
        "Usage: fshw tools <command>\n"
        + "\n"
        + "Nested tools\n"
        + "\n"
        + "Commands:\n"
        + "  confirm          Confirm the gate verdict (default)\n"
        + "                   failed here, not selected by check\n"
        + "                   re-run it to see why\n"
        + "  status           Show status"

    test <@ CommandTree.helpForPath layoutTree [ "tools" ] "fshw" = expected @>

[<Fact>]
let ``helpWithGlobals listing hangs multi-line descriptions under the description column`` () =
    let help = CommandTree.helpWithGlobals layoutTree [] "fshw"

    let listing =
        help.Substring(help.IndexOf("Commands:\n", System.StringComparison.Ordinal))

    test
        <@
            listing.StartsWith(
                "Commands:\n"
                + "  confirm          Confirm the gate verdict\n"
                + "                   failed here, not selected by check\n"
                + "                   re-run it to see why\n",
                System.StringComparison.Ordinal
            )
        @>

[<Fact>]
let ``helpFull hangs multi-line descriptions under each depth's own column`` () =
    let expected =
        "Usage: fshw <command>\n"
        + "\n"
        + "Commands:\n"
        + "confirm              Confirm the gate verdict\n"
        + "                     failed here, not selected by check\n"
        + "                     re-run it to see why\n"
        + "merge <baseline> <current> Merge\n"
        + "                           the inputs\n"
        + "deploy [options]     Deploy\n"
        + "tools                Nested tools\n"
        + "  confirm              Confirm the gate verdict (default)\n"
        + "                       failed here, not selected by check\n"
        + "                       re-run it to see why\n"
        + "  status               Show status"

    test <@ CommandTree.helpFull layoutTree "fshw" = expected @>

[<Fact>]
let ``per-command help keeps a multi-line description flush-left`` () =
    let expected =
        "Usage: fshw confirm\n"
        + "\n"
        + "Confirm the gate verdict\n"
        + "failed here, not selected by check\n"
        + "re-run it to see why"

    test <@ CommandTree.helpForPath layoutTree [ "confirm" ] "fshw" = expected @>

[<Fact>]
let ``nested per-command help keeps a multi-line description flush-left`` () =
    let expected =
        "Usage: fshw tools confirm\n"
        + "\n"
        + "Confirm the gate verdict\n"
        + "failed here, not selected by check\n"
        + "re-run it to see why"

    test <@ CommandTree.helpForPath layoutTree [ "tools"; "confirm" ] "fshw" = expected @>

[<Fact>]
let ``argument and option rows hang multi-line descriptions under their column`` () =
    let merge = CommandTree.helpForPath layoutTree [ "merge" ] "fshw"

    let deploy = CommandTree.helpForPath layoutTree [ "deploy" ] "fshw"

    test
        <@
            merge =
                "Usage: fshw merge <baseline> <current>\n"
                + "\n"
                + "Merge\n"
                + "the inputs\n"
                + "\n"
                + "Arguments:\n"
                + "  <baseline>         Baseline XML\n"
                + "                     from main"
        @>

    test
        <@
            deploy =
                "Usage: fshw deploy [options]\n"
                + "\n"
                + "Deploy\n"
                + "\n"
                + "Options:\n"
                + "  --dry-run, -d                Skip the actual operation\n"
                + "                               and print what would run"
        @>

/// The root listing of layoutTree with its confirm command's description replaced.
let private listingWithConfirmDescription (description: string) =
    let confirm =
        match CommandTree.findByPath layoutTree [ "confirm" ] with
        | Some(CommandTree.Leaf leaf) -> CommandTree.Leaf { leaf with Description = description }
        | other -> failwith $"expected the confirm leaf, got %A{other}"

    match layoutTree with
    | CommandTree.Group g -> CommandTree.help (CommandTree.Group { g with Children = [ confirm ] }) [] "fshw"
    | other -> failwith $"expected a group, got %A{other}"

[<Fact>]
let ``Windows line endings in a description render as single newlines in a listing`` () =
    test
        <@
            (listingWithConfirmDescription "first\r\nsecond")
                .EndsWith("  confirm          first\n                   second")
        @>

[<Fact>]
let ``a blank line in a listed description stays empty instead of trailing spaces`` () =
    test
        <@
            (listingWithConfirmDescription "first\n\nsecond")
                .EndsWith("  confirm          first\n\n                   second")
        @>

type OptionalDefaultHelpCommand =
    | [<Cmd("Ratchet coverage"); CmdDefault>] Ratchet of config: string option
    | [<Cmd("Check coverage")>] Check

[<Fact>]
let ``help marks a default command that takes arguments`` () =
    let tree = CommandReflection.fromUnion<OptionalDefaultHelpCommand> "Test"
    let lines = (CommandTree.help tree [] "tool").Split('\n')

    test
        <@
            lines
            |> Array.exists (fun l -> l.Contains "ratchet [config]" && l.EndsWith "Ratchet coverage (default)")
        @>
