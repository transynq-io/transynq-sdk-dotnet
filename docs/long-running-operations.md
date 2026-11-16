# Long-running operations

Some operations (background imports, document packs, reports) queue work and answer **202 Accepted** with
`Location: /api/v1/long-operations/{id}` and a body `{ id, status }`. The operation then goes through the states
`Queued` -> `Running` -> `Succeeded` or `Failed`; `GET /api/v1/long-operations/{id}` reads it
(`{ id, kind, inputName, requestedBy, requestedUtc, status, startedUtc, finishedUtc, result, error }`), and
`GET /api/v1/long-operations/{id}/file` downloads a produced file (a workbook or a PDF).

```csharp
var accepted = await client.Imports.ImportZonesInBackgroundAsync(new ImportZonesInBackgroundForm { File = FileUpload.FromFile("zones.xlsx") });
var poller = client.PollLongOperation(accepted);               // from the 202 Location header
var done = await poller.WaitAsync(
    pollInterval: TimeSpan.FromSeconds(2),
    timeout: TimeSpan.FromMinutes(10),
    onProgress: op => Console.WriteLine(op.Status));
Console.WriteLine(done.Result);                                // JsonElement: the import report

// a report or document pack: download its file
await using var file = await poller.DownloadResultAsync();
await file.SaveToFileAsync(file.FileName ?? "result.xlsx");
```

`WaitAsync` throws `LongOperationFailedException` (with `Operation.Error`) when the operation ends `Failed`, and `TimeoutException` after `timeout`. `client.PollLongOperation(Guid)` follows an operation by id.

There is no numeric progress in the API: the progress callback receives every state read. Poll intervals of 1 to 5 seconds are
appropriate; the API also notifies the browser UI through SignalR, which SDK clients do not need.
