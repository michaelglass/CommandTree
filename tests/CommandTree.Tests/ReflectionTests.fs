module CommandTree.Tests.ReflectionTests

open Xunit
open Swensen.Unquote
open CommandTree
open CommandTree.Tests.TestHelpers

type MinimalCommand =
    | Check
    | Build
    | TestSuite
    | FileCoverage of path: string

type AttributedCommand =
    | [<Cmd("Run all checks")>] Check
    | [<Cmd("Build the project", Name = "compile")>] Build
    | [<Cmd("Format code", Name = "fmt")>] Format

type DevSubCommand =
    | [<CmdDefault>] Check
    | Build
    | Test

/// A case whose kebab name is SHORTER than the prefix-matching floor, beside a
/// longer case it is a prefix of. Modelled on a real workflow-state union whose
/// `Qa` case could not be typed at all.
type ShortCaseSubCommand =
    | Qa
    | QaFailed
    | Backlog

type NestedCommand =
    | Dev of DevSubCommand
    | [<Cmd("Show help")>] Help

// Union with cases that have fields (for parseFieldValue field count filter)
type MixedFieldUnion =
    | Simple
    | WithArg of x: int

[<Fact>]
let ``toKebabCase converts PascalCase to kebab-case`` () =
    Assert.Equal("file-coverage", CommandReflection.toKebabCase "FileCoverage")
    Assert.Equal("test-suite", CommandReflection.toKebabCase "TestSuite")
    Assert.Equal("check", CommandReflection.toKebabCase "Check")

[<Fact>]
let ``toKebabCase handles single word`` () =
    Assert.Equal("build", CommandReflection.toKebabCase "Build")
    Assert.Equal("test", CommandReflection.toKebabCase "Test")

[<Fact>]
let ``toKebabCase splits acronym boundaries`` () =
    // An uppercase run followed by a capitalized word is split at the last capital:
    // HTMLParser -> HTML + Parser -> html-parser
    Assert.Equal("html-parser", CommandReflection.toKebabCase "HTMLParser")
    Assert.Equal("url-handler", CommandReflection.toKebabCase "URLHandler")
    Assert.Equal("db-migrate", CommandReflection.toKebabCase "DBMigrate")

[<Fact>]
let ``toKebabCase leaves single-capital words unchanged`` () =
    // DryRun has no acronym run, so behavior is unchanged.
    Assert.Equal("dry-run", CommandReflection.toKebabCase "DryRun")
    Assert.Equal("file-coverage", CommandReflection.toKebabCase "FileCoverage")

[<Fact>]
let ``toKebabCase keeps a pure acronym as one token`` () =
    // A trailing/standalone acronym with no following word stays joined.
    Assert.Equal("html", CommandReflection.toKebabCase "HTML")
    Assert.Equal("extract-api", CommandReflection.toKebabCase "ExtractApi")

[<Fact>]
let ``toDescription converts PascalCase to readable description`` () =
    Assert.Equal("File coverage", CommandReflection.toDescription "FileCoverage")
    Assert.Equal("Test suite", CommandReflection.toDescription "TestSuite")
    Assert.Equal("Check", CommandReflection.toDescription "Check")

[<Fact>]
let ``fromUnion derives names from case names`` () =
    let tree = CommandReflection.fromUnion<MinimalCommand> "Test"

    match tree with
    | CommandTree.Group group ->
        let names = group.Children |> List.map CommandTree.name
        test <@ List.contains "check" names @>
        test <@ List.contains "build" names @>
        test <@ List.contains "test-suite" names @>
        test <@ List.contains "file-coverage" names @>
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``fromUnion derives descriptions from case names`` () =
    let tree = CommandReflection.fromUnion<MinimalCommand> "Test"
    Assert.Equal("Check", (getLeaf tree "check").Description)
    Assert.Equal("Test suite", (getLeaf tree "test-suite").Description)

