# Changelog

All notable changes to CommandTree are documented in this file.

## Unreleased

### Added

#### Analyzer

- New opt-in `CommandTree.Analyzers` package (FSharp.Analyzers.SDK) that surfaces command-DU
  shape errors at edit/build time, before the program runs. It finds
  `CommandReflection.fromUnion*` call sites in the typed tree, recovers the command (and
  globals) DU from each call's generic instantiation, and walks the DU the same way the
  runtime does — reporting:
  - **CT001 (unsupported field type)** — a case field or arg-group record field whose type
    the runtime parse machinery rejects. Mirrors `CommandReflection.fromUnion*`'s runtime
    check (`isSupportedFieldType`): the message names the case, field, offending type, and the
    supported set. Recurses into nested subcommand unions and arg-group records, and validates
    the globals DU too.
  - **CT002 (list-field placement)** — a list field that is not last, or more than one list
    field in a case. Mirrors the runtime placement rule.

  Both diagnostics are warnings (the package is opt-in and never breaks a build by itself). The
  predicate is a hand-mirror of `CommandTree.Reflection` in FCS-symbol terms — the two share no
  code (reflection operates on `System.Type`, the analyzer on `FSharpType`). Load it via an
  analyzer host (Ionide, `fshw check`, or the `fsharp-analyzers` CLI). Note: the
  `fsharp-analyzers` 0.36.0 CLI is built against FSharp.Core 10.0 / FCS 43.10 and currently
  cannot load an analyzer compiled against this repo's FSharp.Core 10.1 / FCS 43.12 (an ABI
  skew); hosts that supply a matching FCS load it fine.

### Changed

- **Potentially breaking (CLI name generation):** `toKebabCase` now splits acronym
  boundaries. An uppercase run followed by a capitalized word splits at the last
  capital, so `HTMLParser` → `html-parser` and `DBMigrate` → `db-migrate` (previously
  `htmlparser` / `dbmigrate`). Names with no acronym run are unchanged (`DryRun` →
  `dry-run`, `FileCoverage` → `file-coverage`), as are pure/trailing acronyms (`HTML` →
  `html`, `ExtractApi` → `extract-api`). This changes the derived command/flag name for
  any union case whose name contains two or more consecutive capitals (without a `Name`
  attribute override). No CommandTree example or known local consumer (FsHotWatch,
  FsSemanticTagger, CoverageRatchet, FsProjLint, UnionConfig) has such a case name, so
  no observed downstream CLI name changes. Consumers relying on the old collapsed form
  can pin the name with `[<Cmd(Name = "...")>]` / `[<CmdFlag(Name = "...")>]`.

### Fixed

- Command DUs whose case (or record-arg) fields have an unsupported type now fail
  fast at tree construction (`CommandReflection.fromUnion*`) with an
  `InvalidOperationException` naming the case, the field, the offending type, and the
  list of supported types. Previously an unsupported field type (e.g.
  `DateTimeOffset`) silently fell through `parseFieldValue` to `Ok None` and only
  surfaced as a generic "Invalid arguments" error when the command was parsed at
  runtime. Supported field types are unchanged (string, int, int64, bool, float,
  decimal, Guid, discriminated unions, and options/lists of those).
- Float and decimal argument parsing and formatting are now culture-invariant.
  Previously `parseFieldValue` used `Double.TryParse`/`Decimal.TryParse` and
  `formatFieldValue` used the default `ToString` overloads, all of which honor the
  ambient `CultureInfo`. On cultures where `.` is a grouping separator (e.g. `de-DE`),
  `"1.5"` silently parsed to `15` and a `1.5` value formatted to `"1,5"`, breaking
  both parsing and format→parse roundtrips. Parsing now uses
  `NumberStyles.Float`/`NumberStyles.Number` with `CultureInfo.InvariantCulture`, and
  formatting uses `ToString(CultureInfo.InvariantCulture)`. CLI numeric arguments now
  behave identically regardless of the host's locale.

## 0.6.2 - 2026-06-03

### Fixed

