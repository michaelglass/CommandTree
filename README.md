# CommandTree

Define your CLI commands as F# discriminated unions. Get type-safe parsing, automatic help generation, and fish shell completions -- all from your type definitions.

```fsharp
type Command =
    | [<Cmd("Run all checks")>] Check
    | [<Cmd("Build the project")>] Build
    | [<Cmd("Deploy to environment")>]
      Deploy of env: string * force: bool option

let tree = CommandReflection.fromUnion<Command> "My CLI tool"

match CommandTree.parse tree (argv |> Array.skip 1) with
| Ok Check -> runChecks ()
| Ok Build -> runBuild ()
| Ok(Deploy(env, force)) -> deploy env (force |> Option.defaultValue false)
| Error msg -> printfn "%s" msg
```

**What you get:**
- Commands derived from union cases (PascalCase to kebab-case)
- Nested unions become subcommand groups with optional defaults
- Arguments parsed from case fields (string, int, int64, bool, Guid, option)
- Help text auto-generated from the tree structure
- Fish shell completions from a single function call

## Installation

```bash
dotnet add package CommandTree
```

## How It Works

Commands are discriminated unions. Case names become command names via kebab-case conversion:

```fsharp
type Command =
    | Check                         // "check"
    | FileCoverage of path: string  // "file-coverage <path>"
    | TestSuite                     // "test-suite"
```

Nested unions become subcommand groups:

```fsharp
type DevCommand =
    | [<CmdDefault>] Check    // default when "dev" is invoked alone
    | Build
    | Test

type Command =
    | Dev of DevCommand       // "dev check", "dev build", "dev test"
    | Help
```

Fields become arguments with type-safe parsing:

```fsharp
type Command =
    | Greet of name: string                     // required string
    | Add of x: int * y: int                    // two required ints
    | Deploy of env: string * force: bool option // string + optional bool
```

## Basic Usage

```fsharp
open CommandTree

// 1. Define commands as discriminated unions
type EnvCommand =
    | [<Cmd("Edit environment config"); CmdCompletion("dev", "staging", "prod")>]
      Edit of env: string option
    | [<Cmd("Show current config")>] Show

type CoverageCommand =
    | [<Cmd("Show coverage for file"); CmdFileCompletion>] File of path: string
    | [<Cmd("Show coverage summary")>] Summary

type Command =
    | [<Cmd("Environment management")>] Env of EnvCommand
    | [<Cmd("Code coverage")>] Coverage of CoverageCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Generate fish completions")>] Fish

// 2. Build tree from union type
let tree = CommandReflection.fromUnion<Command> "Project build tool"

// 3. Parse and handle commands
let run (cmd: Command) =
    match cmd with
    | Env(Edit env) -> printfn "Editing %s" (env |> Option.defaultValue "dev")
    | Env Show -> printfn "Showing config"
    | Coverage(File path) -> printfn "Coverage for %s" path
    | Coverage Summary -> printfn "Coverage summary"
    | Test -> Process.dotnet "test"
    | Fish -> FishCompletions.writeToFile tree "my-tool"

// 4. Entry point
[<EntryPoint>]
let main argv =
    match CommandTree.parse tree (argv |> Array.skip 1) with
    | Ok cmd -> run cmd; 0
    | Error msg when msg.EndsWith("_help") ->
        // Show help for the matching group
        let path = msg.Replace("_help", "").Split('/') |> Array.toList |> List.filter ((<>) "")
        printfn "%s" (CommandTree.helpForPath tree path "my-tool")
        0
    | Error msg ->
        UI.fail msg
        let path = CommandTree.closestGroupPath tree (argv |> Array.skip 1 |> Array.toList)
        printfn "%s" (CommandTree.helpForPath tree path "my-tool")
        1
```

## Reference

### Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[<Cmd("desc")>]` | Case | Set description; optionally `Name = "custom"` to override command name |
| `[<CmdDefault>]` | Case | Mark as default subcommand for its parent group |
| `[<CmdCompletion("a", "b")>]` | Case | Provide completion values for fish shell; `FieldIndex` targets specific field |
| `[<CmdFileCompletion>]` | Case | Enable file path completion in fish shell; `FieldIndex` targets specific field |

### CommandTree Module

```fsharp
CommandTree.parse tree args              // Parse string[] into Result<'Cmd, string>
CommandTree.help tree path prefix        // Help text for one level
CommandTree.helpFull tree prefix         // Full help with all subgroups expanded
CommandTree.helpForPath tree path prefix // Help for a specific subcommand path
CommandTree.format tree cmd path prefix  // Format command back to CLI string
CommandTree.findByPath tree path         // Navigate to a subtree
CommandTree.closestGroupPath tree args   // Find deepest matching group (for error help)
CommandTree.fishCompletions tree name    // Generate fish completion commands
```

### CommandReflection Module

```fsharp
CommandReflection.fromUnion<'Cmd> "desc"     // Build tree from discriminated union
CommandReflection.formatCmd cmd              // Format command to CLI string (no tree needed)
CommandReflection.caseName value             // Get kebab-case name of a union value
CommandReflection.toKebabCase "PascalCase"   // "pascal-case"
CommandReflection.parseFieldValue type str   // Parse a string into a typed value
CommandReflection.formatFieldValue value     // Format a typed value to string
```

### Process Module

```fsharp
Process.run "cmd" "args"                     // Run interactively with timing display
Process.runSilent "cmd" "args"               // Capture output, return (exitCode, stdout, stderr)
Process.runCommand "cmd" "args"              // Like runSilent but returns CommandResult record
Process.runAsync "cmd" "args"                // Task-based async execution
Process.runWithSpinner "msg" "cmd" "args"    // Run with spinner animation
Process.runInteractive "cmd" "args"          // No capture, returns exit code
Process.runWithEnv "cmd" "args" env          // Run with extra environment variables
Process.runSilentWithEnv "cmd" "args" env    // Silent with extra environment variables
Process.runSilentWithTimeout "cmd" "args" ms // Silent with optional timeout
Process.dotnet "build"                       // Shorthand for Process.run "dotnet"
Process.dotnetSpinner "msg" "build"          // Shorthand for Process.runWithSpinner "dotnet"
Process.runParallel tasks                    // Task.WhenAll wrapper
```

### UI Module

```fsharp
UI.title "Section Title"       // Bold cyan header
UI.section "Subsection"        // Bold cyan with arrow
UI.success "Done"              // Green checkmark
UI.fail "Error"                // Red X (to stderr)
UI.info "Note"                 // Blue info
UI.warn "Careful"              // Yellow warning
UI.skip "Skipped"              // Yellow skip marker
UI.dimInfo "Detail"            // Dim text
UI.cmd "dotnet" "build"        // Dim command echo
UI.timing elapsed              // Color-coded duration (green to red)
UI.withSpinner "msg" action    // Spinner animation during action
UI.withSpinnerQuiet "msg" action // Spinner that clears on completion
```

### Fish Shell Completions

```fsharp
FishCompletions.generateContent tree "my-tool"  // Generate .fish file content
FishCompletions.writeToFile tree "my-tool"      // Write to ~/.config/fish/completions/
FishCompletions.installHook "my-tool"           // Install auto-update hook in conf.d
```

### Supported Field Types

| Type | Example | Notes |
|------|---------|-------|
| `string` | `of name: string` | Any string value |
| `int` | `of count: int` | Parsed via Int32.TryParse |
| `int64` | `of id: int64` | Parsed via Int64.TryParse |
| `bool` | `of force: bool` | Parsed via Boolean.TryParse |
| `Guid` | `of id: Guid` | Parsed via Guid.TryParse |
| `'T option` | `of env: string option` | Optional; None when omitted |
| Union types | `of env: EnvKind` | Matched by kebab-case name with prefix matching (min 3 chars) |

## License

MIT
