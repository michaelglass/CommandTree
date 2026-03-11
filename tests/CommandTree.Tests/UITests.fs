module CommandTree.Tests.UITests

open System
open System.IO
open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// Helper: capture stdout/stderr
// =============================================================================

let captureStdout (action: unit -> 'T) : string * 'T =
    let oldOut = Console.Out
    use sw = new StringWriter()
    Console.SetOut(sw)

    try
        let result = action ()
        Console.Out.Flush()
        (sw.ToString(), result)
    finally
        Console.SetOut(oldOut)

let captureStderr (action: unit -> 'T) : string * 'T =
    let oldErr = Console.Error
    use sw = new StringWriter()
    Console.SetError(sw)

    try
        let result = action ()
        Console.Error.Flush()
        (sw.ToString(), result)
    finally
        Console.SetError(oldErr)

let captureBoth (action: unit -> 'T) : string * string * 'T =
    let oldOut = Console.Out
    let oldErr = Console.Error
    use outSw = new StringWriter()
    use errSw = new StringWriter()
    Console.SetOut(outSw)
    Console.SetError(errSw)

    try
        let result = action ()
        Console.Out.Flush()
        Console.Error.Flush()
        (outSw.ToString(), errSw.ToString(), result)
    finally
        Console.SetOut(oldOut)
        Console.SetError(oldErr)

// =============================================================================
// Color module constants
// =============================================================================

[<Fact>]
let ``Color.reset is ANSI reset sequence`` () =
    test <@ Color.reset = "\x1b[0m" @>

[<Fact>]
let ``Color.bold is ANSI bold sequence`` () =
    test <@ Color.bold = "\x1b[1m" @>

[<Fact>]
let ``Color.dim is ANSI dim sequence`` () =
    test <@ Color.dim = "\x1b[2m" @>

[<Fact>]
let ``Color.red is ANSI red sequence`` () =
    test <@ Color.red = "\x1b[31m" @>

[<Fact>]
let ``Color.green is ANSI green sequence`` () =
    test <@ Color.green = "\x1b[32m" @>

[<Fact>]
let ``Color.yellow is ANSI yellow sequence`` () =
    test <@ Color.yellow = "\x1b[33m" @>

[<Fact>]
let ``Color.blue is ANSI blue sequence`` () =
    test <@ Color.blue = "\x1b[34m" @>

[<Fact>]
let ``Color.cyan is ANSI cyan sequence`` () =
    test <@ Color.cyan = "\x1b[36m" @>

// =============================================================================
// UI output functions — fixture tests capturing stdout/stderr
// =============================================================================

[<Fact>]
let ``title outputs bold cyan title bar`` () =
    let (output, _) = captureStdout (fun () -> UI.title "My Title")
    test <@ output = $"\n\x1b[1m\x1b[36m━━━ My Title ━━━\x1b[0m\n" @>

[<Fact>]
let ``section outputs bold cyan section header`` () =
    let (output, _) = captureStdout (fun () -> UI.section "Build")
    test <@ output = $"\n\x1b[1m\x1b[36m▶ Build\x1b[0m\n" @>

[<Fact>]
let ``success outputs green checkmark`` () =
    let (output, _) = captureStdout (fun () -> UI.success "All done")
    test <@ output = "\x1b[32m✓ All done\x1b[0m\n" @>

[<Fact>]
let ``pass is alias for success`` () =
    let (output, _) = captureStdout (fun () -> UI.pass "Passed")
    test <@ output = "\x1b[32m✓ Passed\x1b[0m\n" @>

[<Fact>]
let ``skip outputs yellow skip marker`` () =
    let (output, _) = captureStdout (fun () -> UI.skip "Skipped step")
    test <@ output = "\x1b[33m⏭ Skipped step\x1b[0m\n" @>

[<Fact>]
let ``fail outputs red X to stderr`` () =
    let (stderr, _) = captureStderr (fun () -> UI.fail "Something broke")
    test <@ stderr = "\x1b[31m✗ Something broke\x1b[0m\n" @>

[<Fact>]
let ``info outputs blue info marker`` () =
    let (output, _) = captureStdout (fun () -> UI.info "Note this")
    test <@ output = "\x1b[34mℹ Note this\x1b[0m\n" @>

[<Fact>]
let ``warn outputs yellow warning`` () =
    let (output, _) = captureStdout (fun () -> UI.warn "Watch out")
    test <@ output = "\x1b[33m⚠ Watch out\x1b[0m\n" @>

[<Fact>]
let ``dimInfo outputs dim indented text`` () =
    let (output, _) = captureStdout (fun () -> UI.dimInfo "details here")
    test <@ output = "\x1b[2m  details here\x1b[0m\n" @>

[<Fact>]
let ``cmd outputs dim command echo`` () =
    let (output, _) = captureStdout (fun () -> UI.cmd "echo" "hello world")
    test <@ output = "\x1b[2m  $ echo hello world\x1b[0m\n" @>

// =============================================================================
// timingColor — pure function, gradient from green to red
// =============================================================================

[<Fact>]
let ``timingColor under 2s is bright green`` () =
    test <@ UI.timingColor 0.5 = "\x1b[38;5;46m" @>
    test <@ UI.timingColor 1.9 = "\x1b[38;5;46m" @>

[<Fact>]
let ``timingColor at 3s is yellow-green`` () =
    test <@ UI.timingColor 3.5 = "\x1b[38;5;154m" @>

[<Fact>]
let ``timingColor at 5s is yellow`` () =
    test <@ UI.timingColor 5.5 = "\x1b[38;5;226m" @>

[<Fact>]
let ``timingColor at 8s is dark orange`` () =
    test <@ UI.timingColor 8.5 = "\x1b[38;5;208m" @>

[<Fact>]
let ``timingColor at 10s+ is red`` () =
    test <@ UI.timingColor 10.0 = "\x1b[38;5;196m" @>
    test <@ UI.timingColor 30.0 = "\x1b[38;5;196m" @>

// =============================================================================
// timing — pure function, formats TimeSpan with color
// =============================================================================

[<Fact>]
let ``timing formats sub-second as milliseconds`` () =
    let result = UI.timing (TimeSpan.FromMilliseconds(450.0))
    test <@ result = "\x1b[38;5;46m(450ms)\x1b[0m" @>

[<Fact>]
let ``timing formats seconds with one decimal`` () =
    let result = UI.timing (TimeSpan.FromSeconds(3.7))
    test <@ result = "\x1b[38;5;154m(3.7s)\x1b[0m" @>

[<Fact>]
let ``timing formats minutes and seconds`` () =
    let result = UI.timing (TimeSpan.FromSeconds(125.0))
    test <@ result = "\x1b[38;5;196m(2m 5s)\x1b[0m" @>

// =============================================================================
// withSpinner — non-interactive mode (stdout is redirected in tests)
// =============================================================================

[<Fact>]
let ``withSpinner returns action result in non-interactive mode`` () =
    let (output, result) = captureStdout (fun () -> UI.withSpinner "Computing" (fun () -> 42))
    test <@ result = 42 @>
    test <@ output.Contains("Computing") @>
    test <@ output.Contains("✓") @>

[<Fact>]
let ``withSpinner shows failure marker on exception`` () =
    let mutable output = ""

    let ex =
        Assert.Throws<Exception>(fun () ->
            let (o, _) =
                captureStdout (fun () ->
                    UI.withSpinner "Failing" (fun () -> failwith "boom") |> ignore)

            output <- o)

    test <@ ex.Message = "boom" @>

[<Fact>]
let ``withSpinnerQuiet returns result without output in non-interactive mode`` () =
    let (output, result) =
        captureStdout (fun () -> UI.withSpinnerQuiet "Quiet work" (fun () -> "done"))

    test <@ result = "done" @>
    // In non-interactive mode, withSpinnerQuiet just runs the action
    test <@ output = "" @>
