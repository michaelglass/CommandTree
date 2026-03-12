// Run with: dotnet run --project examples/ExampleCli -- <command>
// Example: dotnet run --project examples/ExampleCli -- task add "Buy groceries"

open System
open CommandTree

// =============================================================================
// Domain types
// =============================================================================

type Priority =
    | Low
    | Medium
    | High

// =============================================================================
// Command definitions
// =============================================================================

type TaskCommand =
    | [<Cmd("Add a new task")>] Add of title: string * priority: Priority option
    | [<Cmd("List all tasks"); CmdDefault>] List
    | [<Cmd("Complete a task")>] Complete of id: int
    | [<Cmd("Remove a task")>] Remove of id: int

type DbCommand =
    | [<Cmd("Run database migrations")>] Migrate
    | [<Cmd("Reset the database")>] Reset
    | [<Cmd("Show connection status"); CmdDefault>] Status

type DeployCommand =
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>] Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>] Status of env: string option

type CoverageCommand =
    | [<Cmd("Show coverage for file"); CmdFileCompletion>] File of path: string
    | [<Cmd("Show coverage summary"); CmdDefault>] Summary

type JobCommand =
    | [<Cmd("Start a new job")>] Start of name: string * size: int64 * verbose: bool
    | [<Cmd("Check job status")>] Status of id: Guid
    | [<Cmd("List recent jobs"); CmdDefault>] List

type ProcessDemoCommand =
    | [<Cmd("Run a command visibly with timing")>] Run
    | [<Cmd("Run silently and show captured output")>] Silent
    | [<Cmd("Run and get CommandResult record")>] Result
    | [<Cmd("Run command asynchronously")>] Async
    | [<Cmd("Run with spinner animation")>] Spinner
    | [<Cmd("Run interactively (no capture)")>] Interactive
    | [<Cmd("Run with custom environment variables")>] WithEnv
    | [<Cmd("Run with timeout")>] Timeout
    | [<Cmd("Run dotnet command")>] Dotnet
    | [<Cmd("Run tasks in parallel")>] Parallel

type UiDemoCommand =
    | [<Cmd("Show all output styles")>] Styles
    | [<Cmd("Show timing display with color gradient")>] Timing
    | [<Cmd("Show spinner animations")>] Spinners
    | [<Cmd("Show ANSI color constants")>] Colors

type ReflectionDemoCommand =
    | [<Cmd("Show formatCmd roundtrip")>] FormatCmd
    | [<Cmd("Show naming conversions")>] Naming
    | [<Cmd("Show field value parsing and formatting")>] ParseValues
    | [<Cmd("Show CommandSpec usage")>] Spec
    | [<Cmd("Show tree inspection")>] TreeInfo

type FishDemoCommand =
    | [<Cmd("Write completions to file")>] Generate
    | [<Cmd("Preview completions on stdout"); CmdDefault>] Preview
    | [<Cmd("Install auto-update hook")>] Install

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Database operations")>] Db of DbCommand
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("Code coverage")>] Coverage of CoverageCommand
    | [<Cmd("Job management")>] Job of JobCommand
    | [<Cmd("Process execution demos")>] Proc of ProcessDemoCommand
    | [<Cmd("UI and color demos")>] Ui of UiDemoCommand
    | [<Cmd("Reflection and tree inspection demos")>] Reflect of ReflectionDemoCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Format source code")>] Format
    | [<Cmd("Fish shell completions")>] Fish of FishDemoCommand
    | [<Cmd("Show full help")>] Help

// =============================================================================
// Command handlers
// =============================================================================

let handleTask (cmd: TaskCommand) =
    match cmd with
    | TaskCommand.Add(title, priority) ->
        let p = priority |> Option.defaultValue Priority.Medium
        UI.success $"Added task: \"%s{title}\" (priority: %s{CommandReflection.caseName p})"
    | TaskCommand.List ->
        UI.title "Tasks"
        printfn "  1. [x] Set up project (high)"
        printfn "  2. [ ] Write README (medium)"
        printfn "  3. [ ] Add tests (low)"
    | TaskCommand.Complete id -> UI.success $"Completed task #%d{id}"
    | TaskCommand.Remove id -> UI.warn $"Removed task #%d{id}"

let handleDb (cmd: DbCommand) =
    match cmd with
    | DbCommand.Migrate ->
        UI.section "Running migrations"
        UI.success "Applied 3 migrations"
    | DbCommand.Reset ->
        UI.warn "Resetting database..."
        UI.success "Database reset complete"
    | DbCommand.Status -> UI.info "Database: connected (localhost:5432/myapp_dev)"

