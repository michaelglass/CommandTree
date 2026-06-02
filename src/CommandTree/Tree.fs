namespace CommandTree

open System

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
        /// Whether the argument is optional (wrapped in option)
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

/// Flag metadata for help generation and parsing
type FlagInfo =
    {
        /// Long flag name (without -- prefix)
        LongName: string
        /// Short flag character (without - prefix), if any
        ShortName: string option
        /// Display type name for help text
        TypeName: string
        /// Whether this flag is a boolean toggle (no value argument)
        IsBool: bool
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

/// Default subcommand for a group (collapses name + parse into one value)
and [<NoComparison; NoEquality>] DefaultCommand<'Cmd> =
    {
        /// Name of the default child command
        ChildName: string
        /// Parse function for the default subcommand
        Parse: string array -> Result<'Cmd, ParseError>
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
        /// Default subcommand, if any
        Default: DefaultCommand<'Cmd> option
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

    /// Check if args contain --help
    let private hasHelpFlag (args: string array) = args |> Array.contains "--help"

    /// Check if args contain --version
    let private hasVersionFlag (args: string array) = args |> Array.contains "--version"

    /// Check if a flag list contains an explicit --help flag (override)
    let private hasExplicitHelpFlag (flags: FlagInfo list) =
        flags |> List.exists (fun fi -> fi.LongName = "help")

    /// Parse args using tree structure (recursive)
    let parse (tree: CommandTree<'Cmd>) (args: string array) : Result<'Cmd, ParseError> =
        let rec parseRec (node: CommandTree<'Cmd>) (args: string array) (path: string list) =
            match node, args with
            // Leaf node: check for --help unless leaf has explicit help flag
            | Leaf leaf, _ ->
                let currentPath = path @ [ leaf.Name ]

                if hasHelpFlag args && not (hasExplicitHelpFlag leaf.Flags) then
                    Error(HelpRequested currentPath)
                else
                    leaf.Parse args

            // Group with no args: use default if available, otherwise show help
            | Group group, [||] ->
                let currentPath = if group.Name = "" then path else path @ [ group.Name ]

                match group.Default with
                | Some def -> def.Parse [||]
                | None -> Error(HelpRequested currentPath)

            // Group with args: try routing into child first, then check --help
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
        if arg.IsList then $"<%s{arg.Name}...>"
        elif arg.IsOptional then $"[%s{arg.Name}]"
        else $"<%s{arg.Name}>"

    /// Render a single flag info line for help output
    let private renderFlagLine (fi: FlagInfo) =
        let longPart = $"--%s{fi.LongName}"

        let shortPart =
            match fi.ShortName with
            | Some s -> $", -%s{s}"
            | None -> ""

        let typePart = if fi.IsBool then "" else $" <%s{fi.LongName}>"

        let envPart =
            match fi.EnvVar with
            | Some { VarName = v } -> $" (env: %s{v})"
            | None -> ""

        let label = $"  %s{longPart}%s{shortPart}%s{typePart}"
        $"%s{label.PadRight(30)} %s{fi.Description}%s{envPart}"

    /// Format arguments for a command
    let private formatArgs' (argList: ArgInfo list) =
        if argList.IsEmpty then
            ""
        else
            " " + (argList |> List.map formatArg |> String.concat " ")

    /// Render the children of a group as a help listing
    let private renderChildrenHelp (group: GroupData<'Cmd>) : string =
        let defChild = group.Default |> Option.map (fun d -> d.ChildName)

        group.Children
        |> List.map (fun c ->
            let argsStr = formatArgs' (args c)
            let cmdStr = $"%s{name c}%s{argsStr}"
            let marker = if defChild = Some(name c) then " (default)" else ""
            $"  %s{cmdStr.PadRight(16)} %s{desc c}%s{marker}")
        |> String.concat "\n"

    /// Render a single arg description line for the Arguments section
    let private renderArgLine (arg: ArgInfo) (desc: string) =
        let label = $"  %s{formatArg arg}"

        let defaultSuffix =
            match arg.Default with
            | Some d -> $" (default: %s{d})"
            | None -> ""

        $"%s{label.PadRight(20)} %s{desc}%s{defaultSuffix}"

    /// Render a named help section (returns empty string when lines is empty)
    let private renderSection (header: string) (lines: string list) =
        if lines.IsEmpty then
            ""
        else
            $"\n\n%s{header}:\n" + String.concat "\n" lines

    /// Generate help for a tree node (single level)
    let help (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : string =
        let pathStr =
            if path.IsEmpty then
                cmdPrefix
            else
                cmdPrefix + " " + String.concat " " path

        match tree with
        | Leaf leaf ->
            let argsStr = formatArgs' leaf.Args

            let optionsStr = if leaf.Flags.IsEmpty then "" else " [options]"

            let argsSection =
                leaf.Args
                |> List.choose (fun a -> a.Description |> Option.map (fun d -> renderArgLine a d))
                |> renderSection "Arguments"

            let flagsSection = leaf.Flags |> List.map renderFlagLine |> renderSection "Options"

            let examplesSection =
                leaf.Examples
                |> List.map (fun e -> $"  %s{pathStr} %s{leaf.Name} %s{e}")
                |> renderSection "Examples"

            $"Usage: %s{pathStr} %s{leaf.Name}%s{argsStr}%s{optionsStr}\n\n%s{leaf.Description}%s{argsSection}%s{flagsSection}%s{examplesSection}"

        | Group group ->
            let prefix =
                if group.Name = "" then
                    pathStr
                else
                    $"%s{pathStr} %s{group.Name}"

            $"Usage: %s{prefix} <command>\n\n%s{group.Description}\n\nCommands:\n%s{renderChildrenHelp group}"

    /// Generate full help with all subtrees expanded
    let helpFull (tree: CommandTree<'Cmd>) (cmdPrefix: string) : string =
        let rec formatNode (node: CommandTree<'Cmd>) (indent: int) : string list =
            let pad = String.replicate indent "  "

            match node with
            | Leaf leaf ->
                let argsStr = formatArgs' leaf.Args

                let optionsStr = if leaf.Flags.IsEmpty then "" else " [options]"

                let cmdStr = $"%s{leaf.Name}%s{argsStr}%s{optionsStr}"
                [ $"%s{pad}%s{cmdStr.PadRight(20)} %s{leaf.Description}" ]

            | Group group ->
                let header =
                    if group.Name = "" then
                        []
                    else
                        [ $"%s{pad}%s{group.Name.PadRight(20)} %s{group.Description}" ]

                let childIndent = if group.Name = "" then indent else indent + 1

                let defChild = group.Default |> Option.map (fun d -> d.ChildName)

                let childLines =
                    group.Children
                    |> List.collect (fun c ->
                        let lines = formatNode c childIndent

                        match defChild, lines with
                        | Some dc, first :: rest when name c = dc -> (first + " (default)") :: rest
                        | _ -> lines)

                header @ childLines

        let lines = formatNode tree 0
        $"Usage: %s{cmdPrefix} <command>\n\nCommands:\n" + String.concat "\n" lines

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

    /// Generate help for a subtree at the given path
    let helpForPath (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : string =
        match findByPath tree path with
        // Pass parent path (all but last segment) since help() adds the node's own name
        | Some subtree ->
            let parentPath =
                if path.Length > 0 then
                    path |> List.take (path.Length - 1)
                else
                    []

            help subtree parentPath cmdPrefix
        | None -> help tree [] cmdPrefix

    /// Generate help for the root with global options section
    let helpWithGlobals (tree: CommandTree<'Cmd>) (globalFlags: FlagInfo list) (cmdPrefix: string) : string =
        let globalSection =
            if globalFlags.IsEmpty then
                ""
            else
                let flagLines = globalFlags |> List.map renderFlagLine |> String.concat "\n"
                $"\nGlobal options:\n%s{flagLines}\n"

        match tree with
        | Group group ->
            $"Usage: %s{cmdPrefix} [global options] <command>\n\n%s{group.Description}%s{globalSection}\nCommands:\n%s{renderChildrenHelp group}"
        | _ -> help tree [] cmdPrefix

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

    /// Render a <c>ParseError</c> as the full user-facing stderr text: a clear one-line
    /// "invalid input" message followed by the help for the nearest relevant command or
    /// group. Pure — returns the string; the caller prints it and chooses the exit code
    /// (see <c>isError</c>).
    ///
    /// Per case:
    /// <c>UnknownFlag</c> → "Unknown flag …" + that command's help.
    /// <c>UnknownCommand</c> → "Unknown command …" + the nearest group's help (its child listing).
    /// <c>InvalidArguments</c> → the message + that command's help.
    /// <c>AmbiguousArgument</c> → "Ambiguous …" + nearest group's help.
    /// <c>DuplicateFlag</c> → a duplicate message + that command's help.
    /// <c>HelpRequested</c> → just the help for that path (not an error; no error line).
    /// <c>VersionRequested</c> → empty string: version output is the caller's concern,
    /// since CommandTree does not know the version. Detect it via <c>isError</c> (false)
    /// and print your own version banner instead of calling this.
    let renderParseError (tree: CommandTree<'Cmd>) (error: ParseError) (cmdPrefix: string) : string =
        let withHelp (line: string) (path: string list) =
            $"%s{line}\n\n%s{helpForPath tree path cmdPrefix}"

        match error with
        | HelpRequested path -> helpForPath tree path cmdPrefix
        | VersionRequested -> ""
        | UnknownFlag(flag, command, _) -> withHelp $"Unknown flag '%s{flag}' for '%s{command}'." [ command ]
        | UnknownCommand(input, _, groupPath) ->
            withHelp $"Unknown command '%s{input}'." (closestGroupPath tree groupPath)
        | InvalidArguments(command, message) -> withHelp message [ command ]
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
        | AmbiguousArgument _
        | UnknownFlag _
        | DuplicateFlag _ -> true

    /// Generate fish shell completions from the command tree
    let fishCompletions (tree: CommandTree<'Cmd>) (cmdName: string) : string =
        let escape (s: string) = s.Replace("\"", "\\\"")

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

                        [ $"complete -c %s{cmdName} -n \"%s{condition}\" -l %s{fi.LongName}%s{shortPart} -d \"%s{escape fi.Description}\"" ])

                argCompletions @ flagCompletions
            | Group group ->
                let currentPath = if group.Name = "" then path else path @ [ group.Name ]

                // Generate condition for this level
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

                // Generate completions for children at this level
                let childCompletions =
                    group.Children
                    |> List.map (fun child ->
                        let childName = name child
                        let childDesc = escape (desc child)
                        $"complete -c %s{cmdName} -n \"%s{condition}\" -a \"%s{childName}\" -d \"%s{childDesc}\"")

                // Recurse into child groups and leaves
                let nestedCompletions =
                    group.Children |> List.collect (fun child -> generate child currentPath)

                childCompletions @ nestedCompletions

        generate tree [] |> String.concat "\n"
