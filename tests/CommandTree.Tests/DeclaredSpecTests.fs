module CommandTree.Tests.DeclaredSpecTests

open Xunit
open Swensen.Unquote
open CommandTree

type DeclFlag =
    | Verbose
    | [<CmdEnv("LVL")>] LogLevel of string
    | [<CmdEnvRaw("NO_CACHE")>] NoCache

type DeclGlobal =
    | Quiet
    | [<CmdFlag(Name = "profile")>] Profile of string

type OtherGlobal = | Trace

type DeclSub =
    | [<CmdDefault>] Show
    | Build of DeclFlag list

type PlainCmd =
    | Run of DeclFlag list
    | Sub of DeclSub

[<CmdEnvPrefix("APP")>]
type PrefixCmd =
    | Run of DeclFlag list
    | Sub of DeclSub

[<CmdGlobals(typeof<DeclGlobal>)>]
type GlobalsCmd =
    | Run of DeclFlag list
    | Sub of DeclSub

[<CmdEnvPrefix("APP"); CmdGlobals(typeof<DeclGlobal>)>]
type FullCmd =
    | Run of DeclFlag list
    | Sub of DeclSub

[<CmdEnvPrefix(" ")>]
type BlankPrefixCmd = Run of DeclFlag list

/// Every flag's (long name, env var) across the tree, in tree order.
let rec private envNames (tree: CommandTree<'Cmd>) : (string * string option) list =
    match tree with
    | Leaf leaf ->
        leaf.Flags
        |> List.map (fun fi -> fi.LongName, fi.EnvVar |> Option.map (fun e -> e.VarName))
    | Group group -> group.Children |> List.collect envNames

let private ok result =
    match result with
    | Ok value -> value
    | Error errs -> failwith $"Expected Ok, got %A{errs}"

let private expectedEnvNames =
    [ "verbose", Some "APP_VERBOSE"
      "log-level", Some "APP_LVL"
      "no-cache", Some "NO_CACHE"
      "verbose", Some "APP_VERBOSE"
      "log-level", Some "APP_LVL"
      "no-cache", Some "NO_CACHE" ]

[<Fact>]
let ``declared prefix yields the env names the runtime prefix yields`` () =
    let runtime = CommandReflection.fromUnionWithEnv<PlainCmd> "Test" "APP"
    let declared = CommandReflection.fromUnion<PrefixCmd> "Test"

    test <@ envNames runtime = expectedEnvNames @>
    test <@ envNames declared = envNames runtime @>

[<Fact>]
let ``declared prefix binds env vars at parse time`` () =
    System.Environment.SetEnvironmentVariable("APP_LVL", "debug")

    try
        let tree = CommandReflection.fromUnion<PrefixCmd> "Test"
        test <@ CommandTree.parse tree [| "run" |] = Ok(PrefixCmd.Run [ LogLevel "debug" ]) @>
    finally
        System.Environment.SetEnvironmentVariable("APP_LVL", null)

[<Fact>]
let ``declared prefix agreeing with the runtime prefix is accepted`` () =
    let tree = CommandReflection.tryFromUnionWithEnv<PrefixCmd> "Test" "APP" |> ok
    test <@ envNames tree = expectedEnvNames @>

[<Fact>]
let ``declared prefix conflicting with the runtime prefix is a spec error`` () =
    let result =
        CommandReflection.tryFromUnionWithEnv<PrefixCmd> "Test" "OTHER"
        |> Result.map ignore

    test <@ result = Error [ EnvPrefixConflict("APP", "OTHER") ] @>

    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnionWithEnv<PrefixCmd> "Test" "OTHER" |> ignore)

    test <@ ex.Message.Contains("'APP'") && ex.Message.Contains("'OTHER'") @>

[<Fact>]
let ``blank declared prefix is a spec error`` () =
    let result =
        CommandReflection.tryFromUnion<BlankPrefixCmd> "Test" |> Result.map ignore

    test <@ result = Error [ InvalidEnvPrefix " " ] @>

