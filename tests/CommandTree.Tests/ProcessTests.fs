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
    let (code, stdout, stderr) = Process.runSilent "echo" [ "hello world" ]
    test <@ code = 0 @>
    test <@ stdout = "hello world" @>
    test <@ stderr = "" @>

[<Fact>]
let ``runSilent captures stderr`` () =
    let (code, _stdout, stderr) = Process.runSilent "sh" [ "-c"; "echo error >&2" ]
    test <@ code = 0 @>
    test <@ stderr = "error" @>

[<Fact>]
let ``runSilent returns non-zero exit code`` () =
    let (code, _, _) = Process.runSilent "sh" [ "-c"; "exit 42" ]
    test <@ code = 42 @>

[<Fact>]
let ``runSilent passes an argument with spaces and quotes literally`` () =
    // The whole point of the ArgumentList path: a single argument containing spaces
    // AND both quote kinds survives intact. A shell (or .NET's string splitter) would
    // re-split this and strip/mangle the quotes.
    let arg = "a b \"c\" 'd'"
    let (code, stdout, _) = Process.runSilent "echo" [ arg ]
    test <@ code = 0 @>
    test <@ stdout = arg @>

[<Fact>]
let ``runSilent keeps multiple tokens separate`` () =
    let (code, stdout, _) = Process.runSilent "echo" [ "one"; "two" ]
    test <@ code = 0 @>
    test <@ stdout = "one two" @>

// =============================================================================
// runCommand — CommandResult record
// =============================================================================

[<Fact>]
let ``runCommand returns CommandResult with correct fields`` () =
    let result = Process.runCommand "echo" [ "test output" ]
    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "test output" @>
    test <@ result.Stderr = "" @>

[<Fact>]
let ``runCommand captures failure exit code`` () =
    let result = Process.runCommand "sh" [ "-c"; "echo fail >&2; exit 1" ]
    test <@ result.ExitCode = 1 @>
    test <@ result.Stderr = "fail" @>

// =============================================================================
// runSilentWithEnv — environment variable injection
// =============================================================================

[<Fact>]
let ``runSilentWithEnv passes environment variables`` () =
    let (code, stdout, _) =
        Process.runSilentWithEnv "sh" [ "-c"; "echo $TEST_VAR" ] [ ("TEST_VAR", "injected-value") ]

    test <@ code = 0 @>
    test <@ stdout = "injected-value" @>

[<Fact>]
let ``runSilentWithEnv passes multiple env vars`` () =
    let (code, stdout, _) =
        Process.runSilentWithEnv "sh" [ "-c"; "echo $A-$B" ] [ ("A", "hello"); ("B", "world") ]

    test <@ code = 0 @>
    test <@ stdout = "hello-world" @>

// =============================================================================
// runSilentWithTimeout
// =============================================================================

[<Fact>]
let ``runSilentWithTimeout completes fast commands`` () =
    let (code, stdout, _) = Process.runSilentWithTimeout "echo" [ "fast" ] (Some 5000)
    test <@ code = 0 @>
    test <@ stdout = "fast" @>

[<Fact>]
let ``runSilentWithTimeout kills slow commands`` () =
    let (code, _, stderr) = Process.runSilentWithTimeout "sleep" [ "30" ] (Some 100)
    test <@ code = -1 @>
    test <@ stderr.Contains("timed out") @>

[<Fact>]
let ``runSilentWithTimeout None means no timeout`` () =
    let (code, stdout, _) = Process.runSilentWithTimeout "echo" [ "no-timeout" ] None
    test <@ code = 0 @>
    test <@ stdout = "no-timeout" @>

// =============================================================================
// runInteractive — returns exit code
// =============================================================================

[<Fact>]
let ``runInteractive returns zero for successful command`` () =
    let code = Process.runInteractive "echo" [ "interactive" ]
    test <@ code = 0 @>

[<Fact>]
let ``runInteractive returns non-zero for failed command`` () =
    let code = Process.runInteractive "sh" [ "-c"; "exit 7" ]
    test <@ code = 7 @>

