module CommandTree.Tests.VersionTests

open System
open System.Reflection
open System.Reflection.Emit
open Xunit
open Swensen.Unquote
open CommandTree

// =============================================================================
// assemblyVersion
// =============================================================================

[<Fact>]
let ``assemblyVersion uses InformationalVersion when present`` () =
    // The test assembly is built with an AssemblyInformationalVersionAttribute.
    let asm = Assembly.GetExecutingAssembly()

    let expected =
        asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion

    test <@ not (String.IsNullOrEmpty expected) @>
    test <@ CommandTree.assemblyVersion asm = expected @>

[<Fact>]
let ``assemblyVersion falls back to identity version when informational version is absent`` () =
    // A dynamically-emitted assembly carries no AssemblyInformationalVersionAttribute,
    // so assemblyVersion must fall back to GetName().Version.
    let name = AssemblyName("CommandTreeFallbackProbe")
    name.Version <- Version(1, 2, 3, 4)
    let asm = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run)

    test <@ isNull (asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()) @>
    test <@ CommandTree.assemblyVersion asm = "1.2.3.4" @>

// =============================================================================
// renderVersion
// =============================================================================

[<Fact>]
let ``renderVersion prefixes the command name and includes a non-empty version`` () =
    let rendered = CommandTree.renderVersion "foo"

    test <@ rendered.StartsWith "foo " @>

    let version = rendered.Substring("foo ".Length)
    test <@ not (String.IsNullOrEmpty version) @>
