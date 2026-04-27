# Changelog

All notable changes to CommandTree are documented in this file.

## [Unreleased]

- feat: add `[<CmdArg("description")>]` attribute for documenting positional arguments — applied to the union **case** with `FieldIndex` (0-based) to select which field; mirrors `CmdCompletion`/`CmdFileCompletion` pattern (F# does not allow `[<>]` syntax on named DU case fields)
- feat: add `Default` property to `[<CmdArg>]` — shown in the `Arguments:` section as `(default: value)`
- feat: add `[<CmdExample("...")>]` attribute for example invocations — stacked on a case; `help` renders an `Examples:` section with full command path prefix
- feat: add `Description` property to `[<CmdFlag>]` attribute to override the auto-derived flag description (derived from case name in sentence case)
- **Breaking:** `ArgInfo` gains `Description: string option` and `Default: string option` fields — callers constructing `ArgInfo` directly must add `Description = None` and `Default = None`
- **Breaking:** `LeafData` gains `Examples: string list` field — callers constructing `LeafData` directly must add `Examples = []`
- chore: bump upstream tool versions

## [0.4.0] - 2026-04-11

### Changed

- **Breaking:** `CommandTree` variants use named record fields (`LeafData`, `GroupData`) instead of positional tuples
- **Breaking:** `parseFieldValue` returns `Result<obj option, ParseFieldError>` instead of `Result<obj option, string>`
- **Breaking:** `helpWithGlobals` parameter order changed from `(globalFlags, tree, prefix)` to `(tree, globalFlags, prefix)` for consistency
- **Breaking:** `format` no longer takes an internal `path` parameter
- Collapsed `DefaultParse`/`DefaultChild` into a single `DefaultCommand` record type
- Added `ParseFieldError` discriminated union (`AmbiguousValue` | `InvalidValue`) for structured field parse errors
- Extracted `renderChildrenHelp` to deduplicate help rendering between `help` and `helpWithGlobals`
- Unified `parseDUFlags`/`scanGlobalFlags` into shared `parseFlagsLoop` helper
- Removed unused `FlagArray` field from `FlagLookup`
- Reused `buildTypedList` (renamed from `buildFlagList`) in `parseFields` list construction
- Reused `makeNone` in `parseFieldValue` optional type branch
- Removed spurious `rec` from `help` function

## [0.3.5] - 2026-04-08

### Changed

- Simplified record/leaf parsing internals; improved branch coverage to 97.8%

### Fixed

- Reject trailing arguments on zero-arg commands
- Default bool fields in record-typed arguments

## [0.3.4] - 2026-04-08

### Added

- `InDir` variants for `Process` functions (`runInDir`, `runSilentInDir`, etc.) to run subprocesses in a specified working directory

### Fixed

- Inline `workDir` to fix coverage pass on CI

### Changed

- Updated NuGet dependencies to latest versions

## [0.3.3] - 2026-04-08

### Added

- **Breaking:** Built-in `--help` and `--version` flags handled automatically during command tree parsing
- SourceLink support and `Directory.Build.props` for improved NuGet packaging metadata
- FsProjLint integration for project structure validation
- Auto-discovering example projects in CI workflow

## [0.3.2] - 2026-04-07

### Fixed

- Treat all DU list fields as named flags, not positional values

### Changed

- Updated example CLI and README for global options, env vars, and DU flags

## [0.3.1] - 2026-04-06

### Added

- Global flag support via `GlobalSpec` and `fromUnionWithGlobals`
- Environment variable resolution for flags with `fromUnionWithEnv` and `fromUnionWithGlobalsAndEnv`
- `CmdEnvAttribute` and `CmdEnvRawAttribute` for env var binding on flag cases
- `EnvVarInfo` type and `EnvVar` field on `FlagInfo`
- Env var hints and global options shown in help output
- DU-based flag parsing with `getFlagInfoFromDU` and `parseDUFlags`

### Changed

- **Breaking:** Replaced record-based flags with DU-based flags
- Migrated to shared NuGet tools and reusable CI workflows

### Fixed

- CI workflow permissions for reusable workflow calls

## [0.3.0] - 2026-04-05

### Added

- Named flags support: flags on `Leaf` variant, help display, fish completions, and `formatCmd` roundtrip
- Named flag parsing from record-based option types
- `FlagLookup` type and `renderFlagTokens` helper

## [0.2.0] - 2026-04-05

### Added

- List field support for CLI command parsing
- Multiple `CmdFileCompletion` attributes per case
- Fish completions for list fields

## [0.1.0] - 2026-03-12

### Changed

- Aligned repo config with Falco.UnionRoutes

## [0.1.0-alpha.1] - 2026-03-12

Initial release.

### Added

- Core command tree ADT (`CommandTree<'Cmd>`) with recursive `Leaf`/`Group` structure
- `CommandReflection.fromUnion<'Cmd>` to generate command trees from discriminated unions via reflection
- Supported field types: string, int, int64, float, decimal, bool, Guid, option, nested unions
- `CmdAttribute`, `CmdDefaultAttribute`, `CmdCompletionAttribute`, `CmdFileCompletionAttribute` for customizing command metadata
- Structured `ParseError` type with `HelpRequested`, `UnknownCommand`, `InvalidArguments`, `AmbiguousArgument`
- `parse` returning `Result<'Cmd, ParseError>` with path context on errors
- `format`, `help`, `helpFull`, `findByPath`, `closestGroupPath` tree operations
- Fish shell completion generation and installation (`FishCompletions`)
- Process execution helpers: `run`, `runSilent`, `runAsync`, `runWithSpinner`, `runCommand`, `runInteractive`, `runParallel`
- Terminal UI helpers: colored output, spinner animation, timing display
