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

// Types for positional + flag-DU list tests (AUTOMATION-187)

type RemoveFlag =
    | Force
    | KeepBranch

type PositionalWithFlagDUCommand = | [<Cmd("Remove a workspace")>] Remove of name: string * flags: RemoveFlag list

type OptionalPositionalFlagDUCommand =
    | [<Cmd("Optional-name command")>] Opt of name: string option * flags: RemoveFlag list

type PublishFlag =
    | Latest
    | Tag of string

type ValuePositionalFlagDUCommand = | [<Cmd("Publish a target")>] Publish of target: string * flags: PublishFlag list

type MixedEnvFlag = | Notify

type MixedEnvCommand = | [<Cmd("Ship a target")>] Ship of target: string * flags: MixedEnvFlag list

type BadPositionalFlagDUCommand = | [<Cmd("Bad")>] Bad of stamp: System.DateTime * flags: RemoveFlag list

type MultiPositionalFlagDUCommand =
    | [<Cmd("Move a workspace")>] Move of src: string * dest: string * flags: RemoveFlag list

type TypedPositionalFlagDUCommand = | [<Cmd("Scale a target")>] Scale of count: int * flags: RemoveFlag list

// Types for global flag tests

type GlobalFlag =
    | Verbose
    | LogLevel of string

type GlobalCmd =
    | [<Cmd("Start")>] Start
    | [<Cmd("Scan")>] Scan

// Types for global --help override tests

type GlobalWithHelp =
    | [<Cmd("Show help")>] Help
    | Verbose

type GlobalHelpCmd =
    | [<Cmd("Start")>] Start
    | [<Cmd("Scan")>] Scan

// Types for flag collision detection tests

type CollidingGlobal = Timeout of int

type CollidingScanFlag =
    | Timeout of int
    | Watch

type CollidingCmd = | [<Cmd("Scan")>] Scan of CollidingScanFlag list

// Types for short-name flag collision detection tests

type ShortCollidingGlobal = | [<CmdFlag(Short = "t")>] Trace

type ShortCollidingLeafFlag = | [<CmdFlag(Short = "t")>] Terse

type ShortCollidingCmd = | [<Cmd("Scan")>] Scan of ShortCollidingLeafFlag list

// Types for flag with no short name (collision avoidance suppresses short)

type NoShortFlag =
    | Timeout of int
    | Trace

type NoShortCmd = | [<Cmd("Run")>] Run of NoShortFlag list

type NoShortGlobal = | Debug

// Types for global flag with typed field (invalid value tests)

type TypedGlobalFlag =
    | Count of int
    | Verbose

type TypedGlobalCmd =
    | [<Cmd("Start")>] Start
    | [<Cmd("Scan")>] Scan

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

// Types for --version override tests

type VersionOverrideCommand =
    | [<Cmd("Run")>] Run
    | [<Cmd("Show version")>] Version

// Types for flags with short names (help display)

type ShortNameFlag =
    | [<CmdFlag(Short = "v")>] Verbose
    | [<CmdFlag(Short = "d")>] DryRun

type ShortNameFlagCmd = | [<Cmd("Deploy")>] Deploy of ShortNameFlag list

// Types for --help override tests

type HelpOverrideFlag =
    | [<Cmd("Show help")>] Help
    | Verbose

type HelpOverrideCmd = | [<Cmd("Run")>] Run of HelpOverrideFlag list

// Types for record-typed argument tests

type RecordOptions = { publish: bool }

type RecordCommand =
    | [<Cmd("Alpha command")>] Alpha of RecordOptions
    | [<Cmd("Beta command")>] Beta

type RecordWithRequired = { target: string; publish: bool }

type RecordReqCommand = | [<Cmd("Deploy command")>] Deploy of RecordWithRequired

type RecordWithOptional = { name: string option; verbose: bool }

type RecordOptCommand = | [<Cmd("Run command")>] Run of RecordWithOptional

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
    test <@ result = Error(UnknownCommand("unknown", [||], [])) @>

[<Fact>]
let ``parse unknown command carries raw remaining args`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "frobnicate"; "--all"; "x" |]
    test <@ result = Error(UnknownCommand("frobnicate", [| "--all"; "x" |], [])) @>

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
    test <@ result = Error(UnknownCommand("devv", [||], [])) @>

[<Fact>]
let ``parse rejects unknown subcommand even with default`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "chekc" |]
    test <@ result = Error(UnknownCommand("chekc", [||], [ "dev" ])) @>

[<Fact>]
let ``parse unknown nested subcommand carries raw remaining args`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "chekc"; "--fast" |]
    test <@ result = Error(UnknownCommand("chekc", [| "--fast" |], [ "dev" ])) @>

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
    let result = CommandTree.format tree SimpleCommand.Check "cmd"
    Assert.Equal(Some "cmd check", result)

[<Fact>]
let ``format includes arguments`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let result = CommandTree.format tree (CommandWithArgs.Greet "World") "cmd"
    Assert.Equal(Some "cmd greet World", result)

[<Fact>]
let ``format handles nested commands`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.format tree (RootCommand.Dev DevCommand.Build) "cmd"
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
// --help flag recognition tests
// =============================================================================

[<Fact>]
let ``parse returns HelpRequested when --help passed at root`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "--help" |]
    test <@ result = Error(HelpRequested []) @>

