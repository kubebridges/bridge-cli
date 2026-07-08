# BridgeMCP.Cli

The `bridgemcp` command-line tool for [BridgeMCP](https://bridgemcp.net), a self-hosted MCP aggregation gateway. It drives agent deployment and endpoint status from the command line, authenticating against your BridgeMCP instance through a browser-based OAuth authorization-code flow with PKCE.

## Install

Requires the .NET 10 SDK:

```bash
dotnet tool install --global BridgeMCP.Cli
```

The command is exposed as `bridgemcp`. Update with `dotnet tool update --global BridgeMCP.Cli`.

Self-contained, single-file binaries (no .NET runtime required) are attached to each GitHub release; see the [CLI docs](https://bridgemcp.net/docs/cli) for install-script one-liners.

## Usage

```bash
bridgemcp login [--url <url>]   # authenticate against your instance
bridgemcp status                # composed view of Virtual MCPs and connectors
bridgemcp agents list           # list agents in your organization
bridgemcp agents deploy <agent-name-or-id> <package.zip>
bridgemcp logout                # clear the stored token and revoke it server-side
```

Read commands accept `--json` for machine-readable output. See the [full CLI documentation](https://bridgemcp.net/docs/cli) for configuration and authentication details.

## License

GPL-3.0-or-later. See the [repository](https://github.com/kubebridges/bridge-cli) for details.
