# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

CommandTree is an F# library for type-safe CLI command parsing using discriminated unions. It uses reflection to generate a recursive command tree from union types, with automatic help generation, fish shell completions, and process execution helpers.

## Build & Development Commands

Uses `mise` as task runner (see `mise.toml`). All commands also work with `dotnet` directly.

```bash
mise run build              # Build solution (restores deps first)
mise run test               # Run tests (xUnit v3 standalone exe)
mise run lint               # FSharpLint
mise run format             # Format with Fantomas
mise run check              # All checks with auto-fix (format, lint, test)
mise run ci                 # All CI checks without auto-fix
mise run pack               # Create NuGet package
```

**Running tests directly:**
```bash
dotnet exec tests/CommandTree.Tests/bin/Debug/net10.0/CommandTree.Tests.dll
```

Note: xUnit v3 produces a standalone executable. `dotnet test` also works but `dotnet exec` is preferred.

## Architecture

Six source files in `src/CommandTree/`:

- **Attributes.fs** -- Marker attributes for union cases: `CmdAttribute` (description + name override), `CmdDefaultAttribute` (default subcommand), `CmdCompletionAttribute` (shell completion values), `CmdFileCompletionAttribute` (file path completions).

- **Tree.fs** -- Core ADT and operations. Defines `CommandTree<'Cmd>` (recursive `Leaf`/`Group` union), `ArgInfo`, `ArgCompletionHint`. The `CommandTree` module has `parse`, `format`, `help`, `helpFull`, `findByPath`, `closestGroupPath`, and `fishCompletions`.

- **Reflection.fs** -- Generates `CommandTree<'Cmd>` from discriminated unions via `FSharp.Reflection`. `CommandReflection.fromUnion<'Cmd>` is the main entry point. Also provides `parseFieldValue`, `formatFieldValue`, `formatCmd`, `toKebabCase`, and `CommandSpec<'Cmd>` for bundling tree + execute + format.

- **UI.fs** -- Terminal output helpers: colored printing (`title`, `section`, `success`, `fail`, `info`, `warn`), timing display with color gradient, spinner animation (`withSpinner`, `withSpinnerQuiet`). `Color` module has ANSI escape codes.

- **Process.fs** -- Process execution: `run` (interactive with timing), `runSilent` (captured output), `runAsync` (Task-based), `runWithSpinner`, `runCommand` (returns `CommandResult` record), `runInteractive`, `dotnet`, `runParallel`. All use `ProcessStartInfo` directly (no shell).

- **Fish.fs** -- Fish shell completion generation and installation. `FishCompletions.generateContent` creates `.fish` file content, `writeToFile` installs to `~/.config/fish/completions/`, `installHook` sets up auto-regeneration via `conf.d`.

## Key Concepts

**DU-based command parsing:** Define CLI commands as discriminated unions. Cases become commands, nested unions become subcommand groups, case fields become arguments. `CommandReflection.fromUnion<'Cmd>` builds the tree via reflection.

**Attributes:** `[<Cmd("desc")>]` for description/name, `[<CmdDefault>]` for default subcommand, `[<CmdCompletion("a", "b")>]` for argument completions, `[<CmdFileCompletion>]` for file path completions.

**Tree structure:** `CommandTree<'Cmd>` is a recursive union of `Leaf` (command with parser) and `Group` (subcommands with optional default). Parsing walks the tree matching args to node names.

**Process runner:** The `Process` module wraps `System.Diagnostics.Process` with convenience functions. `ProcessStartInfo` is used directly -- no shell involved, so single quotes are literal.

**Fish completions:** Generated from the command tree structure. Subcommand conditions use `__fish_seen_subcommand_from`. Supports value completions, file completions, and auto-detection of union-typed fields.

## Tests

Three test files in `tests/CommandTree.Tests/`:

- **ReflectionTests.fs** -- `toKebabCase`, `toDescription`, `fromUnion` tree generation, `caseName`, `formatCmd`, field value parsing/formatting for all supported types (string, int, int64, bool, Guid, option, union).
- **CompletionTests.fs** -- Completion attributes, union auto-detection, fish completion output for values/files/nested groups.
- **ParsingTests.fs** -- `parse` for simple/nested/default commands, argument parsing, unknown command errors, `closestGroupPath`, help generation, `format` roundtrips.

Uses xUnit v3 + Unquote (`test <@ assertion @>` syntax). Tests access internal members via `InternalsVisibleTo`.

## Code Style

- **Formatting**: Fantomas (auto-enforced via `mise run format`)
- **Linting**: FSharpLint -- 4-space indent, 120 char max line, 1000 line max file, 500 line max source length
- **Naming**: PascalCase for types/modules/records/unions, camelCase for values/params
- **Build strictness**: `TreatWarningsAsErrors` is enabled; `GenerateDocumentationFile` requires XML docs on public members
- **Coverage**: Tests cover all public API surface including edge cases (ambiguous union prefix matching, optional fields, nested defaults)
