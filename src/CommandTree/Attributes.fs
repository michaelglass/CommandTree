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

/// Attribute to override the derived flag name or short flag for a record field.
/// Applied to record fields used as named flags in command options.
/// Name overrides the long flag name (derived from field name via toKebabCase by default).
/// Short overrides the auto-derived short flag (first letter of field name by default).
///
/// Example:
/// ```fsharp
/// type Options =
///     { [<CmdFlag(Name = "conf", Short = "k")>] Config: string option
///       Verbose: bool }
/// ```
[<AttributeUsage(AttributeTargets.Property, AllowMultiple = false)>]
type CmdFlagAttribute() =
    inherit Attribute()
    /// Long flag name override (without -- prefix)
    member val Name: string = null with get, set
    /// Short flag character override (without - prefix)
    member val Short: string = null with get, set
