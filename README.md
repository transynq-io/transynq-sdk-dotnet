<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/qadir0108/transynq-sdk-dotnet/main/assets/brand/transynq-logo-dark.png">
    <source media="(prefers-color-scheme: light)" srcset="https://raw.githubusercontent.com/qadir0108/transynq-sdk-dotnet/main/assets/brand/transynq-logo-light.png">
    <img alt="Transynq" src="https://raw.githubusercontent.com/qadir0108/transynq-sdk-dotnet/main/assets/brand/transynq-logo-light.png" width="360">
  </picture>
</p>

<h1 align="center">.NET SDK for the Transynq TMS API</h1>

<p align="center"><em>Intelligent Transport Operations</em></p>

The official .NET client for **Transynq TMS** (`/api/v1`): all 705 operations of the API, grouped by area (jobs,
bookings, dispatch, invoicing, customers, driver app...), with typed models, authentication, retries, typed errors, paging,
optimistic concurrency, file transfer and long-running operations handled for you.

- Package: `Transynq.Sdk` - version 1.0.0 (semantic versioning, independent of the API version)
- Generated from the Transynq API OpenAPI 3.1 contract v1.0.0 plus a hand-written core
- Licence: [MIT](LICENSE)

## Requirements

- .NET 8 or .NET 10 (the package targets `net8.0` and `net10.0`)
- No other runtime dependency than `Microsoft.Extensions.Http`, `.Logging.Abstractions` and `.Options` (8.x)

## Install

```shell
dotnet add package Transynq.Sdk
```

## Quick start

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

One property per OpenAPI tag (`client.Jobs`, `client.ReferenceData`, `client.DriverApp`...), one `...Async` method per operation named after its `operationId` (`GetJobAsync`, `ListCountriesAsync`). Path parameters come first, then the body (or a generated `<Operation>Form` for uploads), then a generated `<Operation>Query` record, then `RequestOptions` and a `CancellationToken`.

Every call returns an `ApiResponse`: `ApiResponse<T>` exposes `Data`, `StatusCode`, `Headers`, `ETag`, `ApiVersion` (`X-Api-Version`), `CorrelationId` (`X-Correlation-Id`) and `Location`, and converts implicitly to `T`.

## Authentication

The API accepts three non-browser credentials; pick the one that matches your integration.

| Credential | When | Wire |
|---|---|---|
| `ApiKeyCredential` | server-to-server integrations | `X-Transynq-APIKey: <key>` |
| `SessionCredential` | tools acting as a named user | `POST /api/v1/auth/login`, then the session cookie + `X-CSRF-Token` |
| `DriverLoginCredential` / `DriverTokenCredential` | driver / POD apps (`/api/v1/driver/*`) | `Authorization: Bearer <token>` |

```csharp
var client = new TransynqClient(new TransynqClientOptions
{
    BaseUri = new Uri("https://api.example.com"),
    Credential = new ApiKeyCredential("tq_example_key_123"),   // sent as X-Transynq-APIKey
});
var who = await client.Authentication.GetApiClientAsync();     // { companyId, description }
Console.WriteLine(who.Data.AsText());
```

See [docs/authentication.md](docs/authentication.md) for sessions, CSRF and driver tokens.

## Documentation

- [Getting started](docs/getting-started.md)
- [Authentication](docs/authentication.md)
- [Configuration](docs/configuration.md)
- [Error handling](docs/error-handling.md)
- [Pagination and grid queries](docs/pagination.md)
- [Concurrency (ETag / If-Match)](docs/concurrency.md)
- [Idempotency](docs/idempotency.md)
- [Long-running operations](docs/long-running-operations.md)
- [File upload and download](docs/files.md)
- [Logging and observability](docs/logging-and-observability.md)
- [Dates, money and enums (type mapping)](docs/types.md)
- [Testing your integration](docs/testing.md)
- [Versioning and compatibility](docs/versioning.md)
- [Troubleshooting and FAQ](docs/troubleshooting.md)
- [Resource index (every operation, generated)](docs/resources/README.md)

Runnable samples of real flows (sign in, list and page grid rows, create a booking idempotently, update with If-Match, list
invoices, upload a POD, wait on a long operation) are in [`examples/Transynq.Sdk.Examples/Program.cs`](examples/Transynq.Sdk.Examples/Program.cs).

The API reference is generated from the XML documentation comments with DocFX (`docfx.json` in this repository):

```shell
dotnet tool update -g docfx
docfx docfx.json   # writes _site/ (not checked in)
```

## Support

- Bugs and feature requests: [GitHub issues](https://github.com/qadir0108/transynq-sdk-dotnet/issues) (use the templates; never paste real keys, passwords or customer data).
- Security problems: see [SECURITY.md](SECURITY.md) - do not open a public issue.
- When the API answers with an error, quote its correlation id (`correlationId` on the error).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md).

## Licence

[MIT](LICENSE) - Copyright (c) 2026 Transynq.

The Transynq name and logos are trademarks of Transynq and are not covered by the MIT licence; you may use them only to refer to Transynq.
