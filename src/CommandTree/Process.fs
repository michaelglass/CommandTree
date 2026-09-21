namespace CommandTree

open System
open System.Diagnostics
open System.Text
open System.Threading.Tasks

/// Result of running a command (for named field access)
type CommandResult =
    { ExitCode: int
      Stdout: string
      Stderr: string }

/// Which of a child process's two output streams a line arrived on. A sink is
/// handed the stream alongside the text because the two are routinely written to
/// different places, and a caller that merges them can do so trivially while one
/// that receives them already merged cannot take them apart again.
type OutputStream =
    | Stdout
    | Stderr

/// How to run a child process silently.
///
/// The individual <c>runSilentWith*</c> helpers below cover a CROSS-PRODUCT —
/// environment, timeout, working directory — of which only some corners were ever
/// built, so "an environment AND a timeout" was simply unavailable and could not be
/// reached by choosing a different helper. This record is that cross-product stated
/// once, so a new axis is a field rather than another function whose name lists its
/// arguments.
///
/// Carries a function, so it has neither structural equality nor ordering — two run
/// descriptions would be compared by what they DO, which is not decidable.
[<NoEquality; NoComparison>]
type SilentRun =
    {
        /// Variables added to (not replacing) the inherited environment.
        Env: (string * string) list
        /// Wall-clock bound in milliseconds. <c>None</c> waits indefinitely.
        TimeoutMs: int option
        /// Working directory for the child. <c>None</c> inherits the caller's.
        WorkingDirectory: string option
        /// Called for every line as it ARRIVES, on both streams. The point of a sink
        /// rather than a return value is that a run which is killed — at the timeout
        /// above, or by a Ctrl-C — has already delivered everything the child managed
        /// to say. A buffer read at the end dies with the process.
        Sink: (OutputStream -> string -> unit) option
    }

/// Outcome of a silent run.
///
/// <c>TimedOut</c> is a field rather than a sentinel exit code plus a message in
/// stderr. The older helpers report a timeout as <c>(-1, "", "Process timed out after
/// Nms")</c>, which leaves a caller unable to tell a child that genuinely exited -1
/// from one this runner killed, and mixes a runner's diagnostic into a channel that
/// otherwise carries only the child's own words.
type SilentRunResult =
    {
        /// The child's exit code, or <c>-1</c> when it was killed at the timeout.
        ExitCode: int
        /// Everything the child wrote to stdout, trimmed. Partial when it timed out.
        Stdout: string
        /// Everything the child wrote to stderr, trimmed. Partial when it timed out.
        Stderr: string
        /// Whether this runner killed the child at <c>TimeoutMs</c>.
        TimedOut: bool
    }

/// Construction helpers for <see cref="SilentRun"/>.
[<RequireQualifiedAccess>]
module SilentRun =
    /// Inherit the caller's environment and working directory, wait indefinitely, and
    /// keep the output rather than streaming it.
    let defaults: SilentRun =
        { Env = []
          TimeoutMs = None
          WorkingDirectory = None
          Sink = None }

    let withEnv (env: (string * string) list) (run: SilentRun) = { run with Env = env }
    let withTimeoutMs (ms: int) (run: SilentRun) = { run with TimeoutMs = Some ms }

    let inDirectory (dir: string) (run: SilentRun) =
        { run with WorkingDirectory = Some dir }

    let withSink (sink: OutputStream -> string -> unit) (run: SilentRun) = { run with Sink = Some sink }

