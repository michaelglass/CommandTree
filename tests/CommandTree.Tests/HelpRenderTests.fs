module CommandTree.Tests.HelpRenderTests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open Swensen.Unquote
open CommandTree
open CommandTree.Tests.UITests

// =============================================================================
// A small tree shaped like a real build CLI: long prose descriptions, a usage
// too wide for the description column, nested groups with a default.
// =============================================================================

type RenderFlag = | [<CmdFlag(Description = "Also write an MSBuild binary log of the same build")>] Binlog

type RenderToolsCmd =
    | [<CmdDefault;
        Cmd("Confirm the gate verdict. Runs the full suite and compares it with the stored verdict, refusing a stale one.")>] Confirm
    | [<Cmd("Show status")>] Status

type RenderDbCmd =
    | [<Cmd("Apply pending migrations to the named database, e.g. staging. Never edits an applied one.")>] Migrate of
        database: string
    | [<Cmd("Open a psql shell")>] Shell

type RenderCmd =
    | [<Cmd("Compile only. Type-check every project or one named project, and nothing else: no tests, no database, no verdict.")>] Compile of
        project: string option *
        flags: RenderFlag list
    | [<Cmd("Dev server")>] Serve
    | [<Cmd("Nested tools")>] Tools of RenderToolsCmd
    | [<Cmd("Database management")>] Db of RenderDbCmd
    | [<Cmd("Start the Cloudflare tunnel")>] Tunnel

let private tree = CommandReflection.fromUnion<RenderCmd> "Render CLI"

let private span style text : HelpSpan = { Text = text; Style = style }
let private text = span HelpStyle.Text

let private row depth label (description: string list) : HelpRow =
    {
        Depth = depth
        Label = [ span HelpStyle.Command label ]
        Description = description |> List.map (fun line -> [ text line ])
    }

let private plainAt (columns: int) (doc: HelpDoc) =
    HelpDoc.toText (HelpWidth.create columns) Plain doc

let private escapes = Regex("\x1b\\[[0-9;]*m")

let private visibleLines (rendered: string) =
    escapes.Replace(rendered, "").Split('\n') |> List.ofArray

let private allDocs =
    [
        CommandTree.helpFullDoc tree "./build.fsx"
        CommandTree.helpDoc tree [] "./build.fsx"
        CommandTree.helpForPathDoc tree [ "compile" ] "./build.fsx"
        CommandTree.helpForPathDoc tree [ "tools" ] "./build.fsx"
        CommandTree.helpWithGlobalsDoc tree [] "./build.fsx"
    ]

// =============================================================================
// Width: wrapping, hanging indent, a long usage moving its description down
// =============================================================================

[<Theory>]
[<InlineData(20)>]
[<InlineData(40)>]
[<InlineData(60)>]
[<InlineData(100)>]
let ``no rendered line is longer than the width, plain or styled`` (columns: int) =
    for doc in allDocs do
        for mode in [ Plain; Styled ] do
            let longest =
                HelpDoc.toText (HelpWidth.create columns) mode doc
                |> visibleLines
                |> List.map String.length
                |> List.max

            test <@ longest <= columns @>

[<Fact>]
let ``a word longer than the line is broken rather than overflowing`` () =
    let doc = [ HelpBlock.Line(0, 0, [ text (String.replicate 50 "x") ]) ]

    test
        <@
            plainAt 20 doc =
                String.replicate 20 "x"
                + "\n"
                + String.replicate 20 "x"
                + "\n"
                + String.replicate 10 "x"
        @>

[<Fact>]
let ``a width below the minimum is raised to it`` () =
    test <@ HelpWidth.columns (HelpWidth.create 3) = HelpWidth.minimum @>
    test <@ HelpWidth.columns (HelpWidth.create 77) = 77 @>

[<Fact>]
let ``a wrapped description hangs under its own column`` () =
    let doc =
        [
            HelpBlock.Table [ [ row 0 "run" [ "alpha beta gamma delta epsilon zeta eta theta" ] ] ]
        ]

    test <@ plainAt 30 doc = "  run  alpha beta gamma delta\n" + "       epsilon zeta eta theta" @>