[<Fact>]
let ``declared globals yield the GlobalSpec the runtime globals yield`` () =
    let runtime =
        CommandReflection.fromUnionWithGlobalsAndEnv<PlainCmd, DeclGlobal> "Test" "APP"

    let declared = CommandReflection.fromUnionWithGlobals<FullCmd, DeclGlobal> "Test"

    test <@ declared.GlobalFlags = runtime.GlobalFlags @>
    test <@ envNames declared.Tree = envNames runtime.Tree @>
    test <@ declared.Parse [| "--quiet"; "run" |] = Ok([ Quiet ], FullCmd.Run []) @>

[<Fact>]
let ``declared prefix applies to global flags`` () =
    System.Environment.SetEnvironmentVariable("APP_PROFILE", "ci")

    try
        let spec = CommandReflection.fromUnionWithGlobals<FullCmd, DeclGlobal> "Test"
        test <@ spec.Parse [| "run" |] = Ok([ Profile "ci" ], FullCmd.Run []) @>
    finally
        System.Environment.SetEnvironmentVariable("APP_PROFILE", null)

[<Fact>]
let ``declared globals without a prefix bind no env vars`` () =
    let spec =
        CommandReflection.tryFromUnionWithGlobals<GlobalsCmd, DeclGlobal> "Test" |> ok

    test <@ spec.GlobalFlags |> List.forall (fun fi -> fi.EnvVar.IsNone) @>

[<Fact>]
let ``declared globals conflicting with the type argument is a spec error`` () =
    let result =
        CommandReflection.tryFromUnionWithGlobals<GlobalsCmd, OtherGlobal> "Test"
        |> Result.map ignore

    test <@ result = Error [ GlobalsConflict(typeof<DeclGlobal>, typeof<OtherGlobal>) ] @>

    let ex =
        Assert.Throws<System.InvalidOperationException>(fun () ->
            CommandReflection.fromUnionWithGlobals<GlobalsCmd, OtherGlobal> "Test" |> ignore)

    test <@ ex.Message.Contains("DeclGlobal") && ex.Message.Contains("OtherGlobal") @>

[<Fact>]
let ``declared globals on an entry point without globals is a spec error`` () =
    let plain = CommandReflection.tryFromUnion<GlobalsCmd> "Test" |> Result.map ignore

    let withEnv =
        CommandReflection.tryFromUnionWithEnv<FullCmd> "Test" "APP" |> Result.map ignore

    test <@ plain = Error [ DeclaredGlobalsIgnored typeof<DeclGlobal> ] @>
    test <@ withEnv = Error [ DeclaredGlobalsIgnored typeof<DeclGlobal> ] @>

[<Fact>]
let ``declaration errors are reported together`` () =
    let result =
        CommandReflection.tryFromUnionWithGlobalsAndEnv<FullCmd, OtherGlobal> "Test" "OTHER"
        |> Result.map ignore

    test
        <@
            result = Error
                [ EnvPrefixConflict("APP", "OTHER")
                  GlobalsConflict(typeof<DeclGlobal>, typeof<OtherGlobal>) ]
        @>

[<Fact>]
let ``no declarations: fromUnion binds no env vars, as before`` () =
    let tree = CommandReflection.tryFromUnion<PlainCmd> "Test" |> ok

    test
        <@
            envNames tree = [ "verbose", None
                              "log-level", None
                              "no-cache", Some "NO_CACHE"
                              "verbose", None
                              "log-level", None
                              "no-cache", Some "NO_CACHE" ]
        @>

[<Fact>]
let ``declaration errors format with the declared and passed values`` () =
    test <@ (SpecError.format (EnvPrefixConflict("APP", "OTHER"))).Contains("CmdEnvPrefix") @>
    test <@ (SpecError.format (InvalidEnvPrefix " ")).Contains("CmdEnvPrefix") @>

    let globals =
        SpecError.format (GlobalsConflict(typeof<DeclGlobal>, typeof<OtherGlobal>))

    test <@ globals.Contains("DeclGlobal") && globals.Contains("OtherGlobal") @>

    let ignored = SpecError.format (DeclaredGlobalsIgnored typeof<DeclGlobal>)
    test <@ ignored.Contains("DeclGlobal") && ignored.Contains("fromUnionWithGlobals") @>
