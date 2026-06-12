namespace CommandTree

/// A construction-time problem with a command DU's shape.
/// These are programming errors over static shapes: for a given DU they are
/// deterministic, so <c>fromUnion*</c> fails fast by throwing, while
/// <c>tryFromUnion*</c> returns ALL of them as values for aggregation, testing,
/// and tooling. Distinct from <c>ParseError</c>, which describes runtime
/// parse-time failures over user input.
[<NoComparison>]
type SpecError =
    /// A case field (or arg-group record field) has a type the parser cannot
    /// handle. Carries the command name, field name, and the offending type.
    | UnsupportedFieldType of case: string * field: string * fieldType: System.Type
    /// A list-typed field is not the last field of its case. Only one list
    /// field per case is allowed and it must come last.
    | ListFieldNotLast of case: string * field: string
    /// A case declares more than one list-typed field. At most one is allowed.
    | MultipleListFields of case: string
    /// A command flag's long name (<c>--name</c>) collides with a global flag.
    | GlobalFlagCollision of flag: string * command: string
    /// A command flag's short name (<c>-x</c>) collides with a global flag.
    | GlobalShortFlagCollision of flag: string * command: string

/// Rendering helpers for <see cref="T:CommandTree.SpecError"/>.
[<RequireQualifiedAccess>]
module SpecError =

    /// Human-readable list of supported field types, mirroring
    /// <c>CommandReflection.supportedTypesDescription</c> (kept in sync by
    /// cross-reference; both render the same set the parser accepts).
    let private supportedTypesDescription =
        "string, int, int64, bool, float, decimal, Guid, a discriminated union, "
        + "an option of any of these, or a list of any of these"

    /// One-line human rendering of a single spec error. Carries at least the
    /// information the corresponding <c>invalidOp</c> message carries (case,
    /// field, offending type, supported set, or colliding flag).
    let format (error: SpecError) : string =
        match error with
        | UnsupportedFieldType(case, field, fieldType) ->
            $"Field '%s{field}' of command '%s{case}' has unsupported type '%s{fieldType.Name}'. "
            + $"Supported types: %s{supportedTypesDescription}."
        | ListFieldNotLast(case, field) ->
            $"List field '%s{field}' in case '%s{case}' must be the last field and there can be only one"
        | MultipleListFields case ->
            $"Case '%s{case}' has multiple list fields; a case may have at most one list field and it must be last"
        | GlobalFlagCollision(flag, command) -> $"Flag '%s{flag}' on command '%s{command}' conflicts with a global flag"
        | GlobalShortFlagCollision(flag, command) ->
            $"Flag '%s{flag}' on command '%s{command}' conflicts with a global flag"

    /// Multi-error rendering used by the throwing wrappers: a count header
    /// followed by one line per error.
    let formatAll (errors: SpecError list) : string =
        match errors with
        | [] -> "Invalid command spec"
        | [ single ] -> format single
        | _ ->
            let header = $"Invalid command spec: %d{List.length errors} problems found:"
            let lines = errors |> List.map (fun e -> "  - " + format e)
            String.concat "\n" (header :: lines)
