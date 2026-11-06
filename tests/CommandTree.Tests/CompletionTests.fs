module CommandTree.Tests.CompletionTests

open Xunit
open Swensen.Unquote
open CommandTree
open CommandTree.Tests.TestHelpers

type EnvKind =
    | Dev
    | Staging
    | Prod

type CompletedCommand =
    | [<Cmd("Edit config"); CmdCompletion("dev", "staging", "prod")>] Edit of env: string option
    | [<Cmd("Show coverage"); CmdFileCompletion>] FileCov of path: string
    | [<Cmd("No completions")>] Plain of name: string

type UnionArgCommand = | [<Cmd("Optional environment")>] ChooseOpt of env: EnvKind option

type TwoFileCommand =
    | [<Cmd("Compare APIs"); CmdFileCompletion(FieldIndex = 0); CmdFileCompletion(FieldIndex = 1)>] CompareApi of
        oldDll: string *
        newDll: string

// Nested command types for group completion tests
type DevSubCmd =
    | [<CmdDefault>] Check
    | Build
    | Test

type NestedCmd =
    | Dev of DevSubCmd
    | [<Cmd("Show help")>] Help

/// A case whose kebab name is SHORTER than the abbreviation floor, beside a
/// longer case it is a prefix of. Modelled on a real workflow-state union whose
/// `Qa` case could not be typed at all.
type WorkflowState =
    | Qa
    | QaFailed
    | Backlog

type WorkflowCommand = | [<Cmd("Move a ticket")>] Move of state: WorkflowState option

[<Fact>]
let ``CmdCompletion attribute populates Values completion hint`` () =
    let leaf = CommandReflection.fromUnion<CompletedCommand> "Test" |> getLeaf <| "edit"
    test <@ leaf.Args.Length = 1 @>
    test <@ leaf.Args.[0].Completions = Values [ "dev"; "staging"; "prod" ] @>

[<Fact>]
let ``CmdFileCompletion attribute populates FilePath completion hint`` () =
    let leaf =
        CommandReflection.fromUnion<CompletedCommand> "Test" |> getLeaf <| "file-cov"

    test <@ leaf.Args.Length = 1 @>
    test <@ leaf.Args.[0].Completions = FilePath @>

[<Fact>]
let ``CmdFileCompletion with multiple FieldIndex values marks both fields`` () =
    let leaf =
        CommandReflection.fromUnion<TwoFileCommand> "Test" |> getLeaf <| "compare-api"

    test <@ leaf.Args.Length = 2 @>
    test <@ leaf.Args.[0].Completions = FilePath @>
    test <@ leaf.Args.[1].Completions = FilePath @>

[<Fact>]
let ``No attribute gives NoCompletion for simple types`` () =
    let leaf =
        CommandReflection.fromUnion<CompletedCommand> "Test" |> getLeaf <| "plain"

    test <@ leaf.Args.Length = 1 @>
    test <@ leaf.Args.[0].Completions = NoCompletion @>

[<Fact>]
let ``Optional union-typed field auto-detects Values completion`` () =
    let leaf =
        CommandReflection.fromUnion<UnionArgCommand> "Test" |> getLeaf <| "choose-opt"

    test <@ leaf.Args.Length = 1 @>
    test <@ leaf.Args.[0].Completions = Values [ "dev"; "staging"; "prod" ] @>

[<Fact>]
let ``parseFieldValue handles union type by kebab-case name`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "staging"
    test <@ result = Ok(Some(box EnvKind.Staging)) @>

[<Fact>]
let ``parseFieldValue handles unknown union case`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "unknown"
    test <@ result = Ok None @>

[<Fact>]
let ``formatFieldValue handles union type`` () =
    let result = CommandReflection.formatFieldValue (box EnvKind.Staging)
    test <@ result = "staging" @>

[<Fact>]
let ``roundtrip parse and format for optional union arg`` () =
    let tree = CommandReflection.fromUnion<UnionArgCommand> "Test"
    let result = CommandTree.parse tree [| "choose-opt"; "prod" |]

    match result with
    | Ok(UnionArgCommand.ChooseOpt(Some EnvKind.Prod)) -> ()
    | Ok cmd -> failwith $"Unexpected: %O{cmd}"
    | Error err -> failwith $"Parse error: %O{err}"

