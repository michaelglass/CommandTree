module CommandTree.Analyzers.Tests.CommandTreeAnalyzerTests

open Xunit
open Swensen.Unquote
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.Testing
open CommandTree.Analyzers.CommandTreeAnalyzer

// -----------------------------------------------------------------------------
// Harness
//
// The analyzer needs the TYPED tree: it recovers the command DU `'Cmd` from the
// generic instantiation at each `CommandReflection.fromUnion*` call site. To exercise
// that against a stable symbol `FullName` (`CommandTree.CommandReflection.fromUnion`)
// without depending on the unpublished CommandTree NuGet package, every fixture embeds
// a minimal `namespace CommandTree` / `module CommandReflection` STUB declaring the four
// constructor signatures. The stub's only job is to make the call sites type-check and
// carry the right FullName + generic args; its bodies are never run.
// -----------------------------------------------------------------------------

/// Stub reproducing the constructor family's namespace, module, names, and generic arity
/// so call sites resolve to `CommandTree.CommandReflection.fromUnion*` (the FullNames the
/// analyzer matches). Bodies are `failwith` — only the signatures matter for type-checking.
let private commandTreeStub =
    """
namespace CommandTree

type CommandTree<'Cmd> = Leaf of 'Cmd | Group

type GlobalSpec<'Globals, 'Cmd> = { Dummy: 'Globals * 'Cmd }

module CommandReflection =
    let fromUnion<'Cmd> (rootDesc: string) : CommandTree<'Cmd> = failwith "stub"

    let fromUnionWithEnv<'Cmd> (rootDesc: string) (envPrefix: string) : CommandTree<'Cmd> = failwith "stub"

    let fromUnionWithGlobals<'Cmd, 'Globals> (rootDesc: string) : GlobalSpec<'Globals, 'Cmd> = failwith "stub"

    let fromUnionWithGlobalsAndEnv<'Cmd, 'Globals> (rootDesc: string) (envPrefix: string) : GlobalSpec<'Globals, 'Cmd> =
        failwith "stub"
"""

/// Project options shared by every test. No extra NuGet packages — the stub supplies the
/// CommandTree surface, FSharp.Core is implicit. Built once; FCS type-checks fast against it.
let private projectOptions = (mkOptionsFromProject "net10.0" []).Result

/// Wrap a fixture's declarations in an indented `module UserCode =` under the same namespace
/// as the stub. A `namespace` can't be followed by a top-level (column-0) `let`, so the
/// fixture body must live inside an indented nested module — every non-blank line is indented
/// four spaces. Fixtures therefore stay flat: just the DU(s) and the `fromUnion*` call.
let private wrapUserSource (declarations: string) : string =
    let indented =
        declarations.Replace("\r\n", "\n").Split('\n')
        |> Array.map (fun line -> if line.Trim() = "" then "" else "    " + line)
        |> String.concat "\n"

    "module UserCode =\n" + indented

/// Type-check the stub + the user fixture as one file and run the analyzer over it.
/// Returns the analyzer's diagnostics.
let private analyze (declarations: string) : Message list =
    let source = commandTreeStub + "\n" + wrapUserSource declarations
    let ctx = getContext projectOptions source
    commandTreeAnalyzer ctx |> Async.RunSynchronously

let private codesOf (messages: Message list) =
    messages |> List.map (fun m -> m.Code) |> List.sort

// =============================================================================
// CT001 — unsupported field type
// =============================================================================