[<Fact>]
let ``a label too long for the column puts its description on the next line`` () =
    let doc =
        [
            HelpBlock.Table
                [
                    [
                        row 0 "a" [ "short one" ]
                        row 0 "compile [project]" [ "moved down" ]
                        row 0 "b" [ "after" ]
                    ]
                ]
        ]

    // Width 60 caps the description column at 20 (a third of the width).
    let at20 (label: string) = label.PadRight 20

    test
        <@
            plainAt 60 doc =
                at20 "  a"
                + "short one\n"
                + "  compile [project]\n"
                + at20 ""
                + "moved down\n"
                + at20 "  b"
                + "after"
        @>

[<Fact>]
let ``the description column is capped even without a width`` () =
    let label = String.replicate 40 "l"
    let doc = [ HelpBlock.Table [ [ row 0 label [ "desc" ] ] ] ]

    test <@ HelpDoc.plain doc = $"  %s{label}\n" + String.replicate HelpDoc.maxDescriptionColumn " " + "desc" @>

[<Fact>]
let ``a wrapped line hangs under the given indent`` () =
    let doc = [ HelpBlock.Line(2, 4, [ text "one two three four five six" ]) ]
    test <@ plainAt 20 doc = "  one two three four\n    five six" @>

[<Fact>]
let ``each depth indents two more columns and sections are separated by one blank line`` () =
    let doc =
        [
            HelpBlock.Table
                [
                    [ row 0 "a" [ "first" ] ]
                    [ row 0 "g" [ "group" ]; row 1 "child" [ "nested" ]; row 2 "leaf" [ "deeper" ] ]
                ]
        ]

    // The longest label ends at column 10 ("      leaf"), so descriptions start at 12.
    let at12 (label: string) = label.PadRight 12

    test
        <@
            HelpDoc.plain doc =
                at12 "  a"
                + "first\n"
                + "\n"
                + at12 "  g"
                + "group\n"
                + at12 "    child"
                + "nested\n"
                + at12 "      leaf"
                + "deeper"
        @>

[<Fact>]
let ``an empty description leaves the label alone without trailing spaces`` () =
    let doc = [ HelpBlock.Table [ [ row 0 "a" [ "" ]; row 0 "bb" [ "x" ] ] ] ]
    test <@ HelpDoc.plain doc = "  a\n  bb  x" @>

// =============================================================================
// The overview: one row per command, first sentence only, blank line per group
// =============================================================================

[<Fact>]
let ``helpFull lists first sentences and separates top-level groups with a blank line`` () =
    // The widest label, "  compile [project] [options]", ends at column 29, so every
    // description starts at 31, two columns past it.
    let at31 (label: string) = label.PadRight 31

    let expected =
        "Usage: ./build.fsx <command>\n"
        + "\n"
        + "Commands:\n"
        + at31 "  compile [project] [options]"
        + "Compile only.\n"
        + at31 "  serve"
        + "Dev server\n"
        + "\n"
        + at31 "  tools"
        + "Nested tools\n"
        + at31 "    confirm"
        + "Confirm the gate verdict. (default)\n"
        + at31 "    status"
        + "Show status\n"
        + "\n"
        + at31 "  db"
        + "Database management\n"
        + at31 "    migrate <database>"
        + "Apply pending migrations to the named database, e.g. staging.\n"
        + at31 "    shell"
        + "Open a psql shell\n"
        + "\n"
        + at31 "  tunnel"
        + "Start the Cloudflare tunnel"

    test <@ CommandTree.helpFull tree "./build.fsx" = expected @>

[<Fact>]
let ``help for one command keeps its full description`` () =
    let help = CommandTree.helpForPath tree [ "compile" ] "./build.fsx"
    test <@ help.Contains("Type-check every project or one named project", StringComparison.Ordinal) @>

[<Fact>]
let ``group help lists each child with its first sentence`` () =
    // The widest label, "  migrate <database>", ends at column 20; descriptions start at 22.
    let at22 (label: string) = label.PadRight 22

    let expected =
        "Usage: ./build.fsx db <command>\n"
        + "\n"
        + "Database management\n"
        + "\n"
        + "Commands:\n"
        + at22 "  migrate <database>"
        + "Apply pending migrations to the named database, e.g. staging.\n"
        + at22 "  shell"
        + "Open a psql shell"

    test <@ CommandTree.helpForPath tree [ "db" ] "./build.fsx" = expected @>

