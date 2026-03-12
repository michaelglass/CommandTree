# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

CommandTree is an F# library for type-safe CLI command parsing using discriminated unions. It uses reflection to generate a recursive command tree from union types, with automatic help generation, fish shell completions, and process execution helpers.

## Build & Development Commands

Uses `mise` as task runner (see `mise.toml`). All commands also work with `dotnet` directly.

```bash
mise run build              # Build solution (restores deps first)
mise run test               # Run tests with coverage (Cobertura XML)
mise run coverage-check     # Check per-file coverage thresholds
mise run lint               # FSharpLint
mise run format             # Format with Fantomas (src/ tests/ examples/)
mise run example-build      # Build example app with --warnaserror
mise run docs               # Generate API docs via fsdocs
mise run sync-docs          # Sync README.md sections to docs/index.md
mise run check              # All checks with auto-fix
mise run ci                 # All CI checks without auto-fix
mise run pack               # Create NuGet package
```

**Running tests directly:**
```bash
dotnet test --coverage --coverage-output-format cobertura --coverage-output "$PWD/coverage/coverage.cobertura.xml"
```

## Architecture

Six source files in `src/CommandTree/`:

- **Attributes.fs** -- Marker attributes for union cases: `CmdAttribute` (description + name override), `CmdDefaultAttribute` (default subcommand), `CmdCompletionAttribute` (shell completion values), `CmdFileCompletionAttribute` (file path completions).

- **Tree.fs** -- Core ADT and operations. Defines `CommandTree<'Cmd>` (recursive `Leaf`/`Group` union), `ArgInfo`, `ArgCompletionHint`, and `ParseError` (structured error type with `HelpRequested`, `UnknownCommand`, `InvalidArguments`, `AmbiguousArgument`). The `CommandTree` module has `parse` (returns `Result<'Cmd, ParseError>`), `format`, `help`, `helpFull`, `findByPath`, `closestGroupPath`, and `fishCompletions`.

- **Reflection.fs** -- Generates `CommandTree<'Cmd>` from discriminated unions via `FSharp.Reflection`. `CommandReflection.fromUnion<'Cmd>` is the main entry point. Also provides `parseFieldValue` (returns `Result<obj option, string>`), `formatFieldValue`, `formatCmd`, `toKebabCase`, and `CommandSpec<'Cmd>` for bundling tree + execute + format. Supports field types: string, int, int64, float, decimal, bool, Guid, option, and nested unions.

- **UI.fs** -- Terminal output helpers: colored printing (`title`, `section`, `success`, `fail`, `info`, `warn`), timing display with color gradient, spinner animation (`withSpinner`, `withSpinnerQuiet`). `Color` module has ANSI escape codes.

- **Process.fs** -- Process execution: `run` (interactive with timing), `runSilent` (captured output), `runAsync` (Task-based), `runWithSpinner` (returns `int * string * string`), `runCommand` (returns `CommandResult` record), `runInteractive`, `dotnet`, `runParallel`. All use `ProcessStartInfo` directly (no shell).

- **Fish.fs** -- Fish shell completion generation and installation. `FishCompletions.generateContent` creates `.fish` file content, `writeToFile` installs to `~/.config/fish/completions/`, `installHook` sets up auto-regeneration via `conf.d`.

## Key Concepts

**DU-based command parsing:** Define CLI commands as discriminated unions. Cases become commands, nested unions become subcommand groups, case fields become arguments. `CommandReflection.fromUnion<'Cmd>` builds the tree via reflection.

**Attributes:** `[<Cmd("desc")>]` for description/name, `[<CmdDefault>]` for default subcommand, `[<CmdCompletion("a", "b")>]` for argument completions, `[<CmdFileCompletion>]` for file path completions.

**Tree structure:** `CommandTree<'Cmd>` is a recursive union of `Leaf` (command with parser) and `Group` (subcommands with optional default). Parsing walks the tree matching args to node names, returning `Result<'Cmd, ParseError>` with structured errors carrying path context.

**Structured parse errors:** `ParseError` is a discriminated union with `HelpRequested of path`, `UnknownCommand of input * groupPath`, `InvalidArguments of command * message`, and `AmbiguousArgument of input * candidates`. Path accumulation during recursive parse gives consumers full context for error display.

**Process runner:** The `Process` module wraps `System.Diagnostics.Process` with convenience functions. `ProcessStartInfo` is used directly -- no shell involved, so single quotes are literal.

**Fish completions:** Generated from the command tree structure. Subcommand conditions use `__fish_seen_subcommand_from`. Supports value completions, file completions, and auto-detection of union-typed fields.

## Tests

Six test files in `tests/CommandTree.Tests/`:

- **ReflectionTests.fs** -- `toKebabCase`, `toDescription`, `fromUnion` tree generation, `caseName`, `formatCmd`, field value parsing/formatting for all supported types (string, int, int64, float, decimal, bool, Guid, option, union).
- **CompletionTests.fs** -- Completion attributes, union auto-detection, fish completion output for values/files/nested groups.
- **ParsingTests.fs** -- `parse` for simple/nested/default commands, argument parsing, structured `ParseError` matching (`HelpRequested`, `UnknownCommand`, `InvalidArguments`, `AmbiguousArgument`), `closestGroupPath`, help generation, `format` roundtrips.
- **UITests.fs** -- Color ANSI constants, UI output functions (title, section, success, fail, info, warn, dimInfo, cmd), timingColor gradient, timing format, withSpinner non-interactive mode. Uses stdout/stderr capture helpers.
- **ProcessTests.fs** -- `runSilent`, `runCommand`, `runSilentWithEnv`, `runSilentWithTimeout`, `runInteractive`, `runAsync`, `runWithSpinner`, `runParallel`. Uses real process execution (echo/sh).
- **FishTests.fs** -- `generateContent` snapshot/fixture tests for completion script structure, subcommands, file completions, cmdName variations.

Uses xUnit v3 + Unquote (`test <@ assertion @>` syntax). Tests access internal members via `InternalsVisibleTo`.

## Code Style

- **Formatting**: Fantomas (auto-enforced via `mise run format`)
- **Linting**: FSharpLint -- 4-space indent, 120 char max line, 1000 line max file, 500 line max source length
- **Naming**: PascalCase for types/modules/records/unions, camelCase for values/params
- **Build strictness**: `TreatWarningsAsErrors` is enabled; `GenerateDocumentationFile` requires XML docs on public members
- **Coverage**: Per-file coverage enforced via `scripts/check-coverage.fsx` (100% default, with overrides for I/O modules). Tests cover all public API surface including edge cases (ambiguous union prefix matching, optional fields, nested defaults)
- **Documentation**: fsdocs generates API docs; `scripts/sync-docs.fsx` syncs README sections to `docs/index.md` via `<!-- sync:name:start -->` tags
