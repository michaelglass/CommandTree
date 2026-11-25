namespace CommandTree

open System
open System.Reflection

/// Completion hint for generating shell completions for arguments
type ArgCompletionHint =
    | NoCompletion
    | Values of string list
    | FilePath

/// Argument metadata for help generation
type ArgInfo =
    {
        /// Positional argument name (used in usage synopsis, e.g. <config>)
        Name: string
        /// Display type name for help text (e.g. "string", "int", "string list")
        TypeName: string
        /// Whether the argument may be omitted (an option or ordinary list field)
        IsOptional: bool
        /// Whether the argument accepts multiple values (list field)
        IsList: bool
        /// Shell completion hint for this argument
        Completions: ArgCompletionHint
        /// Human-readable description from [<CmdArg("desc")>], shown in Arguments section
        Description: string option
        /// Default value hint from [<CmdArg(Default = "value")>], shown in Arguments section
        Default: string option
    }

/// Environment variable binding for a flag
type EnvVarInfo =
    {
        /// Full resolved environment variable name
        VarName: string
    }

/// A flag's value arity — whether it takes a value, and if so how.
/// - <c>Nullary</c>: a boolean toggle with no value (e.g. <c>--verbose</c>).
/// - <c>Required</c>: takes a value, supplied either space-separated
///   (<c>--conf file</c>) or inline (<c>--conf=file</c>).
/// - <c>Optional</c>: value is optional and <em>inline-only</em> — bare
///   <c>--wait</c> binds <c>None</c>, <c>--wait=5</c> binds <c>Some 5</c>. An
///   optional-value flag never consumes the following token, so
///   <c>--wait --detach</c> is <c>Wait None</c> plus <c>Detach</c>. Modeled by a
///   single <c>'T option</c> flag-DU field.
type FlagArity =
    | Nullary
    | Required
    | Optional

/// Flag metadata for help generation and parsing
type FlagInfo =
    {
        /// Long flag name (without -- prefix)
        LongName: string
        /// Short flag character (without - prefix), if any
        ShortName: string option
        /// Display type name for help text
        TypeName: string
        /// Value arity: nullary toggle, required value, or optional inline-only value
        Arity: FlagArity
        /// Whether the flag may occur more than once
        IsRepeatable: bool
        /// Human-readable description for help text
        Description: string
        /// Environment variable binding, if configured
        EnvVar: EnvVarInfo option
    }

/// Structured error from command parsing
type ParseError =
    /// User requested help (e.g., no args given). Path is the group where help was requested.
    | HelpRequested of path: string list
    /// User requested version info (--version or version at root level).
    | VersionRequested
    /// Command name not recognized. Carries the unrecognized token, the raw remaining args
    /// after it (everything past the unknown token, ready to forward as-is), and the group
    /// path for context. A consumer that resolves some commands dynamically (e.g. forwards
    /// them to a daemon) uses <c>input</c>+<c>rest</c> directly; otherwise render the
    /// canonical error via <c>renderParseError</c> and exit non-zero (see <c>isError</c>).
    | UnknownCommand of input: string * rest: string array * groupPath: string list
    /// Arguments couldn't be parsed for a known command.
    | InvalidArguments of command: string * message: string
    /// A positional token could not be parsed. Carries the argument name and any finite accepted values.
    | BadPositionalValue of token: string * argument: string * acceptedValues: string list
    /// Argument value matched multiple union cases.
    | AmbiguousArgument of input: string * candidates: string list
    /// Flag not recognized for this command. Includes valid flags for suggestions.
    | UnknownFlag of flag: string * command: string * validFlags: string list
    /// Same flag provided more than once.
    | DuplicateFlag of flag: string * command: string

/// Structured error from field-level value parsing
type ParseFieldError =
    /// Input matched multiple union case names
    | AmbiguousValue of input: string * candidates: string list
    /// Input could not be parsed as the expected type
    | InvalidValue of message: string

/// Data for a leaf command node
[<NoComparison; NoEquality>]
type LeafData<'Cmd> =
    {
        /// Command name (kebab-case)
        Name: string
        /// Human-readable description
        Description: string
        /// Positional argument metadata
        Args: ArgInfo list
        /// Flag metadata for named options
        Flags: FlagInfo list
        /// Example invocations for help display (each is the args portion, prefix prepended automatically)
        Examples: string list
        /// Parse CLI args into a command value
        Parse: string array -> Result<'Cmd, ParseError>
        /// Format a command value back to CLI arg tokens
        FormatArgs: 'Cmd -> string list option
    }

/// Data for a group (subcommand container) node
and [<NoComparison; NoEquality>] GroupData<'Cmd> =
    {
        /// Group name (empty string for root)
        Name: string
        /// Human-readable description
        Description: string
        /// Child command nodes
        Children: CommandTree<'Cmd> list
        /// Name of the child that empty args run, if any. The child parses the empty
        /// args itself, exactly as when named.
        Default: string option
    }

/// Recursive command tree for declarative parsing and help generation
and [<NoComparison; NoEquality>] CommandTree<'Cmd> =
    /// Leaf command with parse and format functions
    | Leaf of LeafData<'Cmd>
    /// Group: contains subcommands
    | Group of GroupData<'Cmd>