/// Process execution helpers.
///
/// Every runner here passes each element of <c>args</c> to the child process as a single
/// literal token via <c>ProcessStartInfo.ArgumentList</c>, with <c>UseShellExecute</c> off.
/// No shell is involved and tokens are never re-parsed, so spaces and quotes inside an
/// argument survive intact.
module Process =
    /// Build a <c>ProcessStartInfo</c> for <c>command</c> with <c>args</c> as literal
    /// tokens. Callers configure redirection / working directory / environment afterward.
    let private mkPsi (command: string) (args: string list) =
        let psi = ProcessStartInfo(command)
        psi.UseShellExecute <- false
        args |> List.iter psi.ArgumentList.Add
        psi

    /// Run a command and wait for it to complete.
    let run (command: string) (args: string list) =
        UI.cmd command (String.concat " " args)
        let sw = Stopwatch.StartNew()
        let psi = mkPsi command args
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        sw.Stop()
        printfn $"    %s{UI.timing sw.Elapsed}"

        if proc.ExitCode <> 0 then
            failwith $"Command failed with exit code %d{proc.ExitCode}"

    /// Run a command with spinner, capturing output.
    let runWithSpinner (message: string) (command: string) (args: string list) =
        let (exitCode, stdout, stderr) =
            UI.withSpinner message (fun () ->
                let psi = mkPsi command args
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                use proc = Diagnostics.Process.Start(psi)
                // Read stdout and stderr in parallel to avoid deadlock when buffer fills
                let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                let stderrTask = proc.StandardError.ReadToEndAsync()
                proc.WaitForExit()
                let stdout = stdoutTask.Result
                let stderr = stderrTask.Result

                if proc.ExitCode <> 0 then
                    if not (String.IsNullOrWhiteSpace(stderr)) then
                        eprintfn "%s" stderr

                    if not (String.IsNullOrWhiteSpace(stdout)) then
                        printfn "%s" stdout

                    failwith $"Command failed with exit code %d{proc.ExitCode}"

                (proc.ExitCode, stdout, stderr))

        // Show output after spinner completes
        if not (String.IsNullOrWhiteSpace(stdout)) then
            printfn "%s" (stdout.TrimEnd())

        if not (String.IsNullOrWhiteSpace(stderr)) then
            eprintfn "%s" (stderr.TrimEnd())

        (exitCode, stdout, stderr)

    /// Run a command asynchronously, returning exit code, stdout, stderr.
    let runAsync (command: string) (args: string list) =
        task {
            UI.cmd command (String.concat " " args)
            let psi = mkPsi command args
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use proc = Diagnostics.Process.Start(psi)
            let! stdout = proc.StandardOutput.ReadToEndAsync()
            let! stderr = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()
            return (proc.ExitCode, stdout, stderr)
        }

    /// Run a command silently under a <see cref="SilentRun"/> — any combination of
    /// environment, timeout, working directory and a streaming sink, including the
    /// combinations the individual helpers below do not offer.
    ///
    /// Output is read LINE BY LINE as it arrives rather than buffered until the end,
    /// which is what makes a killed run still informative: everything the child
    /// managed to say has already reached the sink. <c>runSilentWithTimeout</c>
    /// discards precisely that, returning an empty stdout for the one case — a
    /// process that hung — where its output is what you needed.
    let runSilentWith (options: SilentRun) (command: string) (args: string list) : SilentRunResult =
        let psi = mkPsi command args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.CreateNoWindow <- true

        options.WorkingDirectory |> Option.iter (fun dir -> psi.WorkingDirectory <- dir)

        for (key, value) in options.Env do
            psi.EnvironmentVariables.[key] <- value

        let stdout = StringBuilder()
        let stderr = StringBuilder()

        let collect (buffer: StringBuilder) (stream: OutputStream) (line: string) =
            // A null payload is the stream's end-of-output marker, not a line of text.
            if not (isNull line) then
                buffer.AppendLine line |> ignore
                options.Sink |> Option.iter (fun sink -> sink stream line)

        use proc = new Diagnostics.Process()
        proc.StartInfo <- psi
        proc.OutputDataReceived.Add(fun e -> collect stdout Stdout e.Data)
        proc.ErrorDataReceived.Add(fun e -> collect stderr Stderr e.Data)

        proc.Start() |> ignore
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()

        let exited =
            match options.TimeoutMs with
            | Some ms -> proc.WaitForExit(ms)
            | None ->
                proc.WaitForExit()
                true

        if not exited then
            proc.Kill(entireProcessTree = true)

        // The argument-less overload is what drains the asynchronous readers: the
        // millisecond one can return before the handlers have run, so calling this
        // afterwards is the documented way to be sure the buffers above are complete.
        proc.WaitForExit()

        { ExitCode = (if exited then proc.ExitCode else -1)
          Stdout = stdout.ToString().Trim()
          Stderr = stderr.ToString().Trim()
          TimedOut = not exited }

    /// Run a command silently with additional environment variables.
    let runSilentWithEnv (command: string) (args: string list) (env: (string * string) list) =
        let psi = mkPsi command args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.CreateNoWindow <- true

        for (key, value) in env do
            psi.EnvironmentVariables.[key] <- value

        use proc = Diagnostics.Process.Start(psi)
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()
        proc.WaitForExit()
        let stdout = stdoutTask.Result
        let stderr = stderrTask.Result
        (proc.ExitCode, stdout.Trim(), stderr.Trim())

    /// Run a command with additional environment variables (interactive, no capture).
    let runWithEnv (command: string) (args: string list) (env: (string * string) list) =
        UI.cmd command (String.concat " " args)
        let sw = Stopwatch.StartNew()
        let psi = mkPsi command args

        for (key, value) in env do
            psi.EnvironmentVariables.[key] <- value

        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        sw.Stop()
        printfn $"    %s{UI.timing sw.Elapsed}"

        if proc.ExitCode <> 0 then
            failwith $"Command failed with exit code %d{proc.ExitCode}"

    /// Run a command silently with an optional timeout (milliseconds). Returns
    /// (exitCode, trimmed stdout, trimmed stderr); a timeout yields (-1, "", message).
    let runSilentWithTimeout (command: string) (args: string list) (timeout: int option) =
        let psi = mkPsi command args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.CreateNoWindow <- true

        use proc = Diagnostics.Process.Start(psi)
        // Read stdout and stderr in parallel to avoid deadlock when buffer fills
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()

        let exited =
            match timeout with
            | Some ms -> proc.WaitForExit(ms)
            | None ->
                proc.WaitForExit()
                true

        if not exited then
            proc.Kill(entireProcessTree = true)
            (-1, "", $"Process timed out after %d{timeout.Value}ms")
        else
            let stdout = stdoutTask.Result
            let stderr = stderrTask.Result
            (proc.ExitCode, stdout.Trim(), stderr.Trim())

    /// Run a command silently with an optional timeout (milliseconds) in a specific
    /// directory.
    let runSilentWithTimeoutInDir (command: string) (args: string list) (timeout: int option) (workDir: string) =
        let psi = mkPsi command args
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.CreateNoWindow <- true
        psi.WorkingDirectory <- workDir

        use proc = Diagnostics.Process.Start(psi)
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()

        let exited =
            match timeout with
            | Some ms -> proc.WaitForExit(ms)
            | None ->
                proc.WaitForExit()
                true

        if not exited then
            proc.Kill(entireProcessTree = true)
            (-1, "", $"Process timed out after %d{timeout.Value}ms")
        else
            let stdout = stdoutTask.Result
            let stderr = stderrTask.Result
            (proc.ExitCode, stdout.Trim(), stderr.Trim())

    /// Run a command silently in a specific directory.
    let runSilentInDir (command: string) (args: string list) (workDir: string) =
        runSilentWithTimeoutInDir command args None workDir

    /// Run a command silently and return exit code, stdout, stderr as a tuple.
    let runSilent (command: string) (args: string list) = runSilentWithTimeout command args None

    /// Run a command silently and return a <c>CommandResult</c> record.
    let runCommand (command: string) (args: string list) : CommandResult =
        let (exitCode, stdout, stderr) = runSilent command args

        { ExitCode = exitCode
          Stdout = stdout
          Stderr = stderr }

    /// Run a command interactively (no output capture) and return exit code.
    let runInteractive (command: string) (args: string list) : int =
        let psi = mkPsi command args
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    /// Run a command interactively in a specific directory.
    let runInteractiveInDir (command: string) (args: string list) (workDir: string) : int =
        let psi = mkPsi command args
        psi.WorkingDirectory <- workDir
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    /// Run a dotnet command.
    let dotnet (args: string list) = run "dotnet" args

    /// Run a dotnet command with spinner.
    let dotnetSpinner (msg: string) (args: string list) =
        runWithSpinner msg "dotnet" args |> ignore

    /// Run multiple tasks in parallel
    let runParallel (tasks: Task<'T> array) = Task.WhenAll(tasks).Result
