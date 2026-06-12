/// Opt-in FSharp.Analyzers.SDK analyzer that flags CommandTree command-DU shape errors
/// at edit/build time, before the program ever runs.
///
/// CommandTree derives a CLI from a command discriminated union via reflection
/// (`CommandReflection.fromUnion*`). Some DU shapes are rejected at construction time by
/// `CommandTree.Reflection` with an `invalidOp` — a correct fail-fast, but the failure is
/// invisible in the type signature and only surfaces at runtime startup. This analyzer
/// surfaces the same shape errors as editor squiggles / build warnings.
///
/// Ground truth (mirrored in FCS-symbol terms, NOT referenced — A and B live in different
/// type universes, System.Type vs FSharpType):
///   - CT001 mirrors `CommandTree.Reflection.isSupportedFieldType` + `supportedScalarTypes`
///     and its `validateFieldTypes` call sites (leaf-case fields, arg-group record fields).
///   - CT002 mirrors the list-field placement check in `CommandTree.Reflection.processCase`
///     ("List field … must be the last field and there can be only one").
/// The recursion shape (single nested-union field => subcommand group; single record field
/// => arg group; single `SomeDU list` field => flag DU) mirrors `processCase`'s branching.
module CommandTree.Analyzers.CommandTreeAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text

/// Stable analyzer name.
[<Literal>]
let Name = "CommandTree.SpecShape"

/// CT001: a command-DU field whose type the runtime parse machinery cannot handle.
[<Literal>]
let UnsupportedFieldTypeCode = "CT001"

/// CT002: a list field placed before the last position, or more than one list field
/// in a single case.
[<Literal>]
let ListFieldPlacementCode = "CT002"

/// Human-readable list of supported field types — mirrors
/// `CommandTree.Reflection.supportedTypesDescription`.
[<Literal>]
let SupportedTypesDescription =
    "string, int, int64, bool, float, decimal, Guid, a discriminated union, \
     an option of any of these, or a list of any of these"

/// FullName prefixes of the constructor family that takes a command DU as `'Cmd`
/// (and optionally `'Globals`). Matches `CommandTree.CommandReflection.fromUnion*`.
let private constructorFullNames =
    set
        [ "CommandTree.CommandReflection.fromUnion"
          "CommandTree.CommandReflection.fromUnionWithEnv"
          "CommandTree.CommandReflection.fromUnionWithGlobals"
          "CommandTree.CommandReflection.fromUnionWithGlobalsAndEnv" ]

/// A shape problem found on a specific field, carried with the field's declaration range.
type private Finding =
    { Code: string
      Message: string
      Range: range }

/// Strip type abbreviations to the underlying type, mirroring how reflection sees the
/// runtime `System.Type` (abbreviations vanish at runtime).
let rec private strip (t: FSharpType) : FSharpType =
    if t.IsAbbreviation then strip t.AbbreviatedType else t

/// True when the stripped type is `'a option`.
let private isOption (t: FSharpType) =
    let t = strip t

    t.HasTypeDefinition
    && t.TypeDefinition.TryFullName = Some "Microsoft.FSharp.Core.FSharpOption`1"

/// True when the stripped type is `'a list`.
let private isList (t: FSharpType) =
    let t = strip t

    t.HasTypeDefinition
    && t.TypeDefinition.TryFullName = Some "Microsoft.FSharp.Collections.FSharpList`1"

/// True when the stripped type is an F# discriminated union (excluding option/list, which
/// are unions but handled specially). Mirrors `CommandTree.Reflection.isUnionType`
/// (which also excludes string; string is not a union so no special-case needed here).
let private isUnion (t: FSharpType) =
    let t = strip t
    t.HasTypeDefinition && t.TypeDefinition.IsFSharpUnion && not (isOption t) && not (isList t)

/// True when the stripped type is an F# record. Single record-typed fields become arg
/// groups (`processCase`'s record branch), so this drives the recurse-into-record decision.
let private isRecord (t: FSharpType) =
    let t = strip t
    t.HasTypeDefinition && t.TypeDefinition.IsFSharpRecord

/// Full names of the scalar types the runtime accepts — mirrors
/// `CommandTree.Reflection.supportedScalarTypes`.
let private supportedScalarFullNames =
    set
        [ "System.String"
          "System.Int32"
          "System.Int64"
          "System.Boolean"
          "System.Guid"
          "System.Double" // float
          "System.Decimal" ]