[<Fact>]
let ``fromUnion uses attribute description when provided`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"
    Assert.Equal("Run all checks", (getLeaf tree "check").Description)

[<Fact>]
let ``fromUnion uses attribute name when provided`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"

    match tree with
    | CommandTree.Group group ->
        let names = group.Children |> List.map CommandTree.name
        test <@ List.contains "compile" names @> // Build with Name = "compile"
        test <@ List.contains "fmt" names @> // Format with Name = "fmt"
        test <@ not (List.contains "build" names) @>
        test <@ not (List.contains "format" names) @>
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``fromUnion uses custom name with explicit description`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"
    Assert.Equal("Format code", (getLeaf tree "fmt").Description)

[<Fact>]
let ``fromUnion creates groups for nested unions`` () =
    let tree = CommandReflection.fromUnion<NestedCommand> "Test"

    match tree with
    | CommandTree.Group group ->
        let devNode = group.Children |> List.find (fun c -> CommandTree.name c = "dev")

        match devNode with
        | CommandTree.Group devGroup ->
            let names = devGroup.Children |> List.map CommandTree.name
            test <@ List.contains "check" names @>
            test <@ List.contains "build" names @>
            test <@ List.contains "test" names @>
        | CommandTree.Leaf _ -> failwith "Expected dev to be a group"
    | CommandTree.Leaf _ -> failwith "Expected root group"

[<Fact>]
let ``fromUnion handles CmdDefault attribute`` () =
    let tree = CommandReflection.fromUnion<NestedCommand> "Test"

    match tree with
    | CommandTree.Group group ->
        let devNode = group.Children |> List.find (fun c -> CommandTree.name c = "dev")

        match devNode with
        | CommandTree.Group devGroup -> test <@ devGroup.Default.IsSome @>
        | CommandTree.Leaf _ -> failwith "Expected dev to be a group"
    | CommandTree.Leaf _ -> failwith "Expected root group"

[<Fact>]
let ``caseName returns kebab-case name of command`` () =
    Assert.Equal("check", CommandReflection.caseName MinimalCommand.Check)
    Assert.Equal("test-suite", CommandReflection.caseName MinimalCommand.TestSuite)
    Assert.Equal("file-coverage", CommandReflection.caseName (MinimalCommand.FileCoverage "test.fs"))

[<Fact>]
let ``formatCmd formats simple command`` () =
    test <@ CommandReflection.formatCmd MinimalCommand.Check = "check" @>

[<Fact>]
let ``formatCmd formats command with argument`` () =
    test <@ CommandReflection.formatCmd (MinimalCommand.FileCoverage "test.fs") = "file-coverage test.fs" @>

[<Fact>]
let ``formatCmd respects CmdAttribute Name override`` () =
    test <@ CommandReflection.formatCmd AttributedCommand.Build = "compile" @>
    test <@ CommandReflection.formatCmd AttributedCommand.Format = "fmt" @>

[<Fact>]
let ``formatCmd formats nested command`` () =
    test <@ CommandReflection.formatCmd (NestedCommand.Dev DevSubCommand.Build) = "dev build" @>

[<Fact>]
let ``formatCmd formats nested command with default`` () =
    test <@ CommandReflection.formatCmd (NestedCommand.Dev DevSubCommand.Check) = "dev check" @>

type TypesCommand =
    | Run of count: int64
    | Toggle of flag: bool
    | Lookup of id: System.Guid
    | MaybeNum of n: int option

[<Fact>]
let ``parseFieldValue handles int64`` () =
    let result = CommandReflection.parseFieldValue typeof<int64> "42"
    test <@ result = Ok(Some(box 42L)) @>

[<Fact>]
let ``parseFieldValue returns None for invalid int64`` () =
    let result = CommandReflection.parseFieldValue typeof<int64> "notanumber"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue handles bool`` () =
    let result = CommandReflection.parseFieldValue typeof<bool> "true"
    test <@ result = Ok(Some(box true)) @>

[<Fact>]
let ``parseFieldValue returns None for invalid bool`` () =
    let result = CommandReflection.parseFieldValue typeof<bool> "notabool"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue handles Guid`` () =
    let guid = System.Guid.NewGuid()

    let result =
        CommandReflection.parseFieldValue typeof<System.Guid> (string<System.Guid> guid)

    test <@ result = Ok(Some(box guid)) @>

[<Fact>]
let ``parseFieldValue returns None for invalid Guid`` () =
    let result = CommandReflection.parseFieldValue typeof<System.Guid> "notaguid"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue handles option None for empty string`` () =
    let result = CommandReflection.parseFieldValue typeof<int option> ""

    match result with
    | Ok(Some v) ->
        let unboxed = v :?> int option
        test <@ unboxed = None @>
    | other -> failwith $"Expected Ok(Some ...), got: %O{other}"

[<Fact>]
let ``parseFieldValue returns None for option with invalid inner value`` () =
    let result = CommandReflection.parseFieldValue typeof<int option> "notanumber"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue handles float`` () =
    let result = CommandReflection.parseFieldValue typeof<float> "3.14"
    test <@ result = Ok(Some(box 3.14)) @>

[<Fact>]
let ``parseFieldValue returns None for invalid float`` () =
    let result = CommandReflection.parseFieldValue typeof<float> "notafloat"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue handles decimal`` () =
    let result = CommandReflection.parseFieldValue typeof<decimal> "99.99"
    test <@ result = Ok(Some(box 99.99m)) @>

[<Fact>]
let ``parseFieldValue returns None for invalid decimal`` () =
    let result = CommandReflection.parseFieldValue typeof<decimal> "notadecimal"
    test <@ result = Ok None @>

[<Fact>]
let ``parseFieldValue returns None for unknown type`` () =
    let result = CommandReflection.parseFieldValue typeof<System.DateTime> "2024-01-01"
    test <@ result = Ok None @>

// DU cases (read via UnionCaseInfo.GetCustomAttributes) and record fields (CmdArg
// only, via PropertyInfo.GetCustomAttributes) are the only placements CommandTree
// reflects over. Attaching every attribute kind to both and reading each back proves
// the declared AttributeTargets covers the placements actually in use.
type AllAttrsRecord =
    { [<CmdArg("Record field arg")>]
      Path: string }

type AllAttrsFlag =
    | [<CmdFlag(Name = "lvl", Short = "l", Description = "Log level"); CmdEnv("LVL")>] Level of string
    | [<CmdEnvRaw("NO_CACHE")>] NoCache

type AllAttrsCommand =
    | [<Cmd("Run with everything");
        CmdArg("Positional", Default = "x");
        CmdCompletion("a", "b");
        CmdFileCompletion(FieldIndex = 1);
        CmdExample("a", "b")>] Run of pos: string * file: string
    | [<CmdDefault; Cmd("Default leaf")>] Idle
    | Flagged of AllAttrsFlag list
    | Recorded of AllAttrsRecord

[<Fact>]
let ``all Cmd attributes are readable off their real placements`` () =
    let unionCase name =
        FSharp.Reflection.FSharpType.GetUnionCases(typeof<AllAttrsCommand>)
        |> Array.find (fun c -> c.Name = name)

    let runCase = unionCase "Run"
    let idleCase = unionCase "Idle"

    // Cmd
    test <@ (runCase.GetCustomAttributes(typeof<CmdAttribute>)).Length = 1 @>
    // CmdDefault
    test <@ (idleCase.GetCustomAttributes(typeof<CmdDefaultAttribute>)).Length = 1 @>
    // CmdArg, CmdCompletion, CmdFileCompletion, CmdExample on the Run case
    test <@ (runCase.GetCustomAttributes(typeof<CmdArgAttribute>)).Length = 1 @>
    test <@ (runCase.GetCustomAttributes(typeof<CmdCompletionAttribute>)).Length = 1 @>
    test <@ (runCase.GetCustomAttributes(typeof<CmdFileCompletionAttribute>)).Length = 1 @>
    test <@ (runCase.GetCustomAttributes(typeof<CmdExampleAttribute>)).Length = 1 @>

    // CmdFlag, CmdEnv, CmdEnvRaw on flag DU cases
    let levelCase =
        FSharp.Reflection.FSharpType.GetUnionCases(typeof<AllAttrsFlag>)
        |> Array.find (fun c -> c.Name = "Level")

    let noCacheCase =
        FSharp.Reflection.FSharpType.GetUnionCases(typeof<AllAttrsFlag>)
        |> Array.find (fun c -> c.Name = "NoCache")

    test <@ (levelCase.GetCustomAttributes(typeof<CmdFlagAttribute>)).Length = 1 @>
    test <@ (levelCase.GetCustomAttributes(typeof<CmdEnvAttribute>)).Length = 1 @>
    test <@ (noCacheCase.GetCustomAttributes(typeof<CmdEnvRawAttribute>)).Length = 1 @>

    // CmdArg on a record field (read as a PropertyInfo)
    let recordField = typeof<AllAttrsRecord>.GetProperty("Path")
    test <@ (recordField.GetCustomAttributes(typeof<CmdArgAttribute>, false)).Length = 1 @>

    // End-to-end: the tree builds and the attributes feed through
    let tree = CommandReflection.fromUnion<AllAttrsCommand> "Test"

    match tree with
    | CommandTree.Group _ -> ()
    | CommandTree.Leaf _ -> failwith "Expected a group"

/// Run a function on a dedicated thread whose culture is set to the given culture.
/// A thread's CurrentCulture is thread-local, so this isolates the mutation from
/// the parallel test runner without serializing collections.
let private runWithCulture (cultureName: string) (f: unit -> 'a) : 'a =
    let mutable result = Unchecked.defaultof<'a>
    let mutable err = None

    let thread =
        System.Threading.Thread(fun () ->
            try
                let culture = System.Globalization.CultureInfo(cultureName)
                System.Threading.Thread.CurrentThread.CurrentCulture <- culture
                System.Threading.Thread.CurrentThread.CurrentUICulture <- culture
                result <- f ()
            with e ->
                err <- Some e)

    thread.Start()
    thread.Join()

    match err with
    | Some e -> raise e
    | None -> result

[<Fact>]
let ``parseFieldValue parses float culture-invariantly under de-DE`` () =
    // de-DE uses ',' as decimal separator; "1.5" must still parse to 1.5, not 15.
    let result =
        runWithCulture "de-DE" (fun () -> CommandReflection.parseFieldValue typeof<float> "1.5")

    test <@ result = Ok(Some(box 1.5)) @>

[<Fact>]
let ``parseFieldValue parses decimal culture-invariantly under de-DE`` () =
    let result =
        runWithCulture "de-DE" (fun () -> CommandReflection.parseFieldValue typeof<decimal> "1.5")

    test <@ result = Ok(Some(box 1.5m)) @>

[<Fact>]
let ``formatFieldValue formats float culture-invariantly under de-DE`` () =
    let result =
        runWithCulture "de-DE" (fun () -> CommandReflection.formatFieldValue (box 1.5))

    test <@ result = "1.5" @>

[<Fact>]
let ``formatFieldValue formats decimal culture-invariantly under de-DE`` () =
    let result =
        runWithCulture "de-DE" (fun () -> CommandReflection.formatFieldValue (box 1.5m))

    test <@ result = "1.5" @>

[<Fact>]
let ``formatFieldValue handles int64`` () =
    let result = CommandReflection.formatFieldValue (box 42L)
    test <@ result = "42" @>

[<Fact>]
let ``formatFieldValue handles bool`` () =
    let result = CommandReflection.formatFieldValue (box true)
    test <@ result = "True" @>

[<Fact>]
let ``formatFieldValue handles Guid`` () =
    let guid = System.Guid.NewGuid()
    let result = CommandReflection.formatFieldValue (box guid)
    test <@ result = (string<System.Guid> guid) @>

[<Fact>]
let ``formatFieldValue handles None option`` () =
    let none: int option = None
    let result = CommandReflection.formatFieldValue (box none)
    test <@ result = "" @>

[<Fact>]
let ``formatFieldValue handles float`` () =
    let result = CommandReflection.formatFieldValue (box 3.14)
    test <@ result = "3.14" @>

[<Fact>]
let ``formatFieldValue handles decimal`` () =
    let result = CommandReflection.formatFieldValue (box 99.99m)
    test <@ result = "99.99" @>

[<Fact>]
let ``formatFieldValue handles unknown type`` () =
    let result = CommandReflection.formatFieldValue (box (System.DateTime(2024, 1, 1)))
    test <@ result = (string<obj> (System.DateTime(2024, 1, 1))) @>

[<Fact>]
let ``formatFieldValue handles int`` () =
    let result = CommandReflection.formatFieldValue (box 42)
    test <@ result = "42" @>

[<Fact>]
let ``parseFieldValue union type ignores cases with fields`` () =
    // MixedFieldUnion has Simple (0 fields) and WithArg (1 field)
    // parseFieldValue should match Simple and skip WithArg due to field count check
    let result = CommandReflection.parseFieldValue typeof<MixedFieldUnion> "simple"
    test <@ result = Ok(Some(box MixedFieldUnion.Simple)) @>

[<Fact>]
let ``parseFieldValue union type does not match case with fields`` () =
    // "with-arg" matches the name but WithArg has 1 field, so it's filtered out
    let result = CommandReflection.parseFieldValue typeof<MixedFieldUnion> "with-arg"
    test <@ result = Ok None @>

[<Fact>]
let ``toDescription handles empty string`` () =
    let result = CommandReflection.toDescription ""
    test <@ result = "" @>

[<Fact>]
let ``formatFieldValue handles Some option`` () =
    let value: int option = Some 42
    let result = CommandReflection.formatFieldValue (box value)
    test <@ result = "42" @>

[<Fact>]
let ``formatFieldValue handles null`` () =
    let result = CommandReflection.formatFieldValue null
    test <@ result = "" @>

[<Fact>]
let ``parseFieldValue matches union case by reverse prefix`` () =
    // Input "checking" is longer than case name "check" (8 vs 5)
    // shorter = 5 >= 3, and "checking".StartsWith("check") = true
    // DevSubCommand has Check, Build, Test — only Check matches
    let result = CommandReflection.parseFieldValue typeof<DevSubCommand> "checking"
    test <@ result = Ok(Some(box DevSubCommand.Check)) @>

[<Fact>]
let ``parseFieldValue matches a two-character case name typed in full`` () =
    // REGRESSION: the prefix floor compares the SHORTER of the two strings, so
    // for "qa" vs case "qa" it is 2, below the >= 3 floor — every candidate was
    // filtered out and the field parsed as "no match", failing the whole command
    // with "Invalid arguments". A case name typed in FULL must always select it.
    let result = CommandReflection.parseFieldValue typeof<ShortCaseSubCommand> "qa"
    test <@ result = Ok(Some(box ShortCaseSubCommand.Qa)) @>

[<Fact>]
let ``parseFieldValue prefers an exact case name over a longer case it prefixes`` () =
    // "qa" is a strict prefix of "qa-failed". Exactness, not order, decides.
    let exact = CommandReflection.parseFieldValue typeof<ShortCaseSubCommand> "qa"

    let longer =
        CommandReflection.parseFieldValue typeof<ShortCaseSubCommand> "qa-failed"

    test <@ exact = Ok(Some(box ShortCaseSubCommand.Qa)) @>
    test <@ longer = Ok(Some(box ShortCaseSubCommand.QaFailed)) @>

[<Fact>]
let ``parseFieldValue still refuses an abbreviation matching several cases`` () =
    // The floor's real job survives: "ba" is nobody's full name, so it stays a
    // non-match rather than silently picking Backlog.
    let result = CommandReflection.parseFieldValue typeof<ShortCaseSubCommand> "ba"
    test <@ result = Ok None @>

[<Fact>]
let ``parse and format roundtrip for int64 command`` () =
    let tree = CommandReflection.fromUnion<TypesCommand> "Test"
    let result = CommandTree.parse tree [| "run"; "100" |]
    Assert.Equal(Ok(TypesCommand.Run 100L), result)
    let formatted = CommandTree.format tree (TypesCommand.Run 100L) "cmd"
    Assert.Equal(Some "cmd run 100", formatted)

[<Fact>]
let ``parse and format roundtrip for bool command`` () =
    let tree = CommandReflection.fromUnion<TypesCommand> "Test"
    let result = CommandTree.parse tree [| "toggle"; "true" |]
    Assert.Equal(Ok(TypesCommand.Toggle true), result)

[<Fact>]
let ``parse and format roundtrip for Guid command`` () =
    let guid = System.Guid.Parse("12345678-1234-1234-1234-123456789abc")
    let tree = CommandReflection.fromUnion<TypesCommand> "Test"

    let result =
        CommandTree.parse tree [| "lookup"; "12345678-1234-1234-1234-123456789abc" |]

    Assert.Equal(Ok(TypesCommand.Lookup guid), result)

type ListFormatCommand = | [<Cmd("Tag files")>] Tag of tag: string * files: string list

type InvalidListPosition = Bad of files: string list * tag: string
type MultipleListFields = Bad of files: string list * more: string list

[<Fact>]
let ``formatFieldValue handles list of strings`` () =
    let value: string list = [ "a.fs"; "b.fs"; "c.fs" ]
    let result = CommandReflection.formatFieldValue (box value)
    test <@ result = "a.fs b.fs c.fs" @>

[<Fact>]
let ``formatFieldValue handles list of ints`` () =
    let value: int list = [ 1; 2; 3 ]
    let result = CommandReflection.formatFieldValue (box value)
    test <@ result = "1 2 3" @>

[<Fact>]
let ``formatFieldValue handles empty list`` () =
    let value: string list = []
    let result = CommandReflection.formatFieldValue (box value)
    test <@ result = "" @>

[<Fact>]
let ``fromUnion list field has correct type name`` () =
    let leaf = CommandReflection.fromUnion<ListFormatCommand> "Test" |> getLeaf <| "tag"
    let filesArg = leaf.Args |> List.find (fun a -> a.Name = "files")
    test <@ filesArg.TypeName = "string list" @>
    test <@ filesArg.IsList = true @>
    test <@ filesArg.IsOptional = false @>

[<Fact>]
let ``formatCmd formats command with list field`` () =
    test <@ CommandReflection.formatCmd (ListFormatCommand.Tag("v1", [ "a.fs"; "b.fs" ])) = "tag v1 a.fs b.fs" @>

[<Fact>]
let ``format roundtrip for list field command`` () =
    let tree = CommandReflection.fromUnion<ListFormatCommand> "Test"

    let result =
        CommandTree.format tree (ListFormatCommand.Tag("v1", [ "a.fs"; "b.fs" ])) "cmd"

    Assert.Equal(Some "cmd tag v1 a.fs b.fs", result)

[<Fact>]
let ``fromUnion throws when list field is not last`` () =
    Assert.Throws<System.InvalidOperationException>(fun () ->
        CommandReflection.fromUnion<InvalidListPosition> "Test" |> ignore)

[<Fact>]
let ``fromUnion throws when multiple list fields`` () =
    Assert.Throws<System.InvalidOperationException>(fun () ->
        CommandReflection.fromUnion<MultipleListFields> "Test" |> ignore)

// Item 5: unsupported field types must fail fast at construction time with a
// helpful message, not silently produce "Invalid arguments" at parse time.
type UnsupportedFieldCommand = | [<Cmd("When")>] At of timestamp: System.DateTimeOffset

type UnsupportedOptionFieldCommand = | [<Cmd("Maybe when")>] At of timestamp: System.DateTimeOffset option

type UnsupportedRecordArgs = { When: System.DateTimeOffset }

type UnsupportedRecordCommand = | [<Cmd("Record")>] At of UnsupportedRecordArgs

[<Fact>]
let ``fromUnion throws for unsupported positional field type`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnion<UnsupportedFieldCommand> "Test" |> ignore)

    test <@ ex.Message.Contains("at") @> // case name
    test <@ ex.Message.Contains("timestamp") @> // field name
    test <@ ex.Message.Contains("DateTimeOffset") @> // offending type
    test <@ ex.Message.Contains("string") @> // lists at least one supported type