[<Fact>]
let ``root help lists each command with its first sentence, with or without globals`` () =
    let rootHelp = CommandTree.help tree [] "./build.fsx"
    let globalsHelp = CommandTree.helpWithGlobals tree [] "./build.fsx"

    for help in [ rootHelp; globalsHelp ] do
        test <@ help.Contains("Compile only.", StringComparison.Ordinal) @>
        test <@ not (help.Contains("Type-check every project", StringComparison.Ordinal)) @>

[<Fact>]
let ``a leaf's own help keeps its full description while its group shows the first sentence`` () =
    let groupHelp = CommandTree.helpForPath tree [ "db" ] "./build.fsx"
    let leafHelp = CommandTree.helpForPath tree [ "db"; "migrate" ] "./build.fsx"
    test <@ not (groupHelp.Contains("Never edits an applied one.", StringComparison.Ordinal)) @>
    test <@ leafHelp.Contains("Never edits an applied one.", StringComparison.Ordinal) @>

[<Fact>]
let ``the string help is the plain unbounded rendering of its document`` () =
    test <@ CommandTree.helpFull tree "x" = HelpDoc.plain (CommandTree.helpFullDoc tree "x") @>
    test <@ CommandTree.help tree [] "x" = HelpDoc.plain (CommandTree.helpDoc tree [] "x") @>
    test <@ CommandTree.helpForPath tree [ "db" ] "x" = HelpDoc.plain (CommandTree.helpForPathDoc tree [ "db" ] "x") @>
    test <@ CommandTree.helpWithGlobals tree [] "x" = HelpDoc.plain (CommandTree.helpWithGlobalsDoc tree [] "x") @>

[<Theory>]
[<InlineData("Ship it. Then more.", "Ship it.")>]
[<InlineData("Ship it! Then more.", "Ship it!")>]
[<InlineData("Ready? Then go.", "Ready?")>]
[<InlineData("No terminator", "No terminator")>]
[<InlineData("Use e.g. a flag. Next", "Use e.g. a flag.")>]
[<InlineData("Version 1.2 is out. Yes", "Version 1.2 is out.")>]
[<InlineData("Run `build.fsx check`. A green", "Run `build.fsx check`.")>]
[<InlineData("Ends with a period.", "Ends with a period.")>]
[<InlineData("Confirm the gate\nverdict here. More", "Confirm the gate verdict here.")>]
[<InlineData("Title line\n\nBody text. More", "Title line")>]
[<InlineData("Windows\r\nline. Two", "Windows line.")>]
[<InlineData("", "")>]
let ``firstSentence keeps the opening sentence of the first paragraph`` (description: string, expected: string) =
    test <@ HelpDoc.firstSentence description = expected @>

// =============================================================================
// Styles
// =============================================================================

[<Fact>]
let ``styled rendering wraps each styled run in its escape code and plain drops them`` () =
    let lines =
        [
            [
                span HelpStyle.Heading "Usage:"
                text " x "
                span HelpStyle.Group "grp"
                text " "
                span HelpStyle.Command "cmd"
                span HelpStyle.Argument " <a>"
                span HelpStyle.Secondary " (default)"
            ]
        ]

    test
        <@
            HelpDoc.render Styled lines =
                "\x1b[1mUsage:\x1b[0m x \x1b[1;36mgrp\x1b[0m \x1b[1mcmd\x1b[0m\x1b[32m <a>\x1b[0m\x1b[2m (default)\x1b[0m"
        @>

    test <@ HelpDoc.render Plain lines = "Usage: x grp cmd <a> (default)" @>

[<Fact>]
let ``adjacent runs of one style share one escape sequence`` () =
    let lines =
        [
            [ span HelpStyle.Command "a"; span HelpStyle.Command "b"; text "c"; text "d" ]
        ]

    test <@ HelpDoc.render Styled lines = "\x1b[1mab\x1b[0mcd" @>

[<Fact>]
let ``the overview styles groups, commands and arguments differently`` () =
    let styled =
        HelpDoc.toText HelpWidth.unbounded Styled (CommandTree.helpFullDoc tree "x")

    test <@ styled.Contains("\x1b[1;36mtools\x1b[0m", StringComparison.Ordinal) @>
    test <@ styled.Contains("\x1b[1mserve\x1b[0m", StringComparison.Ordinal) @>
    test <@ styled.Contains("\x1b[32m<database>\x1b[0m", StringComparison.Ordinal) @>
    test <@ styled.Contains("\x1b[2m(default)\x1b[0m", StringComparison.Ordinal) @>