- `build/CommandTree.targets` (shipped under both `build/` and `buildTransitive/`) no longer emits
  `MSB3073` warnings — or fails the build — when jj/git are unavailable or the build directory isn't
  a VCS repo. The four revision/dirty probe `<Exec>` tasks now use `IgnoreExitCode="true"` (instead
  of `ContinueOnError="true"`) plus `IgnoreStandardErrorWarningFormat="true"`. `IgnoreExitCode`
  treats a non-zero probe exit as success so no `MSB3073` warning is logged;
  `IgnoreStandardErrorWarningFormat` stops tool stderr (e.g. jj's "There is no jj repo in ." outside
  a repo) from being promoted to an MSBuild error/warning. Together they deliver the target's stated
  "silent, best-effort, empty on failure" intent without diagnostics that could turn downstream
  warning- or error-counting CI red. The success path is unchanged: when jj/git are present the
  commit id is still captured and stamped into `SourceRevisionId`.

## 0.6.1 - 2026-06-02

### Added

- `CommandTree.assemblyVersion asm` — best-available version string for an assembly: prefers the
  `AssemblyInformationalVersionAttribute.InformationalVersion` (keeping any `+<commit>` build
  metadata), falling back to `GetName().Version`. Fully unit-testable by passing assemblies in.
- `CommandTree.entryAssemblyVersion ()` — version of the process entry assembly (the consumer
  CLI), resolving `Assembly.GetEntryAssembly()` with a `GetCallingAssembly()` fallback for test
  hosts.
- `CommandTree.renderVersion prefix` — renders `"<prefix> <entryAssemblyVersion>"`, the
  recommended default for the `VersionRequested` arm:
  `| Error VersionRequested -> printfn "%s" (CommandTree.renderVersion "toolname"); 0`.
- Auto-importing `build/CommandTree.targets` (shipped under both `build/` and `buildTransitive/`)
  that stamps the build commit id (via jj, falling back to git) into `SourceRevisionId` so the
  SDK folds it into `AssemblyInformationalVersion` as `+<commit>` for dev builds, with a `.dirty`
  suffix when the working copy is dirty. It never fails the build and never clobbers an existing
  `SourceRevisionId` (CI/SourceLink). Opt out with `-p:CommandTreeStampRevision=false` (whole
  feature) or `-p:CommandTreeStampDirty=false` (dirty marker only).

## 0.6.0 - 2026-06-02

### Changed

- **BREAKING:** `ParseError.UnknownCommand` now carries the raw remaining args. Its shape
  changed from `UnknownCommand of input: string * groupPath: string list` to
  `UnknownCommand of input: string * rest: string array * groupPath: string list`, where
  `rest` is the raw argv after the unrecognized token. Every consumer pattern-matching
  `UnknownCommand` must add the `rest` field. `parse` is now the single canonical path: a
  consumer that resolves some commands dynamically (e.g. forwards them to a daemon) reads
  `input` + `rest` directly; otherwise it renders the canonical error and exits non-zero.

### Added

- `CommandTree.renderParseError tree error prefix` — renders a `ParseError` as the canonical
  user-facing stderr text: a one-line "invalid input" message followed by the nearest
  command/group's help. Pure (returns a string). `HelpRequested` renders help only;
  `VersionRequested` returns `""` (version output is the caller's concern).
- `CommandTree.isError error` — classifies a `ParseError` for exit-code selection (`true` for
  genuine input errors, `false` for `HelpRequested`/`VersionRequested`).

## 0.5.1 - 2026-05-27

### Changed

- Bump `Microsoft.SourceLink.GitHub` 10.0.201 → 10.0.300
- Bump `Microsoft.Testing.Extensions.CodeCoverage` 18.6.2 → 18.7.0

## [0.5.0] - 2026-04-27

### Added

- `[<CmdArg("description")>]` attribute for documenting positional arguments — applied to the union **case** with `FieldIndex` (0-based) to select which field; mirrors `CmdCompletion`/`CmdFileCompletion` pattern (F# does not allow `[<>]` syntax on named DU case fields). For multi-field commands, consider a record argument type instead — `[<CmdArg>]` on record fields is also supported and lets you share arg docs across commands.
- `Default` property on `[<CmdArg>]` — shown in the `Arguments:` section as `(default: value)`
- `[<CmdExample("...")>]` attribute for example invocations — stacked on a case; `help` renders an `Examples:` section with full command path prefix
- `Description` property on `[<CmdFlag>]` to override the auto-derived flag description (derived from case name in sentence case)

### Breaking

- `ArgInfo` gains `Description: string option` and `Default: string option` fields — callers constructing `ArgInfo` directly must add `Description = None` and `Default = None`
- `LeafData` gains `Examples: string list` field — callers constructing `LeafData` directly must add `Examples = []`

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