let handleDeploy (cmd: DeployCommand) =
    match cmd with
    | DeployCommand.Push env ->
        UI.section $"Deploying to %s{env}"
        UI.success $"Deployed to %s{env}"
    | DeployCommand.Status env ->
        let e = env |> Option.defaultValue "dev"
        UI.info $"Deploy status for %s{e}: up to date"

let handleCoverage (cmd: CoverageCommand) =
    match cmd with
    | CoverageCommand.File path -> UI.info $"Coverage for %s{path}: 87.5%%"
    | CoverageCommand.Summary ->
        UI.title "Coverage Summary"
        printfn "  Overall: 82.3%%"
        printfn "  src/App.fs: 91.0%%"
        printfn "  src/Lib.fs: 73.5%%"

let handleJob (cmd: JobCommand) =
    match cmd with
    | JobCommand.Start(name, size, verbose) ->
        UI.success $"Started job \"%s{name}\" (size: %d{size} bytes, verbose: %b{verbose})"
    | JobCommand.Status id -> UI.info $"Job %s{string id}: running (42%% complete)"
    | JobCommand.List ->
        UI.title "Recent Jobs"
        printfn "  1. build-assets  (completed)"
        printfn "  2. deploy-prod   (running)"

let handleProcessDemo (cmd: ProcessDemoCommand) =
    match cmd with
    | ProcessDemoCommand.Run ->
        UI.section "Process.run — visible output with timing"
        Process.run "echo" "hello from Process.run"

    | ProcessDemoCommand.Silent ->
        UI.section "Process.runSilent — captured output"
        let (code, stdout, stderr) = Process.runSilent "echo" "captured output"
        UI.info $"Exit code: %d{code}"
        UI.info $"Stdout: %s{stdout}"
        UI.dimInfo $"Stderr: %s{stderr}"

    | ProcessDemoCommand.Result ->
        UI.section "Process.runCommand — CommandResult record"
        let result = Process.runCommand "echo" "hello from runCommand"
        UI.info $"ExitCode: %d{result.ExitCode}"
        UI.info $"Stdout: %s{result.Stdout}"
        UI.dimInfo $"Stderr: %s{result.Stderr}"

    | ProcessDemoCommand.Async ->
        UI.section "Process.runAsync — async execution"
        let task = Process.runAsync "echo" "async hello"
        let (code, stdout, _stderr) = task.Result
        UI.info $"Exit code: %d{code}, Output: %s{stdout.Trim()}"

    | ProcessDemoCommand.Spinner ->
        UI.section "Process.runWithSpinner — spinner animation"
        Process.runWithSpinner "Sleeping for 1 second" "sleep" "1" |> ignore

    | ProcessDemoCommand.Interactive ->
        UI.section "Process.runInteractive — no capture, returns exit code"
        let exitCode = Process.runInteractive "echo" "interactive output"
        UI.info $"Exit code: %d{exitCode}"

    | ProcessDemoCommand.WithEnv ->
        UI.section "Process.runWithEnv — custom environment"
        Process.runWithEnv "sh" "-c \"echo MY_VAR=$MY_VAR\"" [ ("MY_VAR", "hello-from-env") ]
        UI.section "Process.runSilentWithEnv — silent with env"

        let (code, stdout, _) =
            Process.runSilentWithEnv "sh" "-c \"echo MY_VAR=$MY_VAR\"" [ ("MY_VAR", "silent-env") ]

        UI.info $"Exit code: %d{code}, Stdout: %s{stdout}"

    | ProcessDemoCommand.Timeout ->
        UI.section "Process.runSilentWithTimeout — with timeout"

        let (code, stdout, _) =
            Process.runSilentWithTimeout "echo" "fast command" (Some 5000)

        UI.info $"Exit code: %d{code}, Stdout: %s{stdout}"
        UI.section "Process.runSilentWithTimeout — timeout exceeded"
        let (code2, _, stderr2) = Process.runSilentWithTimeout "sleep" "10" (Some 100)
        UI.info $"Exit code: %d{code2}"

        if stderr2 <> "" then
            UI.warn stderr2

    | ProcessDemoCommand.Dotnet ->
        UI.section "Process.dotnet — run dotnet command"
        Process.dotnet "--version"
        UI.section "Process.dotnetSpinner — dotnet with spinner"
        Process.dotnetSpinner "Getting dotnet info" "--version"

    | ProcessDemoCommand.Parallel ->
        UI.section "Process.runParallel — parallel execution"

        let tasks =
            [| Process.runAsync "echo" "task-1"
               Process.runAsync "echo" "task-2"
               Process.runAsync "echo" "task-3" |]

        let results = Process.runParallel tasks

        for (code, stdout, _) in results do
            UI.success $"Exit %d{code}: %s{stdout.Trim()}"

