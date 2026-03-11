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

type Command =
    | [<Cmd("Task management")>] Task of TaskCommand
    | [<Cmd("Database operations")>] Db of DbCommand
    | [<Cmd("Deployment")>] Deploy of DeployCommand
    | [<Cmd("Code coverage")>] Coverage of CoverageCommand
    | [<Cmd("Job management")>] Job of JobCommand
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
