module Server.Cli.Tests.ConfigTests

open Xunit
open FsUnit.Xunit
open Server.Cli.Config

let private stored (url: string) (token: string) : StoredConfig = { Url = url; Token = token }

// ──────────────────────────────────────────────────────────────
// resolveWith: precedence flag > env > file, default URL
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``flag url wins over env and file`` () =
    let cfg =
        resolveWith (Some "https://flag.example") (Some "https://env.example") None (stored "https://file.example" "")

    cfg.Url |> should equal "https://flag.example"

[<Fact>]
let ``env url wins over file when no flag`` () =
    let cfg =
        resolveWith None (Some "https://env.example") None (stored "https://file.example" "")

    cfg.Url |> should equal "https://env.example"

[<Fact>]
let ``file url used when no flag or env`` () =
    let cfg = resolveWith None None None (stored "https://file.example" "")
    cfg.Url |> should equal "https://file.example"

[<Fact>]
let ``default url used when nothing set`` () =
    let cfg = resolveWith None None None (stored "" "")
    cfg.Url |> should equal defaultUrl

[<Fact>]
let ``trailing slash is trimmed from resolved url`` () =
    let cfg = resolveWith (Some "https://flag.example/") None None (stored "" "")
    cfg.Url |> should equal "https://flag.example"

[<Fact>]
let ``whitespace flag falls through to env`` () =
    let cfg = resolveWith (Some "   ") (Some "https://env.example") None (stored "" "")
    cfg.Url |> should equal "https://env.example"

// ──────────────────────────────────────────────────────────────
// token precedence: env > file (no flag for token)
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``env token wins over file token`` () =
    let cfg = resolveWith None None (Some "brg_cli_env") (stored "" "brg_cli_file")
    cfg.Token |> should equal (Some "brg_cli_env")

[<Fact>]
let ``file token used when no env token`` () =
    let cfg = resolveWith None None None (stored "" "brg_cli_file")
    cfg.Token |> should equal (Some "brg_cli_file")

[<Fact>]
let ``token is None when nothing set`` () =
    let cfg = resolveWith None None None (stored "" "")
    cfg.Token |> should equal (None: string option)

// ──────────────────────────────────────────────────────────────
// parse / serialize round-trip
// ──────────────────────────────────────────────────────────────

[<Fact>]
let ``serialize then parse round-trips`` () =
    let original = stored "https://x.example" "brg_cli_abc"
    let text = serializeConfig original

    match parseConfig text with
    | Ok c ->
        c.Url |> should equal "https://x.example"
        c.Token |> should equal "brg_cli_abc"
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``parse empty text yields empty config`` () =
    match parseConfig "" with
    | Ok c ->
        c.Url |> should equal ""
        c.Token |> should equal ""
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``parse malformed json yields ConfigReadError`` () =
    match parseConfig "{ not json" with
    | Error(ConfigReadError _) -> ()
    | other -> failwith $"expected ConfigReadError, got {other}"
