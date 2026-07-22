namespace CommandTree

open System
open System.Text.RegularExpressions
open FSharp.Reflection

/// Bundled global options + command tree for parsing with global flags.
/// 'Globals is a DU where each case is a global flag.
/// 'Cmd is the command DU.
[<NoComparison; NoEquality>]
type GlobalSpec<'Globals, 'Cmd> =
    {
        /// Command tree (for help, completions, etc.)
        Tree: CommandTree<'Cmd>
        /// Parse args into global flags + command
        Parse: string array -> Result<'Globals list * 'Cmd, ParseError>
        /// Flag info for global options (for help rendering)
        GlobalFlags: FlagInfo list
    }

/// Reflection-based command tree generation from F# discriminated unions
module CommandReflection =

    /// Convert PascalCase to kebab-case (e.g., "FileCoverage" -> "file-coverage").
    /// Acronym runs split at the last capital before a capitalized word, so
    /// "HTMLParser" -> "html-parser" and "DBMigrate" -> "db-migrate".
    let toKebabCase (s: string) =
        // First split acronym boundaries (UPPER run followed by a capitalized word),
        // then split the usual lower->Upper boundaries.
        let withAcronymBoundaries = Regex.Replace(s, "([A-Z]+)([A-Z][a-z])", "$1-$2")
        Regex.Replace(withAcronymBoundaries, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant()

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

    /// Get description from CmdAttribute or derive from case name
    let getDescription (case: UnionCaseInfo) =
        match getCmdAttr case with
        | Some attr when not (isNull attr.Description) -> attr.Description
        | _ -> toDescription case.Name

    /// Check if a case has the CmdDefault attribute
    let isDefault (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdDefaultAttribute>) |> Array.isEmpty |> not

    /// Check if a type is optional
    let private isOptionalType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    /// Check if a type is a list type
    let private isListType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    /// Get the element type from a list type
    let private listElementType (t: Type) = t.GetGenericArguments().[0]

    /// Check if a type is a union type (for detecting nested groups)
    let isUnionType (t: Type) =
        FSharpType.IsUnion(t)
        && t <> typeof<string>
        && not (isOptionalType t)
        && not (isListType t)

    /// Check if a type is a flag DU list (`SomeDU list`) — a trailing field of this
    /// shape is parsed as named `--flags`, one flag per element DU case. Single
    /// source of truth shared by tree construction and `formatCmd`.
    let private isFlagDUList (t: Type) =
        isListType t && isUnionType (listElementType t)

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
        elif t = typeof<float> then
            "float"
        elif t = typeof<decimal> then
            "decimal"
        elif t = typeof<Guid> then
            "guid"
        elif isOptionalType t then
            let inner = t.GetGenericArguments().[0]
            getTypeName inner
        elif isListType t then
            let inner = t.GetGenericArguments().[0]
            getTypeName inner + " list"
        else
            t.Name.ToLowerInvariant()

    /// Scalar field types that parseFieldValue / formatFieldValue can round-trip.
    let private supportedScalarTypes =
        [ typeof<string>
          typeof<int>
          typeof<int64>
          typeof<bool>
          typeof<Guid>
          typeof<float>
          typeof<decimal> ]

    /// Human-readable list of supported field types for error messages.
    let private supportedTypesDescription =
        "string, int, int64, bool, float, decimal, Guid, a discriminated union, "
        + "an option of any of these, or a list of any of these"

    /// True when a field type can be parsed/formatted by the reflection machinery.
    /// Mirrors parseFieldValue: scalars, unions (nested groups / flag DUs), option of
    /// supported, and list of supported.
    let rec private isSupportedFieldType (t: Type) : bool =
        if List.contains t supportedScalarTypes then
            true
        elif isOptionalType t then
            isSupportedFieldType (t.GetGenericArguments().[0])
        elif isListType t then
            isSupportedFieldType (listElementType t)
        elif isUnionType t then
            true
        else
            false

    /// Get the inner type, unwrapping option if needed
    let private unwrapOptionType (t: Type) =
        if isOptionalType t then t.GetGenericArguments().[0] else t

    /// Auto-detect completion hint from field type (union types get their cases enumerated)
    let private autoDetectCompletion (field: Reflection.PropertyInfo) : ArgCompletionHint =
        let innerType = unwrapOptionType field.PropertyType

        if isUnionType innerType then
            let cases = FSharpType.GetUnionCases(innerType)
            let values = cases |> Array.map (fun c -> toKebabCase c.Name) |> Array.toList
            Values values
        else
            NoCompletion

    /// Determine completion hint for a field at a given index on a union case
    let private getCompletionHint
        (case: UnionCaseInfo)
        (fieldIndex: int)
        (field: Reflection.PropertyInfo)
        : ArgCompletionHint =
        let completionAttr =
            case.GetCustomAttributes(typeof<CmdCompletionAttribute>)
            |> Array.map (fun a -> a :?> CmdCompletionAttribute)
            |> Array.tryFind (fun a -> a.FieldIndex = fieldIndex)

        match completionAttr with
        | Some attr -> Values(attr.Values |> Array.toList)
        | None ->
            let fileAttr =
                case.GetCustomAttributes(typeof<CmdFileCompletionAttribute>)
                |> Array.map (fun a -> a :?> CmdFileCompletionAttribute)
                |> Array.tryFind (fun a -> a.FieldIndex = fieldIndex)

            match fileAttr with
            | Some _ -> FilePath
            | None -> autoDetectCompletion field

    /// Get CmdArgAttribute for a specific field index on a union case, if present
    let private getCmdArgAttr (case: UnionCaseInfo) (fieldIndex: int) =
        case.GetCustomAttributes(typeof<CmdArgAttribute>)
        |> Array.map (fun a -> a :?> CmdArgAttribute)
        |> Array.tryFind (fun a -> a.FieldIndex = fieldIndex)

    /// Get CmdArgAttribute from a record field PropertyInfo, if present
    let private getCmdArgAttrFromField (field: Reflection.PropertyInfo) =
        field.GetCustomAttributes(typeof<CmdArgAttribute>, false)
        |> Array.tryHead
        |> Option.map (fun a -> a :?> CmdArgAttribute)

    /// Get example strings from CmdExampleAttribute on a union case
    let private getCmdExamples (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdExampleAttribute>)
        |> Array.collect (fun a -> (a :?> CmdExampleAttribute).Examples)
        |> Array.toList

    /// Build ArgInfo list from union case fields
    let private getArgInfo (case: UnionCaseInfo) (fields: Reflection.PropertyInfo array) : ArgInfo list =
        fields
        |> Array.mapi (fun i f ->
            let cmdArgAttr = getCmdArgAttr case i

            { Name = toKebabCase f.Name
              TypeName = getTypeName f.PropertyType
              IsOptional = isOptionalType f.PropertyType
              IsList = isListType f.PropertyType
              Completions = getCompletionHint case i f
              Description = cmdArgAttr |> Option.bind (fun a -> Option.ofObj a.Description)
              Default = cmdArgAttr |> Option.bind (fun a -> Option.ofObj a.Default) })
        |> Array.toList

    /// Convert PascalCase to SCREAMING_SNAKE_CASE (e.g., "LogLevel" -> "LOG_LEVEL", "DryRun" -> "DRY_RUN")
    let internal toScreamingSnakeCase (s: string) =
        Regex.Replace(s, "([a-z])([A-Z])", "$1_$2").ToUpperInvariant()

    /// Get CmdEnvAttribute from a union case, if present
    let private getCmdEnvAttr (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdEnvAttribute>)
        |> Array.tryHead
        |> Option.map (fun a -> a :?> CmdEnvAttribute)

    /// Get CmdEnvRawAttribute from a union case, if present
    let private getCmdEnvRawAttr (case: UnionCaseInfo) =
        case.GetCustomAttributes(typeof<CmdEnvRawAttribute>)
        |> Array.tryHead
        |> Option.map (fun a -> a :?> CmdEnvRawAttribute)

    /// Derive EnvVarInfo for a flag DU case given an optional prefix
    let private deriveEnvVar (case: UnionCaseInfo) (envPrefix: string option) : EnvVarInfo option =
        match getCmdEnvRawAttr case with
        | Some attr -> Some { VarName = attr.VarName }
        | None ->
            match envPrefix with
            | None -> None
            | Some prefix ->
                let suffix =
                    match getCmdEnvAttr case with
                    | Some attr -> attr.Suffix
                    | None -> toScreamingSnakeCase case.Name

                Some { VarName = $"%s{prefix}_%s{suffix}" }

    /// Build FlagInfo list from a DU type where each case = one flag.
    /// No-field cases become boolean flags, single-field cases become value flags.
    let internal getFlagInfoFromDU (flagType: Type) (envPrefix: string option) : FlagInfo list =
        let cases = FSharpType.GetUnionCases(flagType)

        let flagData =
            cases
            |> Array.map (fun case ->
                let fields = case.GetFields()
                let isBool = fields.Length = 0

                let flagAttr =
                    case.GetCustomAttributes(typeof<CmdFlagAttribute>)
                    |> Array.tryHead
                    |> Option.map (fun a -> a :?> CmdFlagAttribute)

                let longName =
                    match flagAttr with
                    | Some a when not (isNull a.Name) -> a.Name
                    | _ -> toKebabCase case.Name

                let explicitShort =
                    match flagAttr with
                    | Some a when not (isNull a.Short) -> Some a.Short
                    | _ -> None

                let typeName =
                    if isBool then
                        "bool"
                    else
                        getTypeName fields.[0].PropertyType

                let description =
                    match flagAttr with
                    | Some a when not (isNull a.Description) -> a.Description
                    | _ -> toDescription case.Name

                let envVar = deriveEnvVar case envPrefix

                (longName, explicitShort, isBool, typeName, description, envVar))

        // Short flag collision detection
        let shortCounts =
            flagData
            |> Array.choose (fun (longName, explicitShort, _, _, _, _) ->
                match explicitShort with
                | Some _ -> None
                | None -> Some(string longName.[0]))
            |> Array.countBy id
            |> Map.ofArray

        flagData
        |> Array.map (fun (longName, explicitShort, isBool, typeName, description, envVar) ->
            let shortName =
                match explicitShort with
                | Some s -> Some s
                | None ->
                    let candidate = string longName.[0]

                    match Map.tryFind candidate shortCounts with
                    | Some count when count = 1 -> Some candidate
                    | _ -> None

            { LongName = longName
              ShortName = shortName
              TypeName = typeName
              IsBool = isBool
              Description = description
              EnvVar = envVar })
        |> Array.toList

    /// Make a None value for an option type
    let makeNone (optionType: Type) =
        let noneCase =
            FSharpType.GetUnionCases(optionType) |> Array.find (fun c -> c.Name = "None")

        FSharpValue.MakeUnion(noneCase, [||])

    /// Parse a single field value from string
    let rec parseFieldValue (fieldType: Type) (value: string) : Result<obj option, ParseFieldError> =
        if fieldType = typeof<string> then
            Ok(Some(box value))
        elif fieldType = typeof<int> then
            match Int32.TryParse(value) with
            | true, n -> Ok(Some(box n))
            | _ -> Ok None
        elif fieldType = typeof<int64> then
            match Int64.TryParse(value) with
            | true, n -> Ok(Some(box n))
            | _ -> Ok None
        elif fieldType = typeof<bool> then
            match Boolean.TryParse(value) with
            | true, b -> Ok(Some(box b))
            | _ -> Ok None
        elif fieldType = typeof<Guid> then
            match Guid.TryParse(value) with
            | true, g -> Ok(Some(box g))
            | _ -> Ok None
        elif fieldType = typeof<float> then
            match
                Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture)
            with
            | true, f -> Ok(Some(box f))
            | _ -> Ok None
        elif fieldType = typeof<decimal> then
            match
                Decimal.TryParse(value, Globalization.NumberStyles.Number, Globalization.CultureInfo.InvariantCulture)
            with
            | true, d -> Ok(Some(box d))
            | _ -> Ok None
        elif isOptionalType fieldType then
            let innerType = fieldType.GetGenericArguments().[0]

            if String.IsNullOrEmpty(value) then
                Ok(Some(makeNone fieldType))
            else
                match parseFieldValue innerType value with
                | Ok(Some v) ->
                    let someCase =
                        FSharpType.GetUnionCases(fieldType) |> Array.find (fun c -> c.Name = "Some")

                    Ok(Some(FSharpValue.MakeUnion(someCase, [| v |])))
                | Ok None -> Ok None
                | Error e -> Error e
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
            | [| single |] -> Ok(Some(FSharpValue.MakeUnion(single, [||])))
            | [||] -> Ok None
            | ambiguous ->
                let names = ambiguous |> Array.map (fun c -> toKebabCase c.Name) |> Array.toList

                Error(AmbiguousValue(value, names))
        else
            Ok None

    /// Format a field value to string
    let rec formatFieldValue (value: obj) : string =
        match value with
        | null -> ""
        | :? string as s -> s
        | :? int as n -> string<int> n
        | :? int64 as n -> string<int64> n
        | :? bool as b -> string<bool> b
        | :? Guid as g -> string<Guid> g
        | :? float as f -> f.ToString(Globalization.CultureInfo.InvariantCulture)
        | :? decimal as d -> d.ToString(Globalization.CultureInfo.InvariantCulture)
        | _ when isOptionalType (value.GetType()) ->
            let case, fields = FSharpValue.GetUnionFields(value, value.GetType())

            if case.Name = "Some" then
                formatFieldValue fields.[0]
            else
                ""
        | _ when isListType (value.GetType()) ->
            let items = value :?> System.Collections.IEnumerable

            items
            |> Seq.cast<obj>
            |> Seq.map formatFieldValue
            |> Seq.filter (fun s -> s <> "")
            |> String.concat " "
        | _ when isUnionType (value.GetType()) ->
            let case, _ = FSharpValue.GetUnionFields(value, value.GetType())
            toKebabCase case.Name
        | _ -> string<obj> value

    /// Convert a ParseFieldError to a ParseError for a given command
    let internal fieldErrorToParseError (cmdName: string) (fieldError: ParseFieldError) : ParseError =
        match fieldError with
        | AmbiguousValue(input, candidates) -> AmbiguousArgument(input, candidates)
        | InvalidValue msg -> InvalidArguments(cmdName, msg)

    /// Build a typed F# list from a sequence of boxed values
    let internal buildTypedList (elemType: Type) (items: obj seq) : obj =
        let listType = typedefof<list<_>>.MakeGenericType(elemType)
        let listCases = FSharpType.GetUnionCases(listType)
        let nilCase = listCases |> Array.find (fun c -> c.Name = "Empty")
        let consCase = listCases |> Array.find (fun c -> c.Name = "Cons")

        Seq.foldBack
            (fun v acc -> FSharpValue.MakeUnion(consCase, [| v; acc |]))
            items
            (FSharpValue.MakeUnion(nilCase, [||]))

    /// Parse fields from args array
    let parseFields
        (fields: Reflection.PropertyInfo array)
        (args: string array)
        : Result<obj option, ParseFieldError> array =
        fields
        |> Array.mapi (fun i field ->
            if isListType field.PropertyType then
                // List field: consume all remaining args from index i onward
                let elemType = listElementType field.PropertyType
                let remaining = if i < args.Length then args.[i..] else [||]

                if remaining.Length = 0 then
                    Ok None
                else
                    let parsed = remaining |> Array.map (fun arg -> parseFieldValue elemType arg)

                    let firstError =
                        parsed
                        |> Array.tryPick (fun r ->
                            match r with
                            | Error e -> Some e
                            | _ -> None)

                    match firstError with
                    | Some e -> Error e
                    | None ->
                        let values =
                            parsed
                            |> Array.choose (fun r ->
                                match r with
                                | Ok(Some v) -> Some v
                                | _ -> None)

                        if values.Length <> remaining.Length then
                            Ok None
                        else
                            Ok(Some(buildTypedList elemType values))
            elif i < args.Length then
                parseFieldValue field.PropertyType args.[i]
            elif isOptionalType field.PropertyType then
                Ok(Some(makeNone field.PropertyType))
            else
                Ok None)

    /// Validate parsed field values and extract obj array, or return ParseError
    let internal validateFields
        (cmdName: string)
        (fieldValues: Result<obj option, ParseFieldError> array)
        : Result<obj array, ParseError> =
        let firstError =
            fieldValues
            |> Array.tryPick (fun r ->
                match r with
                | Error e -> Some e
                | Ok _ -> None)

        match firstError with
        | Some fieldErr -> Error(fieldErrorToParseError cmdName fieldErr)
        | None ->
            if
                fieldValues
                |> Array.forall (fun r ->
                    match r with
                    | Ok(Some _) -> true
                    | _ -> false)
            then
                Ok(
                    fieldValues
                    |> Array.map (fun r ->
                        match r with
                        | Ok(Some v) -> v
                        | _ -> failwith "unreachable")
                )
            else
                Error(InvalidArguments(cmdName, "Invalid arguments"))

    /// Pre-computed lookup tables for flag parsing
    type internal FlagLookup =
        { LongMap: Map<string, int>
          ShortMap: Map<string, int>
          ValidFlags: string list }

    /// Build flag lookup tables from FlagInfo (called once at tree construction)
    let internal buildFlagLookup (flagInfo: FlagInfo list) : FlagLookup =
        { LongMap = flagInfo |> List.mapi (fun i fi -> $"--%s{fi.LongName}", i) |> Map.ofList
          ShortMap =
            flagInfo
            |> List.mapi (fun i fi -> i, fi)
            |> List.choose (fun (i, fi) -> fi.ShortName |> Option.map (fun s -> $"-%s{s}", i))
            |> Map.ofList
          ValidFlags = flagInfo |> List.map (fun fi -> $"--%s{fi.LongName}") }

    /// Split argv at the first standalone POSIX `--` end-of-flags separator:
    /// (tokens before it, Some tokens after it) — or (args, None) when absent.
    /// Tokens after the separator are never flag-matched.
    let private splitAtEndOfFlags (args: string array) : string array * string array option =
        match args |> Array.tryFindIndex (fun a -> a = "--") with
        | Some i -> Array.sub args 0 i, Some(Array.sub args (i + 1) (args.Length - i - 1))
        | None -> args, None

    /// Shared flag parsing loop. On unknown arg, calls onUnknown which returns Some error to stop or None to skip.
    let private parseFlagsLoop
        (cmdName: string)
        (cases: UnionCaseInfo array)
        (lookup: FlagLookup)
        (args: string array)
        (onUnknown: string -> int -> ParseError option)
        : Result<System.Collections.Generic.List<obj>, ParseError> =
        let results = System.Collections.Generic.List<obj>()
        let seenTags = System.Collections.Generic.HashSet<int>()
        let mutable i = 0
        let mutable error: ParseError option = None

        while i < args.Length && error.IsNone do
            let arg = args.[i]

            let flagIdx =
                Map.tryFind arg lookup.LongMap
                |> Option.orElseWith (fun () -> Map.tryFind arg lookup.ShortMap)

            match flagIdx with
            | None ->
                match onUnknown arg i with
                | Some e -> error <- Some e
                | None -> i <- i + 1
            | Some idx ->
                if not (seenTags.Add(idx)) then
                    error <- Some(DuplicateFlag(arg, cmdName))
                else
                    let case = cases.[idx]
                    let fields = case.GetFields()

                    if fields.Length = 0 then
                        results.Add(FSharpValue.MakeUnion(case, [||]))
                        i <- i + 1
                    else if i + 1 >= args.Length then
                        error <- Some(InvalidArguments(cmdName, $"Flag '%s{arg}' requires a value"))
                    else
                        let valueStr = args.[i + 1]

                        match parseFieldValue fields.[0].PropertyType valueStr with
                        | Ok(Some v) ->
                            results.Add(FSharpValue.MakeUnion(case, [| v |]))
                            i <- i + 2
                        | Ok None ->
                            error <- Some(InvalidArguments(cmdName, $"Invalid value '%s{valueStr}' for flag '%s{arg}'"))
                        | Error e -> error <- Some(fieldErrorToParseError cmdName e)

        match error with
        | Some e -> Error e
        | None -> Ok results

    /// Resolve env vars for flags not set by CLI, returning additional flag DU values
    let internal resolveEnvVars
        (flagType: Type)
        (flagInfo: FlagInfo list)
        (cliResults: obj seq)
        : Result<obj list, ParseError> =
        let cases = FSharpType.GetUnionCases(flagType)

        let cliTags =
            cliResults
            |> Seq.map (fun r ->
                let c, _ = FSharpValue.GetUnionFields(r, flagType)
                c.Tag)
            |> Set.ofSeq

        let mutable error: ParseError option = None
        let envResults = System.Collections.Generic.List<obj>()

        for i in 0 .. flagInfo.Length - 1 do
            if not (cliTags.Contains(i)) && error.IsNone then
                match flagInfo.[i].EnvVar with
                | Some { VarName = varName } ->
                    let envVal = System.Environment.GetEnvironmentVariable(varName)

                    if not (isNull envVal) && envVal <> "" then
                        let case = cases.[i]
                        let fields = case.GetFields()

                        if fields.Length = 0 then
                            // Bool flag
                            match envVal.ToLowerInvariant() with
                            | "true"
                            | "1" -> envResults.Add(FSharpValue.MakeUnion(case, [||]))
                            | "false"
                            | "0" -> ()
                            | _ ->
                                error <-
                                    Some(
                                        InvalidArguments(
                                            "env",
                                            $"Invalid value '%s{envVal}' for env var '%s{varName}' (expected true/false/1/0)"
                                        )
                                    )
                        else
                            match parseFieldValue fields.[0].PropertyType envVal with
                            | Ok(Some v) -> envResults.Add(FSharpValue.MakeUnion(case, [| v |]))
                            | Ok None ->
                                error <-
                                    Some(
                                        InvalidArguments("env", $"Invalid value '%s{envVal}' for env var '%s{varName}'")
                                    )
                            | Error e ->
                                let baseErr = fieldErrorToParseError "env" e

                                match baseErr with
                                | InvalidArguments(cmd, msg) ->
                                    error <-
                                        Some(InvalidArguments(cmd, $"Invalid value for env var '%s{varName}': %s{msg}"))
                                | other -> error <- Some other
                | None -> ()

        match error with
        | Some e -> Error e
        | None -> Ok(envResults |> Seq.toList)

    /// Render a DU flag list as CLI tokens (e.g., ["--env"; "prod"; "--dry-run"])
    let private renderDUFlagTokens (flagDUType: Type) (flagList: System.Collections.IEnumerable) : string list =
        flagList
        |> Seq.cast<obj>
        |> Seq.collect (fun flagVal ->
            let fc, ffs = FSharpValue.GetUnionFields(flagVal, flagDUType)
            let flagName = $"--%s{toKebabCase fc.Name}"

            if ffs.Length = 0 then
                seq { flagName }
            else
                seq {
                    flagName
                    formatFieldValue ffs.[0]
                })
        |> Seq.toList

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

                    if isUnionType ft then
                        go v ft
                    elif isFlagDUList ft then
                        let elemType = listElementType ft

                        renderDUFlagTokens elemType (v :?> System.Collections.IEnumerable)
                        |> String.concat " "
                    else
                        formatFieldValue v)
                |> Array.filter (fun s -> s <> "")

            if parts.Length = 0 then
                n
            else
                n + " " + String.concat " " parts

        go (cmd :> obj) typeof<'Cmd>

    /// Walk nested unions to find a matching case, then apply a projection to extract formatted args
    let private findMatchingCase
        (outerCase: UnionCaseInfo)
        (project: obj array -> string list)
        (cmd: 'Cmd)
        : string list option =
        let rec go (value: obj) =
            if isNull value then
                None
            else
                let actualType = value.GetType()

                if FSharpType.IsUnion(actualType, true) then
                    let c, fs = FSharpValue.GetUnionFields(value, actualType, true)

                    if c.Tag = outerCase.Tag && c.DeclaringType = outerCase.DeclaringType then
                        Some(project fs)
                    else
                        fs |> Array.tryPick go
                else
                    None

        go (cmd :> obj)

    /// Reject extra arguments beyond expected count, returning UnknownFlag for flag-like args
    let private rejectExtraArg (cmdName: string) (extra: string) : ParseError =
        if extra.StartsWith("-", StringComparison.Ordinal) then
            UnknownFlag(extra, cmdName, [])
        else
            InvalidArguments(cmdName, $"Unexpected argument '%s{extra}'")

    /// Collect a SpecError for every positional field whose type the parser
    /// cannot handle. Errors are appended to the shared accumulator rather than
    /// thrown, so the construction walk can report ALL shape problems at once.
    /// Without this validation an unsupported type silently parses to nothing and
    /// surfaces only as a generic "Invalid arguments" error at use time.
    let private validateFieldTypes
        (errors: ResizeArray<SpecError>)
        (cmdName: string)
        (fields: (string * Type) seq)
        : unit =
        fields
        |> Seq.iter (fun (fieldName, fieldType) ->
            if not (isSupportedFieldType fieldType) then
                errors.Add(SpecError.UnsupportedFieldType(cmdName, fieldName, fieldType)))

    /// Internal: build a CommandTree from a union type, accumulating every
    /// construction-time shape error into <paramref name="errors"/> instead of
    /// throwing on the first one. The returned tree is well-formed only when the
    /// accumulator is empty; callers gate on that.
    let private buildUnionTree<'Cmd>
        (errors: ResizeArray<SpecError>)
        (envPrefix: string option)
        (rootDesc: string)
        : CommandTree<'Cmd> =
        let cmdType = typeof<'Cmd>
        let cases = FSharpType.GetUnionCases(cmdType)

        let rec processCase (outerCase: UnionCaseInfo) (wrapValue: obj -> obj) : CommandTree<'Cmd> =
            let cmdName = getCommandName outerCase
            let desc = getDescription outerCase
            let fields = outerCase.GetFields()

            let listFieldIndices =
                fields
                |> Array.mapi (fun i f -> i, f)
                |> Array.filter (fun (_, f) -> isListType f.PropertyType)
                |> Array.map fst

            if listFieldIndices.Length > 0 then
                let lastIdx = fields.Length - 1

                if listFieldIndices.Length > 1 then
                    errors.Add(SpecError.MultipleListFields cmdName)
                elif listFieldIndices.[0] <> lastIdx then
                    let badField = fields.[listFieldIndices.[0]].Name
                    errors.Add(SpecError.ListFieldNotLast(cmdName, badField))

            if fields.Length > 0 && isFlagDUList fields.[fields.Length - 1].PropertyType then
                // Flag-DU leaf: zero or more leading positionals + a trailing `SomeDU list`
                // field parsed as named --flags. Flags may appear anywhere relative to the
                // positionals; tokens after a standalone `--` always bind as positionals.
                let positionalFields = Array.sub fields 0 (fields.Length - 1)

                validateFieldTypes errors cmdName (positionalFields |> Seq.map (fun f -> f.Name, f.PropertyType))

                let flagDUType = listElementType fields.[fields.Length - 1].PropertyType
                let flagInfo = getFlagInfoFromDU flagDUType envPrefix
                let flagLookup = buildFlagLookup flagInfo
                let flagCases = FSharpType.GetUnionCases(flagDUType)

                let parse (args: string array) : Result<'Cmd, ParseError> =
                    let scanArgs, afterSeparator = splitAtEndOfFlags args
                    let positionalTokens = ResizeArray<string>()

                    let onUnknown (arg: string) (_index: int) =
                        // With no positional slots every unknown token is a bad flag; with
                        // slots, only `-`-prefixed tokens are (a bare `-` stays a
                        // positional, by stdin convention).
                        if
                            positionalFields.Length = 0
                            || (arg.StartsWith("-", StringComparison.Ordinal) && arg <> "-")
                        then
                            Some(UnknownFlag(arg, cmdName, flagLookup.ValidFlags))
                        else
                            positionalTokens.Add(arg)
                            None

                    match parseFlagsLoop cmdName flagCases flagLookup scanArgs onUnknown with
                    | Error e -> Error e
                    | Ok cliFlags ->
                        positionalTokens.AddRange(defaultArg afterSeparator [||])
                        let positionals = positionalTokens.ToArray()

                        if positionals.Length > positionalFields.Length then
                            // Dash-prefixed extras can only arrive here via `--` (before it
                            // they already errored as unknown flags), so an extra token is
                            // always an unexpected *argument*, never an unknown flag.
                            let extra = positionals.[positionalFields.Length]
                            Error(InvalidArguments(cmdName, $"Unexpected argument '%s{extra}'"))
                        else
                            let fieldValues =
                                positionalFields
                                |> Array.mapi (fun i pf ->
                                    if i < positionals.Length then
                                        parseFieldValue pf.PropertyType positionals.[i]
                                    elif isOptionalType pf.PropertyType then
                                        Ok(Some(makeNone pf.PropertyType))
                                    else
                                        Error(InvalidValue $"Missing required argument '<%s{toKebabCase pf.Name}>'"))

                            match validateFields cmdName fieldValues with
                            | Error e -> Error e
                            | Ok values ->
                                let cliItems = cliFlags |> Seq.cast<obj>

                                match resolveEnvVars flagDUType flagInfo cliItems with
                                | Error e -> Error e
                                | Ok envItems ->
                                    let mergedList = buildTypedList flagDUType (Seq.append cliItems envItems)

                                    let cmdValue =
                                        wrapValue (
                                            FSharpValue.MakeUnion(outerCase, Array.append values [| mergedList |])
                                        )

                                    Ok(cmdValue :?> 'Cmd)

                let formatArgs =
                    findMatchingCase outerCase (fun fs ->
                        let positionalParts =
                            Array.sub fs 0 (fs.Length - 1)
                            |> Array.map formatFieldValue
                            |> Array.filter (fun s -> s <> "")
                            |> Array.toList

                        let flagParts =
                            renderDUFlagTokens flagDUType (fs.[fs.Length - 1] :?> System.Collections.IEnumerable)

                        positionalParts @ flagParts)

                CommandTree.Leaf
                    { Name = cmdName
                      Description = desc
                      Args = getArgInfo outerCase positionalFields
                      Flags = flagInfo
                      Examples = getCmdExamples outerCase
                      Parse = parse
                      FormatArgs = formatArgs }
            elif fields.Length = 1 && isUnionType fields.[0].PropertyType then
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

                let defaultCmd =
                    defaultCase
                    |> Option.map (fun dc ->
                        let dcName = getCommandName dc

                        { ChildName = dcName
                          Parse =
                            fun (args: string array) ->
                                let nestedFields = dc.GetFields()
                                let fieldValues = parseFields nestedFields args

                                match validateFields dcName fieldValues with
                                | Ok values ->
                                    let nestedValue = FSharpValue.MakeUnion(dc, values)
                                    let cmdValue = wrapValue (FSharpValue.MakeUnion(outerCase, [| nestedValue |]))
                                    Ok(cmdValue :?> 'Cmd)
                                | Error e -> Error e })

                CommandTree.Group
                    { Name = cmdName
                      Description = desc
                      Children = nestedChildren
                      Default = defaultCmd }
            elif fields.Length = 1 && FSharpType.IsRecord(fields.[0].PropertyType) then
                // Record-typed argument: expand record fields as positional args with defaults
                let recordType = fields.[0].PropertyType
                let recordFields = FSharpType.GetRecordFields(recordType)

                validateFieldTypes errors cmdName (recordFields |> Seq.map (fun f -> f.Name, f.PropertyType))

                // Pre-compute default values for missing fields (avoids reflection at parse time)
                let recordDefaults =
                    recordFields
                    |> Array.map (fun rf ->
                        if rf.PropertyType = typeof<bool> then
                            Some(Ok(Some(box false)))
                        elif isOptionalType rf.PropertyType then
                            Some(Ok(Some(makeNone rf.PropertyType)))
                        else
                            None)

                let parse (args: string array) : Result<'Cmd, ParseError> =
                    if args.Length > recordFields.Length then
                        Error(rejectExtraArg cmdName args.[recordFields.Length])
                    else
                        let fieldValues =
                            recordFields
                            |> Array.mapi (fun i rf ->
                                if i < args.Length then
                                    parseFieldValue rf.PropertyType args.[i]
                                else
                                    match recordDefaults.[i] with
                                    | Some v -> v
                                    | None -> Ok None)

                        match validateFields cmdName fieldValues with
                        | Ok values ->
                            let recordValue = FSharpValue.MakeRecord(recordType, values)
                            let innerValue = FSharpValue.MakeUnion(outerCase, [| recordValue |])
                            let cmdValue = wrapValue innerValue
                            Ok(cmdValue :?> 'Cmd)
                        | Error e -> Error e

                let formatArgs =
                    findMatchingCase outerCase (fun fs ->
                        let rFields = FSharpValue.GetRecordFields(fs.[0])

                        rFields
                        |> Array.map formatFieldValue
                        |> Array.filter (fun s -> s <> "")
                        |> Array.toList)

                let argInfo =
                    recordFields
                    |> Array.map (fun f ->
                        let cmdArgAttr = getCmdArgAttrFromField f

                        { Name = toKebabCase f.Name
                          TypeName = getTypeName f.PropertyType
                          IsOptional = isOptionalType f.PropertyType || f.PropertyType = typeof<bool>
                          IsList = false
                          Completions = autoDetectCompletion f
                          Description = cmdArgAttr |> Option.bind (fun a -> Option.ofObj a.Description)
                          Default = cmdArgAttr |> Option.bind (fun a -> Option.ofObj a.Default) })
                    |> Array.toList

                CommandTree.Leaf
                    { Name = cmdName
                      Description = desc
                      Args = argInfo
                      Flags = []
                      Examples = getCmdExamples outerCase
                      Parse = parse
                      FormatArgs = formatArgs }
            else
                // Leaf case
                validateFieldTypes errors cmdName (fields |> Seq.map (fun f -> f.Name, f.PropertyType))
                let hasListField = fields |> Array.exists (fun f -> isListType f.PropertyType)

                let parse (args: string array) : Result<'Cmd, ParseError> =
                    if not hasListField && args.Length > fields.Length then
                        Error(rejectExtraArg cmdName args.[fields.Length])
                    else
                        let fieldValues = parseFields fields args

                        match validateFields cmdName fieldValues with
                        | Ok values ->
                            let innerValue = FSharpValue.MakeUnion(outerCase, values)
                            let cmdValue = wrapValue innerValue
                            Ok(cmdValue :?> 'Cmd)
                        | Error e -> Error e

                let formatArgs =
                    findMatchingCase outerCase (fun fs ->
                        fs
                        |> Array.map formatFieldValue
                        |> Array.filter (fun s -> s <> "")
                        |> Array.toList)

                let argInfo = getArgInfo outerCase fields

                CommandTree.Leaf
                    { Name = cmdName
                      Description = desc
                      Args = argInfo
                      Flags = []
                      Examples = getCmdExamples outerCase
                      Parse = parse
                      FormatArgs = formatArgs }

        let children = cases |> Array.map (fun case -> processCase case id) |> Array.toList

        // Check for default at root level
        let rootDefault = cases |> Array.tryFind isDefault

        let defaultCmd =
            rootDefault
            |> Option.map (fun defaultCase ->
                let defaultName = getCommandName defaultCase

                { ChildName = defaultName
                  Parse =
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

                                match validateFields defaultName fieldValues with
                                | Ok values ->
                                    let nestedValue = FSharpValue.MakeUnion(nestedDefault, values)
                                    let cmdValue = FSharpValue.MakeUnion(defaultCase, [| nestedValue |])
                                    Ok(cmdValue :?> 'Cmd)
                                | Error e -> Error e
                            | None -> Error(InvalidArguments(defaultName, "No default command in nested group"))
                        else
                            Error(InvalidArguments(defaultName, "Default command requires no arguments")) })

        CommandTree.Group
            { Name = ""
              Description = rootDesc
              Children = children
              Default = defaultCmd }

    /// Internal: build the tree and surface any accumulated shape errors as a
    /// Result. Single source of truth for the public try-variants below.
    let private fromUnionInternal<'Cmd>
        (envPrefix: string option)
        (rootDesc: string)
        : Result<CommandTree<'Cmd>, SpecError list> =
        let errors = ResizeArray<SpecError>()
        let tree = buildUnionTree<'Cmd> errors envPrefix rootDesc

        if errors.Count = 0 then
            Ok tree
        else
            Error(List.ofSeq errors)

    /// Try to generate a CommandTree from a union type, returning every
    /// construction-time shape problem as a value instead of throwing.
    let tryFromUnion<'Cmd> (rootDesc: string) : Result<CommandTree<'Cmd>, SpecError list> =
        fromUnionInternal<'Cmd> None rootDesc

    /// Try to generate a CommandTree with env var support for DU-based flags,
    /// returning every construction-time shape problem as a value.
    let tryFromUnionWithEnv<'Cmd> (rootDesc: string) (envPrefix: string) : Result<CommandTree<'Cmd>, SpecError list> =
        fromUnionInternal<'Cmd> (Some envPrefix) rootDesc

    /// Generate a CommandTree from a union type. Throws
    /// <see cref="T:System.InvalidOperationException"/> reporting ALL shape
    /// problems if the DU is malformed; see <c>tryFromUnion</c> for the
    /// non-throwing variant.
    let fromUnion<'Cmd> (rootDesc: string) : CommandTree<'Cmd> =
        tryFromUnion<'Cmd> rootDesc
        |> Result.defaultWith (SpecError.formatAll >> invalidOp)

    /// Generate a CommandTree with env var support for DU-based flags. Throws
    /// <see cref="T:System.InvalidOperationException"/> reporting ALL shape
    /// problems if the DU is malformed; see <c>tryFromUnionWithEnv</c> for the
    /// non-throwing variant.
    let fromUnionWithEnv<'Cmd> (rootDesc: string) (envPrefix: string) : CommandTree<'Cmd> =
        tryFromUnionWithEnv<'Cmd> rootDesc envPrefix
        |> Result.defaultWith (SpecError.formatAll >> invalidOp)

    /// Collect flag-name collisions between global and command flags into the
    /// shared accumulator (long names then short names, per command, in tree
    /// order) instead of throwing on the first collision.
    let private collectFlagCollisions
        (errors: ResizeArray<SpecError>)
        (globalFlagInfo: FlagInfo list)
        (tree: CommandTree<'Cmd>)
        : unit =
        let globalFlagNames =
            globalFlagInfo
            |> List.collect (fun fi ->
                $"--%s{fi.LongName}"
                :: (fi.ShortName |> Option.map (fun s -> $"-%s{s}") |> Option.toList))
            |> Set.ofList

        let rec check (node: CommandTree<'Cmd>) : unit =
            match node with
            | Leaf leaf ->
                for fi in leaf.Flags do
                    let long = $"--%s{fi.LongName}"

                    if globalFlagNames.Contains(long) then
                        errors.Add(SpecError.GlobalFlagCollision(long, leaf.Name))

                    match fi.ShortName with
                    | Some s ->
                        let short = $"-%s{s}"

                        if globalFlagNames.Contains(short) then
                            errors.Add(SpecError.GlobalShortFlagCollision(short, leaf.Name))
                    | None -> ()
            | Group group -> group.Children |> List.iter check

        check tree

    /// Scan args for global flags, separating them from command args. Scanning
    /// stops at a standalone `--`: the separator and everything after it are
    /// forwarded to the command untouched (the leaf applies its own `--` rule).
    let private scanGlobalFlags
        (globalType: Type)
        (globalLookup: FlagLookup)
        (args: string array)
        : Result<System.Collections.Generic.List<obj> * string array, ParseError> =
        let globalCases = FSharpType.GetUnionCases(globalType)
        let scanArgs, afterSeparator = splitAtEndOfFlags args
        let commandArgs = System.Collections.Generic.List<string>()

        let onUnknown (arg: string) (_index: int) =
            commandArgs.Add(arg)
            None

        parseFlagsLoop "global" globalCases globalLookup scanArgs onUnknown
        |> Result.map (fun globalResults ->
            match afterSeparator with
            | Some after ->
                commandArgs.Add("--")
                commandArgs.AddRange(after)
            | None -> ()

            (globalResults, commandArgs.ToArray()))

    /// Internal: build a GlobalSpec, accumulating every construction-time shape
    /// error (command-tree field/list errors first, in DU declaration order,
    /// then global/command flag collisions in tree order) into one Result.
    let private fromUnionWithGlobalsInternal<'Cmd, 'Globals>
        (envPrefix: string option)
        (rootDesc: string)
        : Result<GlobalSpec<'Globals, 'Cmd>, SpecError list> =
        let errors = ResizeArray<SpecError>()
        let tree = buildUnionTree<'Cmd> errors envPrefix rootDesc
        let globalType = typeof<'Globals>
        let globalFlagInfo = getFlagInfoFromDU globalType envPrefix
        let globalLookup = buildFlagLookup globalFlagInfo

        collectFlagCollisions errors globalFlagInfo tree

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else

            let parse (args: string array) : Result<'Globals list * 'Cmd, ParseError> =
                match scanGlobalFlags globalType globalLookup args with
                | Error e -> Error e
                | Ok(globalResults, cmdArgs) ->
                    let cliItems = globalResults |> Seq.cast<obj>

                    let globalsResult =
                        match envPrefix with
                        | Some _ ->
                            match resolveEnvVars globalType globalFlagInfo cliItems with
                            | Error e -> Error e
                            | Ok envItems ->
                                let allItems = Seq.append cliItems envItems
                                Ok(buildTypedList globalType allItems :?> 'Globals list)
                        | None -> Ok(buildTypedList globalType cliItems :?> 'Globals list)

                    match globalsResult with
                    | Error e -> Error e
                    | Ok globals ->
                        match CommandTree.parse tree cmdArgs with
                        | Ok cmd -> Ok(globals, cmd)
                        | Error e -> Error e

            Ok
                { Tree = tree
                  Parse = parse
                  GlobalFlags = globalFlagInfo }

    /// Try to generate a GlobalSpec with global flags, returning every
    /// construction-time shape problem (including global/command flag
    /// collisions) as a value instead of throwing.
    let tryFromUnionWithGlobals<'Cmd, 'Globals>
        (rootDesc: string)
        : Result<GlobalSpec<'Globals, 'Cmd>, SpecError list> =
        fromUnionWithGlobalsInternal<'Cmd, 'Globals> None rootDesc

    /// Try to generate a GlobalSpec with global flags and env var support,
    /// returning every construction-time shape problem as a value.
    let tryFromUnionWithGlobalsAndEnv<'Cmd, 'Globals>
        (rootDesc: string)
        (envPrefix: string)
        : Result<GlobalSpec<'Globals, 'Cmd>, SpecError list> =
        fromUnionWithGlobalsInternal<'Cmd, 'Globals> (Some envPrefix) rootDesc

    /// Generate a GlobalSpec with global flags from union types. Throws
    /// <see cref="T:System.InvalidOperationException"/> reporting ALL shape
    /// problems (including global/command flag collisions) if the spec is
    /// malformed; see <c>tryFromUnionWithGlobals</c> for the non-throwing variant.
    let fromUnionWithGlobals<'Cmd, 'Globals> (rootDesc: string) : GlobalSpec<'Globals, 'Cmd> =
        tryFromUnionWithGlobals<'Cmd, 'Globals> rootDesc
        |> Result.defaultWith (SpecError.formatAll >> invalidOp)

    /// Generate a GlobalSpec with global flags and env var support. Throws
    /// <see cref="T:System.InvalidOperationException"/> reporting ALL shape
    /// problems (including global/command flag collisions) if the spec is
    /// malformed; see <c>tryFromUnionWithGlobalsAndEnv</c> for the non-throwing
    /// variant.
    let fromUnionWithGlobalsAndEnv<'Cmd, 'Globals> (rootDesc: string) (envPrefix: string) : GlobalSpec<'Globals, 'Cmd> =
        tryFromUnionWithGlobalsAndEnv<'Cmd, 'Globals> rootDesc envPrefix
        |> Result.defaultWith (SpecError.formatAll >> invalidOp)

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
