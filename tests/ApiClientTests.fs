module Server.Cli.Tests.ApiClientTests

open System.Net.Http
open Xunit
open FsUnit.Xunit
open Server.Cli.Config
open Server.Cli.ApiClient

// ──────────────────────────────────────────────────────────────
// URL construction
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``buildUrl joins base and path`` () =
    buildUrl "https://x.example" "/api/v1/agents"
    |> should equal "https://x.example/api/v1/agents"

[<Fact>]
let ``buildUrl tolerates trailing slash on base`` () =
    buildUrl "https://x.example/" "/api/v1/agents"
    |> should equal "https://x.example/api/v1/agents"

[<Fact>]
let ``buildUrl tolerates missing leading slash on path`` () =
    buildUrl "https://x.example" "api/v1/agents"
    |> should equal "https://x.example/api/v1/agents"

// ──────────────────────────────────────────────────────────────
// Authenticated request construction
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``buildAuthedRequest sets bearer header and method`` () =
    use req =
        buildAuthedRequest HttpMethod.Get "https://x.example" "brg_cli_abc" "/api/v1/agents"

    req.Method |> should equal HttpMethod.Get
    req.Headers.Authorization.Scheme |> should equal "Bearer"
    req.Headers.Authorization.Parameter |> should equal "brg_cli_abc"
    req.RequestUri.ToString() |> should equal "https://x.example/api/v1/agents"

[<Fact>]
let ``buildPackageUpload targets the package endpoint with multipart body`` () =
    use ms = new System.IO.MemoryStream([| 1uy; 2uy; 3uy |])

    use req = buildPackageUpload "https://x.example" "brg_cli_abc" "01ABC" "pkg.zip" ms

    req.Method |> should equal HttpMethod.Post

    req.RequestUri.ToString()
    |> should equal "https://x.example/api/v1/agents/01ABC/package"

    (req.Content :? MultipartFormDataContent) |> should equal true

// ──────────────────────────────────────────────────────────────
// Error mapping
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``errorFromResponse maps 401 to AuthError`` () =
    match errorFromResponse 401 "" with
    | AuthError _ -> ()
    | other -> failwith $"expected AuthError, got {other}"

[<Fact>]
let ``errorFromResponse surfaces oauth error_description`` () =
    match errorFromResponse 400 """{"error":"invalid_grant","error_description":"bad code"}""" with
    | ApiError(400, msg) -> msg |> should equal "bad code"
    | other -> failwith $"expected ApiError, got {other}"

[<Fact>]
let ``errorFromResponse surfaces plain-text body`` () =
    match errorFromResponse 500 "boom" with
    | ApiError(500, msg) -> msg |> should equal "boom"
    | other -> failwith $"expected ApiError, got {other}"

[<Fact>]
let ``errorFromResponse falls back to generic message on empty body`` () =
    match errorFromResponse 502 "" with
    | ApiError(502, msg) -> msg |> should haveSubstring "502"
    | other -> failwith $"expected ApiError, got {other}"
