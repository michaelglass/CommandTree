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

type Environment =
    | Dev
    | Staging
    | Prod

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
    | [<Cmd("Deploy to environment"); CmdCompletion("dev", "staging", "prod")>]
      Push of env: string
    | [<Cmd("Show deploy status"); CmdCompletion("dev", "staging", "prod"); CmdDefault>]
      Status of env: string option

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

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Database operations")>] Db of DbCommand
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("Code coverage")>] Coverage of CoverageCommand
    | [<Cmd("Job management")>] Job of JobCommand
    | [<Cmd("Process execution demos")>] Proc of ProcessDemoCommand
    | [<Cmd("Run the test suite")>] Test
    | [<Cmd("Format source code")>] Format
    | [<Cmd("Generate fish completions")>] Fish
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
    | DbCommand.Status ->
        UI.info "Database: connected (localhost:5432/myapp_dev)"

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
    | JobCommand.Status id ->
        UI.info $"Job %s{string id}: running (42%% complete)"
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
        let (code, stdout, _) = Process.runSilentWithTimeout "echo" "fast command" (Some 5000)
        UI.info $"Exit code: %d{code}, Stdout: %s{stdout}"
        UI.section "Process.runSilentWithTimeout — timeout exceeded"
        let (code2, _, stderr2) = Process.runSilentWithTimeout "sleep" "10" (Some 100)
        UI.info $"Exit code: %d{code2}"
        if stderr2 <> "" then UI.warn stderr2

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

// =============================================================================
// Entry point
// =============================================================================

let tree = CommandReflection.fromUnion<Command> "Example project management CLI"
let cmdName = "example-cli"

let run (cmd: Command) =
    match cmd with
    | Task t -> handleTask t
    | Db d -> handleDb d
    | Deploy d -> handleDeploy d
    | Coverage c -> handleCoverage c
    | Job j -> handleJob j
    | Proc p -> handleProcessDemo p
    | Test ->
        UI.section "Running tests"
        UI.success "All 42 tests passed"
    | Format ->
        UI.section "Formatting code"
        UI.success "Formatted 12 files"
    | Fish -> FishCompletions.writeToFile tree cmdName
    | Help -> printfn "%s" (CommandTree.helpFull tree cmdName)

[<EntryPoint>]
let main argv =
    let args = if argv.Length > 0 then argv else [| "--help" |]

    match CommandTree.parse tree args with
    | Ok cmd ->
        run cmd
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