[<Fact>]
let ``parse returns HelpRequested when --help mixed with command at root`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "--help"; "check" |]
    test <@ result = Error(HelpRequested []) @>

[<Fact>]
let ``parse returns HelpRequested when --help passed at nested group`` () =
    let tree = CommandReflection.fromUnion<NestNoDefault> "Test"
    let result = CommandTree.parse tree [| "inner"; "--help" |]
    test <@ result = Error(HelpRequested [ "inner" ]) @>

[<Fact>]
let ``parse returns HelpRequested when --help passed at leaf`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "check"; "--help" |]
    test <@ result = Error(HelpRequested [ "check" ]) @>

[<Fact>]
let ``parse returns HelpRequested when --help passed at group with default`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "--help" |]
    test <@ result = Error(HelpRequested [ "dev" ]) @>

[<Fact>]
let ``parse passes --help to leaf parser when leaf has explicit help flag`` () =
    let tree = CommandReflection.fromUnion<HelpOverrideCmd> "Test"
    let result = CommandTree.parse tree [| "run"; "--help" |]

    match result with
    | Ok(HelpOverrideCmd.Run flags) ->
        test
            <@
                flags
                |> List.exists (function
                    | HelpOverrideFlag.Help -> true
                    | _ -> false)
            @>
    | other -> failwith $"Expected Ok with Help flag, got: %O{other}"

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
let ``parse DU list as flags — unknown input is UnknownFlag`` () =
    let tree = CommandReflection.fromUnion<ListAmbiguousCommand> "Test"
    // "sta" is not a valid flag (needs --start or --status)
    let result = CommandTree.parse tree [| "do-actions"; "sta" |]

    match result with
    | Error(UnknownFlag("sta", _, _)) -> ()
    | other -> failwith $"Expected UnknownFlag, got: %O{other}"

[<Fact>]
let ``parse DU list as flags — valid flags are accepted`` () =
    let tree = CommandReflection.fromUnion<ListAmbiguousCommand> "Test"
    let result = CommandTree.parse tree [| "do-actions"; "--start"; "--stop" |]

    match result with
    | Ok(ListAmbiguousCommand.DoActions flags) -> test <@ flags.Length = 2 @>
    | other -> failwith $"Expected Ok(DoActions), got: %O{other}"

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
    | Ok(DUFlagCommand.Deploy flags) -> test <@ List.isEmpty flags @>
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
// Positional + flag-DU list parsing tests (AUTOMATION-187)
// =============================================================================

[<Fact>]
let ``parse binds positional and empty flag list for flagless invocation`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "some-value" |]

    match result with
    | Ok(PositionalWithFlagDUCommand.Remove(name, flags)) ->
        test <@ name = "some-value" @>
        test <@ List.isEmpty flags @>
    | other -> failwith $"Expected Ok(Remove) with positional bound and empty flags, got: %A{other}"

[<Fact>]
let ``parse accepts flag after positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "some-value"; "--force" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force ])), result)

[<Fact>]
let ``parse accepts flag before positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "--force"; "some-value" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force ])), result)

[<Fact>]
let ``parse accepts short flag with positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "some-value"; "-f" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force ])), result)

[<Fact>]
let ``parse binds multiple flags around positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"

    let result =
        CommandTree.parse tree [| "remove"; "--force"; "some-value"; "--keep-branch" |]

    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force; KeepBranch ])), result)

[<Fact>]
let ``parse names the missing required positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "--force" |]
    Assert.Equal(Error(InvalidArguments("remove", "Missing required argument '<name>'")), result)

[<Fact>]
let ``parse rejects extra positional on flag-DU leaf`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "a"; "b" |]
    Assert.Equal(Error(InvalidArguments("remove", "Unexpected argument 'b'")), result)

[<Fact>]
let ``parse returns UnknownFlag for unknown dash token beside positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "some-value"; "--typo" |]
    Assert.Equal(Error(UnknownFlag("--typo", "remove", [ "--force"; "--keep-branch" ])), result)

[<Fact>]
let ``parse returns DuplicateFlag on flag-DU leaf with positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "v"; "--force"; "--force" |]
    Assert.Equal(Error(DuplicateFlag("--force", "remove")), result)

[<Fact>]
let ``parse treats bare dash as positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "-"; "--force" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("-", [ Force ])), result)

[<Fact>]
let ``parse binds optional positional to None when omitted`` () =
    let tree = CommandReflection.fromUnion<OptionalPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "opt" |]
    Assert.Equal(Ok(OptionalPositionalFlagDUCommand.Opt(None, [])), result)

[<Fact>]
let ``parse binds flags with optional positional omitted`` () =
    let tree = CommandReflection.fromUnion<OptionalPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "opt"; "--force" |]
    Assert.Equal(Ok(OptionalPositionalFlagDUCommand.Opt(None, [ Force ])), result)

[<Fact>]
let ``parse binds optional positional beside flags`` () =
    let tree = CommandReflection.fromUnion<OptionalPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "opt"; "--force"; "v" |]
    Assert.Equal(Ok(OptionalPositionalFlagDUCommand.Opt(Some "v", [ Force ])), result)

[<Fact>]
let ``value flag consumes next token instead of positional slot`` () =
    let tree = CommandReflection.fromUnion<ValuePositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "publish"; "--tag"; "v2"; "api" |]
    Assert.Equal(Ok(ValuePositionalFlagDUCommand.Publish("api", [ PublishFlag.Tag "v2" ])), result)

[<Fact>]
let ``value and bool flags mix with positional`` () =
    let tree = CommandReflection.fromUnion<ValuePositionalFlagDUCommand> "Test"

    let result =
        CommandTree.parse tree [| "publish"; "api"; "--tag"; "v2"; "--latest" |]

    Assert.Equal(Ok(ValuePositionalFlagDUCommand.Publish("api", [ PublishFlag.Tag "v2"; PublishFlag.Latest ])), result)

[<Fact>]
let ``env var flag merges on positional flag-DU leaf`` () =
    System.Environment.SetEnvironmentVariable("MIXEDSHIP_NOTIFY", "true")

    try
        let tree = CommandReflection.fromUnionWithEnv<MixedEnvCommand> "Test" "MIXEDSHIP"
        let result = CommandTree.parse tree [| "ship"; "prod" |]
        Assert.Equal(Ok(MixedEnvCommand.Ship("prod", [ Notify ])), result)
    finally
        System.Environment.SetEnvironmentVariable("MIXEDSHIP_NOTIFY", null)

[<Fact>]
let ``invalid env var value on positional flag-DU leaf returns error`` () =
    System.Environment.SetEnvironmentVariable("MIXEDSHIP_NOTIFY", "notabool")

    try
        let tree = CommandReflection.fromUnionWithEnv<MixedEnvCommand> "Test" "MIXEDSHIP"
        let result = CommandTree.parse tree [| "ship"; "prod" |]

        match result with
        | Error(InvalidArguments("env", msg)) -> test <@ msg.Contains("MIXEDSHIP_NOTIFY") @>
        | other -> failwith $"Expected env error, got: %A{other}"
    finally
        System.Environment.SetEnvironmentVariable("MIXEDSHIP_NOTIFY", null)

[<Fact>]
let ``unsupported positional type beside flag-DU list is a spec error`` () =
    let result = CommandReflection.tryFromUnion<BadPositionalFlagDUCommand> "Test"

    match result with
    | Error [ SpecError.UnsupportedFieldType("bad", "stamp", _) ] -> ()
    | other -> failwith $"Expected UnsupportedFieldType spec error, got: %A{other}"

// =============================================================================
// POSIX `--` end-of-flags separator tests (AUTOMATION-187)
// =============================================================================

[<Fact>]
let ``separator binds dash token as positional`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "--"; "--force" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("--force", [])), result)

