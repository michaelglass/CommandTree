namespace CommandTree

open System
open System.Diagnostics
open System.Threading

/// Terminal ANSI color escape codes
module Color =
    /// Reset all formatting
    let reset = "\x1b[0m"

    /// Bold text
    let bold = "\x1b[1m"

    /// Dim/faint text
    let dim = "\x1b[2m"

    /// Red foreground
    let red = "\x1b[31m"

    /// Green foreground
    let green = "\x1b[32m"

    /// Yellow foreground
    let yellow = "\x1b[33m"

    /// Blue foreground
    let blue = "\x1b[34m"

    /// Cyan foreground
    let cyan = "\x1b[36m"

/// Terminal UI helpers for colored, structured output
module UI =
    /// Print a bold cyan title bar
    let title name =
        printfn $"\n%s{Color.bold}%s{Color.cyan}━━━ %s{name} ━━━%s{Color.reset}"

    /// Print a bold cyan section header with arrow
    let section name =
        printfn $"\n%s{Color.bold}%s{Color.cyan}▶ %s{name}%s{Color.reset}"

    /// Print a green success message with checkmark
    let success msg =
        printfn $"%s{Color.green}✓ %s{msg}%s{Color.reset}"

    /// Alias for success
    let pass msg = success msg // Alias for success

    /// Print a yellow skip message
    let skip msg =
        printfn $"%s{Color.yellow}⏭ %s{msg}%s{Color.reset}"

    /// Print a red failure message to stderr
    let fail msg =
        eprintfn $"%s{Color.red}✗ %s{msg}%s{Color.reset}"

    /// Print a blue informational message
    let info msg =
        printfn $"%s{Color.blue}ℹ %s{msg}%s{Color.reset}"

    /// Print a yellow warning message
    let warn msg =
        printfn $"%s{Color.yellow}⚠ %s{msg}%s{Color.reset}"

    /// Print dim indented detail text
    let dimInfo msg =
        printfn $"%s{Color.dim}  %s{msg}%s{Color.reset}"

    /// Print a dim command echo ($ command args)
    let cmd command args =
        printfn $"%s{Color.dim}  $ %s{command} %s{args}%s{Color.reset}"

    /// Get timing color based on duration (gradient from green to red)
    let timingColor (secs: float) =
        if secs < 2.0 then "\x1b[38;5;46m" // Bright green
        elif secs < 3.0 then "\x1b[38;5;118m" // Light green
        elif secs < 4.0 then "\x1b[38;5;154m" // Yellow-green
        elif secs < 5.0 then "\x1b[38;5;190m" // Greenish yellow
        elif secs < 6.0 then "\x1b[38;5;226m" // Yellow
        elif secs < 7.0 then "\x1b[38;5;220m" // Gold
        elif secs < 8.0 then "\x1b[38;5;214m" // Orange
        elif secs < 9.0 then "\x1b[38;5;208m" // Dark orange
        elif secs < 10.0 then "\x1b[38;5;202m" // Red-orange
        else "\x1b[38;5;196m" // Red

    /// Format elapsed time with color
    let timing (elapsed: TimeSpan) =
        let secs = elapsed.TotalSeconds
        let color = timingColor secs

        if secs < 1.0 then
            $"%s{color}(%d{int elapsed.TotalMilliseconds}ms)%s{Color.reset}"
        elif secs < 60.0 then
            $"%s{color}(%.1f{secs}s)%s{Color.reset}"
        else
            $"%s{color}(%d{int elapsed.TotalMinutes}m %d{elapsed.Seconds}s)%s{Color.reset}"

    /// True when stdout is a terminal (not redirected), enabling spinners and carriage returns
    let isInteractive = not Console.IsOutputRedirected

    /// Spinner frames for progress indication
    let private spinnerFrames = [| "⠋"; "⠙"; "⠹"; "⠸"; "⠼"; "⠴"; "⠦"; "⠧"; "⠇"; "⠏" |]

    /// Run an action with a spinner animation
    let withSpinner (message: string) (action: unit -> 'T) : 'T =
        let sw = Stopwatch.StartNew()

        if not isInteractive then
            printfn $"  %s{message}..."

            try
                let result = action ()
                sw.Stop()
                printfn $"%s{Color.green}✓%s{Color.reset} %s{message} %s{timing sw.Elapsed}"
                result
            with ex ->
                sw.Stop()
                printfn $"%s{Color.red}✗%s{Color.reset} %s{message} %s{timing sw.Elapsed}"
                reraise ()
        else

            let mutable running = true
            let mutable frameIndex = 0

            let spinnerThread =
                Thread(fun () ->
                    while running do
                        let elapsed = sw.Elapsed

                        let timeStr =
                            if elapsed.TotalSeconds < 1.0 then
                                ""
                            elif elapsed.TotalSeconds < 60.0 then
                                $" %s{Color.dim}(%.0f{elapsed.TotalSeconds}s)%s{Color.reset}"
                            else
                                $" %s{Color.dim}(%d{int elapsed.TotalMinutes}m %d{elapsed.Seconds}s)%s{Color.reset}"

                        Console.Write(
                            $"\r%s{Color.cyan}%s{spinnerFrames.[frameIndex]}%s{Color.reset} %s{message}%s{timeStr}   "
                        )

                        frameIndex <- (frameIndex + 1) % spinnerFrames.Length
                        Thread.Sleep(80))

            spinnerThread.Start()

            try
                let result = action ()
                running <- false
                spinnerThread.Join()
                sw.Stop()
                // Clear line and show final result
                Console.Write($"\r%s{Color.green}✓%s{Color.reset} %s{message} %s{timing sw.Elapsed}   \n")
                result
            with ex ->
                running <- false
                spinnerThread.Join()
                sw.Stop()
                Console.Write($"\r%s{Color.red}✗%s{Color.reset} %s{message} %s{timing sw.Elapsed}   \n")
                reraise ()

    /// Run an action with a spinner, clearing the line when done (caller handles output)
    let withSpinnerQuiet (message: string) (action: unit -> 'T) : 'T =
        if not isInteractive then
            action ()
        else

            let mutable running = true
            let mutable frameIndex = 0
            let sw = Stopwatch.StartNew()

            let spinnerThread =
                Thread(fun () ->
                    while running do
                        let elapsed = sw.Elapsed

                        let timeStr =
                            if elapsed.TotalSeconds < 1.0 then
                                ""
                            elif elapsed.TotalSeconds < 60.0 then
                                $" %s{Color.dim}(%.0f{elapsed.TotalSeconds}s)%s{Color.reset}"
                            else
                                $" %s{Color.dim}(%d{int elapsed.TotalMinutes}m %d{elapsed.Seconds}s)%s{Color.reset}"

                        Console.Write(
                            $"\r%s{Color.cyan}%s{spinnerFrames.[frameIndex]}%s{Color.reset} %s{message}%s{timeStr}   "
                        )

                        frameIndex <- (frameIndex + 1) % spinnerFrames.Length
                        Thread.Sleep(80))

            spinnerThread.Start()

            try
                let result = action ()
                running <- false
                spinnerThread.Join()
                // Clear spinner line — caller handles output
                let blank = String.replicate (message.Length + 20) " "
                Console.Write($"\r%s{blank}\r")
                result
            with ex ->
                running <- false
                spinnerThread.Join()
                let blank = String.replicate (message.Length + 20) " "
                Console.Write($"\r%s{blank}\r")
                reraise ()