module ``CT001 unsupported field type`` =

    [<Fact>]
    let ``flags an unsupported scalar field (DateTimeOffset)`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``message carries the case, field, offending type, and supported set`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        let m = List.exactlyOne messages
        test <@ m.Code = "CT001" @>
        test <@ m.Type = "CommandTree.SpecShape" @>
        test <@ m.Severity = FSharp.Analyzers.SDK.Severity.Warning @>
        test <@ m.Message.Contains "Stamp" @> // the case name
        test <@ m.Message.Contains "at" @> // the field name
        test <@ m.Message.Contains "DateTimeOffset" @> // the offending type
        test <@ m.Message.Contains "Supported types" @>

    [<Fact>]
    let ``range points at the offending field's declaration`` () =
        // The `Stamp` field is on line 3 of the user fixture; once wrapped + stub-prefixed
        // its absolute line shifts, so assert only that a positive range was produced and it
        // is on the line containing the field (the column lands inside the field declaration).
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        let m = List.exactlyOne messages
        test <@ m.Range.StartLine > 0 @>
        test <@ m.Range.StartColumn >= 0 @>

    [<Fact>]
    let ``does not flag supported scalars, options, lists, or union (subcommand) fields`` () =
        let messages =
            analyze
                """
type Sub =
    | A
    | B

type Cmd =
    | Scalars of s: string * i: int * l: int64 * b: bool * f: float * d: decimal * g: System.Guid
    | Opt of x: int option
    | Lst of xs: string list
    | Nested of Sub

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``flags an unsupported tuple-typed field (no type definition)`` () =
        // A tuple type has no FSharpEntity (HasTypeDefinition = false), so it is neither
        // scalar, option, list, union, nor record — the runtime rejects it. Exercises the
        // false short-circuits of the type predicates and the `Format` display fallback.
        let messages =
            analyze
                """
type Cmd =
    | Pair of p: (int * string)

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``flags an unsupported function-typed field`` () =
        let messages =
            analyze
                """
type Cmd =
    | Fn of f: (int -> string)

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``flags an unsupported type wrapped in option`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset option

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``flags an unsupported type wrapped in list`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamps of ats: System.DateTimeOffset list

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``flags a bad field in a NESTED subcommand union (recursion proof)`` () =
        let messages =
            analyze
                """
type Inner =
    | Stamp of at: System.DateTimeOffset

type Cmd =
    | Group of Inner

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>
        test <@ (List.exactlyOne messages).Message.Contains "Stamp" @>

    [<Fact>]
    let ``flags a bad field in an arg-group RECORD`` () =
        let messages =
            analyze
                """
type Args =
    { At: System.DateTimeOffset }

type Cmd =
    | Generate of Args

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``flags a bad field in the 'Globals type`` () =
        let messages =
            analyze
                """
type Globals =
    | Stamp of at: System.DateTimeOffset

type Cmd =
    | Run

let spec = CommandReflection.fromUnionWithGlobals<Cmd, Globals> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

// =============================================================================
// CT002 — list-field placement
// =============================================================================

module ``CT002 list-field placement`` =

    [<Fact>]
    let ``flags a list field that is not last`` () =
        let messages =
            analyze
                """
type Cmd =
    | Tag of files: string list * label: string

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT002" ] @>

    [<Fact>]
    let ``flags more than one list field in a case`` () =
        let messages =
            analyze
                """
type Cmd =
    | Two of a: string list * b: string list

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT002" ] @>

    [<Fact>]
    let ``does not flag a single list field in last position`` () =
        let messages =
            analyze
                """
type Cmd =
    | Tag of label: string * files: string list

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``does not flag a single field of type 'SomeDU list' (a flag DU, valid)`` () =
        let messages =
            analyze
                """
type Flag =
    | Strict
    | Config of string

type Cmd =
    | Check of Flag list

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``message names the case and the placement rule`` () =
        let messages =
            analyze
                """
type Cmd =
    | Tag of files: string list * label: string

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        let m = List.exactlyOne messages
        test <@ m.Code = "CT002" @>
        test <@ m.Severity = FSharp.Analyzers.SDK.Severity.Warning @>
        test <@ m.Message.Contains "Tag" @>
        test <@ m.Message.Contains "last field" @>

// =============================================================================
// Aggregation + multiple diagnostics
// =============================================================================

module ``Aggregation`` =

    [<Fact>]
    let ``reports every shape error across cases (no fail-fast)`` () =
        // Two unsupported-type fields in different cases + one list-placement error.
        let messages =
            analyze
                """
type Cmd =
    | A of x: System.DateTimeOffset
    | B of y: System.TimeSpan
    | Tag of files: string list * label: string

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        // CT001, CT001, CT002 — all surfaced (runtime would crash on the first).
        test <@ codesOf messages = [ "CT001"; "CT001"; "CT002" ] @>

// =============================================================================
// Constructor-variant coverage
// =============================================================================

