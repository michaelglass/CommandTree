namespace CommandTree

open System
open System.IO

/// Generic fish shell completion file generation and installation
module FishCompletions =
    /// Generate complete .fish file content from a command tree.
    /// Returns a string containing the full fish completion script including
    /// header comments, file completion disabling, and all command/argument completions.
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

    /// Install a fish conf.d hook that auto-regenerates completions when entering
    /// the project directory. Creates ~/.config/fish/conf.d/{cmdName}-completions.fish.
    let installHook (cmdName: string) =
        UI.title "Install Fish Completions"

        UI.info "Generating completions..."

        let (code, stdout, stderr) = Process.runSilent $"./%s{cmdName}" "fish generate"

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

        let hookContent =
            $"""# Auto-regenerate completions when entering the project directory
# Installed by: ./%s{cmdName} fish install

function __%s{cmdName.Replace(".", "_")}_update_completions --on-variable PWD
    if test -f "$PWD/%s{cmdName}"
        ./%s{cmdName} fish generate 2>/dev/null
        source ~/.config/fish/completions/%s{cmdName}.fish 2>/dev/null
    end
end
"""

        File.WriteAllText(confDFile, hookContent)

        printfn ""
        UI.success "Fish completions installed!"
        printfn ""
        UI.info "The completions will auto-regenerate when you cd into the project directory."
        printfn ""
        UI.info "To apply immediately, run:"
        printfn $"%s{Color.cyan}  source ~/.config/fish/completions/%s{cmdName}.fish%s{Color.reset}"
