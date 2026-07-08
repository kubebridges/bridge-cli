#!/bin/sh
# BridgeMCP CLI installer for Linux and macOS.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/kubebridges/bridge-cli/main/scripts/install.sh | sh -s -- --version 0.1.0
#
# Downloads a self-contained `bridgemcp` binary from GitHub Releases, installs it
# on PATH, and verifies it runs. Only linux-x64 and osx-arm64 binaries are built.
set -eu

REPO="kubebridges/bridge-cli"
VERSION=""

while [ $# -gt 0 ]; do
    case "$1" in
        --version)
            VERSION="${2:-}"
            if [ -z "$VERSION" ]; then
                echo "error: --version requires a value" >&2
                exit 1
            fi
            shift 2
            ;;
        --version=*)
            VERSION="${1#--version=}"
            shift
            ;;
        -h|--help)
            echo "Usage: install.sh [--version <x.y.z>]"
            exit 0
            ;;
        *)
            echo "error: unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

# Detect OS and architecture, map to the release RID.
os="$(uname -s)"
arch="$(uname -m)"

case "$os" in
    Linux)
        case "$arch" in
            x86_64 | amd64) rid="linux-x64" ;;
            *)
                echo "error: unsupported Linux architecture '$arch'. Only linux-x64 (x86_64) binaries are built." >&2
                exit 1
                ;;
        esac
        ;;
    Darwin)
        case "$arch" in
            arm64 | aarch64) rid="osx-arm64" ;;
            *)
                echo "error: unsupported macOS architecture '$arch'. Only osx-arm64 (Apple Silicon) binaries are built." >&2
                exit 1
                ;;
        esac
        ;;
    *)
        echo "error: unsupported OS '$os'. This script supports Linux and macOS; use install.ps1 on Windows." >&2
        exit 1
        ;;
esac

# Resolve the release tag. Default to the latest release when no version pinned.
if [ -n "$VERSION" ]; then
    tag="cli-v${VERSION}"
    base_url="https://github.com/${REPO}/releases/download/${tag}"
else
    base_url="https://github.com/${REPO}/releases/latest/download"
fi

# The asset carries the version in its name, but for the latest release we do
# not know it ahead of time. Resolve the actual version from the release tag.
if [ -z "$VERSION" ]; then
    api_url="https://api.github.com/repos/${REPO}/releases/latest"
    if command -v curl >/dev/null 2>&1; then
        tag="$(curl -fsSL "$api_url" | grep '"tag_name"' | head -n1 | sed 's/.*"tag_name" *: *"\([^"]*\)".*/\1/')"
    elif command -v wget >/dev/null 2>&1; then
        tag="$(wget -qO- "$api_url" | grep '"tag_name"' | head -n1 | sed 's/.*"tag_name" *: *"\([^"]*\)".*/\1/')"
    else
        echo "error: neither curl nor wget is available" >&2
        exit 1
    fi
    if [ -z "$tag" ]; then
        echo "error: could not resolve the latest release tag from GitHub" >&2
        exit 1
    fi
    VERSION="${tag#cli-v}"
    base_url="https://github.com/${REPO}/releases/download/${tag}"
fi

asset="bridgemcp-${VERSION}-${rid}.tar.gz"
url="${base_url}/${asset}"

echo "Installing bridgemcp ${VERSION} (${rid})"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Download the asset.
if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$url" -o "$tmp/$asset"
elif command -v wget >/dev/null 2>&1; then
    wget -q "$url" -O "$tmp/$asset"
else
    echo "error: neither curl nor wget is available" >&2
    exit 1
fi

tar -xzf "$tmp/$asset" -C "$tmp"
binary="$tmp/bridgemcp-${VERSION}-${rid}"
chmod +x "$binary"

# Choose an install directory: ~/.local/bin preferred, /usr/local/bin fallback.
install_dir="$HOME/.local/bin"
if [ ! -d "$install_dir" ]; then
    mkdir -p "$install_dir" 2>/dev/null || true
fi

if [ -w "$install_dir" ] || mkdir -p "$install_dir" 2>/dev/null; then
    target_dir="$install_dir"
elif [ -w "/usr/local/bin" ]; then
    target_dir="/usr/local/bin"
else
    echo "error: cannot write to $install_dir or /usr/local/bin. Re-run with elevated permissions or set a writable HOME." >&2
    exit 1
fi

mv "$binary" "$target_dir/bridgemcp"
echo "Installed to $target_dir/bridgemcp"

# Warn if the install dir is not on PATH.
case ":$PATH:" in
    *":$target_dir:"*) ;;
    *)
        echo "note: $target_dir is not on your PATH. Add it, e.g.:" >&2
        echo "  export PATH=\"$target_dir:\$PATH\"" >&2
        ;;
esac

# Verify the binary runs. `bridgemcp help` is the CLI's help command (Argu).
if "$target_dir/bridgemcp" help >/dev/null 2>&1; then
    echo "Verified: bridgemcp help runs."
else
    echo "warning: installed binary but 'bridgemcp help' did not run cleanly." >&2
    exit 1
fi
