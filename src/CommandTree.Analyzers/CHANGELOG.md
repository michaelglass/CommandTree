# Changelog — CommandTree.Analyzers

All notable changes to the `CommandTree.Analyzers` package are documented in
this file. The CommandTree library itself has its own
[`src/CommandTree/CHANGELOG.md`](../CommandTree/CHANGELOG.md).

## Unreleased

### Added

- Initial release — new opt-in `CommandTree.Analyzers` package
  (`FSharp.Analyzers.SDK`) that surfaces command-DU shape errors at edit/build
  time, before the program runs. It finds `CommandReflection.fromUnion*` call
  sites in the typed tree, recovers the command (and globals) DU from each
  call's generic instantiation, and walks the DU the same way the runtime does
  — reporting:
  - **CT001 (unsupported field type)** — a case field or arg-group record field
    whose type the runtime parse machinery rejects. Mirrors
    `CommandReflection.fromUnion*`'s runtime check (`isSupportedFieldType`): the
    message names the case, field, offending type, and the supported set.
    Recurses into nested subcommand unions and arg-group records, and validates
    the globals DU too.
  - **CT002 (list-field placement)** — a list field that is not last, or more
    than one list field in a case. Mirrors the runtime placement rule.

  Both diagnostics are warnings (the package is opt-in and never breaks a build
  by itself). The predicate is a hand-mirror of `CommandTree.Reflection` in
  FCS-symbol terms — the two share no code (reflection operates on
  `System.Type`, the analyzer on `FSharpType`). Load it via an analyzer host
  (Ionide, `fshw check`, or the `fsharp-analyzers` CLI). Note: the
  `fsharp-analyzers` 0.36.0 CLI is built against FSharp.Core 10.0 / FCS 43.10
  and currently cannot load an analyzer compiled against this repo's
  FSharp.Core 10.1 / FCS 43.12 (an ABI skew); hosts that supply a matching FCS
  load it fine.
