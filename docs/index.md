<!-- sync:intro -->
# CommandTree

Define your CLI commands as F# discriminated unions. Get type-safe parsing, automatic help generation, and fish shell completions -- all from your type definitions.

```fsharp
// From examples/ExampleCli/Program.fs
type TaskCommand =
    | [<Cmd("Add a new task")>] Add of title: string * priority: Priority option
    | [<Cmd("List all tasks"); CmdDefault>] List
    | [<Cmd("Complete a task")>] Complete of id: int
    | [<Cmd("Remove a task")>] Remove of id: int

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Show full help")>] Help

let tree = CommandReflection.fromUnion<Command> "My CLI tool"

match CommandTree.parse tree argv with
| Ok(Task(Add(title, _))) -> printfn "Adding %s" title
| Ok Test -> printfn "Running tests"
| Ok Help -> printfn "%s" (CommandTree.helpFull tree "my-cli")
| Ok cmd -> printfn "%s" (CommandReflection.formatCmd cmd)
| Error msg -> UI.fail msg
```

**What you get:**
- Commands derived from union cases (PascalCase to kebab-case)
- Nested unions become subcommand groups with optional defaults
- Arguments parsed from case fields (string, int, int64, bool, Guid, option)
- Help text auto-generated from the tree structure
- Fish shell completions from a single function call
<!-- sync:intro:end -->

## Installation

```bash
dotnet add package CommandTree
```

**[API Reference](reference/index.html)**

<!-- sync:howitworks -->
## How It Works

Commands are discriminated unions. Case names become command names via kebab-case conversion:

```fsharp
// From examples/ExampleCli/Program.fs — names derived automatically
type TaskCommand =
    | Add of title: string * priority: Priority option  // "add <title> [priority]"
    | List                                               // "list"
    | Complete of id: int                                // "complete <id>"
    | Remove of id: int                                  // "remove <id>"
```

Nested unions become subcommand groups:

```fsharp
// From examples/ExampleCli/Program.fs
type DbCommand =
    | [<Cmd("Run database migrations")>] Migrate
    | [<Cmd("Reset the database")>] Reset
    | [<Cmd("Show connection status"); CmdDefault>] Status  // default when "db" is invoked alone

type Command =
    | [<Cmd("Database operations")>] Db of DbCommand  // "db migrate", "db reset", "db status"
    | [<Cmd("Run the test suite")>] Test               // "test"
```

Fields become arguments with type-safe parsing:

```fsharp
// From examples/ExampleCli/Program.fs
type TaskCommand =
    | Add of title: string * priority: Priority option  // string + optional union
type JobCommand =
    | Start of name: string * size: int64 * verbose: bool  // string + int64 + bool
    | Status of id: Guid                                    // guid
type DeployCommand =
    | Push of env: string                                   // required string
    | Status of env: string option                          // optional string
```
<!-- sync:howitworks:end -->

<!-- sync:basicusage -->
## Basic Usage

The full example app is at [`examples/ExampleCli/Program.fs`](examples/ExampleCli/Program.fs). Here are the key parts:

```fsharp
// From examples/ExampleCli/Program.fs
open System
open CommandTree

type CoverageCommand =
    | [<Cmd("Show coverage for file"); CmdFileCompletion>] File of path: string
    | [<Cmd("Show coverage summary"); CmdDefault>] Summary

type DeployCommand =
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>] Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>] Status of env: string option

type Command =
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("Code coverage")>] Coverage of CoverageCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Fish shell completions")>] Fish of FishDemoCommand
    | [<Cmd("Show full help")>] Help

let tree = CommandReflection.fromUnion<Command> "Example project management CLI"
let cmdName = "example-cli"

let rec run (tree: CommandTree<Command>) (cmdName: string) (cmd: Command) =
    match cmd with
    | Deploy(DeployCommand.Push env) ->
        UI.section $"Deploying to %s{env}"
        UI.success $"Deployed to %s{env}"
    | Deploy(DeployCommand.Status env) ->
        let e = env |> Option.defaultValue "dev"
        UI.info $"Deploy status for %s{e}: up to date"
    | Coverage(CoverageCommand.File path) -> UI.info $"Coverage for %s{path}: 87.5%%"
    | Coverage CoverageCommand.Summary -> UI.info "Overall: 82.3%%"
    | Test -> Process.dotnet "test"
    | Fish(FishDemoCommand.Generate) -> FishCompletions.writeToFile tree cmdName
    | Fish FishDemoCommand.Preview -> printfn "%s" (FishCompletions.generateContent tree cmdName)
    | Fish(FishDemoCommand.Install) -> FishCompletions.installHook cmdName
    | Help -> printfn "%s" (CommandTree.helpFull tree cmdName)

[<EntryPoint>]
let main argv =
    let args = if argv.Length > 0 then argv else [| "--help" |]

    match CommandTree.parse tree args with
    | Ok cmd ->
        run tree cmdName cmd
        0
    | Error "_help" ->
        printfn "%s" (CommandTree.help tree [] cmdName)
        0
    | Error msg when msg.EndsWith("_help") ->
        let groupName = msg.Replace("_help", "")
        printfn "%s" (CommandTree.helpForPath tree [ groupName ] cmdName)
        0
    | Error msg ->
        UI.fail msg
        let path = CommandTree.closestGroupPath tree (args |> Array.toList)
        printfn "%s" (CommandTree.helpForPath tree path cmdName)
        1
```
<!-- sync:basicusage:end -->

<!-- sync:reference -->
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
<!-- sync:reference:end -->

<!-- sync:license -->
## License

MIT
<!-- sync:license:end -->
