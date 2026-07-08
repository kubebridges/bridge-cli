module Server.Cli.Cli

open Argu

// ──────────────────────────────────────────────────────────────
// Argu command surface
//
// Top-level commands and their sub-arguments. Kept free of side
// effects so parser definitions can be unit-tested directly.
// ──────────────────────────────────────────────────────────────

[<CliPrefix(CliPrefix.DoubleDash)>]
type LoginArgs =
    | Url of url: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Url _ -> "BridgeMCP instance URL (default https://bridgemcp.net)."

[<CliPrefix(CliPrefix.DoubleDash)>]
type StatusArgs =
    | Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Json -> "Emit machine-readable JSON instead of a table."

[<CliPrefix(CliPrefix.DoubleDash)>]
type AgentsListArgs =
    | Json

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Json -> "Emit machine-readable JSON instead of a table."

[<CliPrefix(CliPrefix.None)>]
type AgentsDeployArgs =
    | [<MainCommand; ExactlyOnce>] Target of agentNameOrId: string * packageZip: string

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Target _ -> "<agent-name-or-id> <package.zip> to deploy."

[<CliPrefix(CliPrefix.None)>]
type AgentsArgs =
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<AgentsListArgs>
    | [<CliPrefix(CliPrefix.None)>] Deploy of ParseResults<AgentsDeployArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | List _ -> "List the agents in your organization."
            | Deploy _ -> "Deploy an agent package to an agent."

[<CliPrefix(CliPrefix.None)>]
type CliArgs =
    | [<CliPrefix(CliPrefix.None)>] Login of ParseResults<LoginArgs>
    | [<CliPrefix(CliPrefix.None); SubCommand>] Logout
    | [<CliPrefix(CliPrefix.None)>] Status of ParseResults<StatusArgs>
    | [<CliPrefix(CliPrefix.None)>] Agents of ParseResults<AgentsArgs>

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Login _ -> "Authenticate against a BridgeMCP instance (browser OAuth)."
            | Logout -> "Clear the stored token."
            | Status _ -> "Show composed Virtual MCP and connector health."
            | Agents _ -> "Manage agents (list, deploy)."

/// Program name used in Argu usage output.
let programName = "bridgemcp"

/// Build a parser for the top-level command surface.
let createParser () =
    ArgumentParser.Create<CliArgs>(programName = programName)
