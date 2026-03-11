module CommandTree.Tests.CompletionTests

open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Test types with completion attributes
// =============================================================================

type EnvKind =
    | Dev
    | Staging
    | Prod

type CompletedCommand =
    | [<Cmd("Edit config"); CmdCompletion("dev", "staging", "prod")>] Edit of env: string option
    | [<Cmd("Show coverage"); CmdFileCompletion>] FileCov of path: string
    | [<Cmd("No completions")>] Plain of name: string

type UnionArgCommand = | [<Cmd("Optional environment")>] ChooseOpt of env: EnvKind option

// Nested command types for group completion tests
type DevSubCmd =
    | [<CmdDefault>] Check
    | Build
    | Test

type NestedCmd =
    | Dev of DevSubCmd
    | [<Cmd("Show help")>] Help

// =============================================================================
// ArgCompletionHint from CmdCompletion attribute
// =============================================================================

[<Fact>]
let ``CmdCompletion attribute populates Values completion hint`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let editNode = children |> List.find (fun c -> CommandTree.name c = "edit")

        match editNode with
        | CommandTree.Leaf(_, _, args, _, _) ->
            test <@ args.Length = 1 @>
            test <@ args.[0].Completions = Values [ "dev"; "staging"; "prod" ] @>
        | CommandTree.Group _ -> failwith "Expected leaf"
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``CmdFileCompletion attribute populates FilePath completion hint`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let fileNode = children |> List.find (fun c -> CommandTree.name c = "file-cov")

        match fileNode with
        | CommandTree.Leaf(_, _, args, _, _) ->
            test <@ args.Length = 1 @>
            test <@ args.[0].Completions = FilePath @>
        | CommandTree.Group _ -> failwith "Expected leaf"
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``No attribute gives NoCompletion for simple types`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let plainNode = children |> List.find (fun c -> CommandTree.name c = "plain")

        match plainNode with
        | CommandTree.Leaf(_, _, args, _, _) ->
            test <@ args.Length = 1 @>
            test <@ args.[0].Completions = NoCompletion @>
        | CommandTree.Group _ -> failwith "Expected leaf"
    | CommandTree.Leaf _ -> failwith "Expected group"

// =============================================================================
// Union-type auto-detection for completions (option-wrapped unions)
// =============================================================================

[<Fact>]
let ``Optional union-typed field auto-detects Values completion`` () =
    let tree = CommandReflection.fromUnion<UnionArgCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let chooseNode = children |> List.find (fun c -> CommandTree.name c = "choose-opt")

        match chooseNode with
        | CommandTree.Leaf(_, _, args, _, _) ->
            test <@ args.Length = 1 @>
            test <@ args.[0].Completions = Values [ "dev"; "staging"; "prod" ] @>
        | CommandTree.Group _ -> failwith "Expected leaf"
    | CommandTree.Leaf _ -> failwith "Expected group"

// =============================================================================
// Union-type parseFieldValue / formatFieldValue
// =============================================================================

[<Fact>]
let ``parseFieldValue handles union type by kebab-case name`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "staging"
    test <@ result = Some(box EnvKind.Staging) @>

[<Fact>]
let ``parseFieldValue handles unknown union case`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "unknown"
    test <@ result = None @>

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
    | Error msg -> failwith $"Parse error: %s{msg}"

[<Fact>]
let ``roundtrip format for optional union arg`` () =
    let tree = CommandReflection.fromUnion<UnionArgCommand> "Test"

    let result =
        CommandTree.format tree (UnionArgCommand.ChooseOpt(Some EnvKind.Prod)) [] "cmd"

    test <@ result = Some "cmd choose-opt prod" @>

// =============================================================================
// Prefix matching for union-typed parseFieldValue
// =============================================================================

type AmbiguousKind =
    | Start
    | Stop
    | Status

[<Fact>]
let ``parseFieldValue exact match still works for union type`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "staging"
    test <@ result = Some(box EnvKind.Staging) @>

[<Fact>]
let ``parseFieldValue prefix of case name works`` () =
    // "sta" is a prefix of "staging", shorter=3 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "sta"
    test <@ result = Some(box EnvKind.Staging) @>

[<Fact>]
let ``parseFieldValue case name prefix of input works`` () =
    // "dev" is a prefix of "development", shorter=3 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "development"
    test <@ result = Some(box EnvKind.Dev) @>

[<Fact>]
let ``parseFieldValue case name prefix of longer input works for prod`` () =
    // "prod" is a prefix of "production", shorter=4 >= 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "production"
    test <@ result = Some(box EnvKind.Prod) @>

[<Fact>]
let ``parseFieldValue short prefix returns None`` () =
    // "st" shorter=2 < 3
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "st"
    test <@ result = None @>

[<Fact>]
let ``parseFieldValue single char returns None`` () =
    let result = CommandReflection.parseFieldValue typeof<EnvKind> "s"
    test <@ result = None @>

[<Fact>]
let ``parseFieldValue ambiguous prefix fails with error`` () =
    // "st" matches both "start" and "stop" and "status" — but shorter=2 < 3, so no match
    // Use "sta" which matches "start" and "status" (both start with "sta")
    let ex =
        Assert.Throws<System.Exception>(fun () ->
            CommandReflection.parseFieldValue typeof<AmbiguousKind> "sta" |> ignore)

    test <@ ex.Message.Contains("Ambiguous") @>
    test <@ ex.Message.Contains("start") @>
    test <@ ex.Message.Contains("status") @>

// =============================================================================
// Fish completions with argument values
// =============================================================================

[<Fact>]
let ``fishCompletions includes argument value completions from CmdCompletion`` () =
    let tree = CommandReflection.fromUnion<CompletedCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    // Should contain the subcommand completions
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

    // Should contain -F for file completion on file-cov
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

// =============================================================================
// Fish completions for nested groups
// =============================================================================

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
