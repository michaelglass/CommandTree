namespace CommandTree

open System
open System.Diagnostics
open System.Threading.Tasks

/// Result of running a command (for named field access)
type CommandResult =
    { ExitCode: int
      Stdout: string
      Stderr: string }

/// Process execution helpers
module Process =
    /// Build a <c>ProcessStartInfo</c> for <c>command</c>, adding each element of
    /// <c>args</c> to <c>ArgumentList</c> so it is passed to the child process as a
    /// single literal token. Tokens are never re-parsed, so spaces and quotes inside
    /// an argument survive intact. <c>UseShellExecute</c> is set to <c>false</c>;
    /// callers configure redirection / working directory / environment afterward.
    let private mkPsi (command: string) (args: string list) =
        let psi = ProcessStartInfo(command)
        psi.UseShellExecute <- false
        args |> List.iter psi.ArgumentList.Add
        psi

    /// Run a command and wait for it to complete. Each element of <c>args</c> is
    /// passed as a discrete token via <c>ProcessStartInfo.ArgumentList</c>, so
    /// arguments containing spaces or quotes are not re-parsed.
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

    /// Run a command with spinner, capturing output. Each element of <c>args</c> is
    /// passed as a discrete token via <c>ProcessStartInfo.ArgumentList</c>, so
    /// arguments containing spaces or quotes are not re-parsed.
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

    /// Run a command asynchronously, returning exit code, stdout, stderr. Each
    /// element of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or
    /// quotes are not re-parsed.
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

    /// Run a command silently with additional environment variables. Each element of
    /// <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
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
    /// Each element of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
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

    /// Run a command silently with an optional timeout (milliseconds). Each element
    /// of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed. Returns (exitCode, trimmed stdout, trimmed stderr); a
    /// timeout yields (-1, "", message).
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
    /// directory. Each element of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
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

    /// Run a command silently in a specific directory. Each element of <c>args</c> is
    /// passed as a discrete token via <c>ProcessStartInfo.ArgumentList</c>, so
    /// arguments containing spaces or quotes are not re-parsed.
    let runSilentInDir (command: string) (args: string list) (workDir: string) =
        runSilentWithTimeoutInDir command args None workDir

    /// Run a command silently and return exit code, stdout, stderr as a tuple. Each
    /// element of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
    let runSilent (command: string) (args: string list) = runSilentWithTimeout command args None

    /// Run a command silently and return a <c>CommandResult</c> record. Each element
    /// of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
    let runCommand (command: string) (args: string list) : CommandResult =
        let (exitCode, stdout, stderr) = runSilent command args

        { ExitCode = exitCode
          Stdout = stdout
          Stderr = stderr }

    /// Run a command interactively (no output capture) and return exit code. Each
    /// element of <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
    let runInteractive (command: string) (args: string list) : int =
        let psi = mkPsi command args
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    /// Run a command interactively in a specific directory. Each element of
    /// <c>args</c> is passed as a discrete token via
    /// <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces or quotes
    /// are not re-parsed.
    let runInteractiveInDir (command: string) (args: string list) (workDir: string) : int =
        let psi = mkPsi command args
        psi.WorkingDirectory <- workDir
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    /// Run a dotnet command. Each element of <c>args</c> is passed as a discrete
    /// token via <c>ProcessStartInfo.ArgumentList</c>, so arguments containing spaces
    /// or quotes are not re-parsed.
    let dotnet (args: string list) = run "dotnet" args

    /// Run a dotnet command with spinner. Each element of <c>args</c> is passed as a
    /// discrete token via <c>ProcessStartInfo.ArgumentList</c>, so arguments
    /// containing spaces or quotes are not re-parsed.
    let dotnetSpinner (msg: string) (args: string list) =
        runWithSpinner msg "dotnet" args |> ignore

    /// Run multiple tasks in parallel
    let runParallel (tasks: Task<'T> array) = Task.WhenAll(tasks).Result
