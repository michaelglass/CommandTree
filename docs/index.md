<!-- sync:intro -->
# CommandTree

Define CLI commands as F# discriminated unions. Get type-safe parsing, help generation, and fish completions from your types.

```fsharp
// From examples/ExampleCli/Program.fs

// my-cli task add "Buy groceries"
// my-cli task add "Buy groceries" high
// my-cli task                             ← runs list (CmdDefault)
// my-cli task complete 5
type TaskCommand =
    | [<Cmd("Add a new task")>] Add of title: string * priority: Priority option
    | [<Cmd("List all tasks"); CmdDefault>] List
    | [<Cmd("Complete a task")>] Complete of id: int
    | [<Cmd("Remove a task")>] Remove of id: int

// my-cli --verbose task add "Buy groceries"   ← global flag before command
// my-cli task add "Buy groceries" --verbose   ← global flag after command
type GlobalFlag =
    | [<Cmd("Enable verbose output")>] Verbose
    | [<Cmd("Set log level"); CmdEnv("LVL")>] LogLevel of string

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Show full help")>] Help

let spec =
    CommandReflection.fromUnionWithGlobalsAndEnv<Command, GlobalFlag> "My CLI" "MYAPP"

match spec.Parse argv with
| Ok(globals, Task(Add(title, _))) -> printfn "Adding %s" title
| Ok(_, Help) -> printfn "%s" (CommandTree.helpWithGlobals spec.Tree spec.GlobalFlags "my-cli")
| Error(HelpRequested path) -> printfn "%s" (CommandTree.helpForPath spec.Tree path "my-cli")
| Error(UnknownCommand(input, _rest, _)) -> UI.fail $"Unknown command: %s{input}"
| _ -> ()
```
<!-- sync:intro:end -->

## Installation

```bash
dotnet add package CommandTree
```

**[API Reference](reference/index.html)**

<!-- sync:howitworks -->
## How It Works

Case names become kebab-case commands. Nested unions become subcommand groups. Fields become positional arguments.

```fsharp
// From examples/ExampleCli/Program.fs

// my-cli db migrate
// my-cli db                               ← runs status (CmdDefault)
type DbCommand =
    | [<Cmd("Run database migrations")>] Migrate
    | [<Cmd("Reset the database")>] Reset
    | [<Cmd("Show connection status"); CmdDefault>] Status

// my-cli deploy push staging
// my-cli deploy status prod
// my-cli deploy                           ← runs status with no env (CmdDefault)
type DeployCommand =
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>] Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>] Status of env: string option

// my-cli files tag v1.0 src/App.fs src/Lib.fs   ← list field collects 1+ remaining args
// my-cli files diff old.dll new.dll             ← multiple CmdFileCompletion with FieldIndex
type FilesCommand =
    | [<Cmd("Tag files with a label"); CmdFileCompletion>] Tag of label: string * files: string list
    | [<Cmd("Compare two DLLs"); CmdFileCompletion(FieldIndex = 0); CmdFileCompletion(FieldIndex = 1)>] Diff of
        oldDll: string *
        newDll: string

// my-cli job start build-assets 1024 true
// my-cli job status 550e8400-e29b-41d4-a716-446655440000
// my-cli job                              ← runs list (CmdDefault)
type JobCommand =
    | [<Cmd("Start a new job")>] Start of name: string * size: int64 * verbose: bool
    | [<Cmd("Check job status")>] Status of id: Guid
    | [<Cmd("List recent jobs"); CmdDefault>] List

// my-cli check --conf custom.json --strict --no-cache
type CheckFlag =
    | [<CmdFlag(Name = "conf", Short = "k")>] Config of string
    | [<Cmd("Enable strict checking")>] Strict
    | [<CmdEnvRaw("NO_CACHE")>] NoCache

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Database operations")>] Db of DbCommand
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("File operations")>] Files of FilesCommand
    | [<Cmd("Job management")>] Job of JobCommand
    | [<Cmd("Run checks")>] Check of CheckFlag list
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Show full help")>] Help
```

### Mapping rules

| F# definition | CLI invocation | Notes |
|---|---|---|
| `Task of TaskCommand` | `my-cli task ...` | Nested union becomes a subcommand group |
| `Test` | `my-cli test` | No-field case becomes a simple command |
| `Add of title: string` | `my-cli task add "Buy groceries"` | Fields become positional args |
| `Start of name: string * size: int64 * verbose: bool` | `my-cli job start build 1024 true` | Multiple fields in order |
| `Status of env: string option` | `my-cli deploy status prod` or `my-cli deploy status` | Option fields can be omitted |
| `Push of env: Priority` | `my-cli deploy push high` or `my-cli deploy push hig` | Union fields match by kebab-case prefix (min 3 chars) |
| `Tag of label: string * files: string list` | `my-cli files tag v1 a.fs b.fs` | List field (must be last) collects 1+ remaining args |
| `Check of CheckFlag list` | `my-cli check --conf x.json --strict` | DU flag list becomes named flags |
| `[<CmdDefault>] List` | `my-cli task` | Runs when group is invoked without a subcommand |
| `[<Cmd("desc", Name = "fmt")>] Format` | `my-cli fmt` | `Name` overrides the derived command name |

