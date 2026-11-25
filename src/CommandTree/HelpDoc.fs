namespace CommandTree

open System

/// The role a run of help text plays. Styled output gives each role its own look;
/// plain output drops the roles and keeps only the text.
[<RequireQualifiedAccess>]
type HelpStyle =
    /// Ordinary prose
    | Text
    /// A section heading such as <c>Usage:</c> or <c>Commands:</c>
    | Heading
    /// The name of a command group
    | Group
    /// The name of a runnable command, or a flag
    | Command
    /// A positional argument or placeholder such as <c>&lt;name&gt;</c> or <c>[options]</c>
    | Argument
    /// Secondary detail such as <c>(default)</c> or an environment variable binding
    | Secondary

/// A run of help text in one style.
type HelpSpan =
    {
        /// The text, without line breaks
        Text: string
        /// The role the text plays
        Style: HelpStyle
    }

/// One row of a two-column help table: a label, and a description kept in the table's
/// description column however it wraps.
type HelpRow =
    {
        /// Nesting level: 0 for a top-level row; each level indents two more columns
        Depth: int
        /// The label (command name, usage, flag)
        Label: HelpSpan list
        /// The description, one entry per source line; an empty entry is a blank line
        Description: HelpSpan list list
    }

/// A block of a help document. Blocks are laid out top to bottom.
[<RequireQualifiedAccess>]
type HelpBlock =
    /// One line of text, word-wrapped: the first line starts at <c>indent</c>, continuation
    /// lines at <c>hang</c>. Leading spaces in the text add to both.
    | Line of indent: int * hang: int * spans: HelpSpan list
    /// A table whose rows all share one description column. Sections are separated by a
    /// blank line.
    | Table of sections: HelpRow list list
    /// An empty line
    | Blank

/// A help document: what to say, with no decision yet about width or colour.
type HelpDoc = HelpBlock list

/// Whether rendered help carries terminal escape codes.
type ColorMode =
    /// Bold, colour and dim escape codes for each <c>HelpStyle</c>
    | Styled
    /// Text only
    | Plain

/// The number of columns help is laid out in. Never below <c>HelpWidth.minimum</c>, so
/// every line can hold at least a few characters past its indent.
type HelpWidth = private HelpWidth of int

/// Construct and read a <c>HelpWidth</c>.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HelpWidth =
    /// The narrowest width help is laid out in; a narrower request is raised to this.
    [<Literal>]
    let minimum = 20

    /// A width of <c>columns</c>, raised to <c>minimum</c> if it is narrower.
    let create (columns: int) : HelpWidth = HelpWidth(max minimum columns)

    /// No width limit: lines never wrap. What redirected output and the string help use.
    let unbounded: HelpWidth = HelpWidth Int32.MaxValue

    /// The number of columns.
    let columns (HelpWidth columns) : int = columns

