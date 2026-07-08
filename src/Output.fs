module Server.Cli.Output

open System
open System.Text
open System.Text.Json
open Server.Cli.Config

// ──────────────────────────────────────────────────────────────
// Error message formatting (clear, human-readable)
// ──────────────────────────────────────────────────────────────

/// Render a CliError as a single user-facing line (without the "Error: " prefix).
let formatError (err: CliError) : string =
    match err with
    | NotLoggedIn -> "Not logged in. Run `bridgemcp login` first."
    | MissingUrl -> "No BridgeMCP URL configured. Pass --url or set BRIDGEMCP_URL."
    | ConfigReadError m -> $"Could not read config file: {m}"
    | ConfigWriteError m -> $"Could not write config file: {m}"
    | ApiError(status, m) -> $"API error ({status}): {m}"
    | NetworkError m -> $"Network error: {m}"
    | InvalidResponse m -> $"Unexpected response from server: {m}"
    | UsageError m -> m
    | AuthError m -> m
    | FileNotFound p -> $"File not found: {p}"
    | AgentNotFound name -> $"No agent found matching '{name}'."

// ──────────────────────────────────────────────────────────────
// JSON parsing helpers (tolerant of unknown fields)
// ──────────────────────────────────────────────────────────────

let private tryStr (name: string) (el: JsonElement) =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
    | _ -> ""

let private tryBool (name: string) (el: JsonElement) =
    match el.TryGetProperty(name) with
    | true, v when v.ValueKind = JsonValueKind.True -> true
    | _ -> false

// ──────────────────────────────────────────────────────────────
// Agents list
// ──────────────────────────────────────────────────────────────

type AgentRow =
    {
        Id: string
        Name: string
        Enabled: bool
        VirtualMcpId: string
    }

/// Parse the /api/v1/agents JSON array into rows (pure; testable).
let parseAgents (json: string) : Result<AgentRow list, CliError> =
    try
        let doc = JsonDocument.Parse(json)

        if doc.RootElement.ValueKind <> JsonValueKind.Array then
            Error(InvalidResponse "expected an array of agents")
        else
            [
                for el in doc.RootElement.EnumerateArray() ->
                    {
                        Id = tryStr "id" el
                        Name = tryStr "name" el
                        Enabled = tryBool "enabled" el
                        VirtualMcpId = tryStr "virtualMcpId" el
                    }
            ]
            |> Ok
    with ex ->
        Error(InvalidResponse ex.Message)

// ──────────────────────────────────────────────────────────────
// Status composition (VMCPs + connectors)
// ──────────────────────────────────────────────────────────────

type ConnectorHealth =
    {
        Id: string
        Name: string
        Enabled: bool
        Connected: bool
        OAuthState: string
    }

type VmcpHealth =
    {
        Id: string
        Name: string
        Enabled: bool
        ConnectorIds: string list
    }

type StatusView =
    {
        Vmcps: VmcpHealth list
        Connectors: ConnectorHealth list
    }

let private parseConnectorList (json: string) : Result<ConnectorHealth list, CliError> =
    try
        let doc = JsonDocument.Parse(json)

        if doc.RootElement.ValueKind <> JsonValueKind.Array then
            Error(InvalidResponse "expected an array of connectors")
        else
            [
                for el in doc.RootElement.EnumerateArray() ->
                    let authType = tryStr "authType" el

                    let oauthState =
                        if authType.Equals("oauth", StringComparison.OrdinalIgnoreCase) then
                            if tryBool "connected" el then
                                "authorized"
                            else
                                "not authorized"
                        else
                            "n/a"

                    {
                        Id = tryStr "id" el
                        Name = tryStr "name" el
                        Enabled = tryBool "enabled" el
                        Connected = tryBool "connected" el
                        OAuthState = oauthState
                    }
            ]
            |> Ok
    with ex ->
        Error(InvalidResponse ex.Message)

