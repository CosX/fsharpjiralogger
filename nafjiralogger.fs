module MyFunctions

open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open System.Net.Http
open System.Text
open System
open System.Net.Http.Headers
open Newtonsoft.Json

type Author = {
    Name: string;
}

type Worklog = {
    TimeSpentSeconds: int;
    Started: DateTime;
    Author: Author;
}

type WorklogResults = {
    Worklogs: Worklog[];
}

type Fields = {
    Summary: string;
    Worklog: WorklogResults;
    Customfield_10600: string;
}

type Issue = {
    Key: string;
    Fields: Fields;
}

type Issues = {
    Total: int;
    Issues: Issue[];
}

type WorkItem = {
    ProjectNumber: string;
    Started: string;
    Summary: string;
    TimeSpentSeconds: int;
    Key: string;
}

type EntryNode = {
    Logs: seq<string>;
    Sum: string;
    Project: string;
}

type Entry = {
    Date: string;
    Projects: seq<EntryNode>;
}

type PostData = {
    Username: string;
    Password: string;
    From: string;
    To: string;
}

let ConvertSecondsToHoursAndMinutes (seconds:int) = 
    let mutable minutes = seconds / 60
    let mutable hours = 0
    let mutable reminder = 0
    if minutes >= 60 then
        hours <- minutes / 60
        reminder <- minutes % 60

        if reminder > 0 then
           reminder <- reminder * 100
           reminder <- reminder / 60
    else
        hours <- 0
        reminder <- minutes * 100
        reminder <- reminder / 60
    
    if reminder < 10 then
        sprintf "%d.0%d" hours reminder
    else
        sprintf "%d.%d" hours reminder


let GetJira<'T> (path:string, username:string, password: string) = async {
    let client = new HttpClient()
    let byteArray = Encoding.ASCII.GetBytes(sprintf "%s:%s" username password)
    client.DefaultRequestHeaders.Authorization <- new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray))
    let! response = client.GetStringAsync(path) |> Async.AwaitTask
    let json = JsonConvert.DeserializeObject<'T>(response)
    return json
}

let GetWorkItems (issues: Issue[], fromDate: DateTime, toDate: DateTime, author: string) = seq {
    for issue in issues do
        let worklog = (issue.Fields.Worklog)
        for wl in worklog.Worklogs do
            if wl.Author.Name.Contains(author) && wl.Started > fromDate && wl.Started < toDate then
                yield { 
                    ProjectNumber = issue.Fields.Customfield_10600; 
                    Started = wl.Started.ToShortDateString(); 
                    Summary = issue.Fields.Summary; 
                    TimeSpentSeconds = wl.TimeSpentSeconds; 
                    Key = issue.Key; 
                }
}

[<FunctionName("nafjiralogger")>]
let run([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "nafjiralogger")>] req: HttpRequestMessage) =
    let data = Async.AwaitTask <| req.Content.ReadAsStringAsync() |> Async.RunSynchronously
    let s = JsonConvert.DeserializeObject<PostData> data
    let username = s.Username
    let password = s.Password
    let fromTime = s.From
    let toTime = s.To

    let jql = sprintf "https://nafikt.atlassian.net/rest/api/3/search?startIndex=0&jql=worklogDate >= %s and worklogDate <= %s and worklogAuthor = %s&fields=self,key,summary,worklog,customfield_10600" fromTime toTime username
    let issues = GetJira<Issues>(jql, username, password) |> Async.RunSynchronously

    let workitems = GetWorkItems(issues.Issues, DateTime.Parse(fromTime), DateTime.Parse(toTime), username)

    let worklist = workitems 
                |> Seq.groupBy (fun x -> x.Started) 
                |> Seq.map (fun (key, values) -> (key, Seq.groupBy (fun (x:WorkItem) -> x.ProjectNumber) values))
                |> Seq.sortBy (fun date -> DateTime.Parse(fst date))
                |> Seq.map (fun date -> 
                        let projects = snd date |> Seq.map (fun project -> 
                            let sum = snd project 
                                    |> Seq.sumBy (fun (x:WorkItem) -> x.TimeSpentSeconds) 
                                    |> ConvertSecondsToHoursAndMinutes
                            let logs = snd project |> Seq.map (fun log -> 
                                sprintf "%s: %s" log.Key log.Summary
                            )
                            { Logs = logs; Sum = sum; Project = fst project; }
                        )
                        { Date = fst date; Projects = projects; }
                    )

    let serialized = JsonConvert.SerializeObject worklist
    ContentResult(Content = serialized, ContentType = "application/json")