[<Fact>]
let ``fromUnion throws for unsupported optional field type`` () =
    Assert.Throws<System.InvalidOperationException>(fun () ->
        CommandReflection.fromUnion<UnsupportedOptionFieldCommand> "Test" |> ignore)

[<Fact>]
let ``fromUnion throws for unsupported record field type`` () =
    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnion<UnsupportedRecordCommand> "Test" |> ignore)

    test <@ ex.Message.Contains("DateTimeOffset") @>

type EnvFlag =
    | [<CmdEnv("LVL")>] LogLevel of string
    | [<CmdEnvRaw("NO_CACHE")>] NoCache
    | Verbose

type DeployFlag =
    | DryRun
    | [<CmdFlag(Short = "e")>] Env of string
    | Verbose

[<Fact>]
let ``CmdEnv attribute exposes suffix`` () =
    let cases = FSharp.Reflection.FSharpType.GetUnionCases(typeof<EnvFlag>)
    let logLevel = cases |> Array.find (fun c -> c.Name = "LogLevel")
    let attrs = logLevel.GetCustomAttributes(typeof<CmdEnvAttribute>)
    test <@ attrs.Length = 1 @>
    let attr = attrs.[0] :?> CmdEnvAttribute
    test <@ attr.Suffix = "LVL" @>

