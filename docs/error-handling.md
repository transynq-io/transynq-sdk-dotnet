# Error handling

Every non-2xx answer becomes a typed error built from the API's ProblemDetails body
(`type`, `title`, `status`, `detail`, `code`, `correlationId`, and `errors` for validation).

| Status | `code` (examples) | .NET exception |
|---|---|---|
| 400 | `validation`, `unreadable-request` | `ValidationException` (`Errors`) |
| 401 | `invalid-token` or none | `AuthenticationException` |
| 403 | `forbidden` or none | `PermissionDeniedException` |
| 404 | `not-found`, `unknown-company` | `NotFoundException` |
| 409 | `conflict` | `ConflictException` |
| 412 | `stale-version` | `StaleVersionException` |
| 422 | the rule: `year-exists`, `id-in-use`, `rule-violated`, `mail-held-back`... | `BusinessRuleException` |
| 429 | none (`Retry-After` header) | `RateLimitException` (`RetryAfter`) |
| 5xx | `unexpected`, `not-implemented` (501) | `ServerException` |
| other 4xx | as sent | `ApiException` |
| no answer | - | `TransynqConnectionException` / `TransynqTimeoutException` |

Every `ApiException` exposes `StatusCode`, `Code`, `Title`, `Detail`, `Type`, `CorrelationId`, `RawBody`, `Headers` and `OperationId`. All SDK errors derive from `TransynqException`; a failed long operation raises `LongOperationFailedException` (`Operation`).

```csharp
try
{
    await client.ReferenceData.CreatePublicHolidayAsync(holiday);
}
catch (ValidationException e)          // 400
{
    foreach (var (field, messages) in e.Errors) Console.WriteLine($"{field}: {string.Join("; ", messages)}");
}
catch (BusinessRuleException e)        // 422
{
    Console.WriteLine($"Refused by rule {e.Code}: {e.Detail}");
}
catch (RateLimitException e)           // 429 after retries
{
    Console.WriteLine($"Try again in {e.RetryAfter}");
}
catch (ApiException e)                 // any other answer
{
    Console.WriteLine($"{e.StatusCode} {e.Code} {e.Title} - quote correlation id {e.CorrelationId}");
}
catch (TransynqConnectionException e)  // no answer (TransynqTimeoutException derives from it)
{
    Console.WriteLine(e.Message);
}
```

## What the fields mean

- `code` is stable across languages and releases: branch on it, not on `title` (the title is translated into the user's language).
- `detail` is the business rule's sentence for 422 answers.
- `correlationId` identifies the request in the API's logs. It is in every problem body and in the `X-Correlation-Id` header;
  quote it when you contact support. The SDK sends its own correlation id on every request (the same on every retry of one call).
- Out-of-scope ids answer **404**, never 403: you cannot tell "missing" from "not yours".
- A 400 is also returned when `If-Match` is missing on a PUT or DELETE that requires it.

## Retries happen before you see an error

Retryable failures (see [Configuration](configuration.md#retries)) are retried first; the error you catch is the last answer.
