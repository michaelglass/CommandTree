namespace CommandTree

open System
open System.Text.RegularExpressions
open FSharp.Reflection

/// Reflection-based command tree generation from F# discriminated unions
module CommandReflection =

    /// Convert PascalCase to kebab-case (e.g., "FileCoverage" -> "file-coverage")
    let toKebabCase (s: string) =
        Regex.Replace(s, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant()

    /// Convert PascalCase to space-separated words (e.g., "FileCoverage" -> "File coverage")
    let toDescription (s: string) =
        let spaced = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2")

        if spaced.Length > 0 then
            spaced.[0].ToString().ToUpper() + spaced.Substring(1).ToLower()
        else
            spaced

    /// Get kebab-case name from a union case value
    let inline caseName (value: 'T) =
        let case, _ = FSharpValue.GetUnionFields(value, typeof<'T>)
        toKebabCase case.Name

    /// Get CmdAttribute from a union case, if present
    let getCmdAttr (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdAttribute>)
        |> Array.tryHead
        |> Option.map (fun a -> a :?> CmdAttribute)

    /// Get command name from case (use attribute override or derive from case name)
    let getCommandName (case: UnionCaseInfo) =
        match getCmdAttr case with
        | Some attr when not (isNull attr.Name) -> attr.Name
        | None
        | Some _ -> toKebabCase case.Name

    /// Get description from CmdAttribute (required) or derive from case name
    let getDescription (case: UnionCaseInfo) =
        match getCmdAttr case with
        | Some attr -> attr.Description
        | None -> toDescription case.Name

    /// Check if a case has the CmdDefault attribute
    let isDefault (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdDefaultAttribute>) |> Array.isEmpty |> not

    /// Check if a type is a union type (for detecting nested groups)
    let isUnionType (t: Type) =
        FSharpType.IsUnion(t)
        && t <> typeof<string>
        && not (t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>)

    /// Get a readable type name for display
    let rec private getTypeName (t: Type) =
        if t = typeof<string> then
            "string"
        elif t = typeof<int> then
            "int"
        elif t = typeof<int64> then
            "int64"
        elif t = typeof<bool> then
            "bool"
        elif t = typeof<Guid> then
            "guid"
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>> then
            let inner = t.GetGenericArguments().[0]
            getTypeName inner
        else
            t.Name.ToLowerInvariant()

    /// Check if a type is optional
    let private isOptionalType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    /// Get the inner type, unwrapping option if needed
    let private unwrapOptionType (t: Type) =
        if isOptionalType t then t.GetGenericArguments().[0] else t

    /// Determine completion hint for a field at a given index on a union case
    let private getCompletionHint
        (case: UnionCaseInfo)
        (fieldIndex: int)
        (field: Reflection.PropertyInfo)
        : ArgCompletionHint =
        // Check for CmdCompletionAttribute on the case targeting this field index
        let completionAttr =
            case.GetCustomAttributes(typeof<CmdCompletionAttribute>)
            |> Array.map (fun a -> a :?> CmdCompletionAttribute)
            |> Array.tryFind (fun a -> a.FieldIndex = fieldIndex)

        match completionAttr with
        | Some attr -> Values(attr.Values |> Array.toList)
        | None ->
            // Check for CmdFileCompletionAttribute on the case targeting this field index
            let fileAttr =
                case.GetCustomAttributes(typeof<CmdFileCompletionAttribute>)
                |> Array.map (fun a -> a :?> CmdFileCompletionAttribute)
                |> Array.tryFind (fun a -> a.FieldIndex = fieldIndex)

            match fileAttr with
            | Some _ -> FilePath
            | None ->
                // Auto-detect: if field type (unwrapping option) is a union, enumerate cases
                let innerType = unwrapOptionType field.PropertyType

                if isUnionType innerType then
                    let cases = FSharpType.GetUnionCases(innerType)
                    let values = cases |> Array.map (fun c -> toKebabCase c.Name) |> Array.toList
                    Values values
                else
                    NoCompletion

    /// Build ArgInfo list from union case fields
    let private getArgInfo (case: UnionCaseInfo) (fields: Reflection.PropertyInfo array) : ArgInfo list =
        fields
        |> Array.mapi (fun i f ->
            { Name = toKebabCase f.Name
              TypeName = getTypeName f.PropertyType
              IsOptional = isOptionalType f.PropertyType
              Completions = getCompletionHint case i f })
        |> Array.toList

    /// Parse a single field value from string
    let rec parseFieldValue (fieldType: Type) (value: string) : obj option =
        if fieldType = typeof<string> then
            Some(box value)
        elif fieldType = typeof<int> then
            match Int32.TryParse(value) with
            | true, n -> Some(box n)
            | _ -> None
        elif fieldType = typeof<int64> then
            match Int64.TryParse(value) with
            | true, n -> Some(box n)
            | _ -> None
        elif fieldType = typeof<bool> then
            match Boolean.TryParse(value) with
            | true, b -> Some(box b)
            | _ -> None
        elif fieldType = typeof<Guid> then
            match Guid.TryParse(value) with
            | true, g -> Some(box g)
            | _ -> None
        elif
            fieldType.IsGenericType
            && fieldType.GetGenericTypeDefinition() = typedefof<option<_>>
        then
            let innerType = fieldType.GetGenericArguments().[0]

            if String.IsNullOrEmpty(value) then
                let noneCase =
                    FSharpType.GetUnionCases(fieldType) |> Array.find (fun c -> c.Name = "None")

                Some(FSharpValue.MakeUnion(noneCase, [||]))
            else
                match parseFieldValue innerType value with
                | Some v ->
                    let someCase =
                        FSharpType.GetUnionCases(fieldType) |> Array.find (fun c -> c.Name = "Some")

                    Some(FSharpValue.MakeUnion(someCase, [| v |]))
                | None -> None
        elif isUnionType fieldType then
            // Match kebab-case input against union case names with prefix matching
            let cases = FSharpType.GetUnionCases(fieldType)
            let valueLower = value.ToLowerInvariant()

            let matches =
                cases
                |> Array.filter (fun c ->
                    if c.GetFields().Length <> 0 then
                        false
                    else
                        let caseName = toKebabCase c.Name
                        let shorter = min caseName.Length valueLower.Length

                        shorter >= 3
                        && (caseName.StartsWith(valueLower, StringComparison.Ordinal)
                            || valueLower.StartsWith(caseName, StringComparison.Ordinal)))

            match matches with
            | [| single |] -> Some(FSharpValue.MakeUnion(single, [||]))
            | [||] -> None
            | ambiguous ->
                let names =
                    ambiguous |> Array.map (fun c -> toKebabCase c.Name) |> String.concat ", "

                failwith $"Ambiguous argument '%s{value}' matches: %s{names}"
        else
            None

    /// Format a field value to string
    let rec formatFieldValue (value: obj) : string =
        match value with
        | null -> ""
        | :? string as s -> s
        | :? int as n -> string<int> n
        | :? int64 as n -> string<int64> n
        | :? bool as b -> string<bool> b
        | :? Guid as g -> string<Guid> g
        | _ when
            value.GetType().IsGenericType
            && value.GetType().GetGenericTypeDefinition() = typedefof<option<_>>
            ->
            let case, fields = FSharpValue.GetUnionFields(value, value.GetType())

            if case.Name = "Some" then
                formatFieldValue fields.[0]
            else
                ""
        | _ when isUnionType (value.GetType()) ->
            let case, _ = FSharpValue.GetUnionFields(value, value.GetType())
            toKebabCase case.Name
        | _ -> string<obj> value

    /// Make a None value for an option type
    let makeNone (optionType: Type) =
        let noneCase =
            FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

        FSharpValue.MakeUnion(noneCase, [||])

    /// Parse fields from args array
    let parseFields (fields: Reflection.PropertyInfo array) (args: string array) =
        fields
        |> Array.mapi (fun i field ->
            if i < args.Length then
                parseFieldValue field.PropertyType args.[i]
            elif
                field.PropertyType.IsGenericType
                && field.PropertyType.GetGenericTypeDefinition() = typedefof<option<_>>
            then
                Some(makeNone field.PropertyType)
            else
                None)

    /// Format a command value to its CLI string using reflection (no tree needed).
    /// Recursively walks nested unions using getCommandName + formatFieldValue.
    /// Example: InfraCommand.Sync(InfraSyncCommand.Ses None) → "sync ses"
    let formatCmd (cmd: 'Cmd) : string =
        let rec go (value: obj) (t: Type) : string =
            let case, fields = FSharpValue.GetUnionFields(value, t)
            let n = getCommandName case
            let caseFields = case.GetFields()

            let parts =
                fields
                |> Array.mapi (fun i v ->
                    let ft = caseFields.[i].PropertyType

                    if isUnionType ft then go v ft else formatFieldValue v)
                |> Array.filter (fun s -> s <> "")

            if parts.Length = 0 then
                n
            else
                n + " " + String.concat " " parts

        go (cmd :> obj) typeof<'Cmd>

    /// Generate a CommandTree from a union type
    let fromUnion<'Cmd> (rootDesc: string) : CommandTree<'Cmd> =
        let cmdType = typeof<'Cmd>
        let cases = FSharpType.GetUnionCases(cmdType)

        let rec processCase (outerCase: UnionCaseInfo) (wrapValue: obj -> obj) : CommandTree<'Cmd> =
            let cmdName = getCommandName outerCase
            let desc = getDescription outerCase
            let fields = outerCase.GetFields()

            if fields.Length = 1 && isUnionType fields.[0].PropertyType then
                // Nested union -> Group
                let nestedType = fields.[0].PropertyType
                let nestedCases = FSharpType.GetUnionCases(nestedType)

                let nestedChildren =
                    nestedCases
                    |> Array.map (fun nestedCase ->
                        let nestedWrap = fun v -> wrapValue (FSharpValue.MakeUnion(outerCase, [| v |]))
                        processCase nestedCase nestedWrap)
                    |> Array.toList

                let defaultCase = nestedCases |> Array.tryFind isDefault

                let defaultParse =
                    defaultCase
                    |> Option.map (fun dc ->
                        fun (args: string array) ->
                            let nestedFields = dc.GetFields()

                            try
                                let fieldValues = parseFields nestedFields args

                                if fieldValues |> Array.forall Option.isSome then
                                    let values = fieldValues |> Array.map Option.get
                                    let nestedValue = FSharpValue.MakeUnion(dc, values)

                                    let cmdValue = wrapValue (FSharpValue.MakeUnion(outerCase, [| nestedValue |]))

                                    Ok(cmdValue :?> 'Cmd)
                                else
                                    Error "Invalid arguments"
                            with _ ->
                                Error "Parse error")

                let defaultChildName = defaultCase |> Option.map (fun dc -> getCommandName dc)

                CommandTree.Group(cmdName, desc, nestedChildren, defaultParse, defaultChildName)
            else
                // Leaf case
                let parse (args: string array) : Result<'Cmd, string> =
                    try
                        let fieldValues = parseFields fields args

                        if fieldValues |> Array.forall Option.isSome then
                            let values = fieldValues |> Array.map Option.get
                            let innerValue = FSharpValue.MakeUnion(outerCase, values)
                            let cmdValue = wrapValue innerValue
                            Ok(cmdValue :?> 'Cmd)
                        else
                            Error $"Invalid arguments for %s{cmdName}"
                    with ex ->
                        Error $"Parse error: %s{ex.Message}"

                let formatArgs (cmd: 'Cmd) : string list option =
                    // Navigate through nested unions to find the target case
                    let rec findMatch (value: obj) (targetCase: UnionCaseInfo) =
                        if isNull value then
                            None
                        else
                            let actualType = value.GetType()

                            // Check if this is a union type we can decompose
                            if FSharpType.IsUnion(actualType, true) then
                                let c, fs = FSharpValue.GetUnionFields(value, actualType, true)

                                // Check if this matches our target case (same tag AND same declaring type)
                                if c.Tag = targetCase.Tag && c.DeclaringType = targetCase.DeclaringType then
                                    Some(
                                        fs
                                        |> Array.map formatFieldValue
                                        |> Array.filter (fun s -> s <> "")
                                        |> Array.toList
                                    )
                                // Otherwise, recursively search in fields
                                else
                                    fs |> Array.tryPick (fun f -> findMatch f targetCase)
                            else
                                None

                    findMatch (cmd :> obj) outerCase

                let argInfo = getArgInfo outerCase fields
                CommandTree.Leaf(cmdName, desc, argInfo, parse, formatArgs)

        let children = cases |> Array.map (fun case -> processCase case id) |> Array.toList

        // Check for default at root level
        let rootDefault = cases |> Array.tryFind isDefault

        let defaultParse =
            rootDefault
            |> Option.map (fun defaultCase ->
                fun (args: string array) ->
                    let fields = defaultCase.GetFields()

                    if fields.Length = 0 then
                        Ok(FSharpValue.MakeUnion(defaultCase, [||]) :?> 'Cmd)
                    elif fields.Length = 1 && isUnionType fields.[0].PropertyType then
                        // Nested group - find its default and delegate
                        let nestedType = fields.[0].PropertyType
                        let nestedCases = FSharpType.GetUnionCases(nestedType)

                        match nestedCases |> Array.tryFind isDefault with
                        | Some nestedDefault ->
                            let nestedFields = nestedDefault.GetFields()
                            let fieldValues = parseFields nestedFields args

                            if fieldValues |> Array.forall Option.isSome then
                                let values = fieldValues |> Array.map Option.get
                                let nestedValue = FSharpValue.MakeUnion(nestedDefault, values)
                                let cmdValue = FSharpValue.MakeUnion(defaultCase, [| nestedValue |])
                                Ok(cmdValue :?> 'Cmd)
                            else
                                Error "Invalid arguments for default command"
                        | None -> Error "No default command in nested group"
                    else
                        Error "Default command requires no arguments")

        let defaultChildName = rootDefault |> Option.map getCommandName

        CommandTree.Group("", rootDesc, children, defaultParse, defaultChildName)

/// ADT-based command specification for type safety and exhaustiveness checking
[<NoComparison; NoEquality>]
type CommandSpec<'Cmd> =
    {
        /// Command tree for parsing and help generation
        Tree: CommandTree<'Cmd>
        /// Format ADT to command string (for error messages - uses exhaustive pattern matching)
        Format: 'Cmd -> string
        /// Execute a command (exhaustive pattern matching ensures all cases handled)
        Execute: 'Cmd -> unit
    }
