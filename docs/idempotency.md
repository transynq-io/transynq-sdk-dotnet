# Idempotency

## Creates with a client-generated id

Creates are idempotent by id: you generate the record's `id` and send it in the body. Sending the same request again returns the
same record (200); reusing the id with different data is refused with 422 `id-in-use`. Because of this, the SDK retries such
creates after a network failure or a 5xx without risk of duplicates.

```csharp
var holiday = new PublicHolidayRequest { Id = Ids.NewId(), Name = "Show Day", Date = new DateOnly(2026, 10, 23), CountryId = 1, States = ["QLD"] };
await client.ReferenceData.CreatePublicHolidayAsync(holiday);   // safe to retry: same id, same record
```

## Idempotency-Key for commands

Commands without a natural id may carry an `Idempotency-Key` header. Sending one also makes the SDK treat the request as safe to
retry.

```csharp
await client.Jobs.RepriceJobsAsync(request, RequestOptions.WithNewIdempotencyKey());
await client.Jobs.RepriceJobsAsync(request, new RequestOptions { IdempotencyKey = "reprice-2026-11-16-run-1" });
```

Note: the API accepts the header (it is allowed through CORS) but, in API v1.0.0, no endpoint de-duplicates by it yet; until it
does, prefer creates with a client id, and only add an idempotency key to commands that are naturally repeatable.