[<Fact>]
let ``flags before separator still parse`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "--force"; "--"; "some-value" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force ])), result)

[<Fact>]
let ``extra token after separator is an unexpected argument`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "a"; "--"; "b" |]
    Assert.Equal(Error(InvalidArguments("remove", "Unexpected argument 'b'")), result)

[<Fact>]
let ``separator on zero-positional flag-DU leaf rejects extras`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--"; "x" |]
    Assert.Equal(Error(InvalidArguments("deploy", "Unexpected argument 'x'")), result)

[<Fact>]
let ``help flag after separator is a positional value`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "--"; "--help" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("--help", [])), result)

[<Fact>]
let ``trailing separator with empty tail keeps the positional`` () =
    // `--` as the very last token: the after-separator slice is empty, and the
    // flag already scanned before it is retained.
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "remove"; "some-value"; "--force"; "--" |]
    Assert.Equal(Ok(PositionalWithFlagDUCommand.Remove("some-value", [ Force ])), result)

[<Fact>]
let ``lone separator on zero-positional leaf yields empty flags`` () =
    // `--` is the only token on a flagless-invoked zero-positional leaf: both the
    // before- and after-separator slices are empty.
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "--" |]
    Assert.Equal(Ok(DUFlagCommand.Deploy []), result)

[<Fact>]
let ``global flag scan stops at separator`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalWithCmdFlagCmd, GlobalFlag> "Test"

    let result = spec.Parse [| "scan"; "--watch"; "--"; "--verbose" |]
    Assert.Equal(Error(InvalidArguments("scan", "Unexpected argument '--verbose'")), result)

[<Fact>]
let ``global flag scan with trailing separator forwards empty tail`` () =
    // `--` last in argv: the global scan's after-separator slice is empty, so the
    // command still parses with its flag intact.
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalWithCmdFlagCmd, GlobalFlag> "Test"

    let result = spec.Parse [| "scan"; "--watch"; "--" |]

    match result with
    | Ok(_, GlobalWithCmdFlagCmd.Scan flags) -> test <@ flags = [ ScanDUFlag.Watch ] @>
    | other -> failwith $"Expected Ok(Scan [Watch]), got: %A{other}"

// =============================================================================
// Positional + flag-DU help / completion / format tests (AUTOMATION-187)
// =============================================================================

[<Fact>]
let ``help shows positional and options for flag-DU leaf`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "remove" ] "test"
    test <@ helpText.Contains("Usage: test remove <name> [options]") @>
    test <@ helpText.Contains("--force") @>
    test <@ helpText.Contains("--keep-branch") @>