// =============================================================================
// runAsync — Task-based
// =============================================================================

[<Fact>]
let ``runAsync returns exit code stdout stderr`` () =
    let (_output, result) =
        UITests.captureStdout (fun () ->
            let task = Process.runAsync "echo" [ "async-output" ]
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
        UITests.captureStdout (fun () -> Process.runWithSpinner "echo test" "echo" [ "spinner-output" ])

    let (code, out, _err) = result
    test <@ code = 0 @>
    test <@ out.Trim().Contains("spinner-output") @>

[<Fact>]
let ``runWithSpinner throws on non-zero exit code`` () =
    let ex =
        Assert.Throws<Exception>(fun () ->
            Process.runWithSpinner "failing" "sh" [ "-c"; "echo out; echo err >&2; exit 1" ]
            |> ignore)

    test <@ ex.Message.Contains("exit code") @>

// =============================================================================
// runWithEnv — interactive with environment variables
// =============================================================================

[<Fact>]
let ``runWithEnv runs successfully`` () =
    UITests.captureStdout (fun () -> Process.runWithEnv "sh" [ "-c"; "exit 0" ] [ ("TEST_RWE", "val") ])
    |> ignore

[<Fact>]
let ``runWithEnv throws on non-zero exit`` () =
    let ex =
        Assert.Throws<Exception>(fun () ->
            UITests.captureStdout (fun () -> Process.runWithEnv "sh" [ "-c"; "exit 3" ] [])
            |> ignore)

    test <@ ex.Message.Contains("exit code") @>

// =============================================================================
// dotnet / dotnetSpinner — thin wrappers
// =============================================================================

[<Fact>]
let ``dotnet runs dotnet command`` () =
    UITests.captureStdout (fun () -> Process.dotnet [ "--version" ]) |> ignore

[<Fact>]
let ``dotnetSpinner runs dotnet with spinner`` () =
    UITests.captureStdout (fun () -> Process.dotnetSpinner "Getting version" [ "--version" ])
    |> ignore

// =============================================================================
// runParallel — multiple tasks
// =============================================================================

[<Fact>]
let ``runParallel completes all tasks`` () =
    let (_output, results) =
        UITests.captureStdout (fun () ->
            let tasks =
                [| Process.runAsync "echo" [ "a" ]
                   Process.runAsync "echo" [ "b" ]
                   Process.runAsync "echo" [ "c" ] |]

            Process.runParallel tasks)

    test <@ results.Length = 3 @>

    for (code, _, _) in results do
        test <@ code = 0 @>

// =============================================================================
// runInteractiveInDir — interactive with working directory
// =============================================================================

[<Fact>]
let ``runInteractiveInDir runs in specified directory`` () =
    let code =
        Process.runInteractiveInDir "sh" [ "-c"; "test -d .git || test -d .jj" ] "/tmp"
    // /tmp won't have .git or .jj, so this should fail
    test <@ code <> 0 @>

[<Fact>]
let ``runInteractiveInDir returns zero for successful command`` () =
    let code = Process.runInteractiveInDir "pwd" [] "/tmp"
    test <@ code = 0 @>

// =============================================================================
// runSilentInDir — silent with working directory
// =============================================================================

[<Fact>]
let ``runSilentInDir runs in specified directory`` () =
    let (code, stdout, _) = Process.runSilentInDir "pwd" [] "/tmp"
    test <@ code = 0 @>
    // macOS resolves /tmp to /private/tmp
    test <@ stdout.Contains("tmp") @>

[<Fact>]
let ``runSilentWithTimeoutInDir respects timeout`` () =
    let (code, _, stderr) =
        Process.runSilentWithTimeoutInDir "sleep" [ "30" ] (Some 100) "/tmp"

    test <@ code = -1 @>
    test <@ stderr.Contains("timed out") @>

// =============================================================================
// run — interactive with timing
// =============================================================================

[<Fact>]
let ``run throws on non-zero exit code`` () =
    let ex =
        Assert.Throws<Exception>(fun () ->
            UITests.captureStdout (fun () -> Process.run "sh" [ "-c"; "exit 5" ]) |> ignore)

    test <@ ex.Message.Contains("exit code") @>

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

// =============================================================================
// runSilentWith — the cross-product the individual helpers leave with holes
// =============================================================================

[<Fact>]
let ``runSilentWith passes an environment AND a timeout in one call`` () =
    // The combination that had no helper: every env-taking runner lacked a timeout
    // and every timeout-taking runner lacked an environment, so a caller needing
    // both could not reach it by choosing differently.
    let options =
        SilentRun.defaults
        |> SilentRun.withEnv [ "CT_TEST_VALUE", "from-the-environment" ]
        |> SilentRun.withTimeoutMs 30_000

    let result =
        Process.runSilentWith options "sh" [ "-c"; "printf '%s' \"$CT_TEST_VALUE\"" ]

    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "from-the-environment" @>
    test <@ result.TimedOut = false @>

[<Fact>]
let ``runSilentWith runs in a given directory`` () =
    let options = SilentRun.defaults |> SilentRun.inDirectory "/"
    let result = Process.runSilentWith options "sh" [ "-c"; "pwd" ]

    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "/" @>

[<Fact>]
let ``runSilentWith hands each line to the sink as it arrives, tagged by stream`` () =
    let seen = System.Collections.Concurrent.ConcurrentQueue<OutputStream * string>()

    let options =
        SilentRun.defaults
        |> SilentRun.withSink (fun stream line -> seen.Enqueue(stream, line))

    let result = Process.runSilentWith options "sh" [ "-c"; "echo out; echo err >&2" ]

    let delivered = seen |> List.ofSeq
    test <@ result.ExitCode = 0 @>
    test <@ List.contains (Stdout, "out") delivered @>
    test <@ List.contains (Stderr, "err") delivered @>

[<Fact>]
let ``runSilentWith keeps what a killed child already said`` () =
    // The guard. A child that speaks and then hangs is the case where its output is
    // most wanted, and the case the buffered runners cannot serve: the buffer is read
    // after the process ends, so killing it discards exactly what was needed.
    //
    // Deterministic without a sleep-race: the child writes BEFORE it blocks, so the
    // line is delivered at process start while the timeout is three seconds away.
    let options = SilentRun.defaults |> SilentRun.withTimeoutMs 3_000

    let result =
        Process.runSilentWith options "sh" [ "-c"; "echo spoke-before-hanging; sleep 30" ]

    test <@ result.TimedOut = true @>
    test <@ result.ExitCode = -1 @>
    test <@ result.Stdout = "spoke-before-hanging" @>

[<Fact>]
let ``runSilentWith reports TimedOut false for a child that finishes in time`` () =
    // The positive control for the guard above. Without it, `TimedOut = true` could be
    // a constant rather than a measurement, and `Stdout` surviving would prove nothing
    // about the kill path — the same assertions would pass on a runner that never
    // times out at all.
    let options = SilentRun.defaults |> SilentRun.withTimeoutMs 3_000

    let result =
        Process.runSilentWith options "sh" [ "-c"; "echo spoke-before-hanging" ]

    test <@ result.TimedOut = false @>
    test <@ result.ExitCode = 0 @>
    test <@ result.Stdout = "spoke-before-hanging" @>

[<Fact>]
let ``runSilentWithTimeout discards on a kill what runSilentWith keeps`` () =
    // Pins the difference rather than describing it, so neither behaviour can quietly
    // become the other. This characterises the existing helper, it does not complain
    // about it: its documented contract is (-1, "", message), callers may depend on
    // that, and it is deliberately left alone.
    let command = [ "-c"; "echo spoke-before-hanging; sleep 30" ]

    let (oldCode, oldStdout, _) = Process.runSilentWithTimeout "sh" command (Some 3_000)

    let fresh =
        Process.runSilentWith (SilentRun.defaults |> SilentRun.withTimeoutMs 3_000) "sh" command

    test <@ oldCode = -1 && oldStdout = "" @>
    test <@ fresh.ExitCode = -1 && fresh.Stdout = "spoke-before-hanging" @>
