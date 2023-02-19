module Path =

  open System.IO

  let isAbsolute (a: string) = Path.IsPathRooted(a)

  let dirname (a: string) = Path.GetDirectoryName(a)

  let name (a: string) = Path.GetFileName(a)

  let nameWithoutExt (a: string) = Path.GetFileNameWithoutExtension(a)

  let resolve a = Path.GetFullPath(a)

  let relative a b = Path.GetRelativePath(a, b)

  let combine a b = Path.Combine(a, b)

  let join a b =
    combine a b |> resolve |> relative (resolve a) |> combine a

module rec FileDeps =

  open System.IO

  let collect file =
    seq { yield! File.ReadLines file }
    |> Seq.map trim
    |> Seq.takeWhile shouldTake
    |> Seq.filter shouldKeep
    |> Seq.map readItem

  let private trim (line: string) = line.Trim()

  let private shouldTake (line: string) =
    line.Length = 0 || line.StartsWith("//")

  let private prefix = "// @after "

  let private shouldKeep (line: string) =
    line.Length > "// @after a.fs".Length && line.StartsWith(prefix)

  let private readItem (line: string) = line.Substring(prefix.Length)

type private Dict<'k, 'v> = System.Collections.Generic.Dictionary<'k, 'v>

type private DepGraph = Dict<string, Option<seq<string>>>

