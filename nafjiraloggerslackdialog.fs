module MyFunctions2

open System
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open System.Net.Http
open Microsoft.Extensions.Logging

[<FunctionName("nafjiraloggerslackdialog")>]
let run([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "nafjiraloggerslackdialog")>] req: HttpRequestMessage, logger: ILogger) =
    let data = Async.AwaitTask <| req.Content.ReadAsFormDataAsync() |> Async.RunSynchronously
    let client = new HttpClient()
    logger.LogInformation(data.["trigger_id"])
    let dialog = String.Format("""
    {
      "token": "***",
      "trigger_id": "{0}",
      "dialog": {
        "title": "Submit a helpdesk ticket",
        "callback_id": "submit-ticket",
        "submit_label": "Submit",
        "elements": [
          {
            "label": "Title",
            "type": "text",
            "name": "title"
          }
        ]
      }
    }
    """, data.["trigger_id"])
    logger.LogInformation(dialog)
    let a = Async.AwaitTask <| client.PostAsJsonAsync("https://slack.com/api/dialog.open", dialog) |> Async.RunSynchronously
    ContentResult(Content = "{\"ok\": true}", ContentType = "application/json")