[<Fact>]
let ``a description line keeps its leading indent on every wrapped line`` () =
    let doc = [ HelpBlock.Line(0, 0, [ text "  - one two three four five" ]) ]
    test <@ plainAt 20 doc = "  - one two three\n  four five" @>

// =============================================================================
// Environment: parsed once, decisions are pure
// =============================================================================

let private terminal width height noColor : HelpEnvironment =
    {
        Target = Terminal { Width = width; Height = height }
        NoColor = noColor
        Pager = HelpEnvironment.defaultPager
    }

let private redirected: HelpEnvironment =
    {
        Target = Redirected
        NoColor = false
        Pager = HelpEnvironment.defaultPager
    }

[<Fact>]
let ``colour only on a terminal without NO_COLOR`` () =
    test <@ HelpEnvironment.colorMode (terminal 80 24 false) = Styled @>
    test <@ HelpEnvironment.colorMode (terminal 80 24 true) = Plain @>
    test <@ HelpEnvironment.colorMode redirected = Plain @>

[<Fact>]
let ``a terminal wraps at its width and redirected output does not wrap`` () =
    test <@ HelpEnvironment.width (terminal 77 24 false) = HelpWidth.create 77 @>
    test <@ HelpEnvironment.width redirected = HelpWidth.unbounded @>

[<Fact>]
let ``the pager runs only for a terminal shorter than the text`` () =
    let tall = terminal 80 10 false
    test <@ HelpEnvironment.delivery tall 9 = Direct @>
    test <@ HelpEnvironment.delivery tall 10 = Paged HelpEnvironment.defaultPager @>
    test <@ HelpEnvironment.delivery tall 500 = Paged HelpEnvironment.defaultPager @>
    test <@ HelpEnvironment.delivery redirected 500 = Direct @>

/// A variable lookup over a fixed set of (name, value) pairs.
let private variables (pairs: (string * string) list) (name: string) =
    pairs |> List.tryFind (fst >> (=) name) |> Option.map snd

let private parseVars pairs =
    HelpEnvironment.parse false 80 24 (variables pairs)

[<Fact>]
let ``parse reads NO_COLOR as set only when non-empty`` () =
    let noColor pairs = (parseVars pairs).NoColor

    test <@ not (noColor []) @>
    test <@ not (noColor [ "NO_COLOR", "" ]) @>
    test <@ noColor [ "NO_COLOR", "1" ] @>
    test <@ noColor [ "NO_COLOR", "false" ] @>

let private pager program arguments : PagerCommand =
    {
        Program = program
        Arguments = arguments
        Environment = [ "LESS", "FRX" ]
    }

[<Fact>]
let ``parse splits PAGER into program and arguments and defaults to less -R`` () =
    let parsed value = (parseVars [ "PAGER", value ]).Pager

    test <@ (parseVars []).Pager = pager "less" [ "-R" ] @>
    test <@ parsed "  " = pager "less" [ "-R" ] @>
    test <@ parsed "most" = pager "most" [] @>
    test <@ parsed " less  -R -S " = pager "less" [ "-R"; "-S" ] @>

[<Fact>]
let ``the pager gets LESS=FRX only when LESS is unset`` () =
    test <@ (parseVars []).Pager.Environment = [ "LESS", "FRX" ] @>
    test <@ List.isEmpty (parseVars [ "LESS", "-S" ]).Pager.Environment @>
    test <@ List.isEmpty (parseVars [ "LESS", "" ]).Pager.Environment @>

[<Fact>]
let ``parse treats a redirected stdout as Redirected whatever size it reports`` () =
    test <@ (HelpEnvironment.parse true 120 50 (variables [])).Target = Redirected @>

[<Fact>]
let ``parse falls back to 80x24 for a terminal that reports no size`` () =
    test <@ (HelpEnvironment.parse false 0 0 (variables [])).Target = Terminal { Width = 80; Height = 24 } @>
    test <@ (HelpEnvironment.parse false 132 43 (variables [])).Target = Terminal { Width = 132; Height = 43 } @>

[<Fact>]
let ``plan renders for the environment and picks the delivery`` () =
    let doc = CommandTree.helpFullDoc tree "x"
    let onTerminal = HelpEnvironment.plan (terminal 60 5 false) doc
    test <@ onTerminal.Rendered.Contains("\x1b[", StringComparison.Ordinal) @>
    test <@ visibleLines onTerminal.Rendered |> List.forall (fun l -> l.Length <= 60) @>
    test <@ onTerminal.Delivery = Paged HelpEnvironment.defaultPager @>

    let piped = HelpEnvironment.plan redirected doc

    test
        <@
            piped =
                {
                    Rendered = CommandTree.helpFull tree "x"
                    Delivery = Direct
                }
        @>

