module Server.Cli.Pkce

open System
open System.Security.Cryptography
open System.Text

// ──────────────────────────────────────────────────────────────
// PKCE (RFC 7636) + loopback helpers: pure and testable.
// ──────────────────────────────────────────────────────────────

let private base64Url (bytes: byte[]) =
    Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')

/// Generate a high-entropy code_verifier (43-128 chars, base64url).
let generateVerifier () =
    RandomNumberGenerator.GetBytes(32) |> base64Url

/// Derive the S256 code_challenge from a verifier.
let challengeOf (verifier: string) =
    use sha256 = SHA256.Create()
    verifier |> Encoding.ASCII.GetBytes |> sha256.ComputeHash |> base64Url

/// Generate an opaque state value for CSRF protection.
let generateState () =
    RandomNumberGenerator.GetBytes(24) |> base64Url

/// The loopback redirect URI for a chosen port (matches the backend's strict validation).
let redirectUri (port: int) = $"http://127.0.0.1:{port}/callback"

/// Build the authorize URL for the browser step of the login flow.
let authorizeUrl (baseUrl: string) (clientId: string) (redirect: string) (challenge: string) (state: string) =
    let b = baseUrl.TrimEnd('/')
    let enc (s: string) = Uri.EscapeDataString(s)

    $"{b}/api/v1/oauth/authorize?response_type=code&client_id={enc clientId}&redirect_uri={enc redirect}&scope=cli&code_challenge={enc challenge}&code_challenge_method=S256&state={enc state}"