module rec DepGraph =

  type private State =
    {
      Root: string
      RootFullPath: string
      ImportMap: string -> string
      Graph: DepGraph
    }

  let get (file: string) (graph: DepGraph) =
    try
      graph[file]
    with :? System.Collections.Generic.KeyNotFoundException ->
      None

  let build f root entrypoint =
    let graph = new DepGraph()

    Path.join root entrypoint
    |> traverse
         {
           Root = root
           RootFullPath = Path.resolve root
           ImportMap = f
           Graph = graph
         }
    |> ignore

    graph

  let private traverse state (file: string) =
    if state.Graph.ContainsKey(file) then
      state.Graph[file]
    else
      try
        let xs =
          file |> FileDeps.collect |> Seq.map (Path.dirname file |> join state)

        let xs' = Some xs
        state.Graph.Add(file, xs')
        traverseRelative state file xs
        xs'

      with :? System.IO.FileNotFoundException ->
        ignore <| state.Graph.Remove(file)
        None

  let private join state cwd file =
    let file = state.ImportMap file

    if Path.isAbsolute file then
      Path.relative state.RootFullPath file

    else
      file
      |> Path.combine (Path.resolve cwd)
      |> Path.resolve
      |> Path.relative state.RootFullPath
      |> Path.combine state.Root

  let private traverseRelative state (file: string) xs =
    xs
    |> Seq.map (traverse state >> ignore |> toAsync)
    |> Async.Parallel
    |> Async.Ignore
    |> Async.RunSynchronously

  let private toAsync f a = async { return f (a) }

type ResolutionError =
  | EntryPointNotFound
  | NotFound of file: string * from: Option<string>
  | Cycle of file: string * from: List<string>

module rec DepResolver =

  [<Struct>]
  type private State =
    {
      Graph: DepGraph
      Visited: Set<string>
      Resolved: List<string>
      Errors: List<ResolutionError>
    }

  [<Struct>]
  type private Visit =
    {
      Name: string
      Path: Set<string>
      Rest: List<string>
    }

  let resolve root entrypoint graph =
    let { Resolved = resolved; Errors = errors } = flatten root entrypoint graph

    (List.rev resolved, errors)

  let private flatten root entrypoint graph =
    let entrypoint = Path.join root entrypoint

    let visit: Visit =
      {
        Name = entrypoint
        Path = Set.singleton entrypoint
        Rest =
          graph
          |> DepGraph.get entrypoint
          |> Option.map Seq.toList
          |> Option.defaultValue []
      }

    let state: State =
      {
        Graph = graph
        Visited = Set.singleton entrypoint
        Resolved = []
        Errors = []
      }

    foldStackItem visit [] state

  let private foldStack stack state =
    match stack with
    | head :: tail -> foldStackItem head tail state
    | [] -> state

  let private foldStackItem visit stack state =
    match visit.Rest with
    | head :: tail ->
      let visit = { visit with Rest = tail }

      if Set.contains head visit.Path then
        let errors = Cycle(head, Set.toList visit.Path) :: state.Errors
        foldStack (visit :: stack) { state with Errors = errors }
      else if Set.contains head state.Visited then
        foldStackItem visit stack state
      else
        let visited = Set.add head state.Visited
        foldGraphItem head visit stack { state with Visited = visited }

    | [] ->
      let resolved = visit.Name :: state.Resolved
      foldStack stack { state with Resolved = resolved }

  let private foldGraphItem name visit stack state =
    match DepGraph.get name state.Graph with
    | Some seq ->
      let visit' =
        {
          Name = name
          Path = Set.add name visit.Path
          Rest = Seq.toList seq
        }

      foldStack (visit' :: visit :: stack) state

    | None ->
      let errors =
        NotFound(name, List.tryHead <| Set.toList visit.Path) :: state.Errors

      foldStack (visit :: stack) { state with Errors = errors }

module rec Cli =

  open System
  open System.IO

  [<EntryPoint>]
  let main args =
    let ok =
      args
      |> Seq.ofArray
      |> Seq.map resolve
      |> Async.Parallel
      |> Async.RunSynchronously
      |> Seq.collect report
      |> Seq.isEmpty

    if ok then 0 else 1

  let private resolve fsproj =
    async {
      let root = Path.dirname fsproj

      return
        match getEntryPoint root with
        | Some entrypoint -> resolve' fsproj root entrypoint
        | None -> (fsproj, [ EntryPointNotFound ])
    }

  let private getEntryPoint root =
    [ Path.join root "Main.fs"; Path.join root "Module.txt" ]
    |> List.tryFind File.Exists
    |> Option.map (Path.relative root)

  let private resolve' fsproj root entrypoint =
    let mapper = fun a -> a

    let (resolved, errors) =
      entrypoint
      |> DepGraph.build mapper root
      |> DepResolver.resolve root entrypoint

    let out = Path.join root $"{Path.nameWithoutExt fsproj}.targets"

    File.WriteAllText(out, render root resolved)
    File.SetLastWriteTime(fsproj, DateTime.Now)

    (fsproj, errors)

  let private render root entries =
    let files =
      entries
      |> List.filter (fun file -> not <| file.EndsWith(".txt"))
      |> List.map (Path.relative root)
      |> String.concat ";"

    $"<Project><ItemGroup><Compile Include=\"{files}\"/></ItemGroup></Project>"

  let private report (fsproj, errors) =
    List.iter (report' fsproj) errors
    errors

  let private report' fsproj error =
    match error with
    | EntryPointNotFound ->
      let txt = red "not found"
      printfn "%s" <| $"Entrypoint file {txt} for: '{em fsproj}'"
    | NotFound(file, Some path) ->
      printfn "%s" <| $"{reportNotFound file}\nImported at: {em path}"
    | NotFound(file, None) -> printfn "%s" <| reportNotFound file
    | Cycle(file, path) -> reportCycle file path

  let private reportNotFound file =
    let txt = red "not found"
    $"File {txt} at: {em file}"

  let private reportCycle file path =
    let cycle = red "Cycle"
    printfn $"{cycle} at: {em file}"
    foldTrail file false path

  let private foldTrail file found xs =
    match xs with
    | head :: [] ->
      if head = file then
        printfn $" ─▶ {red' head} (requires itself)"
      else
        printfn $"╰─┨ {red' head}"

    | head :: tail ->
      if head = file then
        printfn $"╭─▶ {red' head}"
        foldTrail file true tail
      else if found then
        printfn $"│   {em head}"
        foldTrail file found tail
      else
        printfn $"    {em head}"
        foldTrail file false tail

    | [] -> ()

  let private red' txt = red <| wrap txt

  let private red txt = "\x1b[31m" + txt + "\x1b[0m"

  let private em txt = "\x1b[34m" + wrap txt + "\x1b[0m"

  let private wrap txt = "'" + txt + "'"

// Cli.main (Array.tail fsi.CommandLineArgs) |> ignore
