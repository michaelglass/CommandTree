namespace CommandTree

open System

/// Completion hint for generating shell completions for arguments
type ArgCompletionHint =
    | NoCompletion
    | Values of string list
    | FilePath

/// Argument metadata for help generation
type ArgInfo =
    { Name: string
      TypeName: string
      IsOptional: bool
      Completions: ArgCompletionHint }

/// Recursive command tree for declarative parsing and help generation
[<NoComparison; NoEquality>]
type CommandTree<'Cmd> =
    /// Leaf command with parse and format functions
    | Leaf of
        name: string *
        desc: string *
        args: ArgInfo list *
        parse: (string array -> Result<'Cmd, string>) *
        formatArgs: ('Cmd -> string list option)
    /// Group: contains subcommands
    | Group of
        name: string *
        desc: string *
        children: CommandTree<'Cmd> list *
        defaultParse: (string array -> Result<'Cmd, string>) option *
        defaultChild: string option

module CommandTree =
    /// Get the name of a command tree node
    let name =
        function
        | Leaf(n, _, _, _, _) -> n
        | Group(n, _, _, _, _) -> n

    /// Get the description of a command tree node
    let desc =
        function
        | Leaf(_, d, _, _, _) -> d
        | Group(_, d, _, _, _) -> d

    /// Get argument info for a leaf node
    let args =
        function
        | Leaf(_, _, a, _, _) -> a
        | Group _ -> []

    /// Parse args using tree structure (recursive)
    let rec parse (tree: CommandTree<'Cmd>) (args: string array) : Result<'Cmd, string> =
        match tree, args with
        // Leaf node: delegate to leaf parser
        | Leaf(_, _, _, leafParse, _), _ -> leafParse args

        // Group with no args: use default if available, otherwise show help
        | Group(groupName, _, _, defaultParse, _), [||] ->
            match defaultParse with
            | Some p -> p [||]
            | None ->
                if groupName = "" then
                    Error "_help"
                else
                    Error $"%s{groupName}_help"

        // Group with args: find matching child
        | Group(groupName, _, children, _, _), _ ->
            let subCmd = args.[0]
            let rest = args |> Array.skip 1

            match children |> List.tryFind (fun c -> name c = subCmd) with
            | Some child -> parse child rest
            | None ->
                if groupName = "" then
                    Error $"Unknown command: %s{subCmd}"
                else
                    Error $"Unknown subcommand '%s{subCmd}' for %s{groupName}"

    /// Format a command by walking the tree to find matching leaf
    /// Returns the full command string (e.g., "build env edit staging")
    let rec format (tree: CommandTree<'Cmd>) (cmd: 'Cmd) (path: string list) (cmdPrefix: string) : string option =
        match tree with
        | Leaf(leafName, _, _, _, formatArgs) ->
            match formatArgs cmd with
            | Some args ->
                let parts = path @ [ leafName ] @ args |> List.filter (fun s -> s <> "")
                Some(cmdPrefix + " " + String.concat " " parts)
            | None -> None

        | Group(groupName, _, children, _, _) ->
            let newPath = if groupName = "" then path else path @ [ groupName ]
            children |> List.tryPick (fun child -> format child cmd newPath cmdPrefix)

    /// Format argument info for display
    let private formatArg (arg: ArgInfo) =
        if arg.IsOptional then
            $"[%s{arg.Name}]"
        else
            $"<%s{arg.Name}>"

    /// Format arguments for a command
    let private formatArgs' (argList: ArgInfo list) =
        if argList.IsEmpty then
            ""
        else
            " " + (argList |> List.map formatArg |> String.concat " ")

    /// Generate help for a tree node (single level)
    let rec help (tree: CommandTree<'Cmd>) (path: string list) (cmdPrefix: string) : string =
        let pathStr =
            if path.IsEmpty then
                cmdPrefix
            else
                cmdPrefix + " " + String.concat " " path

        match tree with
        | Leaf(leafName, leafDesc, leafArgs, _, _) ->
            let argsStr = formatArgs' leafArgs
            $"Usage: %s{pathStr} %s{leafName}%s{argsStr}\n\n%s{leafDesc}"

        | Group(groupName, groupDesc, children, _, defChild) ->
            let prefix =
                if groupName = "" then
                    pathStr
                else
                    $"%s{pathStr} %s{groupName}"

            let childrenHelp =
                children
                |> List.map (fun c ->
                    let argsStr = formatArgs' (args c)
                    let cmdStr = $"%s{name c}%s{argsStr}"
                    let marker = if defChild = Some(name c) then " (default)" else ""
                    $"  %s{cmdStr.PadRight(16)} %s{desc c}%s{marker}")
                |> String.concat "\n"

            $"Usage: %s{prefix} <command>\n\n%s{groupDesc}\n\nCommands:\n%s{childrenHelp}"

    /// Generate full help with all subtrees expanded
    let helpFull (tree: CommandTree<'Cmd>) (cmdPrefix: string) : string =
        let rec formatNode (node: CommandTree<'Cmd>) (indent: int) : string list =
            let pad = String.replicate indent "  "

            match node with
            | Leaf(leafName, leafDesc, leafArgs, _, _) ->
                let argsStr = formatArgs' leafArgs
                let cmdStr = $"%s{leafName}%s{argsStr}"
                [ $"%s{pad}%s{cmdStr.PadRight(20)} %s{leafDesc}" ]

            | Group(groupName, groupDesc, children, _, defChild) ->
                let header =
                    if groupName = "" then
                        []
                    else
                        [ $"%s{pad}%s{groupName.PadRight(20)} %s{groupDesc}" ]

                let childIndent = if groupName = "" then indent else indent + 1

                let childLines =
                    children
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
            | Group(_, _, children, _, _) ->
                children
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

    /// Generate fish shell completions from the command tree
    let fishCompletions (tree: CommandTree<'Cmd>) (cmdName: string) : string =
        let escape (s: string) = s.Replace("\"", "\\\"")

        let rec generate (node: CommandTree<'Cmd>) (path: string list) : string list =
            match node with
            | Leaf(leafName, _, argInfos, _, _) ->
                let leafPath = path @ [ leafName ]

                argInfos
                |> List.collect (fun arg ->
                    match arg.Completions with
                    | Values values ->
                        let condition =
                            leafPath
                            |> List.map (sprintf "__fish_seen_subcommand_from %s")
                            |> String.concat "; and "

                        values
                        |> List.map (fun v ->
                            $"complete -c %s{cmdName} -n \"%s{condition}\" -a \"%s{escape v}\" -d \"%s{escape v}\"")
                    | FilePath ->
                        let condition =
                            leafPath
                            |> List.map (sprintf "__fish_seen_subcommand_from %s")
                            |> String.concat "; and "

                        [ $"complete -c %s{cmdName} -n \"%s{condition}\" -F" ]
                    | NoCompletion -> [])
            | Group(groupName, _, children, _, _) ->
                let currentPath = if groupName = "" then path else path @ [ groupName ]

                // Generate condition for this level
                let condition =
                    if currentPath.IsEmpty then
                        "__fish_use_subcommand"
                    else
                        let seen =
                            currentPath
                            |> List.map (sprintf "__fish_seen_subcommand_from %s")
                            |> String.concat "; and "

                        let childNames = children |> List.map name |> String.concat " "
                        $"%s{seen}; and not __fish_seen_subcommand_from %s{childNames}"

                // Generate completions for children at this level
                let childCompletions =
                    children
                    |> List.map (fun child ->
                        let childName = name child
                        let childDesc = escape (desc child)
                        $"complete -c %s{cmdName} -n \"%s{condition}\" -a \"%s{childName}\" -d \"%s{childDesc}\"")

                // Recurse into child groups and leaves
                let nestedCompletions =
                    children |> List.collect (fun child -> generate child currentPath)

                childCompletions @ nestedCompletions

        generate tree [] |> String.concat "\n"