/// Mirror of `CommandTree.Reflection.isSupportedFieldType`: scalars, option-of-supported,
/// list-of-supported, or any (non-option/list) union. NOT defined for records — records
/// are handled by the arg-group branch, which validates their *fields* individually.
let rec private isSupportedFieldType (t: FSharpType) : bool =
    let t = strip t

    if t.HasTypeDefinition && supportedScalarFullNames.Contains(t.TypeDefinition.TryFullName |> Option.defaultValue "") then
        true
    elif isOption t || isList t then
        // Single generic argument: option<'a> / list<'a>.
        match t.GenericArguments |> Seq.tryHead with
        | Some inner -> isSupportedFieldType inner
        | None -> false
    elif isUnion t then
        true
    else
        false

/// A readable name for a field type in CT001 messages — mirrors the runtime message's
/// `fieldType.Name` (the unqualified type name), with option/list suffixes for clarity.
let rec private typeDisplayName (t: FSharpType) : string =
    let t = strip t

    if isOption t then
        match t.GenericArguments |> Seq.tryHead with
        | Some inner -> typeDisplayName inner
        | None -> "option"
    elif isList t then
        match t.GenericArguments |> Seq.tryHead with
        | Some inner -> typeDisplayName inner + " list"
        | None -> "list"
    elif t.HasTypeDefinition then
        t.TypeDefinition.DisplayName
    else
        t.Format(FSharpDisplayContext.Empty)

/// Best-effort field declaration range; falls back to a zero range if FCS has none.
let private fieldRange (f: FSharpField) : range =
    f.DeclarationLocation

/// Validate the field types of a "command" (a leaf case or an arg-group record) for CT001,
/// mirroring `validateFieldTypes`. `cmdName` is the case/command display name for the message.
let private validateFieldTypesFor (cmdName: string) (fields: FSharpField seq) : Finding list =
    fields
    |> Seq.choose (fun f ->
        if isSupportedFieldType f.FieldType then
            None
        else
            Some
                { Code = UnsupportedFieldTypeCode
                  Message =
                    $"Field '%s{f.DisplayName}' of command '%s{cmdName}' has unsupported type "
                    + $"'%s{typeDisplayName f.FieldType}'. Supported types: %s{SupportedTypesDescription}."
                  Range = fieldRange f })
    |> List.ofSeq

/// CT002: list-field placement on a leaf case's fields — mirrors the placement check in
/// `processCase` ("List field … must be the last field and there can be only one").
let private validateListPlacement (cmdName: string) (fields: FSharpField array) : Finding list =
    let listIndices =
        fields
        |> Array.mapi (fun i f -> i, f)
        |> Array.filter (fun (_, f) -> isList f.FieldType)

    if listIndices.Length = 0 then
        []
    else
        let lastIdx = fields.Length - 1
        let firstListIdx, firstListField = listIndices.[0]

        if listIndices.Length > 1 || firstListIdx <> lastIdx then
            // Report on the first offending list field (the one not in last position, or the
            // first of several). One diagnostic per case, matching the single runtime throw.
            [ { Code = ListFieldPlacementCode
                Message =
                    $"List field in case '%s{cmdName}' must be the last field and there can be only one."
                Range = fieldRange firstListField } ]
        else
            []

/// Display name of a union case for messages — mirrors `getCommandName`'s base
/// (the case name; attribute-driven overrides only change the kebab CLI name, not the
/// message identity the runtime prints, which uses the same source case name).
let private caseDisplayName (c: FSharpUnionCase) = c.Name

/// Recursively analyze a command DU entity exactly as `processCase` walks it:
///   - a case with a single nested-union field => subcommand group: recurse into the union
///   - a case with a single `SomeDU list` field => flag DU: VALID, no field validation
///   - a case with a single record field => arg group: validate the record's fields (CT001)
///   - any other case => leaf: validate its fields (CT001) + list placement (CT002)
/// `visited` guards against infinite recursion on recursive DUs.
let rec private analyzeCommandUnion (visited: Set<string>) (unionEntity: FSharpEntity) : Finding list =
    let key = unionEntity.TryFullName |> Option.defaultValue unionEntity.DisplayName

    if visited.Contains key then
        []
    else
        let visited = visited.Add key

        unionEntity.UnionCases
        |> Seq.collect (fun case ->
            let cmdName = caseDisplayName case
            let fields = case.Fields |> Array.ofSeq

            match fields with
            | [| single |] when isList single.FieldType && isUnion (listElementType single.FieldType) ->
                // `SomeDU list` single field => DU flag list. Valid, nothing to validate.
                []
            | [| single |] when isUnion single.FieldType ->
                // Nested union => subcommand group. Recurse into the nested union.
                match (strip single.FieldType).TypeDefinition with
                | nested -> analyzeCommandUnion visited nested
            | [| single |] when isRecord single.FieldType ->
                // Record-typed arg group => validate the record's own fields.
                let recordEntity = (strip single.FieldType).TypeDefinition
                validateFieldTypesFor cmdName recordEntity.FSharpFields
            | _ ->
                // Leaf case => validate field types (CT001) and list placement (CT002).
                validateFieldTypesFor cmdName fields @ validateListPlacement cmdName fields)
        |> List.ofSeq

