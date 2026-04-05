module CommandTree.Tests.ParsingTests

open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Test command types
// =============================================================================

type SimpleCommand =
    | Check
    | Build
    | Test

type CommandWithArgs =
    | Greet of name: string
    | Add of x: int * y: int
    | Maybe of value: string option

type DevCommand =
    | [<CmdDefault>] Check
    | Build
    | Test

type RootCommand =
    | [<CmdDefault>] Dev of DevCommand
    | Help

// Types for root-level default parse edge cases

type SimpleDefaultCommand =
    | [<CmdDefault>] Status
    | Run of file: string

type InnerNoDefault =
    | Alpha
    | Beta

type DefaultWrapsNoInnerDefault =
    | [<CmdDefault>] Inner of InnerNoDefault
    | Other

type DefaultWithNonUnionArg =
    | [<CmdDefault>] Run of file: string
    | Help

type InnerWithArgDefault =
    | [<CmdDefault>] Execute of count: int
    | Stop

type DefaultWrapsArgInnerDefault =
    | [<CmdDefault>] Inner of InnerWithArgDefault
    | Other

// Types for list field tests

type ListArgCommand =
    | [<Cmd("Tag files")>] Tag of tag: string * files: string list
    | [<Cmd("List items")>] List

type IntListCommand = | [<Cmd("Sum numbers")>] Sum of values: int list

type ListOnlyCommand = | [<Cmd("Run files")>] Run of files: string list

// Types for ambiguous argument tests

type AmbiguousAction =
    | Start
    | Stop
    | Status

type AmbiguousCmd = Do of action: AmbiguousAction * count: int

type ListAmbiguousCommand = | [<Cmd("Do actions")>] DoActions of actions: AmbiguousAction list

// Types for flag parsing tests

type DeployDUFlag =
    | DryRun
    | Env of string
    | Verbose

type DUFlagCommand =
    | [<Cmd("Deploy")>] Deploy of DeployDUFlag list
    | [<Cmd("Help")>] Help

// Types for global flag tests

type GlobalFlag =
    | Verbose
    | LogLevel of string

type GlobalCmd =
    | [<Cmd("Start")>] Start
    | [<Cmd("Scan")>] Scan

// Types for flag collision detection tests

type CollidingGlobal = Timeout of int

type CollidingScanFlag =
    | Timeout of int
    | Watch

type CollidingCmd = | [<Cmd("Scan")>] Scan of CollidingScanFlag list

// Types for combined global + command flag tests

type ScanDUFlag =
    | Watch
    | Timeout of int

type GlobalWithCmdFlagCmd =
    | [<Cmd("Scan")>] Scan of ScanDUFlag list
    | [<Cmd("Start")>] Start

// Types for group-with-no-default error paths

type SubNoDefault =
    | Alpha
    | Beta

type NestNoDefault =
    | Inner of SubNoDefault
    | Other

// =============================================================================
// Simple parsing tests
// =============================================================================

[<Fact>]
let ``parse handles simple command`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "check" |]
    Assert.Equal(Ok SimpleCommand.Check, result)

[<Fact>]
let ``parse handles unknown command`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "unknown" |]
    test <@ result = Error(UnknownCommand("unknown", [])) @>

// =============================================================================
// Argument parsing tests
// =============================================================================

[<Fact>]
let ``parse handles string argument`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.parse tree [| "greet"; "World" |]
    Assert.Equal(Ok(CommandWithArgs.Greet "World"), result)

[<Fact>]
let ``parse handles int arguments`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.parse tree [| "add"; "1"; "2" |]
    Assert.Equal(Ok(CommandWithArgs.Add(1, 2)), result)

[<Fact>]
let ``parse handles optional argument present`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.parse tree [| "maybe"; "hello" |]
    Assert.Equal(Ok(CommandWithArgs.Maybe(Some "hello")), result)

[<Fact>]
let ``parse handles optional argument missing`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.parse tree [| "maybe" |]
    Assert.Equal(Ok(CommandWithArgs.Maybe None), result)

// =============================================================================
// Nested command parsing tests
// =============================================================================

[<Fact>]
let ``parse handles nested command`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "build" |]
    Assert.Equal(Ok(RootCommand.Dev DevCommand.Build), result)

[<Fact>]
let ``parse uses default for nested group`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    // "dev" alone should use DevCommand.Check (the default)
    let result = CommandTree.parse tree [| "dev" |]
    Assert.Equal(Ok(RootCommand.Dev DevCommand.Check), result)