[<Fact>]
let ``CmdEnvRaw attribute exposes full var name`` () =
    let cases = FSharp.Reflection.FSharpType.GetUnionCases(typeof<EnvFlag>)
    let noCache = cases |> Array.find (fun c -> c.Name = "NoCache")
    let attrs = noCache.GetCustomAttributes(typeof<CmdEnvRawAttribute>)
    test <@ attrs.Length = 1 @>
    let attr = attrs.[0] :?> CmdEnvRawAttribute
    test <@ attr.VarName = "NO_CACHE" @>

[<Fact>]
let ``getFlagInfoFromDU generates flag info from union cases`` () =
    let flagInfo = CommandReflection.getFlagInfoFromDU typeof<DeployFlag> None
    test <@ flagInfo.Length = 3 @>

    let dryRun = flagInfo |> List.find (fun f -> f.LongName = "dry-run")
    test <@ dryRun.Arity = FlagArity.Nullary @>
    test <@ dryRun.TypeName = "bool" @>
    test <@ dryRun.EnvVar = None @>

    let env = flagInfo |> List.find (fun f -> f.LongName = "env")
    test <@ env.Arity = FlagArity.Required @>
    test <@ env.TypeName = "string" @>
    test <@ env.ShortName = Some "e" @>

    let verbose = flagInfo |> List.find (fun f -> f.LongName = "verbose")
    test <@ verbose.Arity = FlagArity.Nullary @>