### Attributes

- `[<Cmd("desc")>]` sets help text; optional `Name = "custom"` overrides the command name. Description is optional — omit it to derive from the case name in sentence case
- `[<CmdDefault>]` marks the default subcommand when a group is invoked without arguments
- `[<CmdArg("desc")>]` documents a positional argument — shows in an `Arguments:` section in help. Apply to the case with `FieldIndex = N` (0-based) for multi-field commands. `Default = "value"` adds a default hint. For record-typed command args, apply directly to record fields instead
- `[<CmdExample("ex1", "ex2")>]` adds an `Examples:` section to help output. The command path is prepended automatically. Stack multiple attributes or pass multiple strings to one attribute
- `[<CmdCompletion("a", "b")>]` provides fish shell completion values; `FieldIndex` selects which argument
- `[<CmdFileCompletion>]` enables file path completion in fish (multiple allowed per case with `FieldIndex`)
- `[<CmdFlag(Name = "conf", Short = "k", Description = "...")>]` overrides flag name, short flag, or description on DU flag cases (all optional — names and descriptions are auto-derived)
- `[<CmdEnv("SUFFIX")>]` overrides the env var suffix for a flag (prefix still applied)
- `[<CmdEnvRaw("VAR_NAME")>]` sets the exact env var name, ignoring the prefix

```fsharp
// example-cli report generate coverage.xml
// example-cli report generate coverage.xml report.html --show-gaps
type MergeReportArgs =
    { [<CmdArg("Baseline Cobertura XML")>] Baseline: string
      [<CmdArg("Output file", Default = "diff.html")>] Output: string option }

type ReportCommand =
    | [<Cmd("Generate a coverage report")>]
      [<CmdArg("Path to Cobertura XML input")>]
      [<CmdArg("Output file", FieldIndex = 1, Default = "report.html")>]
      [<CmdExample("coverage.xml", "coverage.xml report.html --show-gaps")>]
      Generate of input: string * output: string option * ReportFlag list
    | [<Cmd("Diff two reports using record args")>]
      Diff of MergeReportArgs * ReportFlag list
```
<!-- sync:howitworks:end -->

<!-- sync:basicusage -->
## Basic Usage

Full example: [`examples/ExampleCli/Program.fs`](examples/ExampleCli/Program.fs)

### Without global options

```fsharp
open CommandTree

let tree = CommandReflection.fromUnion<Command> "My CLI"

[<EntryPoint>]
let main argv =
    match CommandTree.parse tree argv with
    | Ok cmd ->
        run cmd
        0
    | Error(HelpRequested path) ->
        printfn "%s" (CommandTree.helpForPath tree path "my-cli")
        0
    | Error e ->
        // UnknownCommand, InvalidArguments, AmbiguousArgument, UnknownFlag, DuplicateFlag
        UI.fail $"%A{e}"
        1
```

### With global options and env vars

```fsharp
// From examples/ExampleCli/Program.fs
// GlobalFlag defined above in the intro
open CommandTree

let spec =
    CommandReflection.fromUnionWithGlobalsAndEnv<Command, GlobalFlag> "Example project management CLI" "EXAMPLE"

let cmdName = "example-cli"

[<EntryPoint>]
let main argv =
    match spec.Parse argv with
    | Ok(globals, cmd) ->
        if globals |> List.contains GlobalFlag.Verbose then
            UI.dimInfo "Verbose mode enabled"

        globals
        |> List.iter (function
            | GlobalFlag.LogLevel level -> UI.dimInfo $"Log level: %s{level}"
            | _ -> ())

        run spec.Tree cmdName cmd
        0
    | Error(HelpRequested path) ->
        if path.IsEmpty then
            printfn "%s" (CommandTree.helpWithGlobals spec.Tree spec.GlobalFlags cmdName)
        else
            printfn "%s" (CommandTree.helpForPath spec.Tree path cmdName)

        0
    | Error(UnknownCommand(input, _rest, path)) ->
        UI.fail $"Unknown command: %s{input}"
        printfn "%s" (CommandTree.helpForPath spec.Tree path cmdName)
        1
    | Error(InvalidArguments(cmd, msg)) ->
        UI.fail $"Invalid arguments for %s{cmd}: %s{msg}"
        1
    | Error(AmbiguousArgument(input, candidates)) ->
        let joined = String.concat ", " candidates
        UI.fail $"Ambiguous: '{input}' matches: {joined}"
        1
    | Error(UnknownFlag(flag, cmd, validFlags)) ->
        let joined = String.concat ", " validFlags
        UI.fail $"Unknown flag '%s{flag}' for command '%s{cmd}'. Valid flags: %s{joined}"
        1
    | Error(DuplicateFlag(flag, cmd)) ->
        UI.fail $"Flag '%s{flag}' provided more than once for command '%s{cmd}'"
        1
```