[<Fact>]
let ``parse uses root default when no args`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [||]

    match result with
    | Ok(RootCommand.Dev DevCommand.Check) -> ()
    | Ok(RootCommand.Dev(DevCommand.Build | DevCommand.Test)) -> failwith "Expected default command"
    | Ok RootCommand.Help -> failwith "Expected default command"
    | Error err -> failwith $"Expected default command, got error: %O{err}"

// =============================================================================
// Unknown command tests (with defaults present)
// =============================================================================

[<Fact>]
let ``parse rejects unknown root command even with default`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "devv" |]
    test <@ result = Error(UnknownCommand("devv", [])) @>

[<Fact>]
let ``parse rejects unknown subcommand even with default`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "chekc" |]
    test <@ result = Error(UnknownCommand("chekc", [ "dev" ])) @>

// =============================================================================
// Closest help path tests
// =============================================================================

[<Fact>]
let ``closestGroupPath returns empty for misspelled root command`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let path = CommandTree.closestGroupPath tree [ "devv" ]
    test <@ path |> List.isEmpty @>

[<Fact>]
let ``closestGroupPath returns group path for misspelled subcommand`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let path = CommandTree.closestGroupPath tree [ "dev"; "chekc" ]
    test <@ path = [ "dev" ] @>

[<Fact>]
let ``closest help for misspelled subcommand shows group commands`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let path = CommandTree.closestGroupPath tree [ "dev"; "chekc" ]
    let helpText = CommandTree.helpForPath tree path "cmd"
    // Should show dev's subcommands, not root commands
    test <@ helpText.Contains("check") @>
    test <@ helpText.Contains("build") @>
    test <@ helpText.Contains("test") @>
    test <@ helpText.Contains("cmd dev") @>

[<Fact>]
let ``closest help for misspelled root command shows root commands`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let path = CommandTree.closestGroupPath tree [ "devv" ]
    let helpText = CommandTree.helpForPath tree path "cmd"
    // Should show root commands
    test <@ helpText.Contains("dev") @>
    test <@ helpText.Contains("help") @>

// =============================================================================
// Help generation tests
// =============================================================================

[<Fact>]
let ``help includes command names`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let helpText = CommandTree.help tree [] "test"

    test <@ helpText.Contains("check") @>
    test <@ helpText.Contains("build") @>
    test <@ helpText.Contains("test") @>

[<Fact>]
let ``help includes argument names`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let helpText = CommandTree.help tree [] "test"

    // Argument names derived from field names
    test <@ helpText.Contains("<name>") @>
    test <@ helpText.Contains("<x>") @>
    test <@ helpText.Contains("<y>") @>
    // Optional args shown with brackets
    test <@ helpText.Contains("[value]") @>

[<Fact>]
let ``helpFull expands nested commands`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let helpText = CommandTree.helpFull tree "cmd"

    test <@ helpText.Contains("dev") @>
    test <@ helpText.Contains("check") @>
    test <@ helpText.Contains("build") @>

// =============================================================================
// Format tests
// =============================================================================

[<Fact>]
let ``format returns command string`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.format tree SimpleCommand.Check [] "cmd"
    Assert.Equal(Some "cmd check", result)

[<Fact>]
let ``format includes arguments`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.format tree (CommandWithArgs.Greet "World") [] "cmd"
    Assert.Equal(Some "cmd greet World", result)

[<Fact>]
let ``format handles nested commands`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.format tree (RootCommand.Dev DevCommand.Build) [] "cmd"
    Assert.Equal(Some "cmd dev build", result)

// =============================================================================
// Fish completions tests
// =============================================================================

[<Fact>]
let ``fishCompletions generates completions`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"

    test <@ completions.Contains("complete -c test") @>
    test <@ completions.Contains("check") @>
    test <@ completions.Contains("build") @>

// =============================================================================
// Root-level default parse edge cases
// =============================================================================

[<Fact>]
let ``parse uses zero-field root default`` () =
    let tree = CommandReflection.fromUnion<SimpleDefaultCommand> "Test"
    let result = CommandTree.parse tree [||]
    Assert.Equal(Ok SimpleDefaultCommand.Status, result)