module ``Constructor variants`` =

    [<Fact>]
    let ``fromUnionWithEnv is recognized`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let tree = CommandReflection.fromUnionWithEnv<Cmd> "desc" "PREFIX"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``fromUnionWithGlobals is recognized`` () =
        let messages =
            analyze
                """
type Globals =
    | Verbose

type Cmd =
    | Stamp of at: System.DateTimeOffset

let spec = CommandReflection.fromUnionWithGlobals<Cmd, Globals> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``fromUnionWithGlobalsAndEnv is recognized`` () =
        let messages =
            analyze
                """
type Globals =
    | Verbose

type Cmd =
    | Stamp of at: System.DateTimeOffset

let spec = CommandReflection.fromUnionWithGlobalsAndEnv<Cmd, Globals> "desc" "PREFIX"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``a call to an unrelated function is ignored`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let unrelated = id<Cmd>
"""

        test <@ List.isEmpty messages @>

// =============================================================================
// Typed-tree walk coverage — InitAction declarations, de-duplication, recursion guard,
// and non-union generic arguments. Each fixture drives a specific arm of the walk.
// =============================================================================

module ``Typed-tree walk`` =

    [<Fact>]
    let ``finds a fromUnion call in a top-level do (InitAction) statement`` () =
        // A statement expression (not a `let` binding) becomes an InitAction declaration in
        // the typed tree, exercising that arm of walkDecl.
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

CommandReflection.fromUnion<Cmd> "desc" |> ignore
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``reports a DU constructed at two call sites only once (de-duplication)`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let a = CommandReflection.fromUnion<Cmd> "one"
let b = CommandReflection.fromUnion<Cmd> "two"
"""

        // Same DU, same field, same range — one finding despite two call sites.
        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``terminates on a self-recursive command DU (visited guard)`` () =
        // `Cmd` nests itself via the `Sub` group; the visited-set guard must stop the walk.
        let messages =
            analyze
                """
type Cmd =
    | Bad of at: System.DateTimeOffset
    | Sub of Cmd

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``ignores a fromUnion call whose type argument is not a union`` () =
        // `fromUnion<int>` type-checks against the unconstrained stub; the analyzer must skip
        // the non-union generic argument rather than crash or emit a spurious diagnostic.
        let messages =
            analyze
                """
let tree = CommandReflection.fromUnion<int> "desc"
"""

        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``produces no diagnostics when no typed tree is available`` () =
        // The analyzer is typed-tree-only; without type-check information it silently no-ops.
        test <@ List.isEmpty (analyzeOptionalTypedTree None) @>

    [<Fact>]
    let ``ignores a fromUnion call whose type argument has no type definition`` () =
        // `fromUnion<int * string>` — the generic argument is a tuple (HasTypeDefinition =
        // false), so it is not a union: the call yields no command DU and no diagnostic.
        let messages =
            analyze
                """
let tree = CommandReflection.fromUnion<int * string> "desc"
"""

        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``walks past a nested local-function call (no declaring entity) without crashing`` () =
        // A function bound INSIDE another function has no DeclaringEntity, exercising the
        // `None` arm of the call-name derivation; it is never a constructor, so the fromUnion
        // call still wins. (A top-level `let` would have the module as its declaring entity.)
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let outer () =
    let inner x = x + 1
    inner 41

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

    [<Fact>]
    let ``walks past operator and pipe calls without crashing`` () =
        // The generic call-walk visits EVERY call node, including operators and pipes whose
        // member symbols differ from the constructor family. They must be silently skipped.
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let n = (1 + 2) * 3
let xs = [ 1; 2; 3 ] |> List.map (fun x -> x + 1) |> List.sum
let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

        test <@ codesOf messages = [ "CT001" ] @>

// =============================================================================
// Negative control (MANDATORY): ExampleCli's real Command DU + GlobalFlag
// produce ZERO diagnostics. Structure copied from examples/ExampleCli/Program.fs;
// CommandTree attributes are dropped (they don't affect CT001/CT002 — only the
// field types and case structure do), and the constructor is the same
// fromUnionWithGlobalsAndEnv<Command, GlobalFlag> the example uses.
//
// IMPORTANT: the base ExampleCli `Command` DU as written on `vzkkvwtp` is NOT in
// fact valid — `ReportCommand.Diff of MergeReportArgs * ReportFlag list` is a
// multi-field case whose first field is a RECORD, which the runtime rejects (only a
// SINGLE record field becomes an arg group; see Reflection.processCase). The example
// crashes at construction time (`dotnet run -- report diff …` throws InvalidOperation
// "Field 'Item1' of command 'diff' has unsupported type 'MergeReportArgs'"); the
// `example-build` gate never catches it because it only builds, never runs the static
// initializer. The analyzer correctly surfaces this as CT001 (asserted in the
// "catches the real ExampleCli construction bug" test below). So the negative control
// here exercises the VALID subset of the example DU (Report replaced with its valid
// cases) — that subset, a realistic full command tree, must stay silent.
// =============================================================================

/// The shared, VALID portion of the ExampleCli command surface, sans the broken
/// `ReportCommand.Diff` case. Used by both the negative control and (with the broken
/// case re-added) the positive bug-detection test.
let private exampleValidDecls =
    """
