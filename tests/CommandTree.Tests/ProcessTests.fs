module CommandTree.Tests.ProcessTests

open System
open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// runSilent — capture output without terminal side effects
// =============================================================================

[<Fact>]
let ``runSilent captures stdout`` () =
    let (code, stdout, stderr) = Process.runSilent "echo" "hello world"
    test <@ code = 0 @>
    test <@ stdout = "hello world" @>
    test <@ stderr = "" @>

[<Fact>]
let ``runSilent captures stderr`` () =
    let (code, _stdout, stderr) = Process.runSilent "sh" "-c \"echo error >&2\""
    test <@ code = 0 @>
    test <@ stderr = "error" @>

[<Fact>]
let ``runSilent returns non-zero exit code`` () =
    let (code, _, _) = Process.runSilent "sh" "-c \"exit 42\""
    test <@ code = 42 @>

// =============================================================================
// runCommand — CommandResult record
// =============================================================================

[<Fact>]
let ``runCommand returns CommandResult with correct fields`` () =
    let result = Process.runCommand "echo" "test output"
    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "test output" @>
    test <@ result.Stderr = "" @>

[<Fact>]
let ``runCommand captures failure exit code`` () =
    let result = Process.runCommand "sh" "-c \"echo fail >&2; exit 1\""
    test <@ result.ExitCode = 1 @>
    test <@ result.Stderr = "fail" @>

// =============================================================================
// runSilentWithEnv — environment variable injection
// =============================================================================

[<Fact>]
let ``runSilentWithEnv passes environment variables`` () =
    let (code, stdout, _) =
        Process.runSilentWithEnv "sh" "-c \"echo $TEST_VAR\"" [ ("TEST_VAR", "injected-value") ]

    test <@ code = 0 @>
    test <@ stdout = "injected-value" @>

[<Fact>]
let ``runSilentWithEnv passes multiple env vars`` () =
    let (code, stdout, _) =
        Process.runSilentWithEnv "sh" "-c \"echo $A-$B\"" [ ("A", "hello"); ("B", "world") ]

    test <@ code = 0 @>
    test <@ stdout = "hello-world" @>

// =============================================================================
// runSilentWithTimeout
// =============================================================================

[<Fact>]
let ``runSilentWithTimeout completes fast commands`` () =
    let (code, stdout, _) = Process.runSilentWithTimeout "echo" "fast" (Some 5000)
    test <@ code = 0 @>
    test <@ stdout = "fast" @>

[<Fact>]
let ``runSilentWithTimeout kills slow commands`` () =
    let (code, _, stderr) = Process.runSilentWithTimeout "sleep" "30" (Some 100)
    test <@ code = -1 @>
    test <@ stderr.Contains("timed out") @>

[<Fact>]
let ``runSilentWithTimeout None means no timeout`` () =
    let (code, stdout, _) = Process.runSilentWithTimeout "echo" "no-timeout" None
    test <@ code = 0 @>
    test <@ stdout = "no-timeout" @>

// =============================================================================
// runInteractive — returns exit code
// =============================================================================

[<Fact>]
let ``runInteractive returns zero for successful command`` () =
    let code = Process.runInteractive "echo" "interactive"
    test <@ code = 0 @>

[<Fact>]
let ``runInteractive returns non-zero for failed command`` () =
    let code = Process.runInteractive "sh" "-c \"exit 7\""
    test <@ code = 7 @>

// =============================================================================
// runAsync — Task-based
// =============================================================================

[<Fact>]
let ``runAsync returns exit code stdout stderr`` () =
    let (_output, result) =
        UITests.captureStdout (fun () ->
            let task = Process.runAsync "echo" "async-output"
            task.Result)

    let (code, out, err) = result
    test <@ code = 0 @>
    test <@ out.Trim() = "async-output" @>
    test <@ err = "" @>

// =============================================================================
// runWithSpinner — spinner + captured output
// =============================================================================

[<Fact>]
let ``runWithSpinner returns exit code stdout stderr tuple`` () =
    let (_output, result) =
        UITests.captureStdout (fun () -> Process.runWithSpinner "echo test" "echo" "spinner-output")

    let (code, out, _err) = result
    test <@ code = 0 @>
    test <@ out.Trim().Contains("spinner-output") @>

// =============================================================================
// runParallel — multiple tasks
// =============================================================================

[<Fact>]
let ``runParallel completes all tasks`` () =
    let (_output, results) =
        UITests.captureStdout (fun () ->
            let tasks =
                [| Process.runAsync "echo" "a"
                   Process.runAsync "echo" "b"
                   Process.runAsync "echo" "c" |]

            Process.runParallel tasks)

    test <@ results.Length = 3 @>

    for (code, _, _) in results do
        test <@ code = 0 @>

// =============================================================================
// CommandResult record structure
// =============================================================================

[<Fact>]
let ``CommandResult has expected field names`` () =
    let result: CommandResult =
        { ExitCode = 0
          Stdout = "out"
          Stderr = "err" }

    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "out" @>
    test <@ result.Stderr = "err" @>