[<Fact>]
let ``fishCompletions include flags for positional flag-DU leaf`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let completions = CommandTree.fishCompletions tree "test"
    test <@ completions.Contains("-l force") @>
    test <@ completions.Contains("-l keep-branch") @>

[<Fact>]
let ``formatCmd renders positional then flags including all-nullary flag DU`` () =
    let cmd = PositionalWithFlagDUCommand.Remove("x", [ Force ])
    test <@ CommandReflection.formatCmd cmd = "remove x --force" @>

[<Fact>]
let ``formatCmd omits None positional before flags`` () =
    let cmd = OptionalPositionalFlagDUCommand.Opt(None, [ Force ])
    test <@ CommandReflection.formatCmd cmd = "opt --force" @>

[<Fact>]
let ``format roundtrips positional flag-DU command through the tree`` () =
    let tree = CommandReflection.fromUnion<PositionalWithFlagDUCommand> "Test"
    let cmd = PositionalWithFlagDUCommand.Remove("x", [ Force; KeepBranch ])
    let result = CommandTree.format tree cmd "test"
    test <@ result = Some "test remove x --force --keep-branch" @>

// =============================================================================
// Multiple positionals + flag-DU list (AUTOMATION-187)
// =============================================================================

[<Fact>]
let ``parse binds two positionals with flag between them`` () =
    let tree = CommandReflection.fromUnion<MultiPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "move"; "a"; "--force"; "b" |]
    Assert.Equal(Ok(MultiPositionalFlagDUCommand.Move("a", "b", [ Force ])), result)

[<Fact>]
let ``parse names the second missing positional`` () =
    let tree = CommandReflection.fromUnion<MultiPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "move"; "a"; "--force" |]
    Assert.Equal(Error(InvalidArguments("move", "Missing required argument '<dest>'")), result)

[<Fact>]
let ``parse rejects a third positional on a two-positional leaf`` () =
    let tree = CommandReflection.fromUnion<MultiPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "move"; "a"; "b"; "c" |]
    Assert.Equal(Error(InvalidArguments("move", "Unexpected argument 'c'")), result)

[<Fact>]
let ``format roundtrips two-positional flag-DU command`` () =
    let tree = CommandReflection.fromUnion<MultiPositionalFlagDUCommand> "Test"
    let cmd = MultiPositionalFlagDUCommand.Move("a", "b", [ Force ])
    let result = CommandTree.format tree cmd "test"
    test <@ result = Some "test move a b --force" @>

// =============================================================================
// Typed positional + flag-DU list (AUTOMATION-187)
// =============================================================================

[<Fact>]
let ``parse binds a typed int positional beside flags`` () =
    let tree = CommandReflection.fromUnion<TypedPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "scale"; "3"; "--force" |]
    Assert.Equal(Ok(TypedPositionalFlagDUCommand.Scale(3, [ Force ])), result)

[<Fact>]
let ``parse rejects an invalid typed positional value on a flag-DU leaf`` () =
    // An int positional that fails to parse yields the same generic error the
    // non-flag leaf path produces (validateFields' catch-all), reached here with
    // the flags already scanned off — not a missing-argument error.
    let tree = CommandReflection.fromUnion<TypedPositionalFlagDUCommand> "Test"
    let result = CommandTree.parse tree [| "scale"; "notanint"; "--force" |]
    Assert.Equal(Error(InvalidArguments("scale", "Invalid arguments")), result)

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

[<Fact>]
let ``help shows short flag aliases in options`` () =
    let tree = CommandReflection.fromUnion<ShortNameFlagCmd> "Test"
    let helpText = CommandTree.helpForPath tree [ "deploy" ] "test"
    test <@ helpText.Contains("-v") @>
    test <@ helpText.Contains("-d") @>
    test <@ helpText.Contains("--verbose") @>
    test <@ helpText.Contains("--dry-run") @>

// =============================================================================
// DU-based flag format roundtrip tests
// =============================================================================

[<Fact>]
let ``format roundtrip for DU flag command`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let cmd = DUFlagCommand.Deploy [ DeployDUFlag.Env "prod"; DeployDUFlag.DryRun ]
    let result = CommandTree.format tree cmd "test"
    test <@ result = Some "test deploy --env prod --dry-run" @>

[<Fact>]
let ``format roundtrip for DU flag command with no flags`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let cmd = DUFlagCommand.Deploy []
    let result = CommandTree.format tree cmd "test"
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
        test <@ List.isEmpty globals @>
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
// Global --help override tests
// =============================================================================

[<Fact>]
let ``global --help flag overrides built-in help`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<GlobalHelpCmd, GlobalWithHelp> "Test"

    let result = spec.Parse [| "--help"; "start" |]

    match result with
    | Ok(globals, cmd) ->
        test
            <@
                globals
                |> List.exists (function
                    | GlobalWithHelp.Help -> true
                    | _ -> false)
            @>

        test <@ cmd = GlobalHelpCmd.Start @>
    | Error e -> failwith $"Expected Ok with Help global, got: %O{e}"

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

// =============================================================================
// Types for env var tests
// =============================================================================

type EnvTestFlag =
    | Verbose
    | LogLevel of string

type EnvTestCmd = | [<Cmd("Run")>] Run of EnvTestFlag list

// =============================================================================
// Env var resolution tests
// =============================================================================

[<Fact>]
let ``env var sets flag when CLI flag absent`` () =
    System.Environment.SetEnvironmentVariable("TEST_VERBOSE", "true")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Ok(EnvTestCmd.Run flags) -> test <@ flags |> List.contains EnvTestFlag.Verbose @>
        | other -> failwith $"Expected Run with Verbose, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_VERBOSE", null)

[<Fact>]
let ``env var sets value flag when CLI flag absent`` () =
    System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", "debug")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Ok(EnvTestCmd.Run flags) ->
            test
                <@
                    flags
                    |> List.exists (function
                        | EnvTestFlag.LogLevel "debug" -> true
                        | _ -> false)
                @>
        | other -> failwith $"Expected Run with LogLevel, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", null)

[<Fact>]
let ``CLI flag overrides env var`` () =
    System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", "warn")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
        let result = CommandTree.parse tree [| "run"; "--log-level"; "debug" |]

        match result with
        | Ok(EnvTestCmd.Run flags) ->
            test
                <@
                    flags
                    |> List.exists (function
                        | EnvTestFlag.LogLevel "debug" -> true
                        | _ -> false)
                @>

            test
                <@
                    flags
                    |> List.exists (function
                        | EnvTestFlag.LogLevel "warn" -> true
                        | _ -> false)
                    |> not
                @>
        | other -> failwith $"Expected Run with LogLevel debug, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", null)

