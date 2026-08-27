<!-- sync:intro:start -->
# CommandTree

Define CLI commands as F# discriminated unions, and let type-safe parsing, help generation, and fish completions fall out of your types.

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

> **Status: early alpha, and substantially AI-written.** It runs the author's
> own F# CLIs, but behavior and APIs shift between versions and rough edges are
> expected — your mileage may vary. Issues and PRs very welcome.

## The problem

Hand-written CLI parsing tends to drift: strings get parsed into the wrong
types, a `--help` branch goes missing, and the help text stops matching the real
flags. The idea here is to describe the command surface once as a discriminated
union and derive the parser, help text, and shell completions from that single
definition by reflection — so they aim to stay in sync because there's nothing
to keep in sync by hand.

## Install

```bash
dotnet add package CommandTree
```

<!-- sync:howitworks:start -->
## How it works

Case names become kebab-case commands. Nested unions become subcommand groups.
Fields become positional arguments. Every snippet below is sourced verbatim from
the compiled [`examples/ExampleCli/Program.fs`](examples/ExampleCli/Program.fs),
so it can't drift from code that builds.

A simple subcommand group with a default subcommand:

<!-- sync:db-command:start src=examples/ExampleCli/Program.fs -->
```fsharp
// example-cli db migrate
// example-cli db                               ← runs status (CmdDefault)
// example-cli db reset
type DbCommand =
    | [<Cmd("Run database migrations")>] Migrate
    | [<Cmd("Reset the database")>] Reset
    | [<Cmd("Show connection status"); CmdDefault>] Status
```
<!-- sync:db-command:end -->

Value completions and an optional positional field:

<!-- sync:deploy-command:start src=examples/ExampleCli/Program.fs -->
```fsharp
// example-cli deploy push staging
// example-cli deploy status prod
// example-cli deploy                           ← runs status with no env (CmdDefault)
type DeployCommand =
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>] Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>] Status of env: string option
```
<!-- sync:deploy-command:end -->

A trailing `list` field collects remaining args; file completions per field:

<!-- sync:files-command:start src=examples/ExampleCli/Program.fs -->
```fsharp
// example-cli tag v1.0 src/App.fs src/Lib.fs   ← list field collects remaining args
// example-cli tag v1.0                         ← an omitted list binds []
// example-cli diff old.dll new.dll             ← multiple CmdFileCompletion with FieldIndex
type FilesCommand =
    | [<Cmd("Tag files with a label"); CmdFileCompletion>] Tag of label: string * files: string list
    | [<Cmd("Compare two DLLs"); CmdFileCompletion(FieldIndex = 0); CmdFileCompletion(FieldIndex = 1)>] Diff of
        oldDll: string *
        newDll: string
```
<!-- sync:files-command:end -->

A flag DU becomes named `--flags`:

<!-- sync:check-flag:start src=examples/ExampleCli/Program.fs -->
```fsharp
// example-cli check --conf custom.json --strict --no-cache --wait=30
// example-cli check --wait          ← bare optional-value flag binds `Wait None` (never swallows a value)
type CheckFlag =
    | [<CmdFlag(Name = "conf", Short = "k")>] Config of string
    | [<Cmd("Enable strict checking")>] Strict
    | [<CmdEnvRaw("NO_CACHE")>] NoCache
    | [<Cmd("Wait N seconds; bare for the default")>] Wait of int option
```
<!-- sync:check-flag:end -->

| F# definition | CLI invocation | Notes |
|---|---|---|
| `Task of TaskCommand` | `example-cli task ...` | Nested union becomes a subcommand group |
| `Test` | `example-cli test` | No-field case becomes a simple command |
| `Add of title: string` | `example-cli task add "Buy groceries"` | Fields become positional args |
| `Start of name: string * size: int64` | `example-cli job start build 1024` | Multiple fields, in order |
| `Status of env: string option` | `example-cli deploy status prod` or `example-cli deploy status` | Option fields can be omitted |
| `Add of title: string * priority: Priority option` | `example-cli task add "Buy milk" high` or `example-cli task add "Buy milk" hig` | Union-typed field: a name typed in full always matches; abbreviations need 3+ chars |
| `Tag of label: string * files: string list` | `example-cli files tag v1 a.fs b.fs` or `example-cli files tag v1` | List field (must be last) collects zero or more remaining args |
| `Check of CheckFlag list` | `example-cli check --conf x.json --strict` | DU flag list becomes named flags |
| `Wait of int option` (flag case) | `example-cli check --wait` or `--wait=5` | Optional-value flag: bare binds `None`, inline `=` binds `Some`; never swallows the next token |
| `Remove of name: string * flags: RemoveFlag list` | `example-cli remove old-ws --force` | Positionals + trailing DU flag list; flags may appear anywhere |
| `[<CmdDefault>] List` | `example-cli task` | Runs when a group is invoked without a subcommand |
| `[<Cmd(Name = "fmt")>] Format` | `example-cli fmt` | `Name` overrides the derived command name |
<!-- sync:howitworks:end -->

<!-- sync:basicusage:start -->
## Basic usage

Build a tree from your command DU, then `parse` argv against it. The full
runnable example lives in [`examples/ExampleCli/Program.fs`](examples/ExampleCli/Program.fs).

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