// =============================================================================
// The I/O edge, driven through a fake console
// =============================================================================

/// A console whose every effect is recorded or scripted.
[<NoComparison; NoEquality>]
type private FakeConsole =
    {
        Console: HelpConsole
        Written: ResizeArray<string>
        Paged: ResizeArray<PagerCommand * string>
    }

let private fakeConsole redirected (windowSize: unit -> int * int) (outcome: PagerOutcome) =
    let written = ResizeArray<string>()
    let paged = ResizeArray<PagerCommand * string>()

    {
        Console =
            {
                IsOutputRedirected = fun () -> redirected
                WindowSize = windowSize
                Variable = variables [ "PAGER", "most" ]
                WriteLine = written.Add
                Page =
                    fun command text ->
                        paged.Add((command, text))
                        outcome
            }
        Written = written
        Paged = paged
    }

let private noSize () : int * int =
    failwith "the size of a redirected stdout was asked for"

[<Fact>]
let ``detectWith does not ask a redirected stdout for its size`` () =
    let fake = fakeConsole true noSize PagerShown
    let env = HelpPrinter.detectWith fake.Console
    test <@ env.Target = Redirected @>
    test <@ env.Pager.Program = "most" @>

[<Fact>]
let ``detectWith reads a terminal's size`` () =
    let fake = fakeConsole false (fun () -> 120, 40) PagerShown
    test <@ (HelpPrinter.detectWith fake.Console).Target = Terminal { Width = 120; Height = 40 } @>

[<Fact>]
let ``detectWith takes a terminal that cannot report its size to be 80x24`` () =
    let fake = fakeConsole false (fun () -> raise (IOException "no console")) PagerShown

    test <@ (HelpPrinter.detectWith fake.Console).Target = Terminal { Width = 80; Height = 24 } @>

let private plan delivery : HelpPlan =
    {
        Rendered = "text"
        Delivery = delivery
    }

[<Fact>]
let ``deliverWith writes a direct plan once and runs no pager`` () =
    let fake = fakeConsole false noSize PagerShown
    HelpPrinter.deliverWith fake.Console (plan Direct)
    test <@ List.ofSeq fake.Written = [ "text" ] @>
    test <@ fake.Paged.Count = 0 @>

[<Fact>]
let ``deliverWith hands a paged plan to the pager and writes nothing itself`` () =
    let fake = fakeConsole false noSize PagerShown
    HelpPrinter.deliverWith fake.Console (plan (Paged(pager "most" [])))
    test <@ List.ofSeq fake.Paged = [ pager "most" [], "text" ] @>
    test <@ fake.Written.Count = 0 @>

[<Fact>]
let ``deliverWith writes the text itself when the pager cannot start`` () =
    let fake = fakeConsole false noSize PagerUnavailable
    HelpPrinter.deliverWith fake.Console (plan (Paged(pager "most" [])))
    test <@ fake.Paged.Count = 1 @>
    test <@ List.ofSeq fake.Written = [ "text" ] @>

// =============================================================================
// The real console: stdout and a real pager process
// =============================================================================

let private shellPager (script: string) : PagerCommand =
    {
        Program = "sh"
        Arguments = [ "-c"; script ]
        Environment = [ "LESS", "FRX" ]
    }

[<Fact>]
let ``deliver prints a direct plan to stdout`` () =
    let out, () = captureStdout (fun () -> HelpPrinter.deliver (plan Direct))
    test <@ out.Replace("\r\n", "\n") = "text\n" @>

[<Fact>]
let ``deliver prints directly when the pager cannot start`` () =
    let missing = pager "/nonexistent/commandtree-pager" []
    let out, () = captureStdout (fun () -> HelpPrinter.deliver (plan (Paged missing)))
    test <@ out.Replace("\r\n", "\n") = "text\n" @>

[<Fact>]
let ``deliver pipes a paged plan into the pager's stdin with the pager's variables`` () =
    let path = Path.GetTempFileName()

    try
        let script = $"printf '%%s:' \"$LESS\" > '%s{path}'; cat >> '%s{path}'"

        let out, () =
            captureStdout (fun () -> HelpPrinter.deliver (plan (Paged(shellPager script))))

        test <@ out = "" @>
        test <@ File.ReadAllText path = "FRX:text\n" @>
    finally
        File.Delete path

