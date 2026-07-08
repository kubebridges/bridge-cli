module Server.Cli.Login

open System
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Server.Cli.Config
open Server.Cli.Pkce

// ──────────────────────────────────────────────────────────────
// Pure helpers (testable)
// ──────────────────────────────────────────────────────────────

/// Extract the access_token from the token-endpoint JSON response.
let parseTokenResponse (json: string) : Result<string, CliError> =
    try
        let doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        match root.TryGetProperty("access_token") with
        | true, v when v.ValueKind = JsonValueKind.String -> Ok(v.GetString())
        | _ ->
            let err =
                match root.TryGetProperty("error_description") with
                | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                | _ -> "no access_token in response"

            Error(AuthError err)
    with ex ->
        Error(InvalidResponse ex.Message)

/// Extract the client_id from a DCR registration response.
let parseClientId (json: string) : Result<string, CliError> =
    try
        let doc = JsonDocument.Parse(json)

        match doc.RootElement.TryGetProperty("client_id") with
        | true, v when v.ValueKind = JsonValueKind.String -> Ok(v.GetString())
        | _ -> Error(InvalidResponse "no client_id in registration response")
    with ex ->
        Error(InvalidResponse ex.Message)

/// Parse a URL query string (leading '?' optional) into a map of decoded key/value pairs.
let parseQuery (query: string) : Map<string, string> =
    let q = if query.StartsWith("?") then query.Substring(1) else query

    q.Split([| '&' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.choose (fun pair ->
        let kv = pair.Split([| '=' |], 2)

        if kv.Length = 2 then
            Some(Uri.UnescapeDataString(kv.[0]), Uri.UnescapeDataString(kv.[1]))
        elif kv.Length = 1 && kv.[0] <> "" then
            Some(Uri.UnescapeDataString(kv.[0]), "")
        else
            None)
    |> Map.ofArray

/// Extract the ?code=&state= from a callback request path (pure).
let parseCallback (rawUrl: string) : Result<string * string, CliError> =
    try
        let query =
            let idx = rawUrl.IndexOf('?')
            if idx >= 0 then rawUrl.Substring(idx) else ""

        let qs = parseQuery query

        match Map.tryFind "error" qs with
        | Some e -> Error(AuthError(Map.tryFind "error_description" qs |> Option.defaultValue e))
        | None ->
            match Map.tryFind "code" qs, Map.tryFind "state" qs with
            | Some code, Some state -> Ok(code, state)
            | _ -> Error(AuthError "authorization callback missing code or state")
    with ex ->
        Error(InvalidResponse ex.Message)

// ──────────────────────────────────────────────────────────────
// Impure flow
// ──────────────────────────────────────────────────────────────

/// Find a free loopback TCP port.
let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private postForm (client: HttpClient) (url: string) (fields: (string * string) list) =
    task {
        let content =
            new FormUrlEncodedContent(fields |> List.map (fun (k, v) -> Collections.Generic.KeyValuePair(k, v)))

        let! resp = client.PostAsync(url, content)
        let! body = resp.Content.ReadAsStringAsync()

        if resp.IsSuccessStatusCode then
            return Ok body
        else
            return Error(ApiClient.errorFromResponse (int resp.StatusCode) body)
    }

let private openBrowser (url: string) =
    try
        let psi = ProcessStartInfo(url, UseShellExecute = true)
        Process.Start(psi) |> ignore
        true
    with _ ->
        false

let private buildRegisterUrl (baseUrl: string) =
    ApiClient.buildUrl baseUrl "/api/v1/oauth/register"

let private buildTokenUrl (baseUrl: string) =
    ApiClient.buildUrl baseUrl "/api/v1/oauth/token"

/// Run the full auth-code + PKCE login against the given base URL.
/// Returns the persisted config path on success.
let run (baseUrl: string) : Task<Result<string, CliError>> =
    task {
        use client = new HttpClient(Timeout = TimeSpan.FromSeconds(30.0))
        let port = freePort ()
        let redirect = redirectUri port
        let verifier = generateVerifier ()
        let challenge = challengeOf verifier
        let state = generateState ()

        // 1. Dynamic Client Registration with the loopback redirect URI.
        let dcrBody =
            JsonSerializer.Serialize(
                {|
                    client_name = "BridgeMCP CLI"
                    redirect_uris = [| redirect |]
                    grant_types = [| "authorization_code" |]
                    token_endpoint_auth_method = "none"
                |}
            )

        let! dcrResult =
            task {
                try
                    use content = new StringContent(dcrBody, Encoding.UTF8, "application/json")
                    let! resp = client.PostAsync(buildRegisterUrl baseUrl, content)
                    let! body = resp.Content.ReadAsStringAsync()

                    if resp.IsSuccessStatusCode then
                        return parseClientId body
                    else
                        return Error(ApiClient.errorFromResponse (int resp.StatusCode) body)
                with ex ->
                    return Error(NetworkError ex.Message)
            }

        match dcrResult with
        | Error e -> return Error e
        | Ok clientId ->
            // 2. Start loopback listener before opening the browser.
            let listener = new HttpListener()
            listener.Prefixes.Add($"http://127.0.0.1:{port}/")

            let startResult =
                try
                    listener.Start()
                    Ok()
                with ex ->
                    Error(NetworkError $"could not bind loopback listener on port {port}: {ex.Message}")

            match startResult with
            | Error e -> return Error e
            | Ok() ->

                use _ = listener

                let authUrl = authorizeUrl baseUrl clientId redirect challenge state

                printfn "Opening your browser to complete login..."
                printfn "If it does not open, visit:\n  %s" authUrl

                if not (openBrowser authUrl) then
                    printfn "(could not launch a browser automatically)"

                // 3. Await the callback.
                let! ctx = listener.GetContextAsync()
                let callbackResult = parseCallback ctx.Request.Url.PathAndQuery

                // Respond in the browser regardless of outcome.
                let respond (message: string) =
                    task {
                        let bytes =
                            Encoding.UTF8.GetBytes(
                                $"<html><body><p>{message}</p><p>You can close this tab.</p></body></html>"
                            )

                        ctx.Response.ContentType <- "text/html"
                        ctx.Response.ContentLength64 <- int64 bytes.Length
                        do! ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
                        ctx.Response.Close()
                    }

                match callbackResult with
                | Error e ->
                    do! respond "Login failed."
                    return Error e
                | Ok(code, returnedState) when returnedState <> state ->
                    do! respond "Login failed (state mismatch)."
                    return Error(AuthError "state mismatch in authorization callback")
                | Ok(code, _) ->
                    do! respond "Login complete."

                    // 4. Exchange the code for a cli token.
                    let! tokenResult =
                        task {
                            try
                                return!
                                    postForm
                                        client
                                        (buildTokenUrl baseUrl)
                                        [
                                            "grant_type", "authorization_code"
                                            "code", code
                                            "code_verifier", verifier
                                            "client_id", clientId
                                            "redirect_uri", redirect
                                        ]
                            with ex ->
                                return Error(NetworkError ex.Message)
                        }

                    match tokenResult with
                    | Error e -> return Error e
                    | Ok body ->
                        match parseTokenResponse body with
                        | Error e -> return Error e
                        | Ok accessToken ->
                            return
                                saveConfig
                                    {
                                        Url = baseUrl.TrimEnd('/')
                                        Token = accessToken
                                    }
    }