[<Fact>]
let ``invalid env var ignored when CLI flag present`` () =
    System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", "")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
        let result = CommandTree.parse tree [| "run"; "--log-level"; "debug" |]

        match result with
        | Ok(EnvTestCmd.Run flags) ->
            test
                <@
                    flags
                    |> List.exists (function
                        | EnvTestFlag.LogLevel "debug" -> true
                        | _ -> false)
                @>
        | other -> failwith $"Expected Run, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_LOG_LEVEL", null)

[<Fact>]
let ``bool env var false does not set flag`` () =
    System.Environment.SetEnvironmentVariable("TEST_VERBOSE", "false")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Ok(EnvTestCmd.Run flags) -> test <@ flags |> List.contains EnvTestFlag.Verbose |> not @>
        | other -> failwith $"Expected Run without Verbose, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_VERBOSE", null)

[<Fact>]
let ``no env prefix means no env var resolution`` () =
    System.Environment.SetEnvironmentVariable("TEST_VERBOSE", "true")

    try
        let tree = CommandReflection.fromUnion<EnvTestCmd> "Test"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Ok(EnvTestCmd.Run flags) -> test <@ List.isEmpty flags @>
        | other -> failwith $"Expected Run with empty flags, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("TEST_VERBOSE", null)

[<Fact>]
let ``fromUnionWithGlobalsAndEnv resolves env vars for global flags`` () =
    System.Environment.SetEnvironmentVariable("APP_VERBOSE", "1")

    try
        let spec =
            CommandReflection.fromUnionWithGlobalsAndEnv<GlobalCmd, GlobalFlag> "Test" "APP"

        let result = spec.Parse [| "scan" |]

        match result with
        | Ok(globals, cmd) ->
            test <@ globals |> List.contains GlobalFlag.Verbose @>
            test <@ cmd = GlobalCmd.Scan @>
        | Error e -> failwith $"Expected Ok, got: %O{e}"
    finally
        System.Environment.SetEnvironmentVariable("APP_VERBOSE", null)

// =============================================================================
// Env var hints in help tests
// =============================================================================

[<Fact>]
let ``help shows env var hints when configured`` () =
    let tree = CommandReflection.fromUnionWithEnv<EnvTestCmd> "Test" "TEST"
    let helpText = CommandTree.helpForPath tree [ "run" ] "test"
    test <@ helpText.Contains("(env: TEST_VERBOSE)") @>
    test <@ helpText.Contains("(env: TEST_LOG_LEVEL)") @>

[<Fact>]
let ``help does not show env hints without prefix`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let helpText = CommandTree.helpForPath tree [ "deploy" ] "test"
    test <@ not (helpText.Contains("env:")) @>

[<Fact>]
let ``helpWithGlobals shows global options section`` () =
    let spec = CommandReflection.fromUnionWithGlobals<GlobalCmd, GlobalFlag> "Test CLI"
    let helpText = CommandTree.helpWithGlobals spec.Tree spec.GlobalFlags "test"
    test <@ helpText.Contains("Global options:") @>
    test <@ helpText.Contains("--verbose") @>
    test <@ helpText.Contains("--log-level") @>
    test <@ helpText.Contains("[global options]") @>
    test <@ helpText.Contains("Commands:") @>
    test <@ helpText.Contains("scan") @>

[<Fact>]
let ``helpWithGlobals shows env hints when configured`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<GlobalCmd, GlobalFlag> "Test CLI" "APP"

    let helpText = CommandTree.helpWithGlobals spec.Tree spec.GlobalFlags "test"
    test <@ helpText.Contains("(env: APP_VERBOSE)") @>
    test <@ helpText.Contains("(env: APP_LOG_LEVEL)") @>

// =============================================================================
// fromUnionWithGlobalsAndEnv edge case tests
// =============================================================================

[<Fact>]
let ``fromUnionWithGlobalsAndEnv rejects duplicate flag names`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnionWithGlobalsAndEnv<CollidingCmd, CollidingGlobal> "Test" "APP"
            |> ignore)

    test <@ ex.Message.Contains("--timeout") @>

[<Fact>]
let ``fromUnionWithGlobalsAndEnv global flag duplicate rejected`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<GlobalCmd, GlobalFlag> "Test" "APP"

    let result = spec.Parse [| "--verbose"; "scan"; "--verbose" |]

    match result with
    | Error(DuplicateFlag(flag, _)) -> test <@ flag = "--verbose" @>
    | other -> failwith $"Expected DuplicateFlag, got: %O{other}"

[<Fact>]
let ``fromUnionWithGlobalsAndEnv global flag missing value`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<GlobalCmd, GlobalFlag> "Test" "APP"

    let result = spec.Parse [| "--log-level" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``fromUnionWithGlobalsAndEnv with command flags`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<GlobalWithCmdFlagCmd, GlobalFlag> "Test" "APP"

    let result = spec.Parse [| "--verbose"; "scan"; "--watch" |]

    match result with
    | Ok(globals, cmd) ->
        test <@ globals |> List.contains GlobalFlag.Verbose @>

        match cmd with
        | GlobalWithCmdFlagCmd.Scan flags -> test <@ flags |> List.contains ScanDUFlag.Watch @>
        | other -> failwith $"Expected Scan, got: %O{other}"
    | Error e -> failwith $"Expected Ok, got: %O{e}"

