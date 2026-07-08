module Server.Cli.Tests.OutputTests

open Xunit
open FsUnit.Xunit
open Server.Cli.Config
open Server.Cli.Output

// ──────────────────────────────────────────────────────────────
// Error message formatting
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``formatError NotLoggedIn mentions login`` () =
    formatError NotLoggedIn |> should haveSubstring "login"

[<Fact>]
let ``formatError ApiError includes status and message`` () =
    let msg = formatError (ApiError(403, "Forbidden."))
    msg |> should haveSubstring "403"
    msg |> should haveSubstring "Forbidden."

[<Fact>]
let ``formatError AgentNotFound names the agent`` () =
    formatError (AgentNotFound "ghost") |> should haveSubstring "ghost"

// ──────────────────────────────────────────────────────────────
// Agents parsing
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``parseAgents reads id name enabled`` () =
    let json =
        """[{"id":"01A","name":"alpha","enabled":true,"virtualMcpId":"v1"},
            {"id":"01B","name":"beta","enabled":false,"virtualMcpId":""}]"""

    match parseAgents json with
    | Ok rows ->
        rows.Length |> should equal 2
        rows.[0].Id |> should equal "01A"
        rows.[0].Name |> should equal "alpha"
        rows.[0].Enabled |> should equal true
        rows.[1].Enabled |> should equal false
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``parseAgents rejects non-array`` () =
    match parseAgents """{"id":"x"}""" with
    | Error(InvalidResponse _) -> ()
    | other -> failwith $"expected InvalidResponse, got {other}"

[<Fact>]
let ``renderAgentsTable handles empty`` () =
    renderAgentsTable [] |> should haveSubstring "No agents"

// ──────────────────────────────────────────────────────────────
// Status composition
// ──────────────────────────────────────────────────────────────

let private vmcpsJson =
    """[{"id":"v1","name":"Prod","enabled":true,"connectorIds":["c1","c2"]}]"""

let private connectorsJson =
    """[{"id":"c1","name":"Notion","enabled":true,"connected":true,"authType":"oauth"},
        {"id":"c2","name":"Static","enabled":false,"connected":false,"authType":"none"}]"""

[<Fact>]
let ``composeStatus builds vmcp and connector health`` () =
    match composeStatus vmcpsJson connectorsJson with
    | Ok view ->
        view.Vmcps.Length |> should equal 1
        view.Vmcps.[0].Name |> should equal "Prod"
        view.Vmcps.[0].ConnectorIds.Length |> should equal 2
        view.Connectors.Length |> should equal 2
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``composeStatus derives oauth state for oauth connector`` () =
    match composeStatus vmcpsJson connectorsJson with
    | Ok view ->
        let notion = view.Connectors |> List.find (fun c -> c.Id = "c1")
        notion.OAuthState |> should equal "authorized"
        let staticc = view.Connectors |> List.find (fun c -> c.Id = "c2")
        staticc.OAuthState |> should equal "n/a"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``composeStatus surfaces parse error`` () =
    match composeStatus "not json" connectorsJson with
    | Error(InvalidResponse _) -> ()
    | other -> failwith $"expected InvalidResponse, got {other}"

[<Fact>]
let ``statusJson emits both sections`` () =
    match composeStatus vmcpsJson connectorsJson with
    | Ok view ->
        let json = statusJson view
        json |> should haveSubstring "virtualMcps"
        json |> should haveSubstring "connectors"
    | Error e -> failwith $"expected Ok, got {e}"