For global flags and env-var binding, use `fromUnionWithGlobalsAndEnv`, which
returns a `GlobalSpec` whose `Parse` yields `(globals, command)`. Global flags
can appear **anywhere** in the arg list — before, after, or interleaved with
command args. See [the example](examples/ExampleCli/Program.fs) for a full
match over every `ParseError` case.
<!-- sync:basicusage:end -->

## Attributes

Decorate union cases to customize parsing, help, and completions:

| Attribute | Effect |
|---|---|
| `[<Cmd("desc")>]` | Sets help text. `Name = "custom"` overrides the derived command name. Description is optional — omit it to derive from the case name. |
| `[<CmdDefault>]` | Marks the subcommand to run when a group is invoked with no arguments. |
| `[<CmdArg("desc")>]` | Documents a positional argument (shows in an `Arguments:` help section). `FieldIndex = N` targets one field; `Default = "value"` adds a default hint. For record-typed args, apply directly to record fields. |
| `[<CmdExample("ex1", "ex2")>]` | Adds an `Examples:` help section. The command path is prepended automatically. |
| `[<CmdCompletion("a", "b")>]` | Provides fish completion values; `FieldIndex` selects which argument. |
| `[<CmdFileCompletion>]` | Enables file-path completion in fish (repeatable per case with `FieldIndex`). |
| `[<CmdFlag(Name, Short, Description)>]` | Overrides the derived name/short/description of a DU flag case. |
| `[<CmdEnv("SUFFIX")>]` | Overrides the env-var suffix for a flag (the prefix still applies). |
| `[<CmdEnvRaw("VAR_NAME")>]` | Sets the exact env-var name, ignoring the prefix. |

## Flags and env vars

Define flags as their own DU and attach them as a `list` field (e.g.
`Check of CheckFlag list`). A no-field case is a boolean flag; a single
`'T option` field is an *optional-value* flag; any other single field is a
*required-value* flag. Short flags are auto-derived from the first letter
(collisions are dropped); override with `[<CmdFlag>]`.

Required-value flags take their value space-separated (`--conf custom.json`) or
inline (`--conf=custom.json`). Optional-value flags are **inline-only**: bare
`--wait` binds `None`, `--wait=5` binds `Some 5`, and they **never** consume the
following token — so `--wait --detach` is `Wait None` plus `Detach`, and
`--wait 5` leaves `5` as a positional. A bare-but-`=` form with no value
(`--wait=`) or a value that does not parse (`--wait=abc`) is a parse error.

A case can mix positional fields with a trailing flag DU list (e.g.
`Remove of name: string * flags: RemoveFlag list`). Flags may appear anywhere
relative to the positionals, and everything after a standalone `--` binds as
positional values — so a value that looks like a flag (`remove -- --force`)
stays a value.

<!-- sync:flags-check:start src=examples/ExampleCli/Program.fs#check-flag -->
```fsharp
// example-cli check --conf custom.json --strict --no-cache --wait=30
// example-cli check --wait          ← bare optional-value flag binds `Wait None` (never swallows a value)
type CheckFlag =
    | [<CmdFlag(Name = "conf", Short = "k")>] Config of string
    | [<Cmd("Enable strict checking")>] Strict
    | [<CmdEnvRaw("NO_CACHE")>] NoCache
    | [<Cmd("Wait N seconds; bare for the default")>] Wait of int option
```
<!-- sync:flags-check:end -->

When an env prefix is configured (`fromUnionWithEnv` / `fromUnionWithGlobalsAndEnv`),
each flag case also reads `PREFIX_SCREAMING_SNAKE_CASE`. Resolution order is
**CLI flag > env var > absent**. For booleans, `"true"`/`"1"` mean present and
`"false"`/`"0"`/unset mean absent.

## Supported field types

| Type | Example | Notes |
|------|---------|-------|
| `string` | `of name: string` | Any string value |
| `int` | `of count: int` | Int32 |
| `int64` | `of id: int64` | Int64 |
| `float` | `of rate: float` | Double |
| `decimal` | `of price: decimal` | Decimal |
| `bool` | `of force: bool` | Boolean |
| `Guid` | `of id: Guid` | Guid |
| `'T option` | `of env: string option` | `None` when omitted |
| Union | `of priority: Priority option` | Exact kebab-case name, or prefix matching (min 3 chars). A union as a case's *only* field is a subcommand group instead |
| `'T list` | `of files: string list` | Collects remaining args (zero or more, must be last field) |
| `'Flag list` | `of CheckFlag list` | DU flag list becomes named `--flags` |

A field whose type isn't supported is a *spec error* — a malformed command
shape, distinct from a runtime parse error. See [Advanced usage](docs/advanced.md)
for the full API reference (parsing, help rendering, version stamping, spec
errors, fish completions) and the optional build-time analyzer.

## More

- **[Advanced usage](docs/advanced.md)** — full `CommandTree`/`CommandReflection` reference, structured errors, version stamping, fish completions, and the build-time analyzer.
- **[API docs](https://michaelglass.github.io/CommandTree/)** — generated reference for every public member, including the `Process` and `UI` helper modules.

<!-- sync:license:start -->
## License

MIT
<!-- sync:license:end -->
</content>
</invoke>