[<Fact>]
let ``getFlagInfoFromDU with env prefix auto-derives env var names`` () =
    let flagInfo = CommandReflection.getFlagInfoFromDU typeof<DeployFlag> (Some "MYAPP")

    let dryRun = flagInfo |> List.find (fun f -> f.LongName = "dry-run")
    test <@ dryRun.EnvVar = Some { VarName = "MYAPP_DRY_RUN" } @>

    let env = flagInfo |> List.find (fun f -> f.LongName = "env")
    test <@ env.EnvVar = Some { VarName = "MYAPP_ENV" } @>

    let verbose = flagInfo |> List.find (fun f -> f.LongName = "verbose")
    test <@ verbose.EnvVar = Some { VarName = "MYAPP_VERBOSE" } @>

[<Fact>]
let ``getFlagInfoFromDU respects CmdEnv suffix override`` () =
    let flagInfo = CommandReflection.getFlagInfoFromDU typeof<EnvFlag> (Some "MYAPP")
    let logLevel = flagInfo |> List.find (fun f -> f.LongName = "log-level")
    test <@ logLevel.EnvVar = Some { VarName = "MYAPP_LVL" } @>

[<Fact>]
let ``getFlagInfoFromDU respects CmdEnvRaw full override`` () =
    let flagInfo = CommandReflection.getFlagInfoFromDU typeof<EnvFlag> (Some "MYAPP")
    let noCache = flagInfo |> List.find (fun f -> f.LongName = "no-cache")
    test <@ noCache.EnvVar = Some { VarName = "NO_CACHE" } @>