[<Fact>]
let ``parse returns error when nested group has no inner default`` () =
    let tree = CommandReflection.fromUnion<DefaultWrapsNoInnerDefault> "Test"
    let result = CommandTree.parse tree [||]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse returns error when root default has non-union argument`` () =
    let tree = CommandReflection.fromUnion<DefaultWithNonUnionArg> "Test"
    let result = CommandTree.parse tree [||]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse returns error when nested default requires args not provided`` () =
    let tree = CommandReflection.fromUnion<DefaultWrapsArgInnerDefault> "Test"
    let result = CommandTree.parse tree [||]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse returns help error when root group has no default and no args`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [||]
    test <@ result = Error(HelpRequested []) @>

[<Fact>]
let ``parse returns help error when nested group has no default and no args`` () =
    let tree = CommandReflection.fromUnion<NestNoDefault> "Test"
    let result = CommandTree.parse tree [| "inner" |]
    test <@ result = Error(HelpRequested [ "inner" ]) @>

// =============================================================================
// Ambiguous argument tests (through parse)
// =============================================================================

[<Fact>]
let ``parse returns AmbiguousArgument with correct input and candidates`` () =
    let tree = CommandReflection.fromUnion<AmbiguousCmd> "Test"
    // "sta" matches both "start" and "status"
    let result = CommandTree.parse tree [| "do"; "sta"; "1" |]

    match result with
    | Error(AmbiguousArgument(input, candidates)) ->
        test <@ input = "sta" @>
        test <@ candidates = [ "start"; "status" ] @>
    | Ok cmd -> failwith $"Expected AmbiguousArgument, got Ok: %O{cmd}"
    | Error err -> failwith $"Expected AmbiguousArgument, got Error: %O{err}"

[<Fact>]
let ``help generates help for leaf node`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let helpText = CommandTree.help tree [ "greet" ] "cmd"
    test <@ helpText.Contains("greet") @>
    test <@ helpText.Contains("<name>") @>

// =============================================================================
// findByPath edge cases
// =============================================================================

[<Fact>]
let ``findByPath returns None when descending past a leaf`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    // "help" is a leaf; trying to descend further returns None
    let result = CommandTree.findByPath tree [ "help"; "extra" ]
    test <@ result.IsNone @>

[<Fact>]
let ``findByPath returns Some for valid group path`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.findByPath tree [ "dev" ]
    test <@ result.IsSome @>

// =============================================================================
// helpForPath edge cases
// =============================================================================

[<Fact>]
let ``helpForPath falls back to root help for invalid path`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "nonexistent"; "path" ] "cmd"
    // Falls back to root help
    test <@ helpText.Contains("dev") @>
    test <@ helpText.Contains("help") @>

// =============================================================================
// closestGroupPath edge cases
// =============================================================================

[<Fact>]
let ``closestGroupPath returns full path when all segments are groups`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    // "dev" is a valid group, no extra segments
    let path = CommandTree.closestGroupPath tree [ "dev" ]
    test <@ path = [ "dev" ] @>

// =============================================================================
// Nested group default parse error paths
// =============================================================================