module CommandTree =
    /// Get the name of a command tree node
    let name =
        function
        | Leaf leaf -> leaf.Name
        | Group group -> group.Name

    /// Get the description of a command tree node
    let desc =
        function
        | Leaf leaf -> leaf.Description
        | Group group -> group.Description

    /// Get argument info for a leaf node
    let args =
        function
        | Leaf leaf -> leaf.Args
        | Group _ -> []

    /// Check if args contain --help (before any `--` end-of-flags separator,
    /// after which tokens are positional values, never flags)
    let private hasHelpFlag (args: string array) =
        args |> Array.takeWhile (fun a -> a <> "--") |> Array.contains "--help"

    /// Check if args contain --version (before any `--` end-of-flags separator)
    let private hasVersionFlag (args: string array) =
        args |> Array.takeWhile (fun a -> a <> "--") |> Array.contains "--version"

    /// Check if a flag list contains an explicit --help flag (override)
    let private hasExplicitHelpFlag (flags: FlagInfo list) =
        flags |> List.exists (fun fi -> fi.LongName = "help")

    /// Parse args using tree structure (recursive)
    let parse (tree: CommandTree<'Cmd>) (args: string array) : Result<'Cmd, ParseError> =
        let rec parseRec (node: CommandTree<'Cmd>) (args: string array) (path: string list) =
            match node, args with
            | Leaf leaf, _ ->
                let currentPath = path @ [ leaf.Name ]

                if hasHelpFlag args && not (hasExplicitHelpFlag leaf.Flags) then
                    Error(HelpRequested currentPath)
                else
                    leaf.Parse args

            | Group group, [||] ->
                let currentPath = if group.Name = "" then path else path @ [ group.Name ]

                match
                    group.Default
                    |> Option.bind (fun d -> group.Children |> List.tryFind (fun c -> name c = d))
                with
                | Some child -> parseRec child [||] currentPath
                | None -> Error(HelpRequested currentPath)

            // Routing into a child wins over --help, so `cmd sub --help` shows the
            // child's help rather than this group's.
            | Group group, _ ->
                let currentPath = if group.Name = "" then path else path @ [ group.Name ]
                let subCmd = args.[0]
                let rest = args |> Array.skip 1

                match group.Children |> List.tryFind (fun c -> name c = subCmd) with
                | Some child -> parseRec child rest currentPath
                | None ->
                    if hasHelpFlag args then
                        Error(HelpRequested currentPath)
                    elif List.isEmpty currentPath && (subCmd = "version" || hasVersionFlag args) then
                        Error VersionRequested
                    else
                        Error(UnknownCommand(subCmd, rest, currentPath))

        parseRec tree args []

    /// Format a command by walking the tree to find matching leaf (internal recursive)
    let rec private formatRec
        (tree: CommandTree<'Cmd>)
        (cmd: 'Cmd)
        (path: string list)
        (cmdPrefix: string)
        : string option =
        match tree with
        | Leaf leaf ->
            match leaf.FormatArgs cmd with
            | Some args ->
                let parts = path @ [ leaf.Name ] @ args |> List.filter (fun s -> s <> "")
                Some(cmdPrefix + " " + String.concat " " parts)
            | None -> None

        | Group group ->
            let newPath = if group.Name = "" then path else path @ [ group.Name ]

            group.Children
            |> List.tryPick (fun child -> formatRec child cmd newPath cmdPrefix)

    /// Format a command value to its full CLI string (e.g., "mycli build env edit staging")
    let format (tree: CommandTree<'Cmd>) (cmd: 'Cmd) (cmdPrefix: string) : string option =
        formatRec tree cmd [] cmdPrefix

    /// Format argument info for display
    let private formatArg (arg: ArgInfo) =
        if arg.IsList then $"[<%s{arg.Name}...>]"
        elif arg.IsOptional then $"[%s{arg.Name}]"
        else $"<%s{arg.Name}>"

    /// Split a description into its lines, accepting either line ending.
    let private descriptionLines (description: string) : string list =
        description.Split([| "\r\n"; "\n" |], StringSplitOptions.None) |> List.ofArray

    /// The first line of a description: the one-line summary a completion menu shows.
    let private summaryLine (description: string) : string =
        List.head (descriptionLines description)

    /// Mark a flag description as repeatable when the flag may occur more than once.
    let private withRepeatable (fi: FlagInfo) (description: string) =
        if fi.IsRepeatable then
            $"%s{description} (repeatable)"
        else
            description

    let private span (style: HelpStyle) (text: string) : HelpSpan = { Text = text; Style = style }

    let private heading (title: string) : HelpBlock =
        HelpBlock.Line(0, 0, [ span HelpStyle.Heading $"%s{title}:" ])

    /// Continuation lines of a usage line hang under the text after "Usage: ".
    [<Literal>]
    let private usageHang = 7

    let private usageLine (spans: HelpSpan list) : HelpBlock =
        HelpBlock.Line(0, usageHang, span HelpStyle.Heading "Usage:" :: spans)

    /// A description as span lines, with <c>suffix</c> appended to its last line.
    let private describe (description: string) (suffix: HelpSpan list) : HelpSpan list list =
        let lines = descriptionLines description

        lines
        |> List.mapi (fun i line ->
            let spans = [ span HelpStyle.Text line ]
            if i = lines.Length - 1 then spans @ suffix else spans)

    /// The <c>(default)</c> marker on the first line of the default child's description.
    let private markDefault (group: GroupData<'Cmd>) (child: CommandTree<'Cmd>) (lines: HelpSpan list list) =
        if group.Default = Some(name child) then
            lines
            |> List.mapi (fun i line ->
                if i = 0 then
                    line @ [ span HelpStyle.Secondary " (default)" ]
                else
                    line)
        else
            lines

    let private argSpans (argList: ArgInfo list) : HelpSpan list =
        argList |> List.map (fun a -> span HelpStyle.Argument $" %s{formatArg a}")

    let private optionsSpans (flags: FlagInfo list) : HelpSpan list =
        if flags.IsEmpty then
            []
        else
            [ span HelpStyle.Argument " [options]" ]

    /// A node's name, styled as a group or a command.
    let private nameSpan (node: CommandTree<'Cmd>) : HelpSpan =
        match node with
        | Leaf leaf -> span HelpStyle.Command leaf.Name
        | Group group -> span HelpStyle.Group group.Name

    let private flagRow (fi: FlagInfo) : HelpRow =
        let shortPart =
            match fi.ShortName with
            | Some s -> [ span HelpStyle.Text ", "; span HelpStyle.Command $"-%s{s}" ]
            | None -> []

        let typePart =
            match fi.Arity with
            | Nullary -> []
            | Required -> [ span HelpStyle.Argument $" <%s{fi.TypeName}>" ]
            | Optional -> [ span HelpStyle.Argument $"[=<%s{fi.TypeName}>]" ]

        let repeatable =
            if fi.IsRepeatable then
                [ span HelpStyle.Secondary " (repeatable)" ]
            else
                []

        let envPart =
            match fi.EnvVar with
            | Some { VarName = v } -> [ span HelpStyle.Secondary $" (env: %s{v})" ]
            | None -> []

        {
            Depth = 0
            Label = (span HelpStyle.Command $"--%s{fi.LongName}" :: shortPart) @ typePart
            Description = describe fi.Description (repeatable @ envPart)
        }

    let private argRow (arg: ArgInfo) (description: string) : HelpRow =
        let defaultPart =
            match arg.Default with
            | Some d -> [ span HelpStyle.Secondary $" (default: %s{d})" ]
            | None -> []

        {
            Depth = 0
            Label = [ span HelpStyle.Argument (formatArg arg) ]
            Description = describe description defaultPart
        }

    /// A group's children as table rows, each with its full description.
    let private childRows (group: GroupData<'Cmd>) : HelpRow list =
        group.Children
        |> List.map (fun child ->
            {
                Depth = 0
                Label = nameSpan child :: argSpans (args child)
                Description = describe (desc child) [] |> markDefault group child
            })

    /// A titled section: a blank line, the heading, then its blocks. Nothing when empty.
    let private section (title: string) (blocks: HelpBlock list) : HelpBlock list =
        if blocks.IsEmpty then
            []
        else
            HelpBlock.Blank :: heading title :: blocks

    let private table (rows: HelpRow list) : HelpBlock list =
        if rows.IsEmpty then [] else [ HelpBlock.Table [ rows ] ]

    let private descriptionBlocks (description: string) : HelpBlock list =
        descriptionLines description
        |> List.map (fun line -> HelpBlock.Line(0, 0, [ span HelpStyle.Text line ]))

    /// Help for a tree node (single level) as a document. <c>path</c> is the parent path.
    let helpDoc (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : HelpDoc =
        let pathStr =
            if path.IsEmpty then
                cmdPrefix
            else
                cmdPrefix + " " + String.concat " " path

        match tree with
        | Leaf leaf ->
            let usage =
                usageLine (
                    [ span HelpStyle.Text $" %s{pathStr} "; span HelpStyle.Command leaf.Name ]
                    @ argSpans leaf.Args
                    @ optionsSpans leaf.Flags
                )

            let argumentRows =
                leaf.Args |> List.choose (fun a -> a.Description |> Option.map (argRow a))

            let examples =
                leaf.Examples
                |> List.map (fun e -> HelpBlock.Line(2, 4, [ span HelpStyle.Text $"%s{pathStr} %s{leaf.Name} %s{e}" ]))

            [ usage; HelpBlock.Blank ]
            @ descriptionBlocks leaf.Description
            @ section "Arguments" (table argumentRows)
            @ section "Options" (table (leaf.Flags |> List.map flagRow))
            @ section "Examples" examples

        | Group group ->
            let groupName =
                if group.Name = "" then
                    []
                else
                    [ span HelpStyle.Text " "; span HelpStyle.Group group.Name ]

            [
                usageLine (
                    [ span HelpStyle.Text $" %s{pathStr}" ]
                    @ groupName
                    @ [ span HelpStyle.Argument " <command>" ]
                )
                HelpBlock.Blank
            ]
            @ descriptionBlocks group.Description
            @ [ HelpBlock.Blank; heading "Commands"; HelpBlock.Table [ childRows group ] ]

    /// Generate help for a tree node (single level), as plain text
    let help (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : string =
        HelpDoc.plain (helpDoc tree path cmdPrefix)

    /// The overview rows for a node and, for a group, everything under it. Each
    /// description is its first sentence: the full text belongs to that command's help.
    let rec private overviewRows (depth: int) (parent: GroupData<'Cmd> option) (node: CommandTree<'Cmd>) =
        let sentence = [ [ span HelpStyle.Text (HelpDoc.firstSentence (desc node)) ] ]

        let summary =
            match parent with
            | Some group -> markDefault group node sentence
            | None -> sentence

        match node with
        | Leaf leaf ->
            [
                {
                    Depth = depth
                    Label = span HelpStyle.Command leaf.Name :: argSpans leaf.Args @ optionsSpans leaf.Flags
                    Description = summary
                }
            ]
        | Group group ->
            {
                Depth = depth
                Label = [ span HelpStyle.Group group.Name ]
                Description = summary
            }
            :: (group.Children |> List.collect (overviewRows (depth + 1) (Some group)))

    /// The full overview as a document: every command at every depth, one row each, with
    /// its first sentence. Each top-level group is its own section; runs of top-level
    /// commands between groups share one.
    let helpFullDoc (tree: CommandTree<'Cmd>) (cmdPrefix: string) : HelpDoc =
        let topLevel, parent =
            match tree with
            | Group group when group.Name = "" -> group.Children, Some group
            | node -> [ node ], None

        let sections, run =
            topLevel
            |> List.fold
                (fun (sections, run) node ->
                    let rows = overviewRows 0 parent node

                    match node with
                    | Leaf _ -> sections, run @ rows
                    | Group _ -> sections @ [ run; rows ], [])
                ([], [])

        [
            usageLine [ span HelpStyle.Text $" %s{cmdPrefix}"; span HelpStyle.Argument " <command>" ]
            HelpBlock.Blank
            heading "Commands"
            HelpBlock.Table(sections @ [ run ])
        ]

    /// Generate full help with all subtrees expanded, as plain text
    let helpFull (tree: CommandTree<'Cmd>) (cmdPrefix: string) : string =
        HelpDoc.plain (helpFullDoc tree cmdPrefix)

    /// Find a subtree by path (e.g., ["env"] or ["infra"; "app"])
    let rec findByPath (tree: CommandTree<'Cmd>) (path: string list) : CommandTree<'Cmd> option =
        match path with
        | [] -> Some tree
        | segment :: rest ->
            match tree with
            | Leaf _ -> None
            | Group group ->
                group.Children
                |> List.tryFind (fun c -> name c = segment)
                |> Option.bind (fun child -> findByPath child rest)

    /// Help for the subtree at the given path as a document; root help when the path
    /// does not resolve.
    let helpForPathDoc (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : HelpDoc =
        match findByPath tree path with
        // Pass parent path (all but last segment) since helpDoc adds the node's own name
        | Some subtree ->
            let parentPath =
                if path.Length > 0 then
                    path |> List.take (path.Length - 1)
                else
                    []

            helpDoc subtree parentPath cmdPrefix
        | None -> helpDoc tree [] cmdPrefix

    /// Generate help for a subtree at the given path, as plain text
    let helpForPath (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : string =
        HelpDoc.plain (helpForPathDoc tree path cmdPrefix)

    /// Root help with a global options section, as a document
    let helpWithGlobalsDoc (tree: CommandTree<'Cmd>) (globalFlags: FlagInfo list) (cmdPrefix: string) : HelpDoc =
        match tree with
        | Group group ->
            [
                usageLine
                    [
                        span HelpStyle.Text $" %s{cmdPrefix}"
                        span HelpStyle.Argument " [global options] <command>"
                    ]
                HelpBlock.Blank
            ]
            @ descriptionBlocks group.Description
            @ section "Global options" (table (globalFlags |> List.map flagRow))
            @ [ HelpBlock.Blank; heading "Commands"; HelpBlock.Table [ childRows group ] ]
        | Leaf _ -> helpDoc tree [] cmdPrefix

    /// Generate help for the root with global options section, as plain text
    let helpWithGlobals (tree: CommandTree<'Cmd>) (globalFlags: FlagInfo list) (cmdPrefix: string) : string =
        HelpDoc.plain (helpWithGlobalsDoc tree globalFlags cmdPrefix)

    /// Find the deepest valid group path from a list of args.
    /// Used to show help for the nearest matching group when an unknown command is typed.
    /// E.g., ["check", "logci"] → ["check"] (check exists as a group, logci doesn't)
    let closestGroupPath (tree: CommandTree<'Cmd>) (args: string list) : string list =
        let rec findDeepest (path: string list) (remaining: string list) =
            match remaining with
            | [] -> path
            | next :: rest ->
                match findByPath tree (path @ [ next ]) with
                | Some(Group _) -> findDeepest (path @ [ next ]) rest
                | _ -> path

        findDeepest [] args

    /// Case-insensitive Levenshtein edit distance, used to rank near-miss command names.
    /// Plain edit distance (no transposition rule): a swapped pair costs two edits, which the
    /// tolerance for longer tokens already allows, so the extra rule would buy nothing.
    let private editDistance (left: string) (right: string) : int =
        let left = left.ToLowerInvariant()
        let right = right.ToLowerInvariant()

        // Standard row-at-a-time dynamic program: row i holds the distances from left[0..i-1]
        // to every prefix of right, so only the previous row is ever needed.
        let nextRow (previous: int array) (i: int) =
            let current = Array.zeroCreate (right.Length + 1)
            current.[0] <- i

            for j in 1 .. right.Length do
                let substitute = previous.[j - 1] + (if left.[i - 1] = right.[j - 1] then 0 else 1)
                current.[j] <- min (min (previous.[j] + 1) (current.[j - 1] + 1)) substitute

            current

        let firstRow = Array.init (right.Length + 1) id
        let lastRow = List.fold nextRow firstRow [ 1 .. left.Length ]
        lastRow.[right.Length]

    /// How far a typed token may sit from a command name and still be worth offering.
    /// One- and two-character tokens get no slack at all — at that length an edit of one is
    /// most of the word, so only an exact name elsewhere in the tree is evidence of intent.
    let private suggestionTolerance (input: string) =
        if input.Length <= 2 then 0
        elif input.Length <= 5 then 1
        else 2

    /// Every command path in the tree, deepest-last, with group nodes included in their own
    /// right (a mistyped group name is as worth suggesting as a mistyped leaf). The unnamed
    /// root contributes no segment.
    let rec private allCommandPaths (node: CommandTree<'Cmd>) (prefix: string list) : string list list =
        match node with
        | Leaf leaf -> [ prefix @ [ leaf.Name ] ]
        | Group group ->
            let here = if group.Name = "" then prefix else prefix @ [ group.Name ]
            let self = if group.Name = "" then [] else [ here ]

            self
            @ (group.Children |> List.collect (fun child -> allCommandPaths child here))

    /// The single command path a refusal should offer for <paramref name="input" />, a token
    /// that did not match at <paramref name="groupPath" /> — the whole point being that the
    /// tree already knows where the verb the operator wanted actually lives. Returns
    /// <c>None</c> when nothing in the tree is close enough, so a refusal never invents a
    /// suggestion.
    ///
    /// Candidates are every command and group name in the tree whose edit distance from the
    /// token is within <c>suggestionTolerance</c>; the best one is chosen by, in order:
    /// closest spelling; then a sibling at the level where the token was typed, since that is
    /// the context the operator was already in; then the shortest path, being the least to
    /// retype; then alphabetically, so two otherwise-tied candidates resolve the same way on
    /// every run and never depend on declaration order.
    let suggestPath (tree: CommandTree<'Cmd>) (groupPath: string list) (input: string) : string list option =
        let tolerance = suggestionTolerance input
        let depth = List.length groupPath

        let isSibling (path: string list) =
            List.length path = depth + 1 && List.truncate depth path = groupPath

        allCommandPaths tree []
        |> List.choose (fun path ->
            let distance = editDistance input (List.last path)
            if distance <= tolerance then Some(path, distance) else None)
        |> List.sortBy (fun (path, distance) ->
            distance, (if isSibling path then 0 else 1), List.length path, String.concat " " path)
        |> List.tryHead
        |> Option.map fst

    /// Render a <c>ParseError</c> as the full user-facing stderr text: a clear one-line
    /// "invalid input" message followed by the help for the nearest relevant command or
    /// group. Pure — returns the string; the caller prints it and chooses the exit code
    /// (see <c>isError</c>).
    ///
    /// Per case:
    /// <c>UnknownFlag</c> → "Unknown flag …" + that command's help.
    /// <c>UnknownCommand</c> → "Unknown command …", plus a single "Did you mean …" naming the
    /// full path when <c>suggestPath</c> finds one, + the nearest group's help (its child listing).
    /// <c>InvalidArguments</c> → the message + that command's help.
    /// <c>BadPositionalValue</c> → a compact one-line token/argument diagnostic.
    /// <c>AmbiguousArgument</c> → "Ambiguous …" + nearest group's help.
    /// <c>DuplicateFlag</c> → a duplicate message + that command's help.
    /// <c>HelpRequested</c> → just the help for that path (not an error; no error line).
    /// <c>VersionRequested</c> → empty string: version output is the caller's concern,
    /// since CommandTree does not know the version. Detect it via <c>isError</c> (false)
    /// and print a version banner with <c>renderVersion</c> (or <c>entryAssemblyVersion</c>)
    /// instead of calling this.
    let renderParseError (tree: CommandTree<'Cmd>) (error: ParseError) (cmdPrefix: string) : string =
        let withHelp (line: string) (path: string list) =
            $"%s{line}\n\n%s{helpForPath tree path cmdPrefix}"

        match error with
        | HelpRequested path -> helpForPath tree path cmdPrefix
        | VersionRequested -> ""
        | UnknownFlag(flag, command, _) -> withHelp $"Unknown flag '%s{flag}' for '%s{command}'." [ command ]
        | UnknownCommand(input, _, groupPath) ->
            let suggestion =
                match suggestPath tree groupPath input with
                | Some path ->
                    let joined = String.concat " " path
                    $" Did you mean '%s{cmdPrefix} %s{joined}'?"
                | None -> ""

            withHelp $"Unknown command '%s{input}'.%s{suggestion}" (closestGroupPath tree groupPath)
        | InvalidArguments(command, message) -> withHelp message [ command ]
        | BadPositionalValue(token, argument, accepted) ->
            match accepted with
            | [] -> $"'%s{token}' is not a valid <%s{argument}>."
            | values ->
                let joined = String.concat ", " values
                $"'%s{token}' is not a valid <%s{argument}> — expected one of: %s{joined}."
        | AmbiguousArgument(input, candidates) ->
            // input is an argument *value* (an ambiguous union-case prefix), never a group
            // name, so there is no nearer group to scope help to — show root help.
            let joined = String.concat ", " candidates
            withHelp $"Ambiguous '%s{input}'. Did you mean: %s{joined}" []
        | DuplicateFlag(flag, command) ->
            withHelp $"Flag '%s{flag}' provided more than once for '%s{command}'." [ command ]

    /// Classify a <c>ParseError</c> for exit-code selection: <c>true</c> for genuine input
    /// errors (caller should print <c>renderParseError</c> and exit non-zero), <c>false</c>
    /// for <c>HelpRequested</c>/<c>VersionRequested</c> (informational; exit zero).
    let isError (error: ParseError) : bool =
        match error with
        | HelpRequested _
        | VersionRequested -> false
        | UnknownCommand _
        | InvalidArguments _
        | BadPositionalValue _
        | AmbiguousArgument _
        | UnknownFlag _
        | DuplicateFlag _ -> true

    /// Best-available version string for an assembly. Prefers the
    /// <c>AssemblyInformationalVersionAttribute.InformationalVersion</c> (which carries any
    /// <c>+&lt;commit&gt;</c> build metadata stamped at build time — kept intact, not stripped),
    /// falling back to the assembly identity version (<c>GetName().Version</c>) when the
    /// attribute is absent or its informational version is null/empty. Pure with respect to the
    /// passed assembly, so it is fully unit-testable by handing it any <c>Assembly</c>.
    let assemblyVersion (asm: Assembly) : string =
        let informational =
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            |> Option.ofObj
            |> Option.map (fun attr -> attr.InformationalVersion)

        match informational with
        | Some v when not (String.IsNullOrEmpty v) -> v
        | _ -> string (asm.GetName().Version)

    /// Version string of the process entry assembly (the consumer CLI). Resolves
    /// <c>Assembly.GetEntryAssembly()</c>, falling back to <c>Assembly.GetCallingAssembly()</c>
    /// when entry is null (e.g. under some test hosts), then returns its
    /// <c>assemblyVersion</c>. Entry-assembly resolution is host-dependent; the testable logic
    /// lives in <c>assemblyVersion</c>.
    let entryAssemblyVersion () : string =
        let asm =
            match Assembly.GetEntryAssembly() with
            | null -> Assembly.GetCallingAssembly()
            | entry -> entry

        assemblyVersion asm

    /// Render a version banner for a consumer CLI: <c>"&lt;cmdPrefix&gt; &lt;version&gt;"</c>, where the
    /// version is the entry assembly's <c>assemblyVersion</c>. This is the recommended default
    /// for the <c>VersionRequested</c> arm, e.g.
    /// <c>| Error VersionRequested -&gt; printfn "%s" (CommandTree.renderVersion "toolname"); 0</c>.
    let renderVersion (cmdPrefix: string) : string =
        sprintf "%s %s" cmdPrefix (entryAssemblyVersion ())

    /// Generate fish shell completions from the command tree. Each `-d` carries only the
    /// first line of its description: fish shows one line per candidate.
    let fishCompletions (tree: CommandTree<'Cmd>) (cmdName: string) : string =
        // Inside fish double quotes, backslash, double quote and dollar are special.
        let escape (s: string) =
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$")

        let rec generate (node: CommandTree<'Cmd>) (path: string list) : string list =
            match node with
            | Leaf leaf ->
                let leafPath = path @ [ leaf.Name ]

                let condition =
                    leafPath
                    |> List.map (sprintf "__fish_seen_subcommand_from %s")
                    |> String.concat "; and "

                let argCompletions =
                    leaf.Args
                    |> List.collect (fun arg ->
                        match arg.Completions with
                        | Values values ->
                            values
                            |> List.map (fun v ->
                                $"complete -c %s{cmdName} -n \"%s{condition}\" -a \"%s{escape v}\" -d \"%s{escape v}\"")
                        | FilePath -> [ $"complete -c %s{cmdName} -n \"%s{condition}\" -F" ]
                        | NoCompletion -> [])

                let flagCompletions =
                    leaf.Flags
                    |> List.collect (fun fi ->
                        let shortPart =
                            match fi.ShortName with
                            | Some s -> $" -s %s{s}"
                            | None -> ""

                        [
                            $"complete -c %s{cmdName} -n \"%s{condition}\" -l %s{fi.LongName}%s{shortPart} -d \"%s{escape (withRepeatable fi (summaryLine fi.Description))}\""
                        ])

                argCompletions @ flagCompletions
            | Group group ->
                let currentPath = if group.Name = "" then path else path @ [ group.Name ]

                let condition =
                    if currentPath.IsEmpty then
                        "__fish_use_subcommand"
                    else
                        let seen =
                            currentPath
                            |> List.map (sprintf "__fish_seen_subcommand_from %s")
                            |> String.concat "; and "

                        let childNames = group.Children |> List.map name |> String.concat " "
                        $"%s{seen}; and not __fish_seen_subcommand_from %s{childNames}"

                let childCompletions =
                    group.Children
                    |> List.map (fun child ->
                        let childName = name child
                        let childDesc = escape (summaryLine (desc child))
                        $"complete -c %s{cmdName} -n \"%s{condition}\" -a \"%s{childName}\" -d \"%s{childDesc}\"")

                let nestedCompletions =
                    group.Children |> List.collect (fun child -> generate child currentPath)

                childCompletions @ nestedCompletions

        generate tree [] |> String.concat "\n"
