# Concurrency (ETag / If-Match)

The API uses optimistic concurrency. A GET, POST or PUT answer carries an `ETag`; a PUT or DELETE must send it back in
`If-Match`:

- no `If-Match` -> **400** (`validation`);
- an `If-Match` that no longer matches -> **412** (`stale-version`): someone saved first; reload and apply your change again.

```csharp
var current = await client.ReferenceData.GetPublicHolidayAsync(id);        // ETag: "..."
var updated = await client.ReferenceData.UpdatePublicHolidayAsync(id,
    new PublicHolidayRequest { Id = id, Name = "Australia Day", Date = current.Data.Date, CountryId = current.Data.CountryId, States = current.Data.States },
    RequestOptions.WithIfMatch(current.ETag!));                               // 412 StaleVersionException if someone saved first
```

## Read-modify-write helper

The helper loads the record (with its ETag), lets you build the change, saves it with `If-Match`, and on a stale version
loads again and repeats (3 attempts by default):

```csharp
var saved = await Concurrency.ReadModifyWriteAsync(
    ct => client.ReferenceData.GetPublicHolidayAsync(id, null, ct),
    (current, options, ct) => client.ReferenceData.UpdatePublicHolidayAsync(id,
        new PublicHolidayRequest { Id = current.Id, Name = current.Name + " (observed)", Date = current.Date, CountryId = current.CountryId, States = current.States },
        options, ct),
    maxAttempts: 3);
```

The ETag is passed through verbatim (quotes included); never build one yourself.
