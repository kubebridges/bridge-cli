module Server.Cli.Program

open System
open Argu
open Server.Cli.Cli
open Server.Cli.Commands

[<EntryPoint>]
let main argv =
    let parser = createParser ()

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)

        let sender = ApiClient.createSender ()
        (dispatch sender results).GetAwaiter().GetResult()
    with :? ArguParseException as ex ->
        // Usage / help text (or a bad-arguments error). Argu sets ErrorCode.
        printfn "%s" ex.Message

        match ex.ErrorCode with
        | ErrorCode.HelpText -> exitOk
        | _ -> exitError