[<Fact>]
let ``getFlagInfoFromDU short flag collision avoidance`` () =
    // DeployFlag: DryRun and Verbose don't collide (d vs v)
    // but if two flags started with same letter, short would be None
    let flagInfo = CommandReflection.getFlagInfoFromDU typeof<DeployFlag> None
    let dryRun = flagInfo |> List.find (fun f -> f.LongName = "dry-run")
    test <@ dryRun.ShortName = Some "d" @>
    let verbose = flagInfo |> List.find (fun f -> f.LongName = "verbose")
    test <@ verbose.ShortName = Some "v" @>

type FloatCommand = | [<Cmd("Compute")>] Compute of value: float

type DecimalCommand = | [<Cmd("Price")>] Price of amount: decimal

[<Fact>]
let ``fromUnion generates correct typeName for float field`` () =
    let tree = CommandReflection.fromUnion<FloatCommand> "Test"

    match tree with
    | CommandTree.Group { Children = [ child ] } ->
        let args = CommandTree.args child
        test <@ args.Length = 1 @>
        test <@ args.[0].TypeName = "float" @>
    | _ -> failwith "Expected group with one child"

[<Fact>]
let ``fromUnion generates correct typeName for decimal field`` () =
    let tree = CommandReflection.fromUnion<DecimalCommand> "Test"

    match tree with
    | CommandTree.Group { Children = [ child ] } ->
        let args = CommandTree.args child
        test <@ args.Length = 1 @>
        test <@ args.[0].TypeName = "decimal" @>
    | _ -> failwith "Expected group with one child"

