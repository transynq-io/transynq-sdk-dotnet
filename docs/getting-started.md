# Getting started

## 1. Install

```shell
dotnet add package Transynq.Sdk
```

## 2. Get a credential

Ask your Transynq administrator for an **API client key** (recommended for integrations: it is tied to one company and needs no
password), or use a user name and password for a session. Keep secrets out of source control: read them from the environment or a
secret store.

## 3. Make a first call

```csharp
using Transynq.Sdk;
using Transynq.Sdk.Credentials;

using var client = new TransynqClient(new TransynqClientOptions
{
    BaseUri = new Uri("https://api.example.com"),
    Credential = new ApiKeyCredential(Environment.GetEnvironmentVariable("TRANSYNQ_API_KEY")!),
});

var countries = await client.ReferenceData.ListCountriesAsync();
foreach (var country in countries.Data)
    Console.WriteLine(country.Name);
Console.WriteLine($"Answered by API {countries.ApiVersion}, correlation id {countries.CorrelationId}");
```

## 4. Learn the shape of the client

One property per OpenAPI tag (`client.Jobs`, `client.ReferenceData`, `client.DriverApp`...), one `...Async` method per operation named after its `operationId` (`GetJobAsync`, `ListCountriesAsync`). Path parameters come first, then the body (or a generated `<Operation>Form` for uploads), then a generated `<Operation>Query` record, then `RequestOptions` and a `CancellationToken`.

- Every answer is an `ApiResponse` with the data and the headers you need: `ApiResponse<T>` exposes `Data`, `StatusCode`, `Headers`, `ETag`, `ApiVersion` (`X-Api-Version`), `CorrelationId` (`X-Correlation-Id`) and `Location`, and converts implicitly to `T`.
- About 400 operations do not declare their answer in the contract (they return JSON, a file or nothing). They return a buffered
  `ResponseBody` (bytes, content type, file name) with JSON helpers; clear file downloads return a streamed `FileDownload`.
- The [resource index](resources/README.md) lists every operation with its method name.

## 5. Next steps

- [Authentication](authentication.md) - sessions, CSRF and driver tokens.
- [Error handling](error-handling.md) - typed errors and correlation ids.
- [Pagination](pagination.md), [Concurrency](concurrency.md), [Idempotency](idempotency.md), [Long-running operations](long-running-operations.md).
- The runnable samples in `examples/Transynq.Sdk.Examples/Program.cs` (`dotnet run --project examples/Transynq.Sdk.Examples -- sign-in`).