[<Fact>]
let ``roundtrip format for optional union arg`` () =
    let tree = CommandReflection.fromUnion<UnionArgCommand> "Test"

    let result =
        CommandTree.format tree (UnionArgCommand.ChooseOpt(Some EnvKind.Prod)) "cmd"

    test <@ result = Some "cmd choose-opt prod" @>

type AmbiguousKind =
    | Start
    | Stop
    | Status

[<Fact>]
let ``parseFieldValue exact match still works for union type`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "staging"
    test <@ result = Ok(Some(box EnvKind.Staging)) @>

[<Fact>]
let ``parseFieldValue prefix of case name works`` () =
    // "sta" is a prefix of "staging", shorter=3 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "sta"
    test <@ result = Ok(Some(box EnvKind.Staging)) @>

[<Fact>]
let ``parseFieldValue case name prefix of input works`` () =
    // "dev" is a prefix of "development", shorter=3 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "development"
    test <@ result = Ok(Some(box EnvKind.Dev)) @>

[<Fact>]
let ``parseFieldValue case name prefix of longer input works for prod`` () =
    // "prod" is a prefix of "production", shorter=4 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "production"
    test <@ result = Ok(Some(box EnvKind.Prod)) @>

[<Fact>]
let ``parseFieldValue short prefix returns None`` () =
    // "st" shorter=2 < 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "st"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue single char returns None`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "s"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue matches a case name typed in full, below the abbreviation floor`` () =
    // REGRESSION: the floor compares the SHORTER of the two strings, so for "qa"
    // against case "qa" it is 2 — below the >= 3 floor. Every candidate was
    // filtered out, the field parsed as "no match", and the whole command failed
    // with "Invalid arguments" while "qa-failed" worked. A name typed in FULL
    // must select its case, and must win over a longer case it prefixes.
    let exact = CommandReflection.parseFieldValue typeof<WorkflowState> "qa"
    let longer = CommandReflection.parseFieldValue typeof<WorkflowState> "qa-failed"

    test <@ exact = Ok(Some(box WorkflowState.Qa)) @>
    test <@ longer = Ok(Some(box WorkflowState.QaFailed)) @>

[<Fact>]
let ``parseFieldValue still refuses a too-short abbreviation of a longer case`` () =
    // The floor keeps its real job. Unlike "st" against EnvKind above, this asks
    // it beside a case that DOES match exactly at two characters: "ba" is nobody's
    // full name, so exactness cannot rescue it and it stays a non-match.
    let result = CommandReflection.parseFieldValue typeof<WorkflowState> "ba"
    test <@ result = Ok None @>

[<Fact>]
let ``every completion value a union field advertises parses back to its case`` () =
    // The completion list and the parser must agree: fish offered "qa" while the
    // parser rejected it, so tab-completion led straight into "Invalid arguments".
    // Anything advertised as a completion has to round-trip.
    let leaf = CommandReflection.fromUnion<WorkflowCommand> "Test" |> getLeaf <| "move"

    let advertised =
        match leaf.Args.[0].Completions with
        | Values values -> values
        | other -> failwith $"Expected Values completions, got %A{other}"

    test <@ advertised = [ "qa"; "qa-failed"; "backlog" ] @>

    for value in advertised do
        let roundTripped =
            CommandReflection.parseFieldValue typeof<WorkflowState> value
            |> Result.map (Option.map CommandReflection.formatFieldValue)

        test <@ roundTripped = Ok(Some value) @>

[<Fact>]
let ``parseFieldValue ambiguous prefix returns Error`` () =
    // "sta" matches "start" and "status" (both start with "sta")
    let result = CommandReflection.parseFieldValue typeof<AmbiguousKind> "sta"

    match result with
    | Error(AmbiguousValue(input, candidates)) ->
        test <@ input = "sta" @>
        test <@ candidates |> List.contains "start" @>
        test <@ candidates |> List.contains "status" @>
    | other -> failwith $"Expected Error(AmbiguousValue), got %A{other}"

[<Fact>]
let ``fishCompletions includes argument value completions from CmdCompletion`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    test <@ completions.Contains("complete -c test") @>
    test <@ completions.Contains("edit") @>
    // Should contain argument value completions for "edit"
    test <@ completions.Contains("-a \"dev\"") @>
    test <@ completions.Contains("-a \"staging\"") @>
    test <@ completions.Contains("-a \"prod\"") @>

[<Fact>]
let ``fishCompletions includes file completion flag from CmdFileCompletion`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    test <@ completions.Contains("__fish_seen_subcommand_from file-cov") @>
    test <@ completions.Contains("-F") @>