[<Fact>]
let ``fromUnionWithGlobalsAndEnv env var for global flag`` () =
    System.Environment.SetEnvironmentVariable("GBL_VERBOSE", "1")

    try
        let spec =
            CommandReflection.fromUnionWithGlobalsAndEnv<GlobalCmd, GlobalFlag> "Test" "GBL"

        let result = spec.Parse [| "start" |]

        match result with
        | Ok(globals, cmd) ->
            test <@ globals |> List.contains GlobalFlag.Verbose @>
            test <@ cmd = GlobalCmd.Start @>
        | Error e -> failwith $"Expected Ok, got: %O{e}"
    finally
        System.Environment.SetEnvironmentVariable("GBL_VERBOSE", null)

[<Fact>]
let ``fromUnionWithGlobalsAndEnv short flag works`` () =
    let spec =
        CommandReflection.fromUnionWithGlobalsAndEnv<GlobalWithCmdFlagCmd, GlobalFlag> "Test" "APP"

    let result = spec.Parse [| "-v"; "scan" |]

    match result with
    | Ok(globals, _) -> test <@ globals |> List.contains GlobalFlag.Verbose @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

// =============================================================================
// helpWithGlobals edge case tests
// =============================================================================

[<Fact>]
let ``helpWithGlobals with empty global flags omits section`` () =
    let tree = CommandReflection.fromUnion<GlobalCmd> "Test CLI"
    let helpText = CommandTree.helpWithGlobals tree [] "test"
    test <@ not (helpText.Contains("Global options:")) @>

type SingleCmd = | [<Cmd("Do it")>] Do

[<Fact>]
let ``helpWithGlobals falls back for non-group tree`` () =
    let tree = CommandReflection.fromUnion<SingleCmd> "Test"

    match tree with
    | Group { Children = [ child ] } ->
        let helpText = CommandTree.helpWithGlobals child [] "test"
        test <@ helpText.Contains("Do it") @>
    | _ -> failwith "Expected Group with one child"

// =============================================================================
// Built-in --version tests
// =============================================================================

[<Fact>]
let ``parse returns VersionRequested when --version passed at root`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "--version" |]
    test <@ result = Error VersionRequested @>

[<Fact>]
let ``parse returns VersionRequested when version subcommand passed at root`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "version" |]
    test <@ result = Error VersionRequested @>

[<Fact>]
let ``parse does not return VersionRequested for --version at nested level`` () =
    let tree = CommandReflection.fromUnion<NestNoDefault> "Test"
    let result = CommandTree.parse tree [| "inner"; "--version" |]

    match result with
    | Error VersionRequested -> failwith "Should not return VersionRequested at nested level"
    | _ -> ()

[<Fact>]
let ``parse routes to explicit version command instead of built-in`` () =
    let tree = CommandReflection.fromUnion<VersionOverrideCommand> "Test"
    let result = CommandTree.parse tree [| "version" |]
    test <@ result = Ok(VersionOverrideCommand.Version) @>

[<Fact>]
let ``parse returns VersionRequested when --version mixed with other args at root`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "unknown"; "--version" |]
    test <@ result = Error VersionRequested @>

[<Fact>]
let ``parse does not return VersionRequested for version subcommand at nested level`` () =
    let tree = CommandReflection.fromUnion<NestNoDefault> "Test"
    let result = CommandTree.parse tree [| "inner"; "version" |]

    match result with
    | Error VersionRequested -> failwith "Should not return VersionRequested at nested level"
    | _ -> ()

// =============================================================================
// Bug fix: zero-arg commands must reject trailing arguments
// =============================================================================

[<Fact>]
let ``parse rejects trailing flag on zero-arg command`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "check"; "--bogus" |]

    match result with
    | Error(UnknownFlag("--bogus", "check", [])) -> ()
    | other -> failwith $"Expected UnknownFlag, got: %O{other}"

[<Fact>]
let ``parse rejects trailing positional arg on zero-arg command`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "check"; "extra" |]

    match result with
    | Error(InvalidArguments("check", _)) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse rejects trailing args on nested zero-arg command`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let result = CommandTree.parse tree [| "dev"; "build"; "--verbose" |]

    match result with
    | Error(UnknownFlag("--verbose", "build", [])) -> ()
    | other -> failwith $"Expected UnknownFlag, got: %O{other}"

// =============================================================================
// Bug fix: record-typed arguments should default missing fields
// =============================================================================

[<Fact>]
let ``parse defaults bool field in record arg`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let result = CommandTree.parse tree [| "alpha" |]
    Assert.Equal(Ok(RecordCommand.Alpha { publish = false }), result)

[<Fact>]
let ``parse accepts explicit bool value in record arg`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let result = CommandTree.parse tree [| "alpha"; "true" |]
    Assert.Equal(Ok(RecordCommand.Alpha { publish = true }), result)

[<Fact>]
let ``parse defaults optional and bool fields in record arg`` () =
    let tree = CommandReflection.fromUnion<RecordOptCommand> "Test"
    let result = CommandTree.parse tree [| "run" |]
    Assert.Equal(Ok(RecordOptCommand.Run { name = None; verbose = false }), result)

[<Fact>]
let ``parse accepts partial record args`` () =
    let tree = CommandReflection.fromUnion<RecordOptCommand> "Test"
    let result = CommandTree.parse tree [| "run"; "hello" |]
    Assert.Equal(Ok(RecordOptCommand.Run { name = Some "hello"; verbose = false }), result)

[<Fact>]
let ``parse rejects extra positional arg on record command`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let result = CommandTree.parse tree [| "alpha"; "true"; "extra" |]

    match result with
    | Error(InvalidArguments("alpha", msg)) -> test <@ msg.Contains("Unexpected argument") @>
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse rejects extra flag on record command`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let result = CommandTree.parse tree [| "alpha"; "true"; "--unknown" |]

    match result with
    | Error(UnknownFlag("--unknown", "alpha", [])) -> ()
    | other -> failwith $"Expected UnknownFlag, got: %O{other}"

