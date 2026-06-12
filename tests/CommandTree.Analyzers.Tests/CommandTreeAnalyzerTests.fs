module CommandTree.Analyzers.Tests.CommandTreeAnalyzerTests

open Xunit
open Swensen.Unquote
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.Testing
open CommandTree.Analyzers.CommandTreeAnalyzer

// -----------------------------------------------------------------------------
// Harness
//
// The analyzer needs the TYPED tree: it recovers the command DU `'Cmd` from the
// generic instantiation at each `CommandReflection.fromUnion*` call site. To exercise
// that against a stable symbol `FullName` (`CommandTree.CommandReflection.fromUnion`)
// without depending on the unpublished CommandTree NuGet package, every fixture embeds
// a minimal `namespace CommandTree` / `module CommandReflection` STUB declaring the four
// constructor signatures. The stub's only job is to make the call sites type-check and
// carry the right FullName + generic args; its bodies are never run.
// -----------------------------------------------------------------------------

/// Stub reproducing the constructor family's namespace, module, names, and generic arity
/// so call sites resolve to `CommandTree.CommandReflection.fromUnion*` (the FullNames the
/// analyzer matches). Bodies are `failwith` — only the signatures matter for type-checking.
let private commandTreeStub =
    """
namespace CommandTree

type CommandTree<'Cmd> = Leaf of 'Cmd | Group

type GlobalSpec<'Globals, 'Cmd> = { Dummy: 'Globals * 'Cmd }

module CommandReflection =
    let fromUnion<'Cmd> (rootDesc: string) : CommandTree<'Cmd> = failwith "stub"

    let fromUnionWithEnv<'Cmd> (rootDesc: string) (envPrefix: string) : CommandTree<'Cmd> = failwith "stub"

    let fromUnionWithGlobals<'Cmd, 'Globals> (rootDesc: string) : GlobalSpec<'Globals, 'Cmd> = failwith "stub"

    let fromUnionWithGlobalsAndEnv<'Cmd, 'Globals> (rootDesc: string) (envPrefix: string) : GlobalSpec<'Globals, 'Cmd> =
        failwith "stub"
"""

/// Project options shared by every test. No extra NuGet packages — the stub supplies the
/// CommandTree surface, FSharp.Core is implicit. Built once; FCS type-checks fast against it.
let private projectOptions =
    (mkOptionsFromProject "net10.0" []).Result

/// Wrap a fixture's declarations in an indented `module UserCode =` under the same namespace
/// as the stub. A `namespace` can't be followed by a top-level (column-0) `let`, so the
/// fixture body must live inside an indented nested module — every non-blank line is indented
/// four spaces. Fixtures therefore stay flat: just the DU(s) and the `fromUnion*` call.
let private wrapUserSource (declarations: string) : string =
    let indented =
        declarations.Replace("\r\n", "\n").Split('\n')
        |> Array.map (fun line -> if line.Trim() = "" then "" else "    " + line)
        |> String.concat "\n"

    "module UserCode =\n" + indented

/// Type-check the stub + the user fixture as one file and run the analyzer over it.
/// Returns the analyzer's diagnostics.
let private analyze (declarations: string) : Message list =
    let source = commandTreeStub + "\n" + wrapUserSource declarations
    let ctx = getContext projectOptions source
    commandTreeAnalyzer ctx |> Async.RunSynchronously

let private codesOf (messages: Message list) =
    messages |> List.map (fun m -> m.Code) |> List.sort

[<Fact>]
let ``PROBE recovers Cmd and flags an unsupported DateTimeOffset field`` () =
    let messages =
        analyze
            """
type Cmd =
    | Stamp of at: System.DateTimeOffset

let tree = CommandReflection.fromUnion<Cmd> "desc"
"""

    test <@ codesOf messages = [ "CT001" ] @>
