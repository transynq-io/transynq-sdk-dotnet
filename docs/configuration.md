# Configuration

## Base URL, timeouts and retries

```csharp
var options = new TransynqClientOptions
{
    BaseUri = new Uri("https://api.example.com"),
    Credential = new ApiKeyCredential(apiKey),
    Timeout = TimeSpan.FromSeconds(30),          // per attempt (default 60 s)
    MaxRetries = 3,                              // default 2; 0 turns retries off
    RetryBaseDelay = TimeSpan.FromMilliseconds(250),
    MaxRetryDelay = TimeSpan.FromSeconds(5),
    MaxRetryAfter = TimeSpan.FromSeconds(30),
    UserAgentSuffix = "acme-erp-sync/2.1",       // User-Agent: transynq-sdk-dotnet/1.0.0 acme-erp-sync/2.1
};
using var client = new TransynqClient(options);

// per call
await client.ReferenceData.ListCountriesAsync(new RequestOptions { Timeout = TimeSpan.FromSeconds(5), MaxRetries = 0 });
```

| Setting | Default | Meaning |
|---|---|---|
| base URL | required | The API host, for example `https://api.example.com`; paths such as `/api/v1/jobs` are appended. |
| credential | none | See [Authentication](authentication.md). |
| timeout | 60 s | Per attempt (sending and reading a buffered answer). |
| max retries | 2 | Retries after the first attempt for retryable failures; 0 turns retries off. |
| retry base delay | 0.5 s | First back-off delay, doubled per attempt, with full jitter. |
| max retry delay | 8 s | Cap of a computed back-off delay. |
| max Retry-After | 60 s | A longer `Retry-After` is not waited for: the rate-limit error is raised. |
| user agent suffix | none | Appended to `User-Agent: transynq-sdk-dotnet/1.0.0`. |
| hooks / logger | none | See [Logging and observability](logging-and-observability.md). |

Per-call `RequestOptions` override the timeout and retries and add `If-Match`, `Idempotency-Key`, extra headers and a
correlation id.

## Retries

Retried automatically (default `2` retries after the first attempt):

- network failures and timeouts, and the statuses 408, 429, 500, 502, 503 and 504 (never 501 or any other 4xx);
- only when repeating the request is safe: `GET`, `HEAD`, `OPTIONS`, `PUT`, `DELETE`; any request that carries an `Idempotency-Key`;
  a create whose JSON body carries a client-generated, non-empty `id`. A 429 is always retried (the rate limiter refuses before
  the endpoint runs);
- the wait is exponential with full jitter (0.5 s, 1 s, 2 s... capped at 8 s), or the server's `Retry-After` when it sends one.
  A `Retry-After` longer than 60 s is not waited for: the rate-limit error is raised so you can schedule the work;
- every attempt of one call carries the same `X-Correlation-Id`, so the server logs show one call;
- a 401 with a credential that can sign in again (session, driver login) is retried once after signing in, independently of the
  retry count.

## Custom HTTP client and proxies

```csharp
// your own HttpClient (handlers, proxy, certificates). Keep its cookie jar off: the SDK keeps the session cookie itself.
var handler = new SocketsHttpHandler
{
    UseCookies = false,
    Proxy = new WebProxy("http://proxy.example.com:8080"),
    UseProxy = true,
};
var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };   // the SDK applies its own timeout
using var client = new TransynqClient(options, http);
```

## Dependency injection

```csharp
// Program.cs (ASP.NET Core / generic host)
builder.Services.AddTransynqClient(o =>
{
    o.BaseUri = new Uri(builder.Configuration["Transynq:BaseUrl"]!);
    o.Credential = new ApiKeyCredential(builder.Configuration["Transynq:ApiKey"]!);
})
.AddHttpMessageHandler<MyAuditHandler>();   // optional: it returns the IHttpClientBuilder

// any service
public sealed class InvoiceSync(TransynqClient transynq) { /* ... */ }
```

The options (and therefore the credential and its session) are a singleton shared by every client instance; `IHttpClientFactory` manages the handlers (cookie jar off, decompression on).

## Several companies or environments

Create one client per base URL and credential and keep it for the life of your process; clients are thread-safe. Two
credentials need two clients.
