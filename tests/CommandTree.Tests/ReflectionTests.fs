module CommandTree.Tests.ReflectionTests

open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Test command types - minimal attributes
// =============================================================================

type MinimalCommand =
    | Check
    | Build
    | TestSuite
    | FileCoverage of path: string

// =============================================================================
// Test command types - with attributes
// =============================================================================

type AttributedCommand =
    | [<Cmd("Run all checks")>] Check
    | [<Cmd("Build the project", Name = "compile")>] Build
    | [<Cmd("Format code", Name = "fmt")>] Format

// =============================================================================
// Test command types - nested
// =============================================================================

type DevSubCommand =
    | [<CmdDefault>] Check
    | Build
    | Test

type NestedCommand =
    | Dev of DevSubCommand
    | [<Cmd("Show help")>] Help

// Union with cases that have fields (for parseFieldValue field count filter)
type MixedFieldUnion =
    | Simple
    | WithArg of x: int

// =============================================================================
// toKebabCase tests
// =============================================================================

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
let ``toKebabCase handles consecutive capitals`` () =
    Assert.Equal("htmlparser", CommandReflection.toKebabCase "HTMLParser")
    Assert.Equal("urlhandler", CommandReflection.toKebabCase "URLHandler")

// =============================================================================
// toDescription tests
// =============================================================================

[<Fact>]
let ``toDescription converts PascalCase to readable description`` () =
    Assert.Equal("File coverage", CommandReflection.toDescription "FileCoverage")
    Assert.Equal("Test suite", CommandReflection.toDescription "TestSuite")
    Assert.Equal("Check", CommandReflection.toDescription "Check")

// =============================================================================
// Minimal command tests (no attributes)
// =============================================================================

[<Fact>]
let ``fromUnion derives names from case names`` () =
    let tree = CommandReflection.fromUnion<MinimalCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let names = children |> List.map CommandTree.name
        test <@ List.contains "check" names @>
        test <@ List.contains "build" names @>
        test <@ List.contains "test-suite" names @>
        test <@ List.contains "file-coverage" names @>
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``fromUnion derives descriptions from case names`` () =
    let tree = CommandReflection.fromUnion<MinimalCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let checkNode = children |> List.find (fun c -> CommandTree.name c = "check")
        Assert.Equal("Check", CommandTree.desc checkNode)

        let testSuiteNode =
            children |> List.find (fun c -> CommandTree.name c = "test-suite")

        Assert.Equal("Test suite", CommandTree.desc testSuiteNode)
    | CommandTree.Leaf _ -> failwith "Expected group"

// =============================================================================
// Attributed command tests
// =============================================================================

[<Fact>]
let ``fromUnion uses attribute description when provided`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let checkNode = children |> List.find (fun c -> CommandTree.name c = "check")
        Assert.Equal("Run all checks", CommandTree.desc checkNode)
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``fromUnion uses attribute name when provided`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let names = children |> List.map CommandTree.name
        test <@ List.contains "compile" names @> // Build with Name = "compile"
        test <@ List.contains "fmt" names @> // Format with Name = "fmt"
        test <@ not (List.contains "build" names) @>
        test <@ not (List.contains "format" names) @>
    | CommandTree.Leaf _ -> failwith "Expected group"

[<Fact>]
let ``fromUnion uses custom name with explicit description`` () =
    let tree = CommandReflection.fromUnion<AttributedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let fmtNode = children |> List.find (fun c -> CommandTree.name c = "fmt")
        Assert.Equal("Format code", CommandTree.desc fmtNode)
    | CommandTree.Leaf _ -> failwith "Expected group"

// =============================================================================
// Nested command tests
// =============================================================================

[<Fact>]
let ``fromUnion creates groups for nested unions`` () =
    let tree = CommandReflection.fromUnion<NestedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let devNode = children |> List.find (fun c -> CommandTree.name c = "dev")

        match devNode with
        | CommandTree.Group(_, _, devChildren, _, _) ->
            let names = devChildren |> List.map CommandTree.name
            test <@ List.contains "check" names @>
            test <@ List.contains "build" names @>
            test <@ List.contains "test" names @>
        | CommandTree.Leaf _ -> failwith "Expected dev to be a group"
    | CommandTree.Leaf _ -> failwith "Expected root group"

[<Fact>]
let ``fromUnion handles CmdDefault attribute`` () =
    let tree = CommandReflection.fromUnion<NestedCommand> "Test"

    match tree with
    | CommandTree.Group(_, _, children, _, _) ->
        let devNode = children |> List.find (fun c -> CommandTree.name c = "dev")

        match devNode with
        | CommandTree.Group(_, _, _, defaultParse, _) -> test <@ defaultParse.IsSome @>
        | CommandTree.Leaf _ -> failwith "Expected dev to be a group"
    | CommandTree.Leaf _ -> failwith "Expected root group"

// =============================================================================
// caseName tests
// =============================================================================

[<Fact>]
let ``caseName returns kebab-case name of command`` () =
    Assert.Equal("check", CommandReflection.caseName MinimalCommand.Check)
    Assert.Equal("test-suite", CommandReflection.caseName MinimalCommand.TestSuite)
    Assert.Equal("file-coverage", CommandReflection.caseName (MinimalCommand.FileCoverage "test.fs"))

// =============================================================================
// formatCmd tests
// =============================================================================

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

// =============================================================================
// Type parsing/formatting coverage for int64, bool, Guid, option edge cases
// =============================================================================

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
let ``parse and format roundtrip for int64 command`` () =
    let tree = CommandReflection.fromUnion<TypesCommand> "Test"
    let result = CommandTree.parse tree [| "run"; "100" |]
    Assert.Equal(Ok(TypesCommand.Run 100L), result)
    let formatted = CommandTree.format tree (TypesCommand.Run 100L) [] "cmd"
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
