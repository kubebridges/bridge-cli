module Server.Cli.Tests.LoginTests

open System
open Xunit
open FsUnit.Xunit
open Server.Cli.Config
open Server.Cli.Pkce
open Server.Cli.Login

// ──────────────────────────────────────────────────────────────
// PKCE
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``verifier is base64url without padding`` () =
    let v = generateVerifier ()
    v |> should not' (haveSubstring "=")
    v |> should not' (haveSubstring "+")
    v |> should not' (haveSubstring "/")
    v.Length |> should be (greaterThanOrEqualTo 43)

[<Fact>]
let ``challenge is deterministic for a verifier`` () =
    let v = "test-verifier-value"
    challengeOf v |> should equal (challengeOf v)

[<Fact>]
let ``redirectUri uses loopback host and callback path`` () =
    redirectUri 12345 |> should equal "http://127.0.0.1:12345/callback"

[<Fact>]
let ``authorizeUrl carries cli scope and s256`` () =
    let url =
        authorizeUrl "https://x.example" "client1" (redirectUri 5555) "chal" "state1"

    url |> should haveSubstring "scope=cli"
    url |> should haveSubstring "code_challenge_method=S256"
    url |> should haveSubstring "client_id=client1"

// ──────────────────────────────────────────────────────────────
// Callback parsing
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``parseCallback extracts code and state`` () =
    match parseCallback "/callback?code=abc&state=xyz" with
    | Ok(code, state) ->
        code |> should equal "abc"
        state |> should equal "xyz"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``parseCallback surfaces oauth error`` () =
    match parseCallback "/callback?error=access_denied&error_description=nope" with
    | Error(AuthError msg) -> msg |> should equal "nope"
    | other -> failwith $"expected AuthError, got {other}"

[<Fact>]
let ``parseCallback fails without code`` () =
    match parseCallback "/callback?state=xyz" with
    | Error(AuthError _) -> ()
    | other -> failwith $"expected AuthError, got {other}"

// ──────────────────────────────────────────────────────────────
// Token / DCR response parsing
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``parseTokenResponse extracts access_token`` () =
    match parseTokenResponse """{"access_token":"brg_cli_xyz","token_type":"Bearer"}""" with
    | Ok token -> token |> should equal "brg_cli_xyz"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``parseTokenResponse surfaces error_description`` () =
    match parseTokenResponse """{"error":"invalid_grant","error_description":"bad code"}""" with
    | Error(AuthError msg) -> msg |> should equal "bad code"
    | other -> failwith $"expected AuthError, got {other}"

[<Fact>]
let ``parseClientId extracts client_id`` () =
    match parseClientId """{"client_id":"bridgemcp_abc"}""" with
    | Ok id -> id |> should equal "bridgemcp_abc"
    | Error e -> failwith $"expected Ok, got {e}"
