module CommandTree.Tests.TestHelpers

open CommandTree

let getLeaf (tree: CommandTree<'Cmd>) (name: string) =
    match tree with
    | CommandTree.Group group ->
        group.Children
        |> List.find (fun c -> CommandTree.name c = name)
        |> function
            | CommandTree.Leaf leaf -> leaf
            | _ -> failwith $"Expected leaf for '%s{name}'"
    | _ -> failwith "Expected group"