type AmbigUnion =
    | Start
    | Stop
    | Status

[<Fact>]
let ``parseFieldValue returns Error for option with ambiguous union inner`` () =
    // "sta" matches start and status — ambiguous
    let result = CommandReflection.parseFieldValue typeof<AmbigUnion option> "sta"

    match result with
    | Error(AmbiguousValue(input, candidates)) ->
        test <@ input = "sta" @>
        test <@ candidates |> List.length > 1 @>
    | _ -> failwith "Expected Error for ambiguous union in option"

type IntFlag =
    | Count of int
    | Verbose

type IntFlagCmd = | [<Cmd("Run")>] Run of IntFlag list

[<Fact>]
let ``parse returns InvalidArguments for unparseable DU flag value`` () =
    let tree = CommandReflection.fromUnion<IntFlagCmd> "Test"
    let result = CommandTree.parse tree [| "run"; "--count"; "notanumber" |]

    match result with
    | Error(InvalidArguments("run", msg)) -> test <@ msg.Contains("Invalid value") @>
    | other -> failwith $"Expected InvalidArguments, got: %O{other}"

type EnvBoolFlag =
    | Verbose
    | Count of int

type EnvBoolCmd = | [<Cmd("Run")>] Run of EnvBoolFlag list

[<Fact>]
let ``env var with invalid boolean value returns error`` () =
    System.Environment.SetEnvironmentVariable("ENVT_VERBOSE", "maybe")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvBoolCmd> "Test" "ENVT"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Error(InvalidArguments("env", msg)) -> test <@ msg.Contains("expected true/false/1/0") @>
        | other -> failwith $"Expected InvalidArguments, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("ENVT_VERBOSE", null)

[<Fact>]
let ``env var with invalid typed value returns error`` () =
    System.Environment.SetEnvironmentVariable("ENVT_COUNT", "notanumber")

    try
        let tree = CommandReflection.fromUnionWithEnv<EnvBoolCmd> "Test" "ENVT"
        let result = CommandTree.parse tree [| "run" |]

        match result with
        | Error(InvalidArguments("env", msg)) -> test <@ msg.Contains("Invalid value") @>
        | other -> failwith $"Expected InvalidArguments, got: %O{other}"
    finally
        System.Environment.SetEnvironmentVariable("ENVT_COUNT", null)
