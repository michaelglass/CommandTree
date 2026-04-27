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
    { [<CmdArg("Baseline XML file")>]
      Baseline: string
      [<CmdArg("Current XML file")>]
      Current: string
      [<CmdArg("Output path", Default = "merged.xml")>]
      Output: string option }

type RecordArgCommand =
    | [<Cmd("Merge using record"); CmdExample("old.xml new.xml", "a.xml b.xml out.xml")>] Merge of MergeArgs

type DocFlag =
    | [<CmdFlag(Description = "Skip the actual operation")>] DryRun
    | [<CmdFlag(Name = "out", Description = "Output file path")>] Output of string

type FlagDescCommand = | [<Cmd("Generate docs")>] Generate of DocFlag list

[<Fact>]
let ``CmdArg on case populates ArgInfo Description`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "ratchet"
    test <@ leaf.Args.[0].Description = Some "Path to config" @>

[<Fact>]
let ``CmdArg on case populates ArgInfo Default`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "ratchet"
    test <@ leaf.Args.[0].Default = Some "cfg.json" @>

[<Fact>]
let ``CmdArg FieldIndex targets correct field`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "merge"
    test <@ leaf.Args.[0].Description = Some "Baseline XML" @>
    test <@ leaf.Args.[1].Description = Some "Current XML" @>
    test <@ leaf.Args.[2].Description = Some "Output path" @>

[<Fact>]
let ``CmdArg field with no FieldIndex match gives None`` () =
    let tree = CommandReflection.fromUnion<OptionalDescCommand> "Test"
    let leaf = getLeaf tree "deploy"
    // Deploy has no CmdArg attributes, so Args is empty
    test <@ List.isEmpty leaf.Args @>

[<Fact>]
let ``CmdExample with multiple values in one attribute`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "merge"
    test <@ leaf.Examples = [ "old.xml new.xml out.xml"; "a.xml b.xml merged.xml" ] @>

[<Fact>]
let ``Cmd without description derives from case name`` () =
    let tree = CommandReflection.fromUnion<OptionalDescCommand> "Test"
    let leaf = getLeaf tree "fmt"
    test <@ leaf.Name = "fmt" @>
    test <@ leaf.Description = "Format" @>

[<Fact>]
let ``CmdArg with only Default gives None Description`` () =
    let tree = CommandReflection.fromUnion<DefaultOnlyCommand> "Test"
    let leaf = getLeaf tree "build"
    test <@ leaf.Args.[0].Description = None @>
    test <@ leaf.Args.[0].Default = Some "Release" @>

[<Fact>]
let ``CmdArg on record field populates Description`` () =
    let tree = CommandReflection.fromUnion<RecordArgCommand> "Test"
    let leaf = getLeaf tree "merge"
    test <@ leaf.Args.[0].Description = Some "Baseline XML file" @>
    test <@ leaf.Args.[1].Description = Some "Current XML file" @>

[<Fact>]
let ``CmdArg on record field populates Default`` () =
    let tree = CommandReflection.fromUnion<RecordArgCommand> "Test"
    let leaf = getLeaf tree "merge"
    test <@ leaf.Args.[2].Default = Some "merged.xml" @>

[<Fact>]
let ``CmdExample on record command populates Examples`` () =
    let tree = CommandReflection.fromUnion<RecordArgCommand> "Test"
    let leaf = getLeaf tree "merge"
    test <@ leaf.Examples = [ "old.xml new.xml"; "a.xml b.xml out.xml" ] @>

[<Fact>]
let ``CmdFlag Description overrides derived description`` () =
    let tree = CommandReflection.fromUnion<FlagDescCommand> "Test"
    let leaf = getLeaf tree "generate"
    let dryRunFlag = leaf.Flags |> List.find (fun fi -> fi.LongName = "dry-run")
    let outFlag = leaf.Flags |> List.find (fun fi -> fi.LongName = "out")
    test <@ dryRunFlag.Description = "Skip the actual operation" @>
    test <@ outFlag.Description = "Output file path" @>

[<Fact>]
let ``help includes Arguments section when args have descriptions`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "merge"
    let helpText = CommandTree.help (CommandTree.Leaf leaf) [] "mycli"
    test <@ helpText.Contains("Arguments:") @>
    test <@ helpText.Contains("Baseline XML") @>

[<Fact>]
let ``help includes default in Arguments section`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "ratchet"
    let helpText = CommandTree.help (CommandTree.Leaf leaf) [] "mycli"
    test <@ helpText.Contains("(default: cfg.json)") @>

[<Fact>]
let ``help includes Examples section`` () =
    let tree = CommandReflection.fromUnion<AnnotatedCommand> "Test"
    let leaf = getLeaf tree "merge"
    let helpText = CommandTree.help (CommandTree.Leaf leaf) [] "mycli"
    test <@ helpText.Contains("Examples:") @>
    test <@ helpText.Contains("old.xml new.xml out.xml") @>
