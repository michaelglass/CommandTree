namespace CommandTree

open System

/// The size of an interactive terminal, in character cells.
type TerminalSize =
    {
        /// Columns
        Width: int
        /// Rows
        Height: int
    }

/// Where help output goes.
type HelpTarget =
    /// An interactive terminal of this size
    | Terminal of TerminalSize
    /// A pipe or a file
    | Redirected

/// A pager program, its arguments, and variables to set for it. It runs with the help
/// text on its stdin.
type PagerCommand =
    {
        /// The program, looked up on PATH
        Program: string
        /// Its arguments
        Arguments: string list
        /// Environment variables set for the pager only
        Environment: (string * string) list
    }

/// What the help printer knows about its surroundings, parsed once at the edge
/// (see <c>HelpEnvironment.parse</c> and <c>HelpPrinter.detect</c>).
type HelpEnvironment =
    {
        /// Where stdout goes
        Target: HelpTarget
        /// Whether NO_COLOR is set to a non-empty value
        NoColor: bool
        /// The pager for help taller than the terminal
        Pager: PagerCommand
    }

/// How rendered help reaches the reader.
type HelpDelivery =
    /// Written straight to stdout
    | Direct
    /// Piped through this pager
    | Paged of PagerCommand

/// Rendered help and how to deliver it: everything decided, nothing yet printed.
type HelpPlan =
    {
        /// The rendered text, without a trailing newline
        Rendered: string
        /// Stdout or a pager
        Delivery: HelpDelivery
    }

/// Parse the environment and decide colour, width and paging. Pure.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HelpEnvironment =
    /// The pager used when PAGER is unset or blank: <c>less -R</c>, which passes colour through.
    let defaultPager =
        {
            Program = "less"
            Arguments = [ "-R" ]
            Environment = []
        }

    /// With LESS unset, the pager gets <c>LESS=FRX</c>, as git does: less passes colour
    /// through and quits by itself when the text fits after all. A LESS the user set,
    /// even empty, is left alone.
    let private pagerVariables (less: string option) : (string * string) list =
        match less with
        | None -> [ "LESS", "FRX" ]
        | Some _ -> []

    let private parsePager (pager: string option) (less: string option) : PagerCommand =
        let parts =
            pager
            |> Option.map (fun v -> v.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray)

        let command =
            match parts with
            | Some(program :: arguments) ->
                {
                    Program = program
                    Arguments = arguments
                    Environment = []
                }
            | Some []
            | None -> defaultPager

        { command with
            Environment = pagerVariables less
        }

    /// Parse raw facts into a <c>HelpEnvironment</c>. <c>width</c> and <c>height</c> are the
    /// terminal's reported size, ignored when stdout is redirected; a terminal that reports
    /// no size (0) is taken to be 80x24. <c>variable</c> looks up an environment variable:
    /// NO_COLOR, PAGER (split at whitespace, no shell quoting) and LESS are read.
    let parse (isRedirected: bool) (width: int) (height: int) (variable: string -> string option) : HelpEnvironment =
        let orDefault fallback reported =
            if reported > 0 then reported else fallback

        {
            Target =
                if isRedirected then
                    Redirected
                else
                    Terminal
                        {
                            Width = orDefault 80 width
                            Height = orDefault 24 height
                        }
            NoColor = variable "NO_COLOR" |> Option.exists (fun value -> value <> "")
            Pager = parsePager (variable "PAGER") (variable "LESS")
        }

    /// Styled on a terminal unless NO_COLOR is set; plain otherwise.
    let colorMode (environment: HelpEnvironment) : ColorMode =
        match environment.Target with
        | Terminal _ when not environment.NoColor -> Styled
        | Terminal _
        | Redirected -> Plain

    /// A terminal's width; no limit for redirected output, so each row stays one line for grep.
    let width (environment: HelpEnvironment) : HelpWidth =
        match environment.Target with
        | Terminal size -> HelpWidth.create size.Width
        | Redirected -> HelpWidth.unbounded

    /// Page help of <c>lineCount</c> lines only on a terminal it does not fit: one with no
    /// more rows than lines, since the shell prompt that follows needs a row too.
    let delivery (environment: HelpEnvironment) (lineCount: int) : HelpDelivery =
        match environment.Target with
        | Terminal size when lineCount >= size.Height -> Paged environment.Pager
        | Terminal _
        | Redirected -> Direct

    /// Lay out and render a document for this environment and decide how to deliver it.
    let plan (environment: HelpEnvironment) (doc: HelpDoc) : HelpPlan =
        let lines = HelpDoc.layout (width environment) doc

        {
            Rendered = HelpDoc.render (colorMode environment) lines
            Delivery = delivery environment lines.Length
        }