Global flags can appear **anywhere** in the arg list — before, after, or interleaved with command args. Duplicate flag names between global and command-level flags are rejected at tree construction time.
<!-- sync:basicusage:end -->

<!-- sync:reference -->
## Reference

### Parsing & Help

```fsharp
CommandTree.parse tree args              // Result<'Cmd, ParseError>
CommandTree.help tree path prefix        // Help text for one level
CommandTree.helpFull tree prefix         // Full recursive help
CommandTree.helpForPath tree path prefix // Help for a subcommand path
CommandTree.helpWithGlobals tree flags prefix // Help with global options section
CommandTree.format tree cmd prefix       // Format command back to CLI string
CommandTree.findByPath tree path         // Navigate to a subtree
CommandTree.closestGroupPath tree args   // Deepest matching group path
CommandTree.renderParseError tree err prefix // Error line + nearest help (full stderr text)
CommandTree.isError err                  // true for genuine errors, false for help/version
CommandTree.renderVersion prefix         // "<prefix> <version>" banner for the version arm
CommandTree.entryAssemblyVersion ()      // Entry assembly's version string
CommandTree.assemblyVersion asm          // Best-available version of any assembly
```

`parse` is the **single, strict** parse path. An unrecognized command yields
`Error(UnknownCommand(input, rest, groupPath))`, where `rest` is the raw remaining args after
the unknown token. A consumer chooses what to do with it: forward `input` + `rest` to a daemon
for dynamically-resolved commands, or render the canonical error and fail hard. There is no
separate lenient/strict pair of functions.

`renderParseError` turns any `ParseError` into the canonical stderr text — a one-line
"invalid input" message followed by the nearest command/group's help — so every consumer
renders errors uniformly. Pair it with `isError` for the exit code (`HelpRequested` /
`VersionRequested` are not errors).

```fsharp
match CommandTree.parse tree argv with
| Ok cmd -> run cmd; 0
// Forward unknown top-level commands (groupPath = []) to a daemon for dynamic plugins.
| Error(UnknownCommand(cmd, rest, [])) ->
    match tryForwardToDaemon cmd rest with
    | Some code -> code
    | None ->
        eprintfn "%s" (CommandTree.renderParseError tree (UnknownCommand(cmd, rest, [])) "fshw")
        1
| Error err when CommandTree.isError err ->
    eprintfn "%s" (CommandTree.renderParseError tree err "fshw") // includes nested UnknownCommand
    1
| Error err ->
    printfn "%s" (CommandTree.renderParseError tree err "fshw") // help text (HelpRequested)
    0
```

### Version

`renderParseError` returns `""` for `VersionRequested` because the version lives in the
consumer's assembly, not CommandTree's. Use `renderVersion` for the version arm — it reads the
**entry** assembly (your CLI), not CommandTree:

```fsharp
| Error VersionRequested -> printfn "%s" (CommandTree.renderVersion "toolname"); 0
```

`renderVersion prefix` is `"<prefix> <entryAssemblyVersion>"`. `entryAssemblyVersion ()` resolves
`Assembly.GetEntryAssembly()` (falling back to the calling assembly under some test hosts) and
returns `assemblyVersion` of it; `assemblyVersion asm` prefers the
`AssemblyInformationalVersionAttribute.InformationalVersion` (keeping any `+<commit>` build
metadata) and falls back to `GetName().Version`.

The package ships an auto-importing `build/CommandTree.targets` that stamps the build commit id
into `AssemblyInformationalVersion` for dev builds: it resolves the revision via `jj` (falling
back to `git`), sets `SourceRevisionId`, and the SDK folds it in as `+<commit>` (with a `.dirty`
suffix when the working copy has uncommitted changes). It never fails the build and never
overrides a `SourceRevisionId` already set by CI/SourceLink. Opt out with
`-p:CommandTreeStampRevision=false` (whole feature) or `-p:CommandTreeStampDirty=false` (dirty
marker only).

### Reflection

