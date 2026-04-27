namespace CommandTree

open System

/// Attribute to specify command metadata on union cases.
/// Description is required; Name defaults to kebab-case of the case name.
///
/// Example usage:
/// ```fsharp
/// type Command =
///     | Check                                    // Name: "check", Desc: "Check" (derived)
///     | [<Cmd("Run the linter")>] Lint           // Name: "lint", Desc: "Run the linter"
///     | [<Cmd("Format code", Name = "fmt")>] Format  // Name: "fmt", Desc: "Format code"
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdAttribute(description: string) =
    inherit Attribute()

    /// Command description (required)
    member val Description: string = description with get, set

    /// Command name override (defaults to kebab-case of case name)
    member val Name: string = null with get, set

/// Attribute to mark a case as the default for its parent group.
/// When a group is invoked without a subcommand, the default case is used.
///
/// Example:
/// ```fsharp
/// type DevCommand =
///     | [<CmdDefault>] Check    // "build dev" runs Check
///     | Build
///     | Test
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdDefaultAttribute() =
    inherit Attribute()

/// Attribute to specify completion values for a union case's arguments.
/// Applied to the case, provides completion hints for shell completions.
/// FieldIndex specifies which field (0-based) the completions apply to (default: 0).
///
/// Example:
/// ```fsharp
/// type EnvCommand =
///     | [<Cmd("Edit config"); CmdCompletion("dev", "staging", "prod")>] Edit of env: string option
///     | [<Cmd("Set variable"); CmdCompletion("dev", "staging", "prod")>] Set of env: string option * name: string * value: string
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = true)>]
type CmdCompletionAttribute([<ParamArray>] values: string[]) =
    inherit Attribute()
    member val Values = values
    member val FieldIndex = 0 with get, set

/// Attribute to mark a case's argument as accepting file path completions.
/// Fish shell will use its built-in file completion for this argument.
/// FieldIndex specifies which field (0-based) the completions apply to (default: 0).
/// Multiple attributes can be applied to a single case to mark different fields.
///
/// Example:
/// ```fsharp
/// type CoverageCommand =
///     | [<Cmd("Show coverage for file"); CmdFileCompletion>] File of sourceFile: string
///     | [<Cmd("Compare APIs"); CmdFileCompletion(FieldIndex = 0); CmdFileCompletion(FieldIndex = 1)>]
///       CompareApi of oldDll: string * newDll: string
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = true)>]
type CmdFileCompletionAttribute() =
    inherit Attribute()
    member val FieldIndex = 0 with get, set

/// Attribute to document a positional argument on a DU case.
/// Applied to the case (not the field); FieldIndex selects which positional argument (0-based).
/// Multiple attributes can be stacked for multi-field cases.
/// F# does not allow [<>] syntax directly on named DU case fields, so this mirrors
/// the CmdCompletion/CmdFileCompletion pattern.
///
/// Example:
/// ```fsharp
/// type CoverageCommand =
///     | [<Cmd("Merge two Cobertura XMLs")
///        CmdArg("Cobertura XML from a prior full run")
///        CmdArg("Cobertura XML from the current run", FieldIndex = 1)
///        CmdArg("Where to write the merged output", FieldIndex = 2)>]
///       Merge of baseline: string * partial: string * output: string
///
///     | [<Cmd("Ratchet coverage"); CmdArg("Config file", Default = "coverage-ratchet.json")>]
///       Ratchet of config: string option
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = true)>]
type CmdArgAttribute(description: string) =
    inherit Attribute()
    member val Description: string = description
    /// Zero-based index of the positional argument this annotation applies to (default: 0)
    member val FieldIndex = 0 with get, set
    /// Default value to display in help (e.g. "coverage-ratchet.json")
    member val Default: string = null with get, set

/// Attribute to override the derived flag name or short flag for a DU flag case.
/// Applied to union cases in a flag DU (each case = one CLI flag).
/// Name overrides the long flag name (derived from case name via toKebabCase by default).
/// Short overrides the auto-derived short flag (first letter of flag name by default).
/// Description overrides the auto-derived description (derived from case name by default).
///
/// Example:
/// ```fsharp
/// type CheckFlag =
///     | [<CmdFlag(Name = "conf", Short = "k", Description = "Path to config file")>] Config of string
///     | Verbose
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdFlagAttribute() =
    inherit Attribute()
    /// Long flag name override (without -- prefix)
    member val Name: string = null with get, set
    /// Short flag character override (without - prefix)
    member val Short: string = null with get, set
    /// Description override (defaults to case name in sentence case)
    member val Description: string = null with get, set

/// Attribute to provide example invocations for a command case.
/// Applied to union cases; multiple attributes can be stacked for multiple examples.
/// The cmdPrefix is prepended automatically when rendering help.
///
/// Example:
/// ```fsharp
/// type CoverageCommand =
///     | [<Cmd("Merge two Cobertura XMLs")>
///        CmdExample("merge old.xml new.xml merged.xml")>] Merge of ...
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = true)>]
type CmdExampleAttribute(example: string) =
    inherit Attribute()
    member val Example: string = example

/// Attribute to override the env var suffix for a flag union case.
/// The prefix (set via fromUnionWithEnv/fromUnionWithGlobalsAndEnv) is prepended.
/// E.g., [<CmdEnv("LVL")>] with prefix "MYAPP" resolves to MYAPP_LVL.
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdEnvAttribute(suffix: string) =
    inherit Attribute()

    /// Env var suffix (without prefix)
    member val Suffix: string = suffix

/// Attribute to set the exact env var name for a flag union case, ignoring the prefix.
/// E.g., [<CmdEnvRaw("NO_CACHE")>] always resolves to NO_CACHE regardless of prefix.
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdEnvRawAttribute(varName: string) =
    inherit Attribute()

    /// Full env var name (prefix ignored)
    member val VarName: string = varName