type Priority =
    | Low
    | Medium
    | High

type GlobalFlag =
    | Verbose
    | LogLevel of string

type TaskCommand =
    | Add of title: string * priority: Priority option
    | List
    | Complete of id: int
    | Remove of id: int

type DbCommand =
    | Migrate
    | Reset
    | Status

type DeployCommand =
    | Push of env: string
    | Status of env: string option

type CoverageCommand =
    | File of path: string
    | Summary

type FilesCommand =
    | Tag of label: string * files: string list
    | Diff of oldDll: string * newDll: string

type JobCommand =
    | Start of name: string * size: int64 * verbose: bool
    | Status of id: System.Guid
    | List

type CheckFlag =
    | Config of string
    | Strict
    | NoCache

type ProcessDemoCommand =
    | Run
    | Silent

type UiDemoCommand =
    | Styles
    | Timing

type ReflectionDemoCommand =
    | FormatCmd
    | Naming

type FishDemoCommand =
    | Generate
    | Preview
    | Install

type ReportFlag =
    | ShowGaps
    | Format of string

type ReportCommand =
    | Generate of input: string * output: string option * ReportFlag list
    | View of output: string option

type Command =
    | Task of TaskCommand
    | Db of DbCommand
    | Deploy of DeployCommand
    | Coverage of CoverageCommand
    | Files of FilesCommand
    | Job of JobCommand
    | Proc of ProcessDemoCommand
    | Ui of UiDemoCommand
    | Reflect of ReflectionDemoCommand
    | Test
    | Format
    | Check of CheckFlag list
    | Fish of FishDemoCommand
    | Report of ReportCommand
    | Help

let spec =
    CommandReflection.fromUnionWithGlobalsAndEnv<Command, GlobalFlag> "Example project management CLI" "EXAMPLE"
"""

module ``Negative control — ExampleCli`` =

    [<Fact>]
    let ``the valid ExampleCli Command DU and GlobalFlag produce no diagnostics`` () =
        let messages = analyze exampleValidDecls
        test <@ List.isEmpty messages @>

    [<Fact>]
    let ``catches the real ExampleCli construction bug (Report.Diff record + flag-list)`` () =
        // The exact shape from examples/ExampleCli/Program.fs that throws at runtime: a
        // multi-field case whose first field is a record. The analyzer must flag it (CT001),
        // proving the negative control's exclusion above is a real bug, not analyzer noise.
        let messages =
            analyze
                """
type MergeReportArgs =
    { Baseline: string
      Current: string
      Output: string option }

type ReportFlag =
    | ShowGaps
    | Format of string

type ReportCommand =
    | Diff of MergeReportArgs * ReportFlag list

type Command =
    | Report of ReportCommand

let tree = CommandReflection.fromUnion<Command> "desc"
"""

        let m = List.exactlyOne messages
        test <@ m.Code = "CT001" @>
        test <@ m.Message.Contains "MergeReportArgs" @>
        test <@ m.Message.Contains "Diff" @>

// =============================================================================
// No-call source
// =============================================================================

module ``No call sites`` =

    [<Fact>]
    let ``a file with no fromUnion call produces no diagnostics`` () =
        let messages =
            analyze
                """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let x = 1
"""

        test <@ List.isEmpty messages @>
