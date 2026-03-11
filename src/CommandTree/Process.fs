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
    /// Run a command and wait for it to complete
    let run (command: string) (args: string) =
        UI.cmd command args
        let sw = Stopwatch.StartNew()
        let psi = ProcessStartInfo(command, args)
        psi.UseShellExecute <- false
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        sw.Stop()
        printfn $"    %s{UI.timing sw.Elapsed}"

        if proc.ExitCode <> 0 then
            failwith $"Command failed with exit code %d{proc.ExitCode}"

    /// Run a command with spinner, capturing output
    let runWithSpinner (message: string) (command: string) (args: string) =
        let (stdout, stderr) =
            UI.withSpinner message (fun () ->
                let psi = ProcessStartInfo(command, args)
                psi.UseShellExecute <- false
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

                (stdout, stderr))

        // Show output after spinner completes
        if not (String.IsNullOrWhiteSpace(stdout)) then
            printfn "%s" (stdout.TrimEnd())

        if not (String.IsNullOrWhiteSpace(stderr)) then
            eprintfn "%s" (stderr.TrimEnd())

        (stdout, stderr)

    /// Run a command asynchronously, returning exit code, stdout, stderr
    let runAsync (command: string) (args: string) =
        task {
            UI.cmd command args
            let psi = ProcessStartInfo(command, args)
            psi.UseShellExecute <- false
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            use proc = Diagnostics.Process.Start(psi)
            let! stdout = proc.StandardOutput.ReadToEndAsync()
            let! stderr = proc.StandardError.ReadToEndAsync()
            do! proc.WaitForExitAsync()
            return (proc.ExitCode, stdout, stderr)
        }

    /// Run a command silently with additional environment variables
    let runSilentWithEnv (command: string) (args: string) (env: (string * string) list) =
        let psi = ProcessStartInfo(command, args)
        psi.UseShellExecute <- false
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

    /// Run a command with additional environment variables (interactive, no capture)
    let runWithEnv (command: string) (args: string) (env: (string * string) list) =
        UI.cmd command args
        let sw = Stopwatch.StartNew()
        let psi = ProcessStartInfo(command, args)
        psi.UseShellExecute <- false

        for (key, value) in env do
            psi.EnvironmentVariables.[key] <- value

        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        sw.Stop()
        printfn $"    %s{UI.timing sw.Elapsed}"

        if proc.ExitCode <> 0 then
            failwith $"Command failed with exit code %d{proc.ExitCode}"

    /// Run a command silently with optional timeout (milliseconds)
    let runSilentWithTimeout (command: string) (args: string) (timeout: int option) =
        let psi = ProcessStartInfo(command, args)
        psi.UseShellExecute <- false
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

    /// Run a command silently and return exit code, stdout, stderr as tuple
    let runSilent (command: string) (args: string) = runSilentWithTimeout command args None

    /// Run a command silently and return a CommandResult record
    let runCommand (command: string) (args: string) : CommandResult =
        let (exitCode, stdout, stderr) = runSilent command args

        { ExitCode = exitCode
          Stdout = stdout
          Stderr = stderr }

    /// Run a command interactively (no output capture) and return exit code
    let runInteractive (command: string) (args: string) : int =
        let psi = ProcessStartInfo(command, args)
        psi.UseShellExecute <- false
        use proc = Diagnostics.Process.Start(psi)
        proc.WaitForExit()
        proc.ExitCode

    /// Run dotnet command
    let dotnet args = run "dotnet" args

    /// Run dotnet command with spinner
    let dotnetSpinner msg args =
        runWithSpinner msg "dotnet" args |> ignore

    /// Run multiple tasks in parallel
    let runParallel (tasks: Task<'T> array) = Task.WhenAll(tasks).Result