[<Fact>]
let ``format roundtrip for record command`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let cmd = RecordCommand.Alpha { publish = true }
    let result = CommandTree.format tree cmd "test"
    test <@ result = Some "test alpha True" @>

[<Fact>]
let ``format roundtrip for record command with default values`` () =
    let tree = CommandReflection.fromUnion<RecordCommand> "Test"
    let cmd = RecordCommand.Alpha { publish = false }
    let result = CommandTree.format tree cmd "test"
    test <@ result = Some "test alpha False" @>

[<Fact>]
let ``parse rejects record with missing required field`` () =
    let tree = CommandReflection.fromUnion<RecordReqCommand> "Test"
    let result = CommandTree.parse tree [| "deploy" |]

    match result with
    | Error(InvalidArguments("deploy", _)) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse handles record with required field provided`` () =
    let tree = CommandReflection.fromUnion<RecordReqCommand> "Test"
    let result = CommandTree.parse tree [| "deploy"; "prod" |]
    Assert.Equal(Ok(RecordReqCommand.Deploy { target = "prod"; publish = false }), result)

// =============================================================================
// Coverage: short name flag collision detection
// =============================================================================

[<Fact>]
let ``fromUnionWithGlobals rejects short flag name collision`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnionWithGlobals<ShortCollidingCmd, ShortCollidingGlobal> "Test"
            |> ignore)

    test <@ ex.Message.Contains("-t") @>

[<Fact>]
let ``fromUnionWithGlobals accepts command flags with no short names`` () =
    // NoShortFlag has Timeout and Trace both starting with 't', so no short names
    // This exercises the None -> () branch in short name collision check
    let spec = CommandReflection.fromUnionWithGlobals<NoShortCmd, NoShortGlobal> "Test"
    let result = spec.Parse [| "run"; "--timeout"; "30" |]

    match result with
    | Ok(_, cmd) ->
        match cmd with
        | NoShortCmd.Run flags ->
            test
                <@
                    flags
                    |> List.exists (function
                        | NoShortFlag.Timeout 30 -> true
                        | _ -> false)
                @>
    | Error e -> failwith $"Expected Ok, got: %O{e}"

// =============================================================================
// Coverage: global flag with invalid typed value
// =============================================================================

[<Fact>]
let ``global flag with invalid typed value returns InvalidArguments`` () =
    let spec =
        CommandReflection.fromUnionWithGlobals<TypedGlobalCmd, TypedGlobalFlag> "Test"

    let result = spec.Parse [| "--count"; "notanumber"; "start" |]

    match result with
    | Error(InvalidArguments("global", msg)) -> test <@ msg.Contains("Invalid value") @>
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

// =============================================================================
// Coverage: global env var error propagation
// =============================================================================

[<Fact>]
let ``global env var with invalid value returns error`` () =
    System.Environment.SetEnvironmentVariable("GENV_COUNT", "notanumber")

    try
        let spec =
            CommandReflection.fromUnionWithGlobalsAndEnv<TypedGlobalCmd, TypedGlobalFlag> "Test" "GENV"

        let result = spec.Parse [| "start" |]

        match result with
        | Error(InvalidArguments("env", msg)) -> test <@ msg.Contains("Invalid value") @>
        | other -> failwith $"Expected error, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("GENV_COUNT", null)

// =============================================================================
// Coverage: list field with error in element parsing
// =============================================================================

type IntListCmd = | [<Cmd("Sum values")>] Sum of values: int list

[<Fact>]
let ``parse returns error for list field with invalid element`` () =
    let tree = CommandReflection.fromUnion<IntListCmd> "Test"
    let result = CommandTree.parse tree [| "sum"; "1"; "abc"; "3" |]

    match result with
    | Error(InvalidArguments _) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

// =============================================================================
// renderParseError tests (Capability 1: canonical error + nearest help)
// =============================================================================

[<Fact>]
let ``renderParseError UnknownFlag shows error line and command help`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let err = UnknownFlag("--foo", "deploy", [ "--env"; "--dry-run" ])
    let rendered = CommandTree.renderParseError tree err "mycli"
    // Error line names the flag and command
    test <@ rendered.Contains("Unknown flag '--foo' for 'deploy'.") @>
    // Followed by that command's own help (usage + its flags)
    test <@ rendered.Contains("mycli deploy") @>
    test <@ rendered.Contains("--env") @>

[<Fact>]
let ``renderParseError UnknownCommand shows error line and nearest group help`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    // Misspelled subcommand of the "dev" group
    let err = UnknownCommand("chekc", [||], [ "dev" ])
    let rendered = CommandTree.renderParseError tree err "mycli"
    test <@ rendered.Contains("Unknown command 'chekc'.") @>
    // Nearest group help lists dev's children, not root commands
    test <@ rendered.Contains("mycli dev") @>
    test <@ rendered.Contains("build") @>
    test <@ rendered.Contains("test") @>

[<Fact>]
let ``renderParseError UnknownCommand at root shows root help`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let err = UnknownCommand("devv", [||], [])
    let rendered = CommandTree.renderParseError tree err "mycli"
    test <@ rendered.Contains("Unknown command 'devv'.") @>
    test <@ rendered.Contains("dev") @>
    test <@ rendered.Contains("help") @>

[<Fact>]
let ``renderParseError InvalidArguments shows message and command help`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    let err = InvalidArguments("greet", "missing name")
    let rendered = CommandTree.renderParseError tree err "mycli"
    test <@ rendered.Contains("missing name") @>
    test <@ rendered.Contains("mycli greet") @>
    test <@ rendered.Contains("<name>") @>

