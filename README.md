<!-- sync:intro:start -->
# CommandTree

Define CLI commands as F# discriminated unions. Get type-safe parsing, help generation, and fish completions from your types.

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

let tree = CommandReflection.fromUnion<Command> "Example project management CLI"

match CommandTree.parse tree argv with
| Ok(Task(Add(title, _))) -> printfn "Adding %s" title
| Ok Help -> printfn "%s" (CommandTree.helpFull tree "my-cli")
| Error(HelpRequested path) -> printfn "%s" (CommandTree.helpForPath tree path "my-cli")
| Error(UnknownCommand(input, _)) -> UI.fail $"Unknown command: %s{input}"
| _ -> ()
```
<!-- sync:intro:end -->

## Installation

```bash
dotnet add package CommandTree
```

<!-- sync:howitworks:start -->
## How It Works

Case names become kebab-case commands. Nested unions become subcommand groups. Fields become typed arguments.

```fsharp
// From examples/ExampleCli/Program.fs
type DbCommand =
    | [<Cmd("Run database migrations")>] Migrate
    | [<Cmd("Reset the database")>] Reset
    | [<Cmd("Show connection status"); CmdDefault>] Status  // default when "db" is invoked alone

type DeployCommand =
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>] Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>] Status of env: string option

type JobCommand =
    | [<Cmd("Start a new job")>] Start of name: string * size: int64 * verbose: bool
    | [<Cmd("Check job status")>] Status of id: Guid
    | [<Cmd("List recent jobs"); CmdDefault>] List

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Database operations")>] Db of DbCommand
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("Job management")>] Job of JobCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Show full help")>] Help
```

- `[<Cmd("desc")>]` sets help text; optional `Name = "custom"` overrides the command name
- `[<CmdDefault>]` marks the default subcommand when a group is invoked without arguments
- `[<CmdCompletion("a", "b")>]` provides fish shell completion values
- `[<CmdFileCompletion>]` enables file path completion in fish
<!-- sync:howitworks:end -->

<!-- sync:basicusage:start -->
## Basic Usage

Full example: [`examples/ExampleCli/Program.fs`](examples/ExampleCli/Program.fs)

```fsharp
// From examples/ExampleCli/Program.fs
open CommandTree

let tree = CommandReflection.fromUnion<Command> "Example project management CLI"
let cmdName = "example-cli"

[<EntryPoint>]
let main argv =
    match CommandTree.parse tree argv with
    | Ok cmd ->
        run tree cmdName cmd
        0
    | Error(HelpRequested path) ->
        printfn "%s" (CommandTree.helpForPath tree path cmdName)
        0
    | Error(UnknownCommand(input, path)) ->
        UI.fail $"Unknown command: %s{input}"
        printfn "%s" (CommandTree.helpForPath tree path cmdName)
        1
    | Error(InvalidArguments(cmd, msg)) ->
        UI.fail $"Invalid arguments for %s{cmd}: %s{msg}"
        1
    | Error(AmbiguousArgument(input, candidates)) ->
        let joined = String.concat ", " candidates
        UI.fail $"Ambiguous: '{input}' matches: {joined}"
        1
```
<!-- sync:basicusage:end -->

<!-- sync:reference:start -->
## Reference

### Parsing & Help

```fsharp
CommandTree.parse tree args              // Result<'Cmd, ParseError>
CommandTree.help tree path prefix        // Help text for one level
CommandTree.helpFull tree prefix         // Full recursive help
CommandTree.helpForPath tree path prefix // Help for a subcommand path
CommandTree.format tree cmd path prefix  // Format command back to CLI string
CommandTree.findByPath tree path         // Navigate to a subtree
CommandTree.closestGroupPath tree args   // Deepest matching group path
```

### Reflection

```fsharp
CommandReflection.fromUnion<'Cmd> "desc"     // Build tree from DU
CommandReflection.formatCmd cmd              // Format command to CLI string
CommandReflection.caseName value             // Kebab-case name of union value
CommandReflection.toKebabCase "PascalCase"   // "pascal-case"
CommandReflection.parseFieldValue type str   // Result<obj option, string>
CommandReflection.formatFieldValue value     // Typed value to string
```

### Fish Completions

```fsharp
FishCompletions.generateContent tree "my-tool"  // Generate .fish content
FishCompletions.writeToFile tree "my-tool"      // Write to ~/.config/fish/completions/
FishCompletions.installHook "my-tool"           // Auto-update hook in conf.d
```

### Supported Field Types

| Type | Example | Notes |
|------|---------|-------|
| `string` | `of name: string` | Any string value |
| `int` | `of count: int` | Int32 |
| `int64` | `of id: int64` | Int64 |
| `float` | `of rate: float` | Double |
| `decimal` | `of price: decimal` | Decimal |
| `bool` | `of force: bool` | Boolean |
| `Guid` | `of id: Guid` | Guid |
| `'T option` | `of env: string option` | None when omitted |
| Union | `of env: Priority` | Kebab-case name, prefix matching (min 3 chars) |
<!-- sync:reference:end -->

The library also includes `Process` (process execution helpers) and `UI` (colored terminal output) modules. See the [API docs](https://michaelglass.github.io/CommandTree/) for details.

<!-- sync:license:start -->
## License

MIT
<!-- sync:license:end -->
