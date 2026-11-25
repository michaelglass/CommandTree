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

/// Why help is never paged in this environment.
type PagingOffReason =
    /// Stdin is not a terminal, so nobody may be there to press a key: a pager would wait forever
    | InputRedirected
    /// PAGER is set but blank, or names <c>cat</c>
    | PagerDisabled
    /// TERM is <c>dumb</c>
    | DumbTerminal
    /// NO_PAGER is set to a non-empty value
    | NoPagerSet

/// Whether help taller than the terminal may go through a pager, and which one.
type Paging =
    /// Never page; print directly
    | PagingOff of reason: PagingOffReason
    /// Page through this command when the help does not fit
    | PagingOn of PagerCommand

/// What the help printer knows about its surroundings, parsed once at the edge
/// (see <c>HelpEnvironment.parse</c> and <c>HelpPrinter.detect</c>).
type HelpEnvironment =
    {
        /// Where stdout goes
        Target: HelpTarget
        /// Whether NO_COLOR is set to a non-empty value
        NoColor: bool
        /// Whether, and through what, help taller than the terminal is paged
        Paging: Paging
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
    /// The pager used when PAGER is unset: <c>less -R</c>, which passes colour through.
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

    /// PAGER as a command: unset means <c>less -R</c>; blank or <c>cat</c> (with any
    /// arguments) means no pager at all.
    let private parsePager (pager: string option) (less: string option) : Paging =
        let words (value: string) =
            value.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            |> List.ofArray

        let environment = pagerVariables less

        match pager |> Option.map words with
        | None ->
            PagingOn
                { defaultPager with
                    Environment = environment
                }
        | Some []
        | Some("cat" :: _) -> PagingOff PagerDisabled
        | Some(program :: arguments) ->
            PagingOn
                {
                    Program = program
                    Arguments = arguments
                    Environment = environment
                }

    let private isSet (value: string option) =
        value |> Option.exists (fun v -> v <> "")

    let private parsePaging (isInputRedirected: bool) (variable: string -> string option) : Paging =
        if isInputRedirected then
            PagingOff InputRedirected
        elif isSet (variable "NO_PAGER") then
            PagingOff NoPagerSet
        elif variable "TERM" = Some "dumb" then
            PagingOff DumbTerminal
        else
            parsePager (variable "PAGER") (variable "LESS")

    /// Parse raw facts into a <c>HelpEnvironment</c>. <c>isOutputRedirected</c> and
    /// <c>isInputRedirected</c> say whether stdout and stdin are pipes or files rather than
    /// a terminal. <c>width</c> and <c>height</c> are the terminal's reported size, ignored
    /// when stdout is redirected; a terminal that reports no size (0) is taken to be 80x24.
    /// <c>variable</c> looks up an environment variable: NO_COLOR, NO_PAGER, TERM, PAGER
    /// (split at whitespace, no shell quoting) and LESS are read. Paging is off when stdin
    /// is redirected, NO_PAGER is set, TERM is <c>dumb</c>, or PAGER is blank or <c>cat</c>.
    let parse
        (isOutputRedirected: bool)
        (isInputRedirected: bool)
        (width: int)
        (height: int)
        (variable: string -> string option)
        : HelpEnvironment =
        let orDefault fallback reported =
            if reported > 0 then reported else fallback

        {
            Target =
                if isOutputRedirected then
                    Redirected
                else
                    Terminal
                        {
                            Width = orDefault 80 width
                            Height = orDefault 24 height
                        }
            NoColor = isSet (variable "NO_COLOR")
            Paging = parsePaging isInputRedirected variable
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

    /// Page help of <c>lineCount</c> lines only when paging is on and on a terminal it does
    /// not fit: one with no more rows than lines, since the shell prompt that follows needs
    /// a row too.
    let delivery (environment: HelpEnvironment) (lineCount: int) : HelpDelivery =
        match environment.Target, environment.Paging with
        | Terminal size, PagingOn pager when lineCount >= size.Height -> Paged pager
        | Terminal _, PagingOn _
        | Terminal _, PagingOff _
        | Redirected, _ -> Direct

    /// Lay out and render a document for this environment and decide how to deliver it.
    let plan (environment: HelpEnvironment) (doc: HelpDoc) : HelpPlan =
        let lines = HelpDoc.layout (width environment) doc

        {
            Rendered = HelpDoc.render (colorMode environment) lines
            Delivery = delivery environment lines.Length
        }