let handleUiDemo (cmd: UiDemoCommand) =
    match cmd with
    | UiDemoCommand.Styles ->
        UI.title "UI Output Styles"
        UI.section "Section header"
        UI.success "Success message"
        UI.pass "Pass message (alias for success)"
        UI.fail "Fail message (goes to stderr)"
        UI.info "Info message"
        UI.warn "Warning message"
        UI.skip "Skip message"
        UI.dimInfo "Dim info message"
        UI.cmd "echo" "command echo display"
        UI.info $"isInteractive: %b{UI.isInteractive}"

    | UiDemoCommand.Timing ->
        UI.title "Timing Display"
        UI.info "Timing color gradient from green (fast) to red (slow):"

        for secs in [ 0.5; 1.0; 2.0; 4.0; 6.0; 8.0; 10.0; 15.0 ] do
            let elapsed = TimeSpan.FromSeconds(secs)
            printfn $"  %4.1f{secs}s -> %s{UI.timing elapsed}"

        UI.info "Direct timingColor usage:"

        printfn
            $"  %s{UI.timingColor 1.0}fast%s{Color.reset}  %s{UI.timingColor 5.0}medium%s{Color.reset}  %s{UI.timingColor 10.0}slow%s{Color.reset}"

    | UiDemoCommand.Spinners ->
        UI.title "Spinner Demos"
        UI.section "withSpinner — shows result on completion"

        let result =
            UI.withSpinner "Computing something" (fun () ->
                System.Threading.Thread.Sleep(1500)
                42)

        UI.info $"Spinner returned: %d{result}"
        UI.section "withSpinnerQuiet — clears line, caller handles output"

        let result2 =
            UI.withSpinnerQuiet "Working quietly" (fun () ->
                System.Threading.Thread.Sleep(1000)
                "done")

        UI.success $"Quiet spinner returned: %s{result2}"

    | UiDemoCommand.Colors ->
        UI.title "ANSI Color Constants"
        printfn $"  %s{Color.bold}Color.bold%s{Color.reset}"
        printfn $"  %s{Color.dim}Color.dim%s{Color.reset}"
        printfn $"  %s{Color.red}Color.red%s{Color.reset}"
        printfn $"  %s{Color.green}Color.green%s{Color.reset}"
        printfn $"  %s{Color.yellow}Color.yellow%s{Color.reset}"
        printfn $"  %s{Color.blue}Color.blue%s{Color.reset}"
        printfn $"  %s{Color.cyan}Color.cyan%s{Color.reset}"
        printfn $"  Combined: %s{Color.bold}%s{Color.red}bold+red%s{Color.reset}"

// =============================================================================
// Entry point
// =============================================================================

let tree = CommandReflection.fromUnion<Command> "Example project management CLI"
let cmdName = "example-cli"