```fsharp
// Without global options
CommandReflection.fromUnion<'Cmd> "desc"                          // CommandTree<'Cmd>
CommandReflection.fromUnionWithEnv<'Cmd> "desc" "PREFIX"          // CommandTree<'Cmd> (with env vars)

// With global options (returns GlobalSpec with .Tree, .Parse, .GlobalFlags)
CommandReflection.fromUnionWithGlobals<'Cmd, 'G> "desc"           // GlobalSpec<'G, 'Cmd>
CommandReflection.fromUnionWithGlobalsAndEnv<'Cmd, 'G> "desc" "P" // GlobalSpec<'G, 'Cmd> (with env vars)

// Non-throwing variants — return Result<_, SpecError list> with ALL shape
// errors aggregated (see "Spec errors" below). Each fromUnion* is a thin
// wrapper that calls its try* sibling and throws on Error.
CommandReflection.tryFromUnion<'Cmd> "desc"                          // Result<CommandTree<'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithEnv<'Cmd> "desc" "PREFIX"          // Result<CommandTree<'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithGlobals<'Cmd, 'G> "desc"           // Result<GlobalSpec<'G, 'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithGlobalsAndEnv<'Cmd, 'G> "desc" "P" // Result<GlobalSpec<'G, 'Cmd>, SpecError list>

// Utilities
CommandReflection.formatCmd cmd              // Format command to CLI string
CommandReflection.caseName value             // Kebab-case name of union value
CommandReflection.toKebabCase "PascalCase"   // "pascal-case"
CommandReflection.parseFieldValue type str   // Result<obj option, string>
CommandReflection.formatFieldValue value     // Typed value to string
```

### Spec errors

A command DU's *shape* can be malformed independently of any user input: a field
whose type the parser can't handle (e.g. `DateTimeOffset`), a list field that
isn't last, more than one list field in a case, or a command flag name that
collides with a global flag. These are deterministic programming errors over the
static shape, so the `fromUnion*` constructors **fail fast by throwing**
`InvalidOperationException`.

The throwing constructors are thin wrappers over `tryFromUnion*`, which return
`Result<_, SpecError list>` instead. The `try*` variants are the single source of
truth and are recommended whenever you want **all** shape problems reported at
once (rather than fixing them one crash at a time), non-throwing startup, or
programmatic access to the errors:

```fsharp
type Bad =
    | [<Cmd("First")>] One of when_: System.DateTimeOffset // unsupported type
    | [<Cmd("Third")>] Three of span: System.TimeSpan      // unsupported type

match CommandReflection.tryFromUnion<Bad> "My CLI" with
| Ok tree -> // use tree
    ()
| Error errors ->
    // Every problem at once, in DU declaration order:
    //   [ UnsupportedFieldType ("one", "when_", typeof<DateTimeOffset>)
    //     UnsupportedFieldType ("three", "span", typeof<TimeSpan>) ]
    errors |> List.iter (SpecError.format >> eprintfn "%s")
```

`SpecError` is a DU with one case per construction-time problem
(`UnsupportedFieldType`, `ListFieldNotLast`, `MultipleListFields`,
`GlobalFlagCollision`, `GlobalShortFlagCollision`). `SpecError.format` renders one
error as a line; `SpecError.formatAll` renders a list with a count header (this is
exactly the message the throwing constructors raise). `SpecError` is distinct from
`ParseError`, which describes runtime parse failures over user input.

### DU-Based Flags

Define flags as discriminated unions. No-field cases become boolean flags, single-field cases become value flags:

```fsharp
// From examples/ExampleCli/Program.fs

// example-cli check --conf custom.json --strict --no-cache
type CheckFlag =
    | [<CmdFlag(Name = "conf", Short = "k")>] Config of string
    | [<Cmd("Enable strict checking")>] Strict
    | [<CmdEnvRaw("NO_CACHE")>] NoCache

type Command =
    | [<Cmd("Run checks")>] Check of CheckFlag list
```

Short flags are auto-derived from the first letter (collision detection prevents duplicates). Use `[<CmdFlag>]` to override.

### Env Var Binding

When an env prefix is configured, every DU flag case gets an auto-derived env var: `PREFIX_SCREAMING_SNAKE_CASE`.

```fsharp
// From examples/ExampleCli/Program.fs

type GlobalFlag =
    | [<Cmd("Enable verbose output")>] Verbose                     // env: EXAMPLE_VERBOSE
    | [<Cmd("Set log level"); CmdEnv("LVL")>] LogLevel of string   // env: EXAMPLE_LVL (suffix override)

type CheckFlag =
    | [<CmdEnvRaw("NO_CACHE")>] NoCache                            // env: NO_CACHE (exact name)
```

Resolution order: CLI flag > env var > absent. For boolean flags, env var values `"true"`/`"1"` mean present, `"false"`/`"0"`/unset mean absent.

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
| `'T list` | `of files: string list` | Collects remaining args (1+, must be last field) |
| `'Flag list` | `of CheckFlag list` | DU flag list becomes named `--flags` |
<!-- sync:reference:end -->

<!-- sync:license -->
## License

MIT
<!-- sync:license:end -->