[<Fact>]
let ``detect reads the process environment into a parsed environment`` () =
    let env = HelpPrinter.detect ()

    let expectedNoColor =
        not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "NO_COLOR"))

    test <@ env.NoColor = expectedNoColor @>
    test <@ env.Pager.Program <> "" @>

[<Fact>]
let ``the real console reads the window size or says it cannot`` () =
    // The test host's stdout is not a terminal, so the platform may report a size or
    // throw; either way the call is the console's own, and detectWith handles both.
    let outcome =
        try
            Ok(HelpPrinter.system.WindowSize())
        with error ->
            Error error

    match outcome with
    | Ok(width, height) -> test <@ width >= 0 && height >= 0 @>
    | Error _ -> ()

[<Fact>]
let ``deliver carries on when the pager quits before reading everything`` () =
    let huge = String.replicate 1_000_000 "x"

    let out, () =
        captureStdout (fun () ->
            HelpPrinter.deliver
                {
                    Rendered = huge
                    Delivery = Paged(shellPager "exit 0")
                })

    test <@ out = "" @>

[<Fact>]
let ``printWith renders for the given environment and prints it`` () =
    let doc = CommandTree.helpFullDoc tree "x"
    let out, () = captureStdout (fun () -> HelpPrinter.printWith redirected doc)
    test <@ out.Replace("\r\n", "\n") = HelpDoc.plain doc + "\n" @>

[<Fact>]
let ``print writes a short document to stdout`` () =
    let out, () =
        captureStdout (fun () -> HelpPrinter.print [ HelpBlock.Line(0, 0, [ text "tiny" ]) ])

    test <@ escapes.Replace(out, "").Trim() = "tiny" @>

// =============================================================================
// Edges of the layout
// =============================================================================

[<Fact>]
let ``an empty line renders as an empty line`` () =
    let doc =
        [
            HelpBlock.Line(0, 0, [])
            HelpBlock.Line(0, 0, [ text "" ])
            HelpBlock.Line(0, 0, [ text "x" ])
        ]

    test <@ HelpDoc.plain doc = "\n\nx" @>

[<Fact>]
let ``a word spanning two styles breaks where the line ends`` () =
    let doc =
        [
            HelpBlock.Line(
                0,
                0,
                [
                    span HelpStyle.Command (String.replicate 20 "a")
                    span HelpStyle.Argument "bbbbb"
                ]
            )
        ]

    test <@ plainAt 20 doc = String.replicate 20 "a" + "\nbbbbb" @>

[<Fact>]
let ``indents deeper than half the width stop there`` () =
    let doc = [ HelpBlock.Line(30, 30, [ text "one two three four five six seven" ]) ]
    let pad = String.replicate 10 " "

    test <@ plainAt 20 doc = pad + "one two\n" + pad + "three four\n" + pad + "five six\n" + pad + "seven" @>

[<Fact>]
let ``a flag without a short name lists only its long name`` () =
    let verbose =
        {
            LongName = "verbose"
            ShortName = None
            TypeName = "bool"
            Arity = Nullary
            IsRepeatable = false
            Description = "Talk more"
            EnvVar = None
        }

    let help = CommandTree.helpWithGlobals tree [ verbose ] "x"
    test <@ help.Contains("Global options:\n  --verbose  Talk more\n", StringComparison.Ordinal) @>

let private subtree path =
    match CommandTree.findByPath tree path with
    | Some node -> node
    | None -> failwith $"no node at %A{path}"

[<Fact>]
let ``helpFull of a single command lists just that command`` () =
    test <@ CommandTree.helpFull (subtree [ "serve" ]) "x" = "Usage: x <command>\n\nCommands:\n  serve  Dev server" @>

[<Fact>]
let ``helpFull of a named group lists the group and its children`` () =
    let at13 (label: string) = label.PadRight 13

    test
        <@
            CommandTree.helpFull (subtree [ "tools" ]) "x" =
                "Usage: x <command>\n\nCommands:\n"
                + at13 "  tools"
                + "Nested tools\n"
                + at13 "    confirm"
                + "Confirm the gate verdict. (default)\n"
                + at13 "    status"
                + "Show status"
        @>