let handleReflectionDemo
    (tree: CommandTree<Command>)
    (cmdName: string)
    (runCmd: Command -> unit)
    (cmd: ReflectionDemoCommand)
    =
    match cmd with
    | ReflectionDemoCommand.FormatCmd ->
        UI.title "CommandReflection.formatCmd"

        let examples: Command list =
            [ Task(TaskCommand.Add("buy milk", Some Priority.High))
              Deploy(DeployCommand.Push "staging")
              Job(JobCommand.Start("build", 1024L, true))
              Job(JobCommand.Status(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"))) ]

        for ex in examples do
            let formatted = CommandReflection.formatCmd ex
            UI.info $"%s{formatted}"

            match CommandTree.format tree ex [] cmdName with
            | Some full -> UI.dimInfo $"Full: %s{full}"
            | None -> UI.dimInfo "Could not format via tree"

    | ReflectionDemoCommand.Naming ->
        UI.title "Naming Conversions"
        let names = [ "FileCoverage"; "RunAsync"; "ProcessDemoCommand"; "MyApp"; "UI" ]

        for name in names do
            UI.info $"\"%s{name}\""
            UI.dimInfo $"  toKebabCase:    %s{CommandReflection.toKebabCase name}"
            UI.dimInfo $"  toDescription:  %s{CommandReflection.toDescription name}"

    | ReflectionDemoCommand.ParseValues ->
        UI.title "Field Value Parsing and Formatting"

        let testCases: (Type * string) list =
            [ (typeof<string>, "hello")
              (typeof<int>, "42")
              (typeof<int64>, "9876543210")
              (typeof<bool>, "true")
              (typeof<Guid>, "550e8400-e29b-41d4-a716-446655440000")
              (typeof<Priority option>, "high")
              (typeof<Priority>, "med") ]

        for (t, input) in testCases do
            match CommandReflection.parseFieldValue t input with
            | Ok(Some value) ->
                let formatted = CommandReflection.formatFieldValue value
                UI.success $"parse(%s{t.Name}, \"%s{input}\") = %s{formatted}"
            | Ok None -> UI.warn $"parse(%s{t.Name}, \"%s{input}\") = None"
            | Error err -> UI.fail $"parse(%s{t.Name}, \"%s{input}\") failed: %s{err}"

    | ReflectionDemoCommand.Spec ->
        UI.title "CommandSpec Usage"
        UI.info "CommandSpec bundles tree + format + execute:"

        let spec: CommandSpec<Command> =
            { Tree = tree
              Format = CommandReflection.formatCmd
              Execute = runCmd }

        UI.dimInfo $"Tree root desc: %s{CommandTree.desc spec.Tree}"
        UI.dimInfo $"Format example: %s{spec.Format(Test)}"
        UI.info "Executing 'test' via spec.Execute:"
        spec.Execute Test

    | ReflectionDemoCommand.TreeInfo ->
        UI.title "Tree Inspection"
        UI.section "CommandTree.name / desc on root"
        UI.info $"name: \"%s{CommandTree.name tree}\""
        UI.info $"desc: \"%s{CommandTree.desc tree}\""
        UI.section "CommandTree.findByPath"

        match CommandTree.findByPath tree [ "task" ] with
        | Some subtree ->
            UI.success $"Found: \"%s{CommandTree.name subtree}\" — %s{CommandTree.desc subtree}"
            UI.dimInfo "Args (group has none):"

            for arg in CommandTree.args subtree do
                UI.dimInfo $"  %s{arg.Name}: %s{arg.TypeName} (optional: %b{arg.IsOptional})"
        | None -> UI.fail "Not found"

        UI.section "CommandTree.findByPath for leaf"

        match CommandTree.findByPath tree [ "task"; "add" ] with
        | Some leaf ->
            UI.success $"Found leaf: \"%s{CommandTree.name leaf}\""

            for arg in CommandTree.args leaf do
                UI.info $"  %s{arg.Name}: %s{arg.TypeName} (optional: %b{arg.IsOptional})"
        | None -> UI.fail "Not found"

        UI.section "CommandTree.fishCompletions (raw)"
        let raw = CommandTree.fishCompletions tree cmdName
        let lines = raw.Split('\n')

        for line in lines |> Array.truncate 5 do
            UI.dimInfo line

        UI.dimInfo $"... (%d{lines.Length} lines total)"

let handleFishDemo (tree: CommandTree<Command>) (cmdName: string) (cmd: FishDemoCommand) =
    match cmd with
    | FishDemoCommand.Generate -> FishCompletions.writeToFile tree cmdName
    | FishDemoCommand.Preview ->
        UI.title "Fish Completions Preview"
        UI.info "FishCompletions.generateContent output:"
        printfn "%s" (FishCompletions.generateContent tree cmdName)
    | FishDemoCommand.Install -> FishCompletions.installHook cmdName

let rec run (tree: CommandTree<Command>) (cmdName: string) (cmd: Command) =
    match cmd with
    | Task t -> handleTask t
    | Db d -> handleDb d
    | Deploy d -> handleDeploy d
    | Coverage c -> handleCoverage c
    | Job j -> handleJob j
    | Proc p -> handleProcessDemo p
    | Ui u -> handleUiDemo u
    | Reflect r -> handleReflectionDemo tree cmdName (run tree cmdName) r
    | Test ->
        UI.section "Running tests"
        UI.success "All 42 tests passed"
    | Format ->
        UI.section "Formatting code"
        UI.success "Formatted 12 files"
    | Fish f -> handleFishDemo tree cmdName f
    | Help -> printfn "%s" (CommandTree.helpFull tree cmdName)

[<EntryPoint>]
let main argv =
    let args = argv

    match CommandTree.parse tree args with
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
