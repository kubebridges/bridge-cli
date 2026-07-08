module Server.Cli.Tests.ParserTests

open Xunit
open FsUnit.Xunit
open Argu
open Server.Cli.Cli

let private parse (args: string list) =
    let parser = createParser ()
    parser.ParseCommandLine(inputs = Array.ofList args, raiseOnUsage = false)

// ──────────────────────────────────────────────────────────────
// Top-level commands
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``login parses with no url`` () =
    let r = parse [ "login" ]

    match r.TryGetSubCommand() with
    | Some(Login loginArgs) -> loginArgs.TryGetResult LoginArgs.Url |> should equal (None: string option)
    | other -> failwith $"expected Login, got {other}"

[<Fact>]
let ``login parses with url flag`` () =
    let r =
        parse
            [
                "login"
                "--url"
                "https://dev.bridgemcp.net"
            ]

    match r.TryGetSubCommand() with
    | Some(Login loginArgs) ->
        loginArgs.TryGetResult LoginArgs.Url
        |> should equal (Some "https://dev.bridgemcp.net")
    | other -> failwith $"expected Login, got {other}"

[<Fact>]
let ``logout parses`` () =
    let r = parse [ "logout" ]
    r.TryGetSubCommand() |> should equal (Some Logout)

[<Fact>]
let ``status parses without json`` () =
    let r = parse [ "status" ]

    match r.TryGetSubCommand() with
    | Some(Status statusArgs) -> statusArgs.Contains StatusArgs.Json |> should equal false
    | other -> failwith $"expected Status, got {other}"

[<Fact>]
let ``status parses with json flag`` () =
    let r = parse [ "status"; "--json" ]

    match r.TryGetSubCommand() with
    | Some(Status statusArgs) -> statusArgs.Contains StatusArgs.Json |> should equal true
    | other -> failwith $"expected Status, got {other}"

// ──────────────────────────────────────────────────────────────
// agents subcommands
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``agents list parses with json`` () =
    let r = parse [ "agents"; "list"; "--json" ]

    match r.TryGetSubCommand() with
    | Some(Agents agentsArgs) ->
        match agentsArgs.TryGetSubCommand() with
        | Some(AgentsArgs.List listArgs) -> listArgs.Contains AgentsListArgs.Json |> should equal true
        | other -> failwith $"expected List, got {other}"
    | other -> failwith $"expected Agents, got {other}"

[<Fact>]
let ``agents deploy parses name and package positionally`` () =
    let r =
        parse
            [
                "agents"
                "deploy"
                "my-agent"
                "pkg.zip"
            ]

    match r.TryGetSubCommand() with
    | Some(Agents agentsArgs) ->
        match agentsArgs.TryGetSubCommand() with
        | Some(AgentsArgs.Deploy deployArgs) ->
            let name, pkg = deployArgs.GetResult AgentsDeployArgs.Target
            name |> should equal "my-agent"
            pkg |> should equal "pkg.zip"
        | other -> failwith $"expected Deploy, got {other}"
    | other -> failwith $"expected Agents, got {other}"
