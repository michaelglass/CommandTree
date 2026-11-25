namespace CommandTree

open System

/// What happened when help was handed to a pager.
type PagerOutcome =
    /// The pager ran and the reader quit it (having read all of the help or not)
    | PagerShown
    /// The pager could not be started
    | PagerUnavailable

/// The side effects help output needs, as functions. <c>HelpPrinter.system</c> is the real
/// console; tests pass their own, so every arm of <c>HelpPrinter</c> runs without a terminal.
[<NoComparison; NoEquality>]
type HelpConsole =
    {
        /// Whether stdout is a pipe or a file
        IsOutputRedirected: unit -> bool
        /// The terminal's size as (columns, rows). May throw when there is no console.
        WindowSize: unit -> int * int
        /// An environment variable, if set
        Variable: string -> string option
        /// Write text and a newline to stdout
        WriteLine: string -> unit
        /// Run a pager with the text on its stdin and wait for the reader to quit it
        Page: PagerCommand -> string -> PagerOutcome
    }

/// Detect the environment and print help: the I/O edge. Every decision lives in the pure
/// <c>HelpEnvironment</c> module and in the functions here that take a <c>HelpConsole</c>;
/// only <c>system</c> touches the terminal or starts a process.
module HelpPrinter =
    /// Read the environment through a console. The window size is asked only of a terminal;
    /// one that cannot tell (it throws) is treated as reporting no size.
    let detectWith (console: HelpConsole) : HelpEnvironment =
        let redirected = console.IsOutputRedirected()

        let width, height =
            if redirected then
                0, 0
            else
                // Any failure means the size is unknown, which parse maps to 80x24.
                try
                    console.WindowSize()
                with _ ->
                    0, 0

        HelpEnvironment.parse redirected width height console.Variable

    /// Print a plan through a console: straight to stdout, or through its pager, falling
    /// back to stdout when the pager cannot start.
    let deliverWith (console: HelpConsole) (plan: HelpPlan) : unit =
        match plan.Delivery with
        | Direct -> console.WriteLine plan.Rendered
        | Paged pager ->
            match console.Page pager plan.Rendered with
            | PagerShown -> ()
            | PagerUnavailable -> console.WriteLine plan.Rendered

    /// Start the pager, write the text to its stdin and wait for it. Any failure to start
    /// is <c>PagerUnavailable</c>; a failure to write means the reader quit early.
    let private runPager (pager: PagerCommand) (text: string) : PagerOutcome =
        let psi =
            Diagnostics.ProcessStartInfo(pager.Program, UseShellExecute = false, RedirectStandardInput = true)

        pager.Arguments |> List.iter psi.ArgumentList.Add

        pager.Environment
        |> List.iter (fun (name, value) -> psi.Environment[name] <- value)

        Console.Out.Flush()
        let proc = new Diagnostics.Process(StartInfo = psi)

        try
            let started =
                try
                    proc.Start()
                with _ ->
                    false

            if started then
                try
                    proc.StandardInput.Write(text + "\n")
                    proc.StandardInput.Close()
                with _ ->
                    // The reader quit the pager before it read everything.
                    ()

                proc.WaitForExit()
                PagerShown
            else
                PagerUnavailable
        finally
            proc.Dispose()

    /// The real console: this process's stdout, terminal and environment.
    let system: HelpConsole =
        {
            IsOutputRedirected = fun () -> Console.IsOutputRedirected
            WindowSize = fun () -> Console.WindowWidth, Console.WindowHeight
            Variable = fun name -> Environment.GetEnvironmentVariable name |> Option.ofObj
            WriteLine = fun text -> Console.Out.WriteLine text
            Page = runPager
        }

    /// Read stdout's terminal state, its size, NO_COLOR, PAGER and LESS.
    let detect () : HelpEnvironment = detectWith system

    /// Print a plan to this process's stdout or through its pager.
    let deliver (plan: HelpPlan) : unit = deliverWith system plan

    /// Print a help document for the given environment.
    let printWith (environment: HelpEnvironment) (doc: HelpDoc) : unit =
        deliver (HelpEnvironment.plan environment doc)

    /// Print a help document for this process's stdout: styled and wrapped on a terminal
    /// (plain when NO_COLOR is set), paged when taller than the window, plain and unwrapped
    /// when redirected.
    let print (doc: HelpDoc) : unit = printWith (detect ()) doc
