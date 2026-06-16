# Advanced usage

Deeper reference for CommandTree: the full parsing/help API, structured errors,
record-typed arguments, version stamping, fish completions, and the build-time
analyzer. For the basics, start with the [README](../README.md).

## Parsing & help

```fsharp
CommandTree.parse tree args              // Result<'Cmd, ParseError>
CommandTree.help tree path prefix        // Help text for one level
CommandTree.helpFull tree prefix         // Full recursive help
CommandTree.helpForPath tree path prefix // Help for a subcommand path
CommandTree.helpWithGlobals tree flags prefix // Help with a global-options section
CommandTree.format tree cmd prefix       // Format a command back to a CLI string
CommandTree.findByPath tree path         // Navigate to a subtree
CommandTree.closestGroupPath tree args   // Deepest matching group path
CommandTree.renderParseError tree err prefix // Error line + nearest help (full stderr text)
CommandTree.isError err                  // true for genuine errors, false for help/version
CommandTree.renderVersion prefix         // "<prefix> <version>" banner for the version arm
CommandTree.entryAssemblyVersion ()      // Entry assembly's version string
CommandTree.assemblyVersion asm          // Best-available version of any assembly
```

`parse` is the single, strict parse path. An unrecognized command yields
`Error(UnknownCommand(input, rest, groupPath))`, where `rest` is the raw
remaining args after the unknown token. A consumer decides what to do with it:
forward `input` + `rest` to a daemon for dynamically-resolved commands, or render
the canonical error and fail hard. There is no separate lenient/strict pair of
functions.

`renderParseError` turns any `ParseError` into the canonical stderr text — a
one-line "invalid input" message followed by the nearest command/group's help —
so every consumer renders errors uniformly. Pair it with `isError` for the exit
code (`HelpRequested` / `VersionRequested` are not errors).

```fsharp
match CommandTree.parse tree argv with
| Ok cmd -> run cmd; 0
// Forward unknown top-level commands (groupPath = []) to a daemon for dynamic plugins.
| Error(UnknownCommand(cmd, rest, [])) ->
    match tryForwardToDaemon cmd rest with
    | Some code -> code
    | None ->
        eprintfn "%s" (CommandTree.renderParseError tree (UnknownCommand(cmd, rest, [])) "my-cli")
        1
| Error err when CommandTree.isError err ->
    eprintfn "%s" (CommandTree.renderParseError tree err "my-cli") // includes nested UnknownCommand
    1
| Error err ->
    printfn "%s" (CommandTree.renderParseError tree err "my-cli") // help text (HelpRequested)
    0
```

## Reflection

```fsharp
// Without global options
CommandReflection.fromUnion<'Cmd> "desc"                          // CommandTree<'Cmd>
CommandReflection.fromUnionWithEnv<'Cmd> "desc" "PREFIX"          // CommandTree<'Cmd> (with env vars)

// With global options (returns GlobalSpec with .Tree, .Parse, .GlobalFlags)
CommandReflection.fromUnionWithGlobals<'Cmd, 'G> "desc"           // GlobalSpec<'G, 'Cmd>
CommandReflection.fromUnionWithGlobalsAndEnv<'Cmd, 'G> "desc" "P" // GlobalSpec<'G, 'Cmd> (with env vars)

// Non-throwing variants — return Result<_, SpecError list> with ALL shape
// errors aggregated (see "Spec errors" below). Each fromUnion* is a thin
// wrapper that calls its try* sibling and throws on Error.
CommandReflection.tryFromUnion<'Cmd> "desc"                          // Result<CommandTree<'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithEnv<'Cmd> "desc" "PREFIX"          // Result<CommandTree<'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithGlobals<'Cmd, 'G> "desc"           // Result<GlobalSpec<'G, 'Cmd>, SpecError list>
CommandReflection.tryFromUnionWithGlobalsAndEnv<'Cmd, 'G> "desc" "P" // Result<GlobalSpec<'G, 'Cmd>, SpecError list>

// Utilities
CommandReflection.formatCmd cmd              // Format a command to a CLI string
CommandReflection.caseName value             // Kebab-case name of a union value
CommandReflection.toKebabCase "PascalCase"   // "pascal-case"
CommandReflection.parseFieldValue type str   // Result<obj option, ParseFieldError>
CommandReflection.formatFieldValue value     // Typed value to string
```

## Record-typed arguments

A case can take a record instead of a tuple of fields. The record's fields
become positional arguments, and `[<CmdArg>]` applied to record fields documents
and defaults them — handy for sharing argument docs across commands.

```fsharp
// my-cli report generate coverage.xml
// my-cli report generate coverage.xml report.html --show-gaps
type MergeReportArgs =
    { [<CmdArg("Baseline Cobertura XML")>] Baseline: string
      [<CmdArg("Output file", Default = "diff.html")>] Output: string option }

type ReportCommand =
    | [<Cmd("Generate a coverage report")>]
      [<CmdArg("Path to Cobertura XML input")>]
      [<CmdArg("Output file", FieldIndex = 1, Default = "report.html")>]
      [<CmdExample("coverage.xml", "coverage.xml report.html --show-gaps")>]
      Generate of input: string * output: string option * ReportFlag list
    | [<Cmd("Diff two reports using record args")>]
      Diff of MergeReportArgs
```

