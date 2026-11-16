# Pagination and grid queries

## Paged collections

Collections answer `PagedResponse { items, page, pageSize, total }` (`page` starts at 1, `pageSize` 1..200, default 50;
`sort` is a comma list such as `-date,name` from the resource's whitelist). Operations that return one also have an
**auto-paging** variant that reads page after page until an empty page or `page * pageSize >= total`.

```csharp
// one page
var page = await client.ReferenceData.ListPublicHolidaysAsync(new ListPublicHolidaysQuery { CountryId = 1, Year = 2026, Page = 1, PageSize = 50, Sort = "-date" });
Console.WriteLine($"{page.Data.Items.Count} of {page.Data.Total}");

// every item, page by page (IAsyncEnumerable)
await foreach (var holiday in client.ReferenceData.ListPublicHolidaysAutoPagingAsync(new ListPublicHolidaysQuery { CountryId = 1, Year = 2026 }))
    Console.WriteLine($"{holiday.Date}: {holiday.Name}");
```

## Grid queries (large lists)

Large lists - jobs, invoices, manifests - are read through the grid endpoints: `GET /api/v1/grids` lists the grids,
`GET /api/v1/grids/{id}` is a grid's definition (its columns, filter operators and sort keys are the whitelist), and
`POST /api/v1/grids/{id}/query` reads one page of rows (page size up to 500, at most 4 sort keys; filter values as text, dates
`yyyy-MM-dd` or relative such as `today-30`). The SDK's grid helper iterates every row:

```csharp
var definition = (await client.Grids.GetGridAsync("jobs")).Data;           // the columns and operators you may use
var request = new GridQueryRequest
{
    Columns = ["jobNumber", "customer", "status"],
    Filters = [new GridFilterDto { Column = "status", Operator = "eq", Values = ["Booked"] }],
    Sort = null, Search = null, Summaries = null,
    PageSize = 500,
};
await foreach (var row in client.QueryGridRowsAsync("jobs", request))       // JsonElement per row
    Console.WriteLine(row.GetProperty("jobNumber"));
```

Value lists (`.../facets/{column}`), CSV export (`.../export`, up to 100,000 rows, a streamed file) and saved views
(`.../views/{viewId}`) are ordinary operations of the Grids group; see the [resource index](resources/README.md#grids).
