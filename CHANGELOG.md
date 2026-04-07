# Changelog

All notable changes to CommandTree are documented in this file.

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