## Spec errors

A command DU's *shape* can be malformed independently of any user input: a field
whose type the parser can't handle (e.g. `DateTimeOffset`), a list field that
isn't last, more than one list field in a case, or a command flag name that
collides with a global flag. These are deterministic programming errors over the
static shape, so the `fromUnion*` constructors fail fast by throwing
`InvalidOperationException`.

The throwing constructors are thin wrappers over `tryFromUnion*`, which return
`Result<_, SpecError list>` instead. The `try*` variants are the single source
of truth and are recommended whenever you want **all** shape problems reported at
once (rather than fixing them one crash at a time), non-throwing startup, or
programmatic access to the errors:

```fsharp
type Bad =
    | [<Cmd("First")>] One of when_: System.DateTimeOffset // unsupported type
    | [<Cmd("Third")>] Three of span: System.TimeSpan      // unsupported type

match CommandReflection.tryFromUnion<Bad> "My CLI" with
| Ok tree -> // use tree
    ()
| Error errors ->
    // Every problem at once, in DU declaration order:
    //   [ UnsupportedFieldType ("one", "when_", typeof<DateTimeOffset>)
    //     UnsupportedFieldType ("three", "span", typeof<TimeSpan>) ]
    errors |> List.iter (SpecError.format >> eprintfn "%s")
```

`SpecError` is a DU with one case per construction-time problem
(`UnsupportedFieldType`, `ListFieldNotLast`, `MultipleListFields`,
`GlobalFlagCollision`, `GlobalShortFlagCollision`). `SpecError.format` renders one
error as a line; `SpecError.formatAll` renders a list with a count header (this is
exactly the message the throwing constructors raise). `SpecError` is distinct from
`ParseError`, which describes runtime parse failures over user input.

## Version

`renderParseError` returns `""` for `VersionRequested` because the version lives
in the consumer's assembly, not CommandTree's. Use `renderVersion` for the
version arm — it reads the **entry** assembly (your CLI), not CommandTree:

```fsharp
| Error VersionRequested -> printfn "%s" (CommandTree.renderVersion "my-cli"); 0
```

`renderVersion prefix` is `"<prefix> <entryAssemblyVersion>"`. `entryAssemblyVersion ()`
resolves `Assembly.GetEntryAssembly()` (falling back to the calling assembly under
some test hosts) and returns `assemblyVersion` of it; `assemblyVersion asm`
prefers `AssemblyInformationalVersionAttribute.InformationalVersion` (keeping any
`+<commit>` build metadata) and falls back to `GetName().Version`.

The package ships an auto-importing `build/CommandTree.targets` that stamps the
build commit id into `AssemblyInformationalVersion` for dev builds, so a binary
built without CI/SourceLink metadata still reports the commit it came from. It
resolves the revision from your version control (it tries Jujutsu, then Git),
sets `SourceRevisionId`, and the SDK folds it in as `+<commit>` (with a `.dirty`
suffix when the working copy has uncommitted changes). It never fails the build
and never overrides a `SourceRevisionId` already set by CI/SourceLink. Opt out
with `-p:CommandTreeStampRevision=false` (whole feature) or
`-p:CommandTreeStampDirty=false` (dirty marker only).

## Fish completions

```fsharp
FishCompletions.generateContent tree "my-tool"  // Generate .fish content
FishCompletions.writeToFile tree "my-tool"      // Write to ~/.config/fish/completions/
FishCompletions.installHook "my-tool"           // Auto-update hook in conf.d
```

## Build-time analyzer

`CommandTree.Analyzers` is an optional
[FSharp.Analyzers.SDK](https://github.com/ionide/FSharp.Analyzers.SDK) package
that flags command-DU shape errors at edit/build time — as editor squiggles, in
`fsharp-analyzers` CLI runs, and via analyzer-aware build tooling — instead of at
runtime startup. It reports the same shape problems `CommandReflection.fromUnion*`
would otherwise reject when the tree is built:

- **CT001 — unsupported field type:** a case field (or arg-group record field)
  whose type the parser can't handle. The message names the case, field,
  offending type, and the supported set.
- **CT002 — list-field placement:** a list field that isn't last, or more than
  one list field in a single case.

Both are warnings (the package is opt-in and never fails a build on its own). The
analyzer recurses into nested subcommand unions and arg-group records, and also
validates the global-flags DU. Add a reference to `CommandTree.Analyzers` and
point your analyzer host at it; the analyzer finds every `fromUnion`,
`fromUnionWithEnv`, `fromUnionWithGlobals`, and `fromUnionWithGlobalsAndEnv` call
and checks the DU it is given.

Host compatibility: the analyzer is built against this repo's FSharp.Core 10.1 /
FCS 43.x. The `fsharp-analyzers` 0.36.0 CLI is pinned to FSharp.Core 10.0 / FCS
43.10 and cannot currently load it (an ABI version skew); hosts that supply a
matching FCS load it without issue.
</content>
