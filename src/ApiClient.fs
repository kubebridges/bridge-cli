module Server.Cli.ApiClient

open System
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Server.Cli.Config

// ──────────────────────────────────────────────────────────────
// Request construction (pure; testable)
// ──────────────────────────────────────────────────────────────

/// Join a base URL and a path into an absolute API URL, tolerant of trailing/leading slashes.
let buildUrl (baseUrl: string) (path: string) : string =
    let b = baseUrl.TrimEnd('/')
    let p = if path.StartsWith("/") then path else "/" + path
    b + p

/// Build an authenticated GET request. The Authorization header carries the CLI bearer token.
let buildAuthedRequest (method: HttpMethod) (baseUrl: string) (token: string) (path: string) : HttpRequestMessage =
    let req = new HttpRequestMessage(method, buildUrl baseUrl path)
    req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
    req

/// Build a multipart/form-data package upload request for POST /api/v1/agents/{id}/package.
/// The zip is sent as a form file part named "file".
let buildPackageUpload
    (baseUrl: string)
    (token: string)
    (agentId: string)
    (fileName: string)
    (content: Stream)
    : HttpRequestMessage =
    let req =
        buildAuthedRequest HttpMethod.Post baseUrl token $"/api/v1/agents/{agentId}/package"

    let form = new MultipartFormDataContent()
    let fileContent = new StreamContent(content)
    fileContent.Headers.ContentType <- MediaTypeHeaderValue("application/zip")
    form.Add(fileContent, "file", fileName)
    req.Content <- form
    req

// ──────────────────────────────────────────────────────────────
// Error formatting
// ──────────────────────────────────────────────────────────────

/// Translate an HTTP status + body into a CliError with a clear message.
let errorFromResponse (status: int) (body: string) : CliError =
    let trimmed = if isNull body then "" else body.Trim()

    let detail =
        if String.IsNullOrWhiteSpace(trimmed) then
            ""
        else
            // Surface an OAuth/JSON error_description or message field when present.
            try
                let doc = JsonDocument.Parse(trimmed)
                let root = doc.RootElement

                let pick (name: string) =
                    match root.TryGetProperty(name) with
                    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                    | _ -> None

                [
                    "error_description"
                    "message"
                    "error"
                ]
                |> List.tryPick pick
                |> Option.defaultValue trimmed
            with _ ->
                trimmed

    match status with
    | 401 -> AuthError "Not authorized. Run `bridgemcp login` to authenticate."
    | 403 -> ApiError(status, if detail = "" then "Forbidden." else detail)
    | _ ->
        ApiError(
            status,
            if detail = "" then
                $"Request failed with status {status}."
            else
                detail
        )

// ──────────────────────────────────────────────────────────────
// HTTP send (impure)
// ──────────────────────────────────────────────────────────────

/// A minimal HTTP surface the command layer depends on, so commands can be tested
/// against request construction without a live server.
type IHttpSender =
    abstract Send: HttpRequestMessage -> Task<HttpResponseMessage>

type HttpSender(client: HttpClient) =
    interface IHttpSender with
        member _.Send(req) = client.SendAsync(req)

let createSender () =
    let client = new HttpClient(Timeout = TimeSpan.FromSeconds(30.0))
    HttpSender(client) :> IHttpSender

/// Read a response, mapping non-success statuses to a CliError.
let readResponse (resp: HttpResponseMessage) : Task<Result<string, CliError>> =
    task {
        let! body = resp.Content.ReadAsStringAsync()

        if resp.IsSuccessStatusCode then
            return Ok body
        else
            return Error(errorFromResponse (int resp.StatusCode) body)
    }

/// Perform an authenticated GET and return the raw JSON body or a CliError.
let getJson (sender: IHttpSender) (baseUrl: string) (token: string) (path: string) : Task<Result<string, CliError>> =
    task {
        try
            use req = buildAuthedRequest HttpMethod.Get baseUrl token path
            use! resp = sender.Send(req)
            return! readResponse resp
        with ex ->
            return Error(NetworkError ex.Message)
    }