[<Fact>]
let ``renderParseError AmbiguousArgument shows candidates and help`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let err = AmbiguousArgument("de", [ "dev"; "deploy" ])
    let rendered = CommandTree.renderParseError tree err "mycli"
    test <@ rendered.Contains("Ambiguous 'de'. Did you mean: dev, deploy") @>
    // Falls back to root help (input is an arg value, not a group)
    test <@ rendered.Contains("dev") @>

[<Fact>]
let ``renderParseError DuplicateFlag shows message and command help`` () =
    let tree = CommandReflection.fromUnion<DUFlagCommand> "Test"
    let err = DuplicateFlag("--env", "deploy")
    let rendered = CommandTree.renderParseError tree err "mycli"
    test <@ rendered.Contains("Flag '--env' provided more than once for 'deploy'.") @>
    test <@ rendered.Contains("mycli deploy") @>

[<Fact>]
let ``renderParseError HelpRequested renders help without error line`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let rendered = CommandTree.renderParseError tree (HelpRequested [ "dev" ]) "mycli"
    test <@ rendered.Contains("mycli dev") @>
    test <@ not (rendered.Contains("Unknown")) @>

[<Fact>]
let ``renderParseError VersionRequested returns empty string`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    let rendered = CommandTree.renderParseError tree VersionRequested "mycli"
    test <@ rendered = "" @>

[<Fact>]
let ``isError classifies genuine errors as true and help/version as false`` () =
    test <@ CommandTree.isError (UnknownCommand("x", [||], [])) @>
    test <@ CommandTree.isError (UnknownFlag("--x", "c", [])) @>
    test <@ CommandTree.isError (InvalidArguments("c", "m")) @>
    test <@ CommandTree.isError (AmbiguousArgument("x", [])) @>
    test <@ CommandTree.isError (DuplicateFlag("--x", "c")) @>
    test <@ not (CommandTree.isError (HelpRequested [])) @>
    test <@ not (CommandTree.isError VersionRequested) @>

// =============================================================================
// Single-path parse: unknown top-level command carries raw args for forwarding
// =============================================================================

[<Fact>]
let ``parse unknown top-level command exposes raw args for forwarding`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    // A consumer forwards an unknown top-level command (groupPath = []) + its raw rest.
    let result = CommandTree.parse tree [| "frobnicate"; "--all"; "x" |]

    match result with
    | Error(UnknownCommand(cmd, rest, [])) ->
        test <@ cmd = "frobnicate" @>
        test <@ rest = [| "--all"; "x" |] @>
    | other -> failwith $"Expected UnknownCommand at root, got: %O{other}"

[<Fact>]
let ``parse unknown top-level command with no trailing args has empty rest`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    let result = CommandTree.parse tree [| "frobnicate" |]

    match result with
    | Error(UnknownCommand(cmd, rest, [])) ->
        test <@ cmd = "frobnicate" @>
        test <@ Array.isEmpty rest @>
    | other -> failwith $"Expected UnknownCommand at root, got: %O{other}"

[<Fact>]
let ``parse known top-level command parses normally`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    test <@ CommandTree.parse tree [| "check" |] = Ok SimpleCommand.Check @>

[<Fact>]
let ``parse unknown nested subcommand still fails hard with non-empty groupPath`` () =
    let tree = CommandReflection.fromUnion<RootCommand> "Test"
    // "dev" is a known group; "chekc" is an unknown subcommand and must fail hard.
    // groupPath is non-empty, so a forwarding consumer ignores it (only roots forward).
    let result = CommandTree.parse tree [| "dev"; "chekc" |]

    match result with
    | Error(UnknownCommand("chekc", [||], [ "dev" ])) -> ()
    | other -> failwith $"Expected nested UnknownCommand, got: %O{other}"

[<Fact>]
let ``parse surfaces help and version, never as UnknownCommand`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    test <@ CommandTree.parse tree [| "--help" |] = Error(HelpRequested []) @>
    test <@ CommandTree.parse tree [| "--version" |] = Error VersionRequested @>
    test <@ CommandTree.parse tree [| "version" |] = Error VersionRequested @>

[<Fact>]
let ``parse surfaces leaf parse errors, not UnknownCommand`` () =
    let tree = CommandReflection.fromUnion<CommandWithArgs> "Test"
    // "add" is known but the args are invalid -> a real error, not an unknown command.
    let result = CommandTree.parse tree [| "add"; "notanint"; "2" |]

    match result with
    | Error(InvalidArguments("add", _)) -> ()
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

[<Fact>]
let ``parse empty args yields default or help, never UnknownCommand`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"
    test <@ CommandTree.parse tree [||] = Error(HelpRequested []) @>

[<Fact>]
let ``unresolved top-level command renders canonical error and help`` () =
    let tree = CommandReflection.fromUnion<SimpleCommand> "Test"

    match CommandTree.parse tree [| "frobnicate" |] with
    | Error(UnknownCommand(cmd, rest, [])) ->
        // Consumer could not resolve it dynamically -> render canonical error + help.
        let rendered =
            CommandTree.renderParseError tree (UnknownCommand(cmd, rest, [])) "mycli"

        test <@ rendered.Contains("Unknown command 'frobnicate'.") @>
        test <@ rendered.Contains("check") @>
    | other -> failwith $"Expected UnknownCommand at root, got: %O{other}"
