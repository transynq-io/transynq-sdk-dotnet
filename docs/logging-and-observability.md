# Logging and observability

```csharp
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var options = new TransynqClientOptions
{
    BaseUri = new Uri("https://api.example.com"),
    Credential = new ApiKeyCredential(apiKey),
    Logger = loggerFactory.CreateLogger("Transynq"),          // one debug line per attempt
    OnRequest = info => { Console.WriteLine($"-> {info.Method} {info.OperationId} #{info.Attempt}"); return ValueTask.CompletedTask; },
    OnResponse = info => { Console.WriteLine($"<- {info.StatusCode} in {info.Elapsed.TotalMilliseconds} ms"); return ValueTask.CompletedTask; },
};
```

- The logger receives one debug line per attempt: operation id, attempt number, status (or `timeout` / network error),
  duration and correlation id. Never request or response bodies, never credentials.
- The request hook runs before every attempt (method, URL, operation id, attempt, headers); the response hook after every
  answer (status, elapsed time, headers). Credential headers are `[redacted]` in both.
- Every request carries `User-Agent: transynq-sdk-dotnet/1.0.0` (plus your suffix) and a client-generated `X-Correlation-Id`
  (32 hex characters, the same on every retry of one call). The API logs under that id and echoes it back.

## On every response

`ApiResponse<T>` exposes `Data`, `StatusCode`, `Headers`, `ETag`, `ApiVersion` (`X-Api-Version`), `CorrelationId` (`X-Correlation-Id`) and `Location`, and converts implicitly to `T`.

`X-Api-Version` is the version of the contract that answered; log it next to your own version to diagnose a mismatch after an
API release.
