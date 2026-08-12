module CommandTree.Tests.SpecErrorTests

open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Ok-path equivalence arbiter
//
// A representative DU exercising every VALID construction shape: scalar /
// optional / Guid fields, a nested union group with a default, record args, a
// DU flag list (with long + short names), and a list-last field. The golden help
// text and parse-matrix expectations below are byte-equal pins: `fromUnion*` and
// `tryFromUnion*` must build identical trees with identical behavior.
// =============================================================================

type EqPriority =
    | Low
    | Medium
    | High

type EqTaskCmd =
    | [<Cmd("Add a task")>] Add of title: string * priority: EqPriority option
    | [<CmdDefault; Cmd("List tasks")>] List
    | [<Cmd("Complete a task")>] Complete of id: int

type EqDeployFlag =
    | [<CmdFlag(Description = "Dry run only")>] DryRun
    | [<CmdFlag(Name = "to", Short = "t", Description = "Target env")>] Target of string

type EqMergeArgs =
    { [<CmdArg("Baseline file")>]
      Baseline: string
      [<CmdArg("Output", Default = "out.xml")>]
      Output: string option }

type EqCommand =
    | [<Cmd("Task management")>] Task of EqTaskCmd
    | [<Cmd("Deploy somewhere")>] Deploy of EqDeployFlag list
    | [<Cmd("Merge with record args")>] Merge of EqMergeArgs
    | [<Cmd("Tag files"); CmdArg("Label")>] Tag of label: string * files: string list
    | [<Cmd("Run the suite")>] Test
    | [<Cmd("A guid arg")>] Ident of id: System.Guid

type EqGlobalFlag =
    | [<CmdFlag(Description = "Verbose output")>] Verbose
    | [<CmdFlag(Name = "log-level", Description = "Log level")>] LogLevel of string

/// Golden full help text captured from the pre-refactor path. Byte-equal across
/// `fromUnion*` (wrapper) and `tryFromUnion*` (direct) on this valid DU.
let private goldenHelpFull =
    "Usage: refcli <command>\n"
    + "\n"
    + "Commands:\n"
    + "task                 Task management\n"
    + "  add <title> [priority] Add a task\n"
    + "  list                 List tasks (default)\n"
    + "  complete <id>        Complete a task\n"
    + "deploy [options]     Deploy somewhere\n"
    + "merge <baseline> [output] Merge with record args\n"
    + "tag <label> <files...> Tag files\n"
    + "test                 Run the suite\n"
    + "ident <id>           A guid arg"

[<Fact>]
let ``tryFromUnionWithGlobalsAndEnv Ok-path help text matches pre-refactor golden`` () =
    match CommandReflection.tryFromUnionWithGlobalsAndEnv<EqCommand, EqGlobalFlag> "Reference CLI" "REF" with
    | Ok spec -> test <@ CommandTree.helpFull spec.Tree "refcli" = goldenHelpFull @>
    | Error errs -> failwith $"Expected Ok, got Error: %A{errs}"

[<Fact>]
let ``throwing wrapper produces tree byte-identical to try-variant`` () =
    let viaWrapper =
        CommandReflection.fromUnionWithGlobalsAndEnv<EqCommand, EqGlobalFlag> "Reference CLI" "REF"

    let viaTry =
        match CommandReflection.tryFromUnionWithGlobalsAndEnv<EqCommand, EqGlobalFlag> "Reference CLI" "REF" with
        | Ok spec -> spec
        | Error errs -> failwith $"Expected Ok, got Error: %A{errs}"

    test <@ CommandTree.helpFull viaWrapper.Tree "refcli" = CommandTree.helpFull viaTry.Tree "refcli" @>