[<Fact>]
let ``parse returns error when nested group default requires missing args`` () =
    // DefaultWrapsArgInnerDefault: Inner wraps InnerWithArgDefault
    // InnerWithArgDefault's default is Execute of count: int
    // Parsing "inner" with no further args triggers the group-level default
    // which fails because count is missing
    let tree = CommandReflection.fromUnion<DefaultWrapsArgInnerDefault> "Test"
    let result = CommandTree.parse tree [| "inner" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

// =============================================================================
// List field parsing tests
// =============================================================================

[<Fact>]
let ``parse handles list field collecting remaining args`` () =
    let tree = CommandReflection.fromUnion<ListArgCommand> "Test"
    let result = CommandTree.parse tree [| "tag"; "v1"; "a.fs"; "b.fs" |]
    Assert.Equal(Ok(ListArgCommand.Tag("v1", [ "a.fs"; "b.fs" ])), result)

[<Fact>]
let ``parse handles list field with single element`` () =
    let tree = CommandReflection.fromUnion<ListArgCommand> "Test"
    let result = CommandTree.parse tree [| "tag"; "v1"; "file.fs" |]
    Assert.Equal(Ok(ListArgCommand.Tag("v1", [ "file.fs" ])), result)

[<Fact>]
let ``parse rejects list field with no elements`` () =
    let tree = CommandReflection.fromUnion<ListArgCommand> "Test"
    let result = CommandTree.parse tree [| "tag"; "v1" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse handles list field with int elements`` () =
    let tree = CommandReflection.fromUnion<IntListCommand> "Test"
    let result = CommandTree.parse tree [| "sum"; "1"; "2"; "3" |]
    Assert.Equal(Ok(IntListCommand.Sum [ 1; 2; 3 ]), result)

[<Fact>]
let ``parse rejects list field with invalid element type`` () =
    let tree = CommandReflection.fromUnion<IntListCommand> "Test"
    let result = CommandTree.parse tree [| "sum"; "1"; "abc"; "3" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse returns error when list element is ambiguous union`` () =
    let tree = CommandReflection.fromUnion<ListAmbiguousCommand> "Test"
    // "sta" matches both "start" and "status"
    let result = CommandTree.parse tree [| "do-actions"; "sta" |]

    match result with
    | Error(AmbiguousArgument(input, candidates)) ->
        test <@ input = "sta" @>
        test <@ candidates = [ "start"; "status" ] @>
    | other -> failwith $"Expected AmbiguousArgument, got: %O{other}"

[<Fact>]
let ``parse handles list-only command`` () =
    let tree = CommandReflection.fromUnion<ListOnlyCommand> "Test"
    let result = CommandTree.parse tree [| "run"; "a.fs"; "b.fs" |]
    Assert.Equal(Ok(ListOnlyCommand.Run [ "a.fs"; "b.fs" ]), result)

[<Fact>]
let ``parse rejects list-only command with no args`` () =
    let tree = CommandReflection.fromUnion<ListOnlyCommand> "Test"
    let result = CommandTree.parse tree [| "run" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``help shows list field with ellipsis`` () =
    let tree = CommandReflection.fromUnion<ListArgCommand> "Test"
    let helpText = CommandTree.help tree [] "test"
    test <@ helpText.Contains("<files...>") @>

// =============================================================================
// UnknownFlag and DuplicateFlag error tests
// =============================================================================

[<Fact>]
let ``UnknownFlag error carries flag name, command, and valid flags`` () =
    let err = UnknownFlag("--foo", "deploy", [ "--env"; "--config"; "--dry-run" ])

    match err with
    | UnknownFlag(flag, cmd, valid) ->
        test <@ flag = "--foo" @>
        test <@ cmd = "deploy" @>
        test <@ valid = [ "--env"; "--config"; "--dry-run" ] @>
    | _ -> failwith "Expected UnknownFlag"

[<Fact>]
let ``DuplicateFlag error carries flag name and command`` () =
    let err = DuplicateFlag("--config", "deploy")

    match err with
    | DuplicateFlag(flag, cmd) ->
        test <@ flag = "--config" @>
        test <@ cmd = "deploy" @>
    | _ -> failwith "Expected DuplicateFlag"

// =============================================================================
// DU-based flag parsing tests
// =============================================================================

[<Fact>]
let ``parse handles DU flags in any order`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--dry-run"; "--env"; "prod" |]

    match result with
    | Ok(DUFlagCommand.Deploy flags) ->
        test <@ flags |> List.contains DeployDUFlag.DryRun @>

        test
            <@
                flags
                |> List.exists (function
                    | DeployDUFlag.Env "prod" -> true
                    | _ -> false)
            @>

        test
            <@
                flags
                |> List.exists (function
                    | DeployDUFlag.Verbose -> true
                    | _ -> false)
                |> not
            @>
    | other -> failwith $"Expected Deploy, got: %O{other}"

[<Fact>]
let ``parse handles DU flags with no flags`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy" |]

    match result with
    | Ok(DUFlagCommand.Deploy flags) -> test <@ flags = [] @>
    | other -> failwith $"Expected Deploy with empty flags, got: %O{other}"

[<Fact>]
let ``parse handles DU short flags`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "-d" |]

    match result with
    | Ok(DUFlagCommand.Deploy flags) -> test <@ flags |> List.contains DeployDUFlag.DryRun @>
    | other -> failwith $"Expected Deploy with DryRun, got: %O{other}"

[<Fact>]
let ``parse returns UnknownFlag for unrecognized DU flag`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--foo" |]

    match result with
    | Error(UnknownFlag(flag, cmd, _)) ->
        test <@ flag = "--foo" @>
        test <@ cmd = "deploy" @>
    | other -> failwith $"Expected UnknownFlag, got: %O{other}"

[<Fact>]
let ``parse returns DuplicateFlag for repeated DU flag`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--env"; "a"; "--env"; "b" |]

    match result with
    | Error(DuplicateFlag(flag, cmd)) ->
        test <@ flag = "--env" @>
        test <@ cmd = "deploy" @>
    | other -> failwith $"Expected DuplicateFlag, got: %O{other}"

[<Fact>]
let ``parse returns InvalidArguments when DU flag value missing`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--env" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

// =============================================================================
// DU-based flag help display tests
// =============================================================================

[<Fact>]
let ``help shows DU flags with long and short names`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "deploy" ] "test"
    test <@ helpText.Contains("[options]") @>
    test <@ helpText.Contains("--env") @>
    test <@ helpText.Contains("--dry-run") @>
    test <@ helpText.Contains("--verbose") @>

// =============================================================================
// DU-based flag format roundtrip tests
// =============================================================================

[<Fact>]
let ``format roundtrip for DU flag command`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let cmd = DUFlagCommand.Deploy [ DeployDUFlag.Env "prod"; DeployDUFlag.DryRun ]
    let result = CommandTree.format tree cmd [] "test"
    test <@ result = Some "test deploy --env prod --dry-run" @>

[<Fact>]
let ``format roundtrip for DU flag command with no flags`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let cmd = DUFlagCommand.Deploy []
    let result = CommandTree.format tree cmd [] "test"
    test <@ result = Some "test deploy" @>

[<Fact>]
let ``formatCmd handles DU flag command`` () =
    let cmd = DUFlagCommand.Deploy [ DeployDUFlag.Env "prod"; DeployDUFlag.DryRun ]
    let result = CommandReflection.formatCmd cmd
    test <@ result = "deploy --env prod --dry-run" @>

// =============================================================================
// Global flag parsing tests
// =============================================================================

[<Fact>]
let ``global flags parsed before command`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "--verbose"; "scan" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>
        test <@ cmd = GlobalCmd.Scan @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``global flags parsed after command`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "scan"; "--verbose" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>
        test <@ cmd = GlobalCmd.Scan @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``global flags interleaved with command`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "--verbose"; "scan"; "--log-level"; "debug" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>

        test
            <@
                globals
                |> List.exists (function
                    | GlobalFlag.LogLevel "debug" -> true
                    | _ -> false)
            @>

        test <@ cmd = GlobalCmd.Scan @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``no global flags returns empty list`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "start" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals = [] @>
        test <@ cmd = GlobalCmd.Start @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``global flag duplicate rejected`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "--verbose"; "scan"; "--verbose" |]

    match result with
    | Error(DuplicateFlag(flag, _)) -> test <@ flag = "--verbose" @>
    | other -> failwith $"Expected DuplicateFlag, got: %O{other}"

[<Fact>]
let ``global flag unknown rejected`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test"
    let result = spec.Parse [| "--unknown"; "scan" |]
    // Unknown flags that aren't global should pass through to command parsing
    // which will then report UnknownCommand since --unknown isn't a command name
    match result with
    | Error _ -> ()
    | other -> failwith $"Expected Error, got: %O{other}"

// =============================================================================
// Flag collision detection tests
// =============================================================================

[<Fact>]
let ``fromUnionWithGlobals rejects duplicate flag names`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnionWithGlobals<CollidingCmd, CollidingGlobal> "Test"
            |> ignore)

    test <@ ex.Message.Contains("--timeout") @>

// =============================================================================
// Combined global and per-command flag tests
// =============================================================================

[<Fact>]
let ``global and command flags both parsed`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalWithCmdFlagCmd, GlobalFlag> "Test"

    let result =
        spec.Parse [| "--verbose"; "scan"; "--watch"; "--timeout"; "30"; "--log-level"; "debug" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>

        test
            <@
                globals
                |> List.exists (function
                    | GlobalFlag.LogLevel "debug" -> true
                    | _ -> false)
            @>

        match cmd with
        | GlobalWithCmdFlagCmd.Scan flags ->
            test <@ flags |> List.contains ScanDUFlag.Watch @>

            test
                <@
                    flags
                    |> List.exists (function
                        | ScanDUFlag.Timeout 30 -> true
                        | _ -> false)
                @>
        | other -> failwith $"Expected Scan, got: %O{other}"
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``global flags work with command that has no flags`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalWithCmdFlagCmd, GlobalFlag> "Test"

    let result = spec.Parse [| "--verbose"; "start" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>
        test <@ cmd = GlobalWithCmdFlagCmd.Start @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``command flags after global flags work`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalWithCmdFlagCmd, GlobalFlag> "Test"

    let result = spec.Parse [| "scan"; "--watch"; "--verbose" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>

        match cmd with
        | GlobalWithCmdFlagCmd.Scan flags -> test <@ flags |> List.contains ScanDUFlag.Watch @>
        | other -> failwith $"Expected Scan, got: %O{other}"
    | Error e -> failwith $"Expected Ok, got: %O{e}"
