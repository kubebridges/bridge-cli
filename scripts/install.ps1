#Requires -Version 5.0
<#
.SYNOPSIS
    BridgeMCP CLI installer for Windows (win-x64).

.DESCRIPTION
    Downloads a self-contained bridgemcp.exe from GitHub Releases, installs it
    under $env:LOCALAPPDATA\bridgemcp, adds that directory to the user PATH, and
    verifies the binary runs.

.EXAMPLE
    irm https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.ps1))) -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repo = "kubebridges/bridge-cli"

# Only win-x64 binaries are built. Reject non-x64 Windows.
$arch = $env:PROCESSOR_ARCHITECTURE
if ($arch -ne "AMD64") {
    Write-Error "Unsupported architecture '$arch'. Only win-x64 (AMD64) binaries are built."
    exit 1
}
$rid = "win-x64"

# Resolve the release tag and version.
if ([string]::IsNullOrWhiteSpace($Version)) {
    $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "bridgemcp-install" }
    $tag = $latest.tag_name
    if ([string]::IsNullOrWhiteSpace($tag)) {
        Write-Error "Could not resolve the latest release tag from GitHub."
        exit 1
    }
    $Version = $tag -replace '^cli-v', ''
}
else {
    $tag = "cli-v$Version"
}

$asset = "bridgemcp-$Version-$rid.zip"
$url = "https://github.com/$repo/releases/download/$tag/$asset"

Write-Host "Installing bridgemcp $Version ($rid)"

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("bridgemcp-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    $zipPath = Join-Path $tmp $asset
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force
    $exeSource = Join-Path $tmp "bridgemcp-$Version-$rid.exe"
    if (-not (Test-Path $exeSource)) {
        Write-Error "Downloaded archive did not contain the expected binary."
        exit 1
    }

    $installDir = Join-Path $env:LOCALAPPDATA "bridgemcp"
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    $target = Join-Path $installDir "bridgemcp.exe"
    Copy-Item -Path $exeSource -Destination $target -Force
    Write-Host "Installed to $target"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

# Add the install dir to the user PATH if it is not already present.
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ([string]::IsNullOrEmpty($userPath)) { $userPath = "" }
$onPath = $userPath.Split(';') | Where-Object { $_.TrimEnd('\') -ieq $installDir.TrimEnd('\') }
if (-not $onPath) {
    $newPath = if ($userPath.TrimEnd(';') -eq "") { $installDir } else { $userPath.TrimEnd(';') + ";" + $installDir }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Added $installDir to your user PATH. Restart your shell to pick it up."
}
# Make it available in the current session too.
if (($env:Path -split ';') -notcontains $installDir) {
    $env:Path = "$installDir;$env:Path"
}

# Verify the binary runs. `bridgemcp help` is the CLI's help command (Argu).
& $target help | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Verified: bridgemcp help runs."
}
else {
    Write-Error "Installed binary but 'bridgemcp help' did not run cleanly."
    exit 1
}