[<Fact>]
let ``Ok-path parse matrix matches pre-refactor behavior`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<EqCommand, EqGlobalFlag> "Reference CLI" "REF"

    // Representative matrix across every command shape.
    test
        <@ spec.Parse [| "task"; "add"; "hello"; "high" |] = Ok([], EqCommand.Task(EqTaskCmd.Add("hello", Some High))) @>

    test <@ spec.Parse [| "task"; "add"; "hello" |] = Ok([], EqCommand.Task(EqTaskCmd.Add("hello", None))) @>
    test <@ spec.Parse [| "task"; "complete"; "7" |] = Ok([], EqCommand.Task(EqTaskCmd.Complete 7)) @>
    test <@ spec.Parse [| "task" |] = Ok([], EqCommand.Task EqTaskCmd.List) @> // default

    test
        <@ spec.Parse [| "deploy"; "--dry-run"; "--to"; "prod" |] = Ok([], EqCommand.Deploy [ DryRun; Target "prod" ]) @>

    test <@ spec.Parse [| "deploy"; "-t"; "stage" |] = Ok([], EqCommand.Deploy [ Target "stage" ]) @>

    test <@ spec.Parse [| "merge"; "base.xml" |] = Ok([], EqCommand.Merge { Baseline = "base.xml"; Output = None }) @>

    test
        <@
            spec.Parse [| "merge"; "base.xml"; "result.xml" |] = Ok(
                [],
                EqCommand.Merge
                    { Baseline = "base.xml"
                      Output = Some "result.xml" }
            )
        @>

    test <@ spec.Parse [| "tag"; "v1"; "a.fs"; "b.fs" |] = Ok([], EqCommand.Tag("v1", [ "a.fs"; "b.fs" ])) @>
    test <@ spec.Parse [| "test" |] = Ok([], EqCommand.Test) @>

    test
        <@
            spec.Parse [| "ident"; "00000000-0000-0000-0000-000000000001" |] = Ok(
                [],
                EqCommand.Ident(System.Guid "00000000-0000-0000-0000-000000000001")
            )
        @>

    test <@ spec.Parse [| "--verbose"; "test" |] = Ok([ Verbose ], EqCommand.Test) @>

    test
        <@
            spec.Parse [| "--log-level"; "debug"; "task"; "list" |] = Ok(
                [ LogLevel "debug" ],
                EqCommand.Task EqTaskCmd.List
            )
        @>

    // Error cases — identical structured ParseErrors.
    test <@ spec.Parse [| "nonexistent" |] = Error(UnknownCommand("nonexistent", [||], [])) @>

    match spec.Parse [| "task"; "add" |] with
    | Error(InvalidArguments("add", _)) -> ()
    | other -> failwith $"Expected InvalidArguments for 'add', got %A{other}"

// =============================================================================
// Aggregation: ALL shape errors reported at once
// =============================================================================

type BadField2 =
    | [<Cmd("First")>] One of when_: System.DateTimeOffset // unsupported
    | [<Cmd("Second")>] Two of ok: string
    | [<Cmd("Third")>] Three of span: System.TimeSpan // unsupported

[<Fact>]
let ``tryFromUnion aggregates every unsupported field across cases in declaration order`` () =
    match CommandReflection.tryFromUnion<BadField2> "Test" with
    | Ok _ -> failwith "Expected Error"
    | Error errs ->
        test <@ List.length errs = 2 @>

        // [<Cmd(...)>] sets the description, not the name — names are kebab-cased
        // from the case names (One -> "one", Three -> "three").
        match errs with
        | [ UnsupportedFieldType("one", "when_", t1); UnsupportedFieldType("three", "span", t2) ] ->
            test <@ t1 = typeof<System.DateTimeOffset> @>
            test <@ t2 = typeof<System.TimeSpan> @>
        | _ -> failwith $"Unexpected error shape: %A{errs}"

type CombinedGlobal = Timeout of int

type CombinedScanFlag =
    | Timeout of int
    | Watch

type CombinedCmd =
    | [<Cmd("Scan")>] Scan of CombinedScanFlag list
    | [<Cmd("Bad")>] Bad of stamp: System.DateTimeOffset // unsupported

[<Fact>]
let ``tryFromUnionWithGlobals aggregates field errors AND flag collisions together`` () =
    // CombinedCmd has both an unsupported field (Bad/stamp) AND flag collisions:
    // Scan's --timeout/-t flags vs the global Timeout flag's --timeout/-t. A single
    // Error must carry every one of them, field errors first (DU declaration order),
    // then collisions (tree order), long before short per command.
    match CommandReflection.tryFromUnionWithGlobals<CombinedCmd, CombinedGlobal> "Test" with
    | Ok _ -> failwith "Expected Error"
    | Error errs ->
        match errs with
        | [ UnsupportedFieldType("bad", "stamp", _)
            GlobalFlagCollision("--timeout", "scan")
            GlobalShortFlagCollision("-t", "scan") ] -> ()
        | _ -> failwith $"Unexpected aggregated error shape: %A{errs}"

// =============================================================================
// Per-case `format` information-content pins
// =============================================================================

[<Fact>]
let ``format UnsupportedFieldType carries case, field, type, and a supported type`` () =
    let s =
        SpecError.format (SpecError.UnsupportedFieldType("at", "timestamp", typeof<System.DateTimeOffset>))

    test <@ s.Contains("at") @> // case name
    test <@ s.Contains("timestamp") @> // field name
    test <@ s.Contains("DateTimeOffset") @> // offending type
    test <@ s.Contains("string") @> // at least one supported type

[<Fact>]
let ``format ListFieldNotLast carries case and field`` () =
    let s = SpecError.format (SpecError.ListFieldNotLast("bad", "files"))
    test <@ s.Contains("bad") @>
    test <@ s.Contains("files") @>
    test <@ s.Contains("last") @>

[<Fact>]
let ``format MultipleListFields carries case`` () =
    let s = SpecError.format (SpecError.MultipleListFields "bad")
    test <@ s.Contains("bad") @>
    test <@ s.Contains("one list field") @>

[<Fact>]
let ``format GlobalFlagCollision carries flag and command`` () =
    let s = SpecError.format (SpecError.GlobalFlagCollision("--timeout", "scan"))
    test <@ s.Contains("--timeout") @>
    test <@ s.Contains("scan") @>
    test <@ s.Contains("global flag") @>

[<Fact>]
let ``format GlobalShortFlagCollision carries flag and command`` () =
    let s = SpecError.format (SpecError.GlobalShortFlagCollision("-t", "scan"))
    test <@ s.Contains("-t") @>
    test <@ s.Contains("scan") @>
    test <@ s.Contains("global flag") @>

[<Fact>]
let ``formatAll single error has no count header`` () =
    let single =
        SpecError.UnsupportedFieldType("at", "timestamp", typeof<System.DateTimeOffset>)

    test <@ SpecError.formatAll [ single ] = SpecError.format single @>

[<Fact>]
let ``formatAll multiple errors has count header and one line per error`` () =
    let errs =
        [ SpecError.UnsupportedFieldType("first", "when_", typeof<System.DateTimeOffset>)
          SpecError.GlobalFlagCollision("--timeout", "scan") ]

    let s = SpecError.formatAll errs
    test <@ s.Contains("2 problems found") @>
    test <@ s.Contains("when_") @>
    test <@ s.Contains("--timeout") @>

[<Fact>]
let ``formatAll empty list is non-empty fallback`` () =
    test <@ SpecError.formatAll [] = "Invalid command spec" @>

// =============================================================================
// try / throw consistency: tryFromUnion is Ok  <=>  fromUnion does not throw
// =============================================================================

let private throws (f: unit -> unit) : bool =
    try
        f ()
        false
    with :? System.InvalidOperationException ->
        true

[<Fact>]
let ``valid DU: tryFromUnion Ok and fromUnion does not throw`` () =
    let isOk =
        match CommandReflection.tryFromUnion<EqCommand> "Test" with
        | Ok _ -> true
        | Error _ -> false

    let didThrow =
        throws (fun () -> CommandReflection.fromUnion<EqCommand> "Test" |> ignore)

    test <@ isOk @>
    test <@ not didThrow @>
    test <@ isOk = not didThrow @>

[<Fact>]
let ``invalid DU: tryFromUnion Error and fromUnion throws`` () =
    let isOk =
        match CommandReflection.tryFromUnion<BadField2> "Test" with
        | Ok _ -> true
        | Error _ -> false

    let didThrow =
        throws (fun () -> CommandReflection.fromUnion<BadField2> "Test" |> ignore)

    test <@ not isOk @>
    test <@ didThrow @>
    test <@ isOk = not didThrow @>

[<Fact>]
let ``invalid globals spec: tryFromUnionWithGlobals Error and fromUnionWithGlobals throws`` () =
    let isOk =
        match CommandReflection.tryFromUnionWithGlobals<CombinedCmd, CombinedGlobal> "Test" with
        | Ok _ -> true
        | Error _ -> false

    let didThrow =
        throws (fun () ->
            CommandReflection.fromUnionWithGlobals<CombinedCmd, CombinedGlobal> "Test"
            |> ignore)

    test <@ not isOk @>
    test <@ didThrow @>

[<Fact>]
let ``throwing wrapper message aggregates all problems`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnion<BadField2> "Test" |> ignore)

    // Both unsupported fields named in one message.
    test <@ ex.Message.Contains("when_") @>
    test <@ ex.Message.Contains("span") @>
    test <@ ex.Message.Contains("2 problems found") @>

// =============================================================================
// List-placement error mapping (the single base throw split into two cases)
// =============================================================================

type EqListNotLast = Bad of files: string list * tag: string

type EqMultipleLists = Bad of files: string list * more: string list

[<Fact>]
let ``tryFromUnion maps list-field-not-last to ListFieldNotLast`` () =
    match CommandReflection.tryFromUnion<EqListNotLast> "Test" with
    | Error [ ListFieldNotLast("bad", "files") ] -> ()
    | other -> failwith $"Expected single ListFieldNotLast, got %A{other}"

[<Fact>]
let ``tryFromUnion maps multiple list fields to MultipleListFields`` () =
    match CommandReflection.tryFromUnion<EqMultipleLists> "Test" with
    | Error [ MultipleListFields "bad" ] -> ()
    | other -> failwith $"Expected single MultipleListFields, got %A{other}"
