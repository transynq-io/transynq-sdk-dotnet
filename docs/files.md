# File upload and download

## Upload (multipart/form-data)

27 operations take files (documents, PODs, photos, imports, logos). Their parts are typed: a `FileUpload` per file part, plain
values for text parts.

```csharp
var added = await client.Jobs.AddJobPodsAsync(jobId, new AddJobPodsForm
{
    Files = [FileUpload.FromFile("pod.pdf", "application/pdf"), FileUpload.FromBytes(photoBytes, "photo.jpg", "image/jpeg")],
});
Console.WriteLine(string.Join(", ", added.Data.Ids));
```

`FileUpload.FromBytes`, `FromFile` and `FromStream` build a part. A request built from a non-seekable stream is not retried (it cannot be sent twice).

## Download (streamed)

Operations that answer with a file (document, POD, photo and attachment downloads, exports, journals, long-operation results)
return a streamed `FileDownload` with its media type, file name (from `Content-Disposition`) and length.

```csharp
await using var pod = (await client.Jobs.DownloadJobPodAsync(jobId, podId)).Data;   // streamed: dispose it
Console.WriteLine($"{pod.FileName} ({pod.ContentType}, {pod.Length} bytes)");
await pod.SaveToFileAsync(Path.Combine("pods", pod.FileName ?? "pod.pdf"));
```

## Operations with an undeclared answer

The contract does not declare the answer of about 400 operations; they return a buffered `ResponseBody` that may hold JSON, a
small file or nothing:

```csharp
var answer = await client.ReferenceData.ListCountryStatesAsync(countryId);   // an operation whose answer the contract does not declare
if (answer.Data.IsJson) Console.WriteLine(answer.Data.AsJson());
var asModel = answer.Data.As<CountryResponse>();   // or deserialise into a generated model when you know its shape
```
