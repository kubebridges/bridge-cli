module Server.Cli.Commands

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Argu
open Server.Cli.Config
open Server.Cli.Cli
open Server.Cli.Output

// ──────────────────────────────────────────────────────────────
// Exit codes
// ──────────────────────────────────────────────────────────────

[<Literal>]
let exitOk = 0

[<Literal>]
let exitError = 1

// ──────────────────────────────────────────────────────────────
// Shared helpers
// ──────────────────────────────────────────────────────────────

let private printError (err: CliError) =
    eprintfn "Error: %s" (formatError err)
    exitError

/// Resolve config and ensure a token is present, or fail with a clear message.
let private requireAuth (flagUrl: string option) : Result<string * string, CliError> =
    match resolve flagUrl with
    | Error e -> Error e
    | Ok cfg ->
        match cfg.Token with
        | Some token -> Ok(cfg.Url, token)
        | None -> Error NotLoggedIn

// ──────────────────────────────────────────────────────────────
// login / logout
// ──────────────────────────────────────────────────────────────

let runLogin (args: ParseResults<LoginArgs>) : Task<int> =
    task {
        let flagUrl = args.TryGetResult LoginArgs.Url

        // For login, precedence is flag > env URL > default (a stored token is irrelevant here).
        let baseUrl =
            match flagUrl with
            | Some u when not (String.IsNullOrWhiteSpace u) -> u
            | _ ->
                match Environment.GetEnvironmentVariable(envUrlVar) with
                | null
                | "" -> defaultUrl
                | envUrl -> envUrl

        let! result = Login.run baseUrl

        match result with
        | Ok path ->
            printfn "Logged in to %s" (baseUrl.TrimEnd('/'))
            printfn "Token saved to %s" path
            return exitOk
        | Error e -> return printError e
    }

let runLogout () : Task<int> =
    task {
        // Best-effort revoke on the server before clearing local state.
        match resolve None with
        | Ok cfg when cfg.Token.IsSome ->
            try
                use client = new HttpClient(Timeout = TimeSpan.FromSeconds(10.0))
                let url = ApiClient.buildUrl cfg.Url "/api/v1/oauth/revoke"

                use content =
                    new FormUrlEncodedContent(
                        [
                            Collections.Generic.KeyValuePair("token", cfg.Token.Value)
                        ]
                    )

                let! _ = client.PostAsync(url, content)
                ()
            with _ ->
                () // best-effort only
        | _ -> ()

        match clearToken () with
        | Ok() ->
            printfn "Logged out."
            return exitOk
        | Error e -> return printError e
    }

// ──────────────────────────────────────────────────────────────
// status
// ──────────────────────────────────────────────────────────────

let runStatus (sender: ApiClient.IHttpSender) (args: ParseResults<StatusArgs>) : Task<int> =
    task {
        match requireAuth None with
        | Error e -> return printError e
        | Ok(url, token) ->
            let! vmcps = ApiClient.getJson sender url token "/api/v1/virtual-mcps"
            let! connectors = ApiClient.getJson sender url token "/api/v1/connectors"

            match vmcps, connectors with
            | Error e, _
            | _, Error e -> return printError e
            | Ok vjson, Ok cjson ->
                match composeStatus vjson cjson with
                | Error e -> return printError e
                | Ok view ->
                    if args.Contains StatusArgs.Json then
                        printfn "%s" (statusJson view)
                    else
                        printfn "%s" (renderStatusTable view)

                    return exitOk
    }

// ──────────────────────────────────────────────────────────────
// agents
// ──────────────────────────────────────────────────────────────

let runAgentsList (sender: ApiClient.IHttpSender) (args: ParseResults<AgentsListArgs>) : Task<int> =
    task {
        match requireAuth None with
        | Error e -> return printError e
        | Ok(url, token) ->
            let! result = ApiClient.getJson sender url token "/api/v1/agents"

            match result with
            | Error e -> return printError e
            | Ok json ->
                if args.Contains AgentsListArgs.Json then
                    printfn "%s" (prettyJson json)
                    return exitOk
                else
                    match parseAgents json with
                    | Error e -> return printError e
                    | Ok rows ->
                        printfn "%s" (renderAgentsTable rows)
                        return exitOk
    }

/// Resolve an agent name-or-id to an id against the agents listing (pure over parsed rows).
let resolveAgentId (nameOrId: string) (rows: AgentRow list) : Result<string, CliError> =
    match rows |> List.tryFind (fun r -> r.Id = nameOrId) with
    | Some r -> Ok r.Id
    | None ->
        let matches =
            rows
            |> List.filter (fun r -> String.Equals(r.Name, nameOrId, StringComparison.OrdinalIgnoreCase))

        match matches with
        | [ r ] -> Ok r.Id
        | [] -> Error(AgentNotFound nameOrId)
        | _ -> Error(UsageError $"Multiple agents match '{nameOrId}'; specify the id instead.")

let runAgentsDeploy (sender: ApiClient.IHttpSender) (args: ParseResults<AgentsDeployArgs>) : Task<int> =
    task {
        let nameOrId, packagePath = args.GetResult AgentsDeployArgs.Target

        if not (File.Exists packagePath) then
            return printError (FileNotFound packagePath)
        elif not (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) then
            return printError (UsageError "The agent package must be a .zip file.")
        else
            match requireAuth None with
            | Error e -> return printError e
            | Ok(url, token) ->
                let! listResult = ApiClient.getJson sender url token "/api/v1/agents"

                match listResult with
                | Error e -> return printError e
                | Ok json ->
                    match parseAgents json |> Result.bind (resolveAgentId nameOrId) with
                    | Error e -> return printError e
                    | Ok agentId ->
                        try
                            use stream = File.OpenRead packagePath
                            let fileName = Path.GetFileName packagePath

                            use req = ApiClient.buildPackageUpload url token agentId fileName stream

                            use! resp = sender.Send(req)
                            let! bodyResult = ApiClient.readResponse resp

                            match bodyResult with
                            | Error e -> return printError e
                            | Ok _ ->
                                printfn "Deployed %s to agent %s" fileName agentId
                                return exitOk
                        with ex ->
                            return printError (NetworkError ex.Message)
    }

// ──────────────────────────────────────────────────────────────
// Dispatch
// ──────────────────────────────────────────────────────────────

let dispatchAgents (sender: ApiClient.IHttpSender) (args: ParseResults<AgentsArgs>) : Task<int> =
    match args.TryGetSubCommand() with
    | Some(AgentsArgs.List listArgs) -> runAgentsList sender listArgs
    | Some(AgentsArgs.Deploy deployArgs) -> runAgentsDeploy sender deployArgs
    | None -> task { return printError (UsageError "Specify an agents subcommand: list or deploy.") }

let dispatch (sender: ApiClient.IHttpSender) (results: ParseResults<CliArgs>) : Task<int> =
    match results.TryGetSubCommand() with
    | Some(Login loginArgs) -> runLogin loginArgs
    | Some Logout -> runLogout ()
    | Some(Status statusArgs) -> runStatus sender statusArgs
    | Some(Agents agentsArgs) -> dispatchAgents sender agentsArgs
    | None -> task { return printError (UsageError "Specify a command: login, logout, status, or agents.") }