[<Fact>]
let ``fishCompletions includes union-type auto-detected completions for optional union field`` () =
    let tree = CommandReflection.fromUnion<UnionArgCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    // Should contain auto-detected completion values from EnvKind option union
    test <@ completions.Contains("-a \"dev\"") @>
    test <@ completions.Contains("-a \"staging\"") @>
    test <@ completions.Contains("-a \"prod\"") @>

[<Fact>]
let ``fishCompletions generates completions for nested command groups`` () =
    let tree = CommandReflection.fromUnion<NestedCmd> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    // Root level should list top-level commands
    test <@ completions.Contains("__fish_use_subcommand") @>
    test <@ completions.Contains("-a \"dev\"") @>
    test <@ completions.Contains("-a \"help\"") @>

    // Nested group should have condition for parent seen
    test <@ completions.Contains("__fish_seen_subcommand_from dev") @>

    // Nested group children should be included
    test <@ completions.Contains("-a \"check\"") @>
    test <@ completions.Contains("-a \"build\"") @>
    test <@ completions.Contains("-a \"test\"") @>

type ListFileCommand = | [<Cmd("Compare files"); CmdFileCompletion>] Compare of files: string list

[<Fact>]
let ``CmdFileCompletion works on list field`` () =
    let leaf =
        CommandReflection.fromUnion<ListFileCommand> "Test" |> getLeaf <| "compare"

    test <@ leaf.Args.Length = 1 @>
    test <@ leaf.Args.[0].Completions = FilePath @>
    test <@ leaf.Args.[0].IsList = true @>

[<Fact>]
let ``fishCompletions includes file completion for list field`` () =
    let tree = CommandReflection.fromUnion<ListFileCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    test <@ completions.Contains("__fish_seen_subcommand_from compare") @>
    test <@ completions.Contains("-F") @>

type CompDeployFlag =
    | DryRun
    | [<CmdFlag(Short = "e", Repeatable = true)>] Env of string

type DUFlagCompCommand =
    | [<Cmd("Deploy")>] Deploy of CompDeployFlag list
    | [<Cmd("Help")>] Help

[<Fact>]
let ``fishCompletions generates flag completions with long and short names`` () =
    let tree = CommandReflection.fromUnion<DUFlagCompCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"
    test <@ completions.Contains("-l env") @>
    test <@ completions.Contains("-s e") @>
    test <@ completions.Contains("-l dry-run") @>
    test <@ completions.Contains("-s d") @>

[<Fact>]
let ``fishCompletions identifies a repeatable flag`` () =
    let tree = CommandReflection.fromUnion<DUFlagCompCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"
    test <@ completions.Contains("-l env -s e -d \"Env (repeatable)\"") @>

type MultiLineFlag =
    | [<CmdFlag(Description = "Skip the actual operation\nand print what would run", Repeatable = true)>] Tag of string

type MultiLineToolsCmd = | [<Cmd("List tools\none per line")>] Ls

type MultiLineCommand =
    | [<Cmd("Confirm the gate verdict\r\nfailed here, not selected by check\nre-run it to see why")>] Confirm of
        MultiLineFlag list
    | [<Cmd("Nested tools\nwith a second line")>] Tools of MultiLineToolsCmd
    | [<Cmd("Greet \"you\" as $USER from C:\\home\nnot this line")>] Greet

[<Fact>]
let ``fishCompletions describes each command and flag with only the first description line`` () =
    let tree = CommandReflection.fromUnion<MultiLineCommand> "Multi\nline"
    let completions = CommandTree.fishCompletions tree "test"

    let descriptions =
        completions.Split('\n')
        |> Array.filter (fun line -> line.Contains(" -d \""))
        |> Array.map (fun line -> line.Substring(line.IndexOf(" -d \"") + 4))
        |> List.ofArray

    test
        <@
            descriptions =
                [
                    "\"Confirm the gate verdict\""
                    "\"Nested tools\""
                    "\"Greet \\\"you\\\" as \\$USER from C:\\\\home\""
                    "\"Skip the actual operation (repeatable)\""
                    "\"List tools\""
                ]
        @>

    test <@ not (completions.Contains("\r")) @>

    test
        <@
            completions.Split('\n')
            |> Array.forall (fun line -> line.StartsWith("complete -c test"))
        @>
