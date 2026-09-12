module Server.Cli.Config

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

// ──────────────────────────────────────────────────────────────
// CLI error model (CLI is a standalone app; the server-side
// AppError DU does not apply here).
// ──────────────────────────────────────────────────────────────

/// Errors surfaced by the CLI. Each maps to a clear message + non-zero exit.
type CliError =
    | NotLoggedIn
    | MissingUrl
    | ConfigReadError of string
    | ConfigWriteError of string
    | ApiError of status: int * message: string
    | NetworkError of string
    | InvalidResponse of string
    | UsageError of string
    | AuthError of string
    | FileNotFound of string
    | AgentNotFound of string

// ──────────────────────────────────────────────────────────────
// Config file model
// ──────────────────────────────────────────────────────────────

let defaultUrl = "https://bridgemcp.io"

let envUrlVar = "BRIDGEMCP_URL"
let envTokenVar = "BRIDGEMCP_TOKEN"

/// Persisted config file contents (~/.bridgemcp/config.json).
type StoredConfig =
    {
        [<JsonPropertyName("url")>]
        Url: string
        [<JsonPropertyName("token")>]
        Token: string
    }

/// Fully resolved settings for a command invocation.
type ResolvedConfig =
    {
        Url: string
        /// None when no token could be resolved from any source.
        Token: string option
    }

let private jsonOptions =
    let o = JsonSerializerOptions(WriteIndented = true)
    o.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
    o

// ──────────────────────────────────────────────────────────────
// Config file location
// ──────────────────────────────────────────────────────────────

/// Directory holding the CLI config, honouring an override for tests.
let configDir () =
    match Environment.GetEnvironmentVariable("BRIDGEMCP_CONFIG_DIR") with
    | null
    | "" ->
        let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

        Path.Combine(home, ".bridgemcp")
    | dir -> dir

let configPath () =
    Path.Combine(configDir (), "config.json")

// ──────────────────────────────────────────────────────────────
// Read / write config file
// ──────────────────────────────────────────────────────────────

/// Parse config-file JSON text into a StoredConfig (pure; testable).
let parseConfig (text: string) : Result<StoredConfig, CliError> =
    if String.IsNullOrWhiteSpace(text) then
        Ok { Url = ""; Token = "" }
    else
        try
            let cfg = JsonSerializer.Deserialize<StoredConfig>(text, jsonOptions)

            Ok
                {
                    Url =
                        (if isNull (box cfg) then "" else cfg.Url)
                        |> Option.ofObj
                        |> Option.defaultValue ""
                    Token =
                        (if isNull (box cfg) then "" else cfg.Token)
                        |> Option.ofObj
                        |> Option.defaultValue ""
                }
        with ex ->
            Error(ConfigReadError ex.Message)

/// Serialize a StoredConfig to JSON text (pure; testable).
let serializeConfig (cfg: StoredConfig) : string =
    JsonSerializer.Serialize(cfg, jsonOptions)

/// Load the stored config from disk, returning an empty config when absent.
let loadConfig () : Result<StoredConfig, CliError> =
    let path = configPath ()

    if not (File.Exists path) then
        Ok { Url = ""; Token = "" }
    else
        try
            File.ReadAllText path |> parseConfig
        with ex ->
            Error(ConfigReadError ex.Message)

/// Persist the config to disk (0600 permissions on Unix) and return the path.
let saveConfig (cfg: StoredConfig) : Result<string, CliError> =
    try
        let dir = configDir ()
        Directory.CreateDirectory(dir) |> ignore
        let path = configPath ()
        File.WriteAllText(path, serializeConfig cfg)

        // Restrict permissions to the owner on Unix (0600).
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)

        Ok path
    with ex ->
        Error(ConfigWriteError ex.Message)

/// Remove the stored token (used by logout). Missing file is a no-op success.
let clearToken () : Result<unit, CliError> =
    match loadConfig () with
    | Error e -> Error e
    | Ok cfg ->
        match saveConfig { cfg with Token = "" } with
        | Ok _ -> Ok()
        | Error e -> Error e

// ──────────────────────────────────────────────────────────────
// Resolution (pure): flag > env > file
// ──────────────────────────────────────────────────────────────

let private firstNonEmpty (values: string option list) : string option =
    values
    |> List.tryPick (fun v ->
        match v with
        | Some s when not (String.IsNullOrWhiteSpace s) -> Some s
        | _ -> None)

/// Resolve the effective URL and token given each source explicitly.
/// Precedence: command-line flag > environment variable > config file > default (URL only).
let resolveWith
    (flagUrl: string option)
    (envUrl: string option)
    (envToken: string option)
    (stored: StoredConfig)
    : ResolvedConfig =
    let url =
        firstNonEmpty [ flagUrl; envUrl; (Some stored.Url) ]
        |> Option.defaultValue defaultUrl
        |> fun u -> u.TrimEnd('/')

    let token = firstNonEmpty [ envToken; (Some stored.Token) ]

    { Url = url; Token = token }

/// Resolve config from the environment and disk, applying an optional --url flag.
let resolve (flagUrl: string option) : Result<ResolvedConfig, CliError> =
    match loadConfig () with
    | Error e -> Error e
    | Ok stored ->
        let envUrl = Environment.GetEnvironmentVariable(envUrlVar) |> Option.ofObj
        let envToken = Environment.GetEnvironmentVariable(envTokenVar) |> Option.ofObj
        Ok(resolveWith flagUrl envUrl envToken stored)