/// The element type of a `'a list` (already known to be a list). Mirrors `listElementType`.
and private listElementType (t: FSharpType) : FSharpType =
    let t = strip t

    match t.GenericArguments |> Seq.tryHead with
    | Some inner -> inner
    | None -> t

/// The command/globals DU entities passed to a `fromUnion*` call, recovered from the call's
/// member type-arguments. `'Cmd` (and `'Globals`, for the globals variants) both go through
/// the same parse machinery, so every generic argument that resolves to a union is returned.
///
/// Detection surface: a hand-rolled walk of the typed tree's `FSharpExpr.Call` nodes, NOT the
/// SDK's `TASTCollecting.walkTast`. The SDK 0.36.0 walk is compiled against FCS 43.10.101 and
/// calls `FSharpType.BasicQualifiedName`, removed in this repo's FCS 43.12 — it throws
/// `MissingMethodException` at load time. This walk is compiled against this repo's FCS, so it
/// is the necessary approach until the SDK ships an FCS-43.12-aligned build (same reasoning as
/// TestPrune.Analyzers' hand-rolled untyped walk).
let private unionsFromCall (mfv: FSharpMemberOrFunctionOrValue) (memberTypeArgs: FSharpType list) : FSharpEntity list =
    let fullName =
        try
            Some mfv.FullName
        with _ ->
            None

    match fullName with
    | Some fn when constructorFullNames.Contains fn ->
        memberTypeArgs
        |> List.choose (fun ty ->
            let stripped = strip ty

            if stripped.HasTypeDefinition && stripped.TypeDefinition.IsFSharpUnion then
                Some stripped.TypeDefinition
            else
                None)
    | _ -> []

/// Walk every `FSharpExpr` reachable from an implementation file, collecting the command DU
/// entities at `fromUnion*` call sites. Recursion is generic via `ImmediateSubExpressions`,
/// so no per-expression-shape enumeration is needed (and nothing drifts as FCS adds nodes).
let private collectCommandUnions (typedTree: FSharpImplementationFileContents) : FSharpEntity list =
    let entities = ResizeArray<FSharpEntity>()
    let seen = System.Collections.Generic.HashSet<string>()

    let add (ent: FSharpEntity) =
        let key = ent.TryFullName |> Option.defaultValue ent.DisplayName

        if seen.Add key then
            entities.Add ent

    let rec walkExpr (expr: FSharpExpr) =
        match expr with
        | FSharpExprPatterns.Call(_objExprOpt, mfv, _objTypeArgs, memberTypeArgs, _argExprs) ->
            unionsFromCall mfv memberTypeArgs |> List.iter add
        | _ -> ()

        for sub in expr.ImmediateSubExpressions do
            walkExpr sub

    let rec walkDecl (decl: FSharpImplementationFileDeclaration) =
        match decl with
        | FSharpImplementationFileDeclaration.Entity(_, subDecls) -> List.iter walkDecl subDecls
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(_, _, body) -> walkExpr body
        | FSharpImplementationFileDeclaration.InitAction expr -> walkExpr expr

    List.iter walkDecl typedTree.Declarations
    List.ofSeq entities

/// Build SDK messages from findings. Severity is `Warning`: these are real shape bugs that
/// will crash the program at startup, but the analyzer is opt-in and must never break a
/// build by itself — a warning gives the IDE squiggle / `fshw check` surface the design wants.
let private toMessages (findings: Finding list) : Message list =
    findings
    |> List.map (fun f ->
        { Type = Name
          Message = f.Message
          Code = f.Code
          Severity = Severity.Warning
          Range = f.Range
          Fixes = [] })

/// Analyze a typed implementation file: find every `fromUnion*` call, recover its command
/// DU(s), and validate their shape. De-duplicates findings by (code, range, message) so a
/// DU constructed at several call sites is reported once.
let analyzeTypedTree (typedTree: FSharpImplementationFileContents) : Message list =
    collectCommandUnions typedTree
    |> List.collect (analyzeCommandUnion Set.empty)
    |> List.distinctBy (fun f -> f.Code, f.Range, f.Message)
    |> toMessages

/// Analyzer entry point. Requires the typed tree (recovers `'Cmd` from call-site generic
/// instantiation), so it is a CLI/editor analyzer with full type-check information.
[<CliAnalyzer(Name, "Flags CommandTree command-DU shape errors (unsupported field types, list-field placement)")>]
let commandTreeAnalyzer: Analyzer<CliContext> =
    fun (context: CliContext) ->
        async {
            match context.TypedTree with
            | Some typedTree -> return analyzeTypedTree typedTree
            | None -> return []
        }
