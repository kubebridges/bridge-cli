# BridgeMCP CLI

Public source repository for the `bridgemcp` command-line tool.

## Install

Install from NuGet:

```bash
dotnet tool install --global BridgeMCP.Cli
```

Install the latest self-contained binary:

```bash
curl -fsSL https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.sh | sh
```

On Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.ps1 | iex
```

## Build

```bash
dotnet test BridgeMCP.Cli.slnx
dotnet pack src/Cli.fsproj -c Release
```

## Release

Push a tag named `cli-v<version>`, for example `cli-v0.1.2`. The release workflow publishes `BridgeMCP.Cli` to NuGet using the `NUGET_API_KEY` repository secret and attaches self-contained binaries to the GitHub release.

## License

GPL-3.0-or-later. See `LICENSE`.
