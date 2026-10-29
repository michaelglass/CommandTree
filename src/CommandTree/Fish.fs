namespace CommandTree

open System
open System.IO

/// Generic fish shell completion file generation and installation
module FishCompletions =
    /// Generate complete .fish file content from a command tree.
    let generateContent (tree: CommandTree<'Cmd>) (cmdName: string) : string =
        let completions = CommandTree.fishCompletions tree cmdName

        $"""# Fish completions for %s{cmdName}
# Generated automatically from CommandTree

# Disable file completions for command
complete -c %s{cmdName} -f

# Commands, subcommands, and argument completions
%s{completions}"""

    /// Write fish completions to ~/.config/fish/completions/{cmdName}.fish.
    /// Creates the directory if it doesn't exist, then prints reload instructions.
    let writeToFile (tree: CommandTree<'Cmd>) (cmdName: string) =
        UI.title "Generate Fish Completions"
        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let completionsDir = Path.Combine(home, ".config", "fish", "completions")
        let completionsFile = Path.Combine(completionsDir, $"%s{cmdName}.fish")

        if not (Directory.Exists(completionsDir)) then
            Directory.CreateDirectory(completionsDir) |> ignore

        let content = generateContent tree cmdName
        File.WriteAllText(completionsFile, content)
        UI.success $"Generated %s{completionsFile}"
        printfn ""
        UI.info "To reload completions, run:"
        printfn $"%s{Color.cyan}  source %s{completionsFile}%s{Color.reset}"

    /// A watched pattern must reach fish as ONE literal glob relative to the
    /// project root: no whitespace, quoting, expansion or command syntax, no
    /// absolute path and no `..` segment.
    let private validateWatched (pattern: string) =
        let allowed (c: char) =
            Char.IsAsciiLetterOrDigit c || "._-/*?".Contains c

        let segments = pattern.Split '/'

        if
            String.IsNullOrEmpty pattern
            || pattern.StartsWith "/"
            || not (Seq.forall allowed pattern)
            || Array.contains ".." segments
        then
            invalidArg
                "watched"
                $"fish hook: %A{pattern} is not a relative glob of letters, digits and . _ - / * ? without a .. segment"

    /// The conf.d hook `installHook` writes, as text.
    ///
    /// On every change of directory into a project holding `cmdName`, it runs
    /// `./cmdName fish generate` only when the completions file is missing, or
    /// older than `cmdName` itself or than a file matching one of `watched`
    /// (globs relative to the project root; fish's `**` recurses). The
    /// regeneration runs in the background and is disowned, so `cd` never waits
    /// on the CLI, and one runs at a time. The first prompt after it exits
    /// sources the new completions.
    ///
    /// Choose `watched` so that it changes whenever the command tree can: the
    /// sources that define it, or the built assembly that carries it.
    let hookScript (cmdName: string) (watched: string list) : string =
        List.iter validateWatched watched
        let name = cmdName.Replace(".", "_")
        let completions = $"~/.config/fish/completions/%s{cmdName}.fish"

        let inputs =
            ($"\"$root/%s{cmdName}\""
             :: (watched |> List.map (fun pattern -> $"$root/%s{pattern}")))
            |> String.concat " "

        $"""# Auto-regenerate completions when entering the project directory
# Installed by: ./%s{cmdName} fish install
#
# Regenerates only when the completions are missing or older than an input,
# and always in the background: `cd` never waits on the CLI.

function __%s{name}_completions_stale --argument-names root completions
    test -f "$completions"; or return 0
    for input in %s{inputs}
        test "$input" -nt "$completions"; and return 0
    end
    return 1
end

function __%s{name}_update_completions --on-variable PWD
    set -l root $PWD
    test -f "$root/%s{cmdName}"; or return
    if set -q __%s{name}_completions_pid; and kill -0 $__%s{name}_completions_pid 2>/dev/null
        return
    end
    __%s{name}_completions_stale $root %s{completions}; or return
    ./%s{cmdName} fish generate >/dev/null 2>&1 &
    set -g __%s{name}_completions_pid $last_pid
    disown $last_pid
end

function __%s{name}_reload_completions --on-event fish_prompt
    set -q __%s{name}_completions_pid; or return
    kill -0 $__%s{name}_completions_pid 2>/dev/null; and return
    set -e __%s{name}_completions_pid
    test -f %s{completions}; and source %s{completions}
end
"""

    /// Install a fish conf.d hook that regenerates completions when entering
    /// the project directory and they are stale (see `hookScript`). Creates
    /// ~/.config/fish/conf.d/{cmdName}-completions.fish.
    let installHook (cmdName: string) (watched: string list) =
        let hookContent = hookScript cmdName watched
        UI.title "Install Fish Completions"

        UI.info "Generating completions..."

        let code, stdout, stderr = Process.runSilent $"./%s{cmdName}" [ "fish"; "generate" ]

        if code <> 0 then
            UI.fail "Failed to generate completions"

            if not (String.IsNullOrWhiteSpace(stderr)) then
                eprintfn "%s" stderr

            exit 1

        if not (String.IsNullOrWhiteSpace(stdout)) then
            printfn "%s" stdout

        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let confDDir = Path.Combine(home, ".config", "fish", "conf.d")
        let confDFile = Path.Combine(confDDir, $"%s{cmdName}-completions.fish")

        if not (Directory.Exists(confDDir)) then
            Directory.CreateDirectory(confDDir) |> ignore

        UI.info "Creating auto-update hook..."

        File.WriteAllText(confDFile, hookContent)

        printfn ""
        UI.success "Fish completions installed!"
        printfn ""
        UI.info "The completions will auto-regenerate when you cd into the project directory."
        printfn ""
        UI.info "To apply immediately, run:"
        printfn $"%s{Color.cyan}  source ~/.config/fish/completions/%s{cmdName}.fish%s{Color.reset}"