/// Lay out and render a <c>HelpDoc</c>. Pure: no console access.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HelpDoc =
    /// A table's description column never starts further right than this, so a long label
    /// moves its description to the next line instead of pushing every row's out.
    [<Literal>]
    let maxDescriptionColumn = 32

    [<Literal>]
    let private gutter = 2

    [<Literal>]
    let private indentStep = 2

    let private prose (text: string) : HelpSpan = { Text = text; Style = HelpStyle.Text }

    let private spansLength (spans: HelpSpan list) =
        spans |> List.sumBy (fun span -> span.Text.Length)

    /// Split spans into words at spaces. A word is never empty and may cross spans, so
    /// <c>--wait</c> followed directly by <c>[=&lt;int&gt;]</c> stays one word.
    let private words (spans: HelpSpan list) : HelpSpan list list =
        let flush (finished: HelpSpan list list) (current: HelpSpan list) =
            if List.isEmpty current then
                finished
            else
                List.rev current :: finished

        let step (finished, current) (span: HelpSpan) =
            span.Text.Split(' ')
            |> Array.indexed
            |> Array.fold
                (fun (finished, current) (index, part) ->
                    let finished, current =
                        if index = 0 then
                            finished, current
                        else
                            flush finished current, []

                    if part = "" then
                        finished, current
                    else
                        finished, { span with Text = part } :: current)
                (finished, current)

        let finished, current = spans |> List.fold step ([], [])
        flush finished current |> List.rev

    let private joinWords (words: HelpSpan list list) : HelpSpan list =
        match words with
        | [] -> []
        | first :: rest -> first @ (rest |> List.collect (fun word -> prose " " :: word))

    /// Split a word after <c>count</c> characters.
    let private splitWord (count: int) (word: HelpSpan list) : HelpSpan list * HelpSpan list =
        let left, right, _ =
            word
            |> List.fold
                (fun (left, right, remaining) span ->
                    if span.Text.Length <= remaining then
                        span :: left, right, remaining - span.Text.Length
                    elif remaining > 0 then
                        { span with
                            Text = span.Text.Substring(0, remaining)
                        }
                        :: left,
                        { span with
                            Text = span.Text.Substring remaining
                        }
                        :: right,
                        0
                    else
                        left, span :: right, 0)
                ([], [], count)

        List.rev left, List.rev right

    /// Greedy word wrap. The first line holds <c>firstWidth</c> columns, the rest
    /// <c>restWidth</c> (both positive); a word longer than a whole line is broken. Returns the first line
    /// and the continuation lines.
    let private wrap (firstWidth: int) (restWidth: int) (spans: HelpSpan list) : HelpSpan list * HelpSpan list list =
        let rec go finished current currentLength available remaining =
            match remaining with
            | [] ->
                let last = joinWords (List.rev current)

                match List.rev finished with
                | [] -> last, []
                | first :: rest -> first, rest @ [ last ]
            | word :: rest ->
                let length = spansLength word

                let needed =
                    if List.isEmpty current then
                        length
                    else
                        currentLength + 1 + length

                if needed <= available then
                    go finished (word :: current) needed available rest
                elif List.isEmpty current then
                    let head, tail = splitWord available word
                    go (head :: finished) [] 0 restWidth (tail :: rest)
                else
                    go (joinWords (List.rev current) :: finished) [] 0 restWidth remaining

        go [] [] 0 firstWidth (words spans)

    /// A laid-out line: its indent and its content, before the indent becomes spaces.
    type private Placed = { Indent: int; Content: HelpSpan list }

    let private blankLine = { Indent = 0; Content = [] }

    let private leadingSpaces (spans: HelpSpan list) =
        match spans with
        | first :: _ -> first.Text.Length - first.Text.TrimStart(' ').Length
        | [] -> 0

    /// Wrap one source line to <c>width</c>, starting at <c>indent</c> and hanging
    /// continuation lines at <c>hang</c>. The line's own leading spaces add to both.
    let private placeLine (width: int) (indent: int) (hang: int) (spans: HelpSpan list) : Placed list =
        let lead = leadingSpaces spans
        // Indents stop at half the width, so every line keeps room for text and none is
        // longer than the width, however deep the nesting.
        let cap = width / 2
        let indent, hang = min cap (indent + lead), min cap (hang + lead)
        let first, rest = wrap (width - indent) (width - hang) spans

        { Indent = indent; Content = first }
        :: (rest |> List.map (fun line -> { Indent = hang; Content = line }))

    let private rowIndent (row: HelpRow) = indentStep * (row.Depth + 1)

    let private labelEnd (row: HelpRow) =
        rowIndent row + spansLength (joinWords (words row.Label))

    let private descriptionColumn (width: int) (rows: HelpRow list) =
        let widest = rows |> List.fold (fun widest row -> max widest (labelEnd row)) 0
        min (widest + gutter) (min maxDescriptionColumn (width / 3))

    let private placeRow (width: int) (column: int) (row: HelpRow) : Placed list =
        let indent = rowIndent row
        let label = joinWords (words row.Label)

        let description =
            row.Description
            |> List.collect (fun line ->
                match words line with
                | [] -> [ blankLine ]
                | _ -> placeLine width column column line)

        match description with
        | first :: rest when labelEnd row + gutter <= column && not (List.isEmpty first.Content) ->
            let padding = prose (String.replicate (first.Indent - labelEnd row) " ")

            {
                Indent = indent
                Content = label @ padding :: first.Content
            }
            :: rest
        | [ only ] when List.isEmpty only.Content -> placeLine width indent (indent + 2 * indentStep) label
        | _ -> placeLine width indent (indent + 2 * indentStep) label @ description

    let private placeBlock (width: int) (block: HelpBlock) : Placed list =
        match block with
        | HelpBlock.Line(indent, hang, spans) -> placeLine width indent hang spans
        | HelpBlock.Blank -> [ blankLine ]
        | HelpBlock.Table sections ->
            let sections = sections |> List.filter (List.isEmpty >> not)
            let column = descriptionColumn width (List.concat sections)

            sections
            |> List.mapi (fun index section ->
                let placed = section |> List.collect (placeRow width column)
                if index = 0 then placed else blankLine :: placed)
            |> List.concat

    /// Lay a document out in <c>width</c> columns: no line is longer than the width
    /// (measured in characters). Each line is a list of spans, indentation included.
    let layout (width: HelpWidth) (doc: HelpDoc) : HelpSpan list list =
        let columns = HelpWidth.columns width

        doc
        |> List.collect (placeBlock columns)
        |> List.map (fun placed ->
            if List.isEmpty placed.Content || placed.Indent = 0 then
                placed.Content
            else
                prose (String.replicate placed.Indent " ") :: placed.Content)

    let private escapeCode (style: HelpStyle) : string option =
        match style with
        | HelpStyle.Text -> None
        | HelpStyle.Heading
        | HelpStyle.Command -> Some "\x1b[1m"
        | HelpStyle.Group -> Some "\x1b[1;36m"
        | HelpStyle.Argument -> Some "\x1b[32m"
        | HelpStyle.Secondary -> Some "\x1b[2m"

    /// Merge adjacent spans of one style and drop empty ones.
    let private coalesce (line: HelpSpan list) : HelpSpan list =
        line
        |> List.filter (fun span -> span.Text <> "")
        |> List.fold
            (fun merged span ->
                match merged with
                | previous :: earlier when previous.Style = span.Style ->
                    { previous with
                        Text = previous.Text + span.Text
                    }
                    :: earlier
                | _ -> span :: merged)
            []
        |> List.rev

    let private renderSpan (mode: ColorMode) (span: HelpSpan) =
        match mode, escapeCode span.Style with
        | Styled, Some code -> $"%s{code}%s{span.Text}\x1b[0m"
        | Styled, None
        | Plain, _ -> span.Text

    /// Render laid-out lines as text, with escape codes in <c>Styled</c> mode.
    let render (mode: ColorMode) (lines: HelpSpan list list) : string =
        lines
        |> List.map (coalesce >> List.map (renderSpan mode) >> String.concat "")
        |> String.concat "\n"

    /// Lay out and render a document.
    let toText (width: HelpWidth) (mode: ColorMode) (doc: HelpDoc) : string = render mode (layout width doc)

    /// Plain text with no width limit: what the string help functions return.
    let plain (doc: HelpDoc) : string = toText HelpWidth.unbounded Plain doc

    /// The first sentence of a description's first paragraph, for a one-line summary.
    /// The paragraph's lines are joined with spaces; a sentence ends at <c>.</c>, <c>!</c>
    /// or <c>?</c> followed by the end of the text, or by whitespace and then anything
    /// but a lowercase letter (so <c>e.g. foo</c> does not end one).
    let firstSentence (description: string) : string =
        let paragraph =
            description.Split([| "\r\n"; "\n" |], StringSplitOptions.None)
            |> Array.map (fun line -> line.Trim())
            |> Array.skipWhile ((=) "")
            |> Array.takeWhile ((<>) "")
            |> String.concat " "

        let endsSentence index =
            let isTerminator = ".!?".Contains paragraph.[index]
            let atEnd = index + 1 = paragraph.Length

            // The paragraph neither ends in whitespace nor holds a line break, so whitespace
            // after a terminator is always followed by another character.
            let beforeNewSentence () =
                Char.IsWhiteSpace paragraph.[index + 1]
                && not (Char.IsLower paragraph.[index + 2])

            isTerminator && (atEnd || beforeNewSentence ())

        match Seq.tryFind endsSentence (seq { 0 .. paragraph.Length - 1 }) with
        | Some index -> paragraph.Substring(0, index + 1)
        | None -> paragraph
