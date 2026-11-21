<p align="center">
  <a href="https://transynq.io/"><img alt="Transynq - transport management software" src="https://raw.githubusercontent.com/transynq-io/transynq-sdk-dotnet/main/assets/brand/transynq-readme-banner.png" width="720"></a>
</p>

<h1 align="center">Transynq TMS .NET SDK</h1>

<p align="center"><em>The official .NET client for the <a href="https://transynq.io/transport-management-system">Transynq transport management system (TMS)</a> API</em></p>

<p align="center">
  <a href="https://github.com/transynq-io/transynq-sdk-dotnet/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/transynq-io/transynq-sdk-dotnet/actions/workflows/ci.yml/badge.svg?branch=main"></a>
  <a href="https://github.com/transynq-io/transynq-sdk-dotnet/actions/workflows/release.yml"><img alt="Release" src="https://github.com/transynq-io/transynq-sdk-dotnet/actions/workflows/release.yml/badge.svg"></a>
  <a href="https://www.nuget.org/packages/Transynq.Sdk"><img alt="NuGet" src="https://img.shields.io/nuget/v/Transynq.Sdk?logo=nuget&color=2563EB"></a>
  <a href="https://www.nuget.org/packages/Transynq.Sdk"><img alt="Downloads" src="https://img.shields.io/nuget/dt/Transynq.Sdk?color=06B6D4"></a>
  <img alt="Runtime" src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?logo=dotnet">
  <a href="https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-8B5CF6"></a>
  <a href="https://github.com/transynq-io/transynq-sdk-dotnet/tree/main/docs/"><img alt="Docs" src="https://img.shields.io/badge/docs-guides-2563EB"></a>
  <img alt="API operations" src="https://img.shields.io/badge/API%20operations-705-0B1220">
  <a href="https://transynq.io/"><img alt="Website: transynq.io" src="https://img.shields.io/badge/website-transynq.io-2563EB"></a>
  <a href="https://transynq.io/transport-management-system"><img alt="Transynq TMS" src="https://img.shields.io/badge/product-Transynq%20TMS-8B5CF6"></a>
</p>

The official .NET SDK for [**Transynq TMS**](https://transynq.io/transport-management-system), the transport management system (TMS) from
[Transynq](https://transynq.io/) for carriers, fleet operators, 3PLs, brokers and shippers. Use it to connect your own
systems to bookings, dispatch, live tracking, driver proof of delivery, invoicing and costing: all 705 operations
of the Transynq API (`/api/v1`), grouped by area, with typed models, authentication, retries, typed errors,
paging, optimistic concurrency, file transfer and long-running operations handled for you.

- Package: `Transynq.Sdk` - version 1.0.3 (semantic versioning, independent of the API version)
- Generated from the Transynq API OpenAPI 3.1 contract v1.0.0 plus a hand-written core
- Licence: [MIT](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/LICENSE)

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

See [docs/authentication.md](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/authentication.md) for sessions, CSRF and driver tokens.

## Documentation

- [Getting started](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/getting-started.md)
- [Authentication](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/authentication.md)
- [Configuration](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/configuration.md)
- [Error handling](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/error-handling.md)
- [Pagination and grid queries](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/pagination.md)
- [Concurrency (ETag / If-Match)](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/concurrency.md)
- [Idempotency](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/idempotency.md)
- [Long-running operations](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/long-running-operations.md)
- [File upload and download](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/files.md)
- [Logging and observability](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/logging-and-observability.md)
- [Dates, money and enums (type mapping)](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/types.md)
- [Testing your integration](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/testing.md)
- [Versioning and compatibility](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/versioning.md)
- [Troubleshooting and FAQ](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/troubleshooting.md)
- [Resource index (every operation, generated)](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/docs/resources/README.md)

Runnable samples of real flows (sign in, list and page grid rows, create a booking idempotently, update with If-Match, list
invoices, upload a POD, wait on a long operation) are in [`examples/Transynq.Sdk.Examples/Program.cs`](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/examples/Transynq.Sdk.Examples/Program.cs).

The API reference is generated from the XML documentation comments with DocFX (`docfx.json` in this repository):

```shell
dotnet tool update -g docfx
docfx docfx.json   # writes _site/ (not checked in)
```

## About Transynq

[**Transynq**](https://transynq.io/) is transport management software for carriers, fleet operators, 3PLs, brokers and shippers.
[**Transynq TMS**](https://transynq.io/transport-management-system), the Transynq transport management system, keeps the whole job on one record, from booking
to invoice: jobs and planning, dispatch, live tracking, a driver app with electronic proof of delivery (POD), invoicing,
costing, compliance and AI. This SDK is the official way to connect your own .NET applications to it.

- Website: [transynq.io](https://transynq.io/)
- Transport management system: [transynq.io/transport-management-system](https://transynq.io/transport-management-system)
- Contact: [contact@transynq.io](mailto:contact@transynq.io)

## Support

- Bugs and feature requests: [GitHub issues](https://github.com/transynq-io/transynq-sdk-dotnet/issues) (use the templates; never paste real keys, passwords or customer data).
- Security problems: see [SECURITY.md](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/SECURITY.md) - do not open a public issue.
- When the API answers with an error, quote its correlation id (`correlationId` on the error).

## Contributing

See [CONTRIBUTING.md](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/CONTRIBUTING.md) and the [code of conduct](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/CODE_OF_CONDUCT.md).

## Licence

[MIT](https://github.com/transynq-io/transynq-sdk-dotnet/blob/main/LICENSE) - Copyright (c) 2026 Transynq.

The Transynq name and logos are trademarks of Transynq and are not covered by the MIT licence; you may use them only to refer to Transynq.

---

<p align="center">
  <a href="https://transynq.io/"><img alt="Transynq" src="https://raw.githubusercontent.com/transynq-io/transynq-sdk-dotnet/main/assets/brand/transynq-icon-64.png" width="40"></a><br>
  <a href="https://transynq.io/transport-management-system"><strong>Transynq TMS</strong></a> &middot; transport management software by <a href="https://transynq.io/">Transynq</a><br>
  <sub>Smarter Transport. Connected Operations.</sub>
</p>
