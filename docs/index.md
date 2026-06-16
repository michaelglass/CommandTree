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
    | [<Cmd("Run checks")>] Check of CheckFlag list
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Show full help")>] Help
```

| F# definition | CLI invocation | Notes |
|---|---|---|
| `Task of TaskCommand` | `my-cli task ...` | Nested union becomes a subcommand group |
| `Test` | `my-cli test` | No-field case becomes a simple command |
| `Add of title: string` | `my-cli task add "Buy groceries"` | Fields become positional args |
| `Start of name: string * size: int64` | `my-cli job start build 1024` | Multiple fields, in order |
| `Status of env: string option` | `my-cli deploy status prod` or `my-cli deploy status` | Option fields can be omitted |
| `Push of env: Priority` | `my-cli deploy push high` or `my-cli deploy push hig` | Union fields match by kebab-case prefix (min 3 chars) |
| `Tag of label: string * files: string list` | `my-cli files tag v1 a.fs b.fs` | List field (must be last) collects 1+ remaining args |
| `Check of CheckFlag list` | `my-cli check --conf x.json --strict` | DU flag list becomes named flags |
| `[<CmdDefault>] List` | `my-cli task` | Runs when a group is invoked without a subcommand |
| `[<Cmd(Name = "fmt")>] Format` | `my-cli fmt` | `Name` overrides the derived command name |
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
