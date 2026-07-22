<!-- sync:intro -->
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

## Installation

```bash
dotnet add package CommandTree
```

**[API Reference](reference/index.html)**

<!-- sync:howitworks -->
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
// example-cli tag v1.0 src/App.fs src/Lib.fs   ← list field collects 1+ remaining args
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
| `Push of env: Priority` | `example-cli deploy push high` or `example-cli deploy push hig` | Union fields match by kebab-case prefix (min 3 chars) |
| `Tag of label: string * files: string list` | `example-cli files tag v1 a.fs b.fs` | List field (must be last) collects 1+ remaining args |
| `Check of CheckFlag list` | `example-cli check --conf x.json --strict` | DU flag list becomes named flags |
| `Wait of int option` (flag case) | `example-cli check --wait` or `--wait=5` | Optional-value flag: bare binds `None`, inline `=` binds `Some`; never swallows the next token |
| `Remove of name: string * flags: RemoveFlag list` | `example-cli remove old-ws --force` | Positionals + trailing DU flag list; flags may appear anywhere |
| `[<CmdDefault>] List` | `example-cli task` | Runs when a group is invoked without a subcommand |
| `[<Cmd(Name = "fmt")>] Format` | `example-cli fmt` | `Name` overrides the derived command name |
<!-- sync:howitworks:end -->

<!-- sync:basicusage -->
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

## Reference

The full API reference — parsing and help rendering, the `CommandReflection`
surface, structured spec/parse errors, record-typed arguments, version stamping,
fish completions, and the optional build-time analyzer — lives in
[Advanced usage](advanced.md). Generated per-member docs are in the
[API Reference](reference/index.html).

<!-- sync:license -->
## License

MIT
<!-- sync:license:end -->
</content>