let private parseVmcpList (json: string) : Result<VmcpHealth list, CliError> =
    try
        let doc = JsonDocument.Parse(json)

        if doc.RootElement.ValueKind <> JsonValueKind.Array then
            Error(InvalidResponse "expected an array of virtual MCPs")
        else
            [
                for el in doc.RootElement.EnumerateArray() ->
                    let connectorIds =
                        match el.TryGetProperty("connectorIds") with
                        | true, v when v.ValueKind = JsonValueKind.Array ->
                            [
                                for c in v.EnumerateArray() do
                                    if c.ValueKind = JsonValueKind.String then
                                        yield c.GetString()
                            ]
                        | _ -> []

                    {
                        Id = tryStr "id" el
                        Name = tryStr "name" el
                        Enabled = tryBool "enabled" el
                        ConnectorIds = connectorIds
                    }
            ]
            |> Ok
    with ex ->
        Error(InvalidResponse ex.Message)

/// Compose the /api/v1/virtual-mcps and /api/v1/connectors payloads into a status view (pure).
let composeStatus (vmcpsJson: string) (connectorsJson: string) : Result<StatusView, CliError> =
    match parseVmcpList vmcpsJson, parseConnectorList connectorsJson with
    | Ok vmcps, Ok connectors ->
        Ok
            {
                Vmcps = vmcps
                Connectors = connectors
            }
    | Error e, _ -> Error e
    | _, Error e -> Error e

// ──────────────────────────────────────────────────────────────
// Table rendering
// ──────────────────────────────────────────────────────────────

let private enabledLabel (b: bool) = if b then "enabled" else "disabled"

let private connLabel (b: bool) =
    if b then "connected" else "disconnected"

let renderAgentsTable (rows: AgentRow list) : string =
    if List.isEmpty rows then
        "No agents found."
    else
        let sb = StringBuilder()
        sb.AppendLine("NAME                 ENABLED   ID") |> ignore

        for r in rows do
            let name = r.Name.PadRight(20)
            let en = (enabledLabel r.Enabled).PadRight(9)
            sb.AppendLine($"{name} {en} {r.Id}") |> ignore

        sb.ToString().TrimEnd()

let renderStatusTable (view: StatusView) : string =
    let sb = StringBuilder()
    sb.AppendLine("Virtual MCPs") |> ignore

    if List.isEmpty view.Vmcps then
        sb.AppendLine("  (none)") |> ignore
    else
        for v in view.Vmcps do
            sb.AppendLine($"  {v.Name}  [{enabledLabel v.Enabled}]  connectors: {v.ConnectorIds.Length}")
            |> ignore

    sb.AppendLine() |> ignore
    sb.AppendLine("Connectors") |> ignore

    if List.isEmpty view.Connectors then
        sb.AppendLine("  (none)") |> ignore
    else
        for c in view.Connectors do
            sb.AppendLine($"  {c.Name}  [{enabledLabel c.Enabled}]  {connLabel c.Connected}  oauth: {c.OAuthState}")
            |> ignore

    sb.ToString().TrimEnd()

// ──────────────────────────────────────────────────────────────
// JSON re-emission (pretty-print raw arrays for --json)
// ──────────────────────────────────────────────────────────────

let private prettyOptions = JsonSerializerOptions(WriteIndented = true)

/// Re-serialize raw JSON text with indentation for --json output.
let prettyJson (raw: string) : string =
    try
        let doc = JsonDocument.Parse(raw)
        JsonSerializer.Serialize(doc.RootElement, prettyOptions)
    with _ ->
        raw

/// JSON output for the composed status view.
let statusJson (view: StatusView) : string =
    let payload =
        {|
            virtualMcps =
                view.Vmcps
                |> List.map (fun v ->
                    {|
                        id = v.Id
                        name = v.Name
                        enabled = v.Enabled
                        connectorCount = v.ConnectorIds.Length
                    |})
            connectors =
                view.Connectors
                |> List.map (fun c ->
                    {|
                        id = c.Id
                        name = c.Name
                        enabled = c.Enabled
                        connected = c.Connected
                        oauthState = c.OAuthState
                    |})
        |}

    JsonSerializer.Serialize(payload, prettyOptions)
