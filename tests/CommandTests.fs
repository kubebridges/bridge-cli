module Server.Cli.Tests.CommandTests

open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Server.Cli.Config
open Server.Cli.Output
open Server.Cli.ApiClient
open Server.Cli.Commands

// ──────────────────────────────────────────────────────────────
// resolveAgentId
// ──────────────────────────────────────────────────────────────

let private rows: AgentRow list =
    [
        {
            Id = "01A"
            Name = "alpha"
            Enabled = true
            VirtualMcpId = "v1"
        }
        {
            Id = "01B"
            Name = "beta"
            Enabled = true
            VirtualMcpId = "v1"
        }
        {
            Id = "01C"
            Name = "beta"
            Enabled = true
            VirtualMcpId = "v2"
        }
    ]

[<Fact>]
let ``resolveAgentId matches by exact id`` () =
    match resolveAgentId "01A" rows with
    | Ok id -> id |> should equal "01A"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``resolveAgentId matches by unique name`` () =
    match resolveAgentId "alpha" rows with
    | Ok id -> id |> should equal "01A"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``resolveAgentId is case-insensitive on name`` () =
    match resolveAgentId "ALPHA" rows with
    | Ok id -> id |> should equal "01A"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``resolveAgentId errors on unknown name`` () =
    match resolveAgentId "ghost" rows with
    | Error(AgentNotFound "ghost") -> ()
    | other -> failwith $"expected AgentNotFound, got {other}"

[<Fact>]
let ``resolveAgentId errors on ambiguous name`` () =
    match resolveAgentId "beta" rows with
    | Error(UsageError _) -> ()
    | other -> failwith $"expected UsageError, got {other}"

// ──────────────────────────────────────────────────────────────
// Fake sender: records requests, replies from a scripted queue
// ──────────────────────────────────────────────────────────────

type FakeSender(responses: (HttpStatusCode * string) list) =
    let queue = Queue<HttpStatusCode * string>(responses)
    let recorded = ResizeArray<HttpRequestMessage>()
    member _.Requests = recorded

    interface IHttpSender with
        member _.Send(req) =
            recorded.Add(req)
            let status, body = queue.Dequeue()
            let resp = new HttpResponseMessage(status)
            resp.Content <- new StringContent(body)
            Task.FromResult(resp)

// ──────────────────────────────────────────────────────────────
// getJson request construction against a fake sender
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``getJson issues a GET with bearer token to the expected path`` () =
    let sender = FakeSender([ HttpStatusCode.OK, "[]" ])

    let result =
        getJson sender "https://x.example" "brg_cli_abc" "/api/v1/agents"
        |> fun t -> t.GetAwaiter().GetResult()

    match result with
    | Ok body -> body |> should equal "[]"
    | Error e -> failwith $"expected Ok, got {e}"

    sender.Requests.Count |> should equal 1
    let req = sender.Requests.[0]
    req.Method |> should equal HttpMethod.Get
    req.RequestUri.ToString() |> should equal "https://x.example/api/v1/agents"
    req.Headers.Authorization.Parameter |> should equal "brg_cli_abc"

[<Fact>]
let ``getJson maps a 403 to a CliError`` () =
    let sender = FakeSender([ HttpStatusCode.Forbidden, "denied" ])

    let result =
        getJson sender "https://x.example" "brg_cli_abc" "/api/v1/agents"
        |> fun t -> t.GetAwaiter().GetResult()

    match result with
    | Error(ApiError(403, _)) -> ()
    | other -> failwith $"expected ApiError 403, got {other}"
