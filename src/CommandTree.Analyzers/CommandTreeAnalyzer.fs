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
/// => arg group; trailing `SomeDU list` field => flag DU, optionally after positionals)
/// mirrors `processCase`'s branching.
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

/// Qualified names of the constructor family that takes a command DU as `'Cmd` (and
/// optionally `'Globals`) — `<declaring-entity>.<compiled-name>` form, matched against the
/// key built in `unionsFromCall`. These are `CommandTree.CommandReflection.fromUnion*`.
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

/// A stable, total identity for an entity, used both to compare against the supported scalar
/// set and to de-duplicate / guard recursion on command DUs. `AccessPath` + `CompiledName`
/// are non-optional strings (unlike `TryFullName`, an option, or `FullName`, which throws for
/// some symbols), so the key is total — e.g. `System.String`, `System.Double`.
let private entityKey (e: FSharpEntity) : string = e.AccessPath + "." + e.CompiledName

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

    t.HasTypeDefinition
    && t.TypeDefinition.IsFSharpUnion
    && not (isOption t)
    && not (isList t)

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

/// The single generic argument of an `'a option` / `'a list` (mirrors
/// `listElementType` / `t.GetGenericArguments().[0]` in reflection). The caller has already
/// established `t` is option/list, and a checked option/list type always carries exactly one
/// generic argument, so direct indexing is total here.
let private innerArg (t: FSharpType) : FSharpType = (strip t).GenericArguments.[0]

/// Mirror of `CommandTree.Reflection.isSupportedFieldType`: scalars, option-of-supported,
/// list-of-supported, or any (non-option/list) union. NOT defined for records — records
/// are handled by the arg-group branch, which validates their *fields* individually.
let rec private isSupportedFieldType (t: FSharpType) : bool =
    let t = strip t

    if
        t.HasTypeDefinition
        && supportedScalarFullNames.Contains(entityKey t.TypeDefinition)
    then
        true
    elif isOption t || isList t then
        isSupportedFieldType (innerArg t)
    elif isUnion t then
        true
    else
        false

/// A readable name for a field type in CT001 messages — mirrors the runtime message's
/// `fieldType.Name` (the unqualified type name), with option/list suffixes for clarity.
let rec private typeDisplayName (t: FSharpType) : string =
    let t = strip t

    if isOption t then typeDisplayName (innerArg t)
    elif isList t then typeDisplayName (innerArg t) + " list"
    elif t.HasTypeDefinition then t.TypeDefinition.DisplayName
    else t.Format(FSharpDisplayContext.Empty)

/// Best-effort field declaration range; falls back to a zero range if FCS has none.
let private fieldRange (f: FSharpField) : range = f.DeclarationLocation

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
                Message = $"List field in case '%s{cmdName}' must be the last field and there can be only one."
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
///     (a trailing flag DU list *after* positionals goes through the leaf arm below —
///     both its positionals and the flag list itself pass CT001, and CT002's
///     list-placement rule applies unchanged)
///   - a case with a single record field => arg group: validate the record's fields (CT001)
///   - any other case => leaf: validate its fields (CT001) + list placement (CT002)
/// `visited` guards against infinite recursion on recursive DUs.
let rec private analyzeCommandUnion (visited: Set<string>) (unionEntity: FSharpEntity) : Finding list =
    let key = entityKey unionEntity

    if visited.Contains key then
        []
    else
        let visited = visited.Add key

        unionEntity.UnionCases |> Seq.collect (analyzeCase visited) |> List.ofSeq

/// Classify one union case the way `processCase` does and produce its findings.
and private analyzeCase (visited: Set<string>) (case: FSharpUnionCase) : Finding list =
    let cmdName = caseDisplayName case
    let fields = case.Fields |> Array.ofSeq

    // A leaf is validated for field types (CT001) and list placement (CT002). Computed up
    // front (not as a closure) so the single, multi, and zero-field arms all share it without
    // an extra branch.
    let leafFindings =
        validateFieldTypesFor cmdName fields @ validateListPlacement cmdName fields

    // The single-field cases are the special branches (flag-DU / subcommand group / arg
    // group); zero- or multi-field cases are always leaves.
    match fields with
    | [| single |] ->
        let ft = single.FieldType

        if isList ft && isUnion (innerArg ft) then
            // `SomeDU list` single field => DU flag list. Valid, nothing to validate.
            []
        elif isUnion ft then
            // Nested union => subcommand group. Recurse into the nested union.
            analyzeCommandUnion visited (strip ft).TypeDefinition
        elif isRecord ft then
            // Record-typed arg group => validate the record's own fields.
            validateFieldTypesFor cmdName (strip ft).TypeDefinition.FSharpFields
        else
            // Single supported/scalar/option/list field => leaf.
            leafFindings
    | _ -> leafFindings

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
    // Build the called member's qualified name from its declaring entity + compiled name —
    // both total (unlike `mfv.FullName`, which throws for some compiler-generated symbols
    // the generic call-walk will encounter). A call with no declaring entity (e.g. a local
    // function) is never a constructor, so it is skipped.
    let isConstructorCall =
        match mfv.DeclaringEntity with
        | Some e -> constructorFullNames.Contains(entityKey e + "." + mfv.CompiledName)
        | None -> false

    if isConstructorCall then
        // Keep only the generic arguments that are command/globals DUs. `isUnion` is the same
        // predicate the field walk uses, so a non-union arg (e.g. `int`, a tuple) is skipped.
        memberTypeArgs
        |> List.filter isUnion
        |> List.map (fun ty -> (strip ty).TypeDefinition)
    else
        []

/// Walk every `FSharpExpr` reachable from an implementation file, collecting the command DU
/// entities at `fromUnion*` call sites. Recursion is generic via `ImmediateSubExpressions`,
/// so no per-expression-shape enumeration is needed (and nothing drifts as FCS adds nodes).
let private collectCommandUnions (typedTree: FSharpImplementationFileContents) : FSharpEntity list =
    let entities = ResizeArray<FSharpEntity>()
    let seen = System.Collections.Generic.HashSet<string>()

    let add (ent: FSharpEntity) =
        if seen.Add(entityKey ent) then
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

/// Analyze an optional typed tree. `None` (no type-check information available) yields no
/// diagnostics — the analyzer is typed-tree-only and silently no-ops without it. Split out
/// from the entry point so it is directly unit-testable.
let analyzeOptionalTypedTree (typedTree: FSharpImplementationFileContents option) : Message list =
    match typedTree with
    | Some tree -> analyzeTypedTree tree
    | None -> []

/// Analyzer entry point. Requires the typed tree (recovers `'Cmd` from call-site generic
/// instantiation), so it is a CLI/editor analyzer with full type-check information.
[<CliAnalyzer(Name, "Flags CommandTree command-DU shape errors (unsupported field types, list-field placement)")>]
let commandTreeAnalyzer: Analyzer<CliContext> =
    fun (context: CliContext) -> async { return analyzeOptionalTypedTree context.TypedTree }
