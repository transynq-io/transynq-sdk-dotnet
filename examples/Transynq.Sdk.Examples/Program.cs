// Runnable samples of real Transynq flows. Configure with environment variables (never hard-code secrets):
//   TRANSYNQ_BASE_URL   e.g. https://api.example.com
//   TRANSYNQ_API_KEY    an API client key, or
//   TRANSYNQ_USER / TRANSYNQ_PASSWORD   a user for session sign-in
// Run: dotnet run --project examples/Transynq.Sdk.Examples -- <flow>
// Flows: sign-in | page-grid | create-booking | update-holiday | list-invoices | upload-pod | long-operation
using Transynq.Sdk;
using Transynq.Sdk.Credentials;
using Transynq.Sdk.Examples;

var flow = args.Length > 0 ? args[0] : "sign-in";
using var client = Samples.CreateClient();

switch (flow)
{
    case "sign-in":
        await Samples.SignInAsync(client);
        break;
    case "page-grid":
        await Samples.PageGridAsync(client, args.ElementAtOrDefault(1) ?? "jobs");
        break;
    case "create-booking":
        await Samples.CreateBookingAsync(client, Guid.Parse(args[1]), Guid.Parse(args[2]), Guid.Parse(args[3]));
        break;
    case "update-holiday":
        await Samples.UpdatePublicHolidayAsync(client, Guid.Parse(args[1]));
        break;
    case "list-invoices":
        await Samples.ListInvoicesAsync(client);
        break;
    case "upload-pod":
        await Samples.UploadPodAsync(client, Guid.Parse(args[1]), args[2]);
        break;
    case "long-operation":
        await Samples.ImportZonesAsync(client, args[1]);
        break;
    default:
        Console.Error.WriteLine($"Unknown flow '{flow}'.");
        return 1;
}
return 0;

namespace Transynq.Sdk.Examples
{
    using Transynq.Sdk.Models;

    internal static class Samples
    {
        public static TransynqClient CreateClient()
        {
            var baseUri = new Uri(Environment.GetEnvironmentVariable("TRANSYNQ_BASE_URL") ?? "https://api.example.com");
            ITransynqCredential credential = Environment.GetEnvironmentVariable("TRANSYNQ_API_KEY") is { Length: > 0 } key
                ? new ApiKeyCredential(key)
                : new SessionCredential(
                    Environment.GetEnvironmentVariable("TRANSYNQ_USER") ?? "user@example.com",
                    Environment.GetEnvironmentVariable("TRANSYNQ_PASSWORD") ?? "change-me");
            return new TransynqClient(new TransynqClientOptions { BaseUri = baseUri, Credential = credential, UserAgentSuffix = "transynq-examples/1.0" });
        }

        /// <summary>Sign in (session or API key) and show who the client acts as.</summary>
        public static async Task SignInAsync(TransynqClient client)
        {
            if (client.Options.Credential is SessionCredential)
            {
                var me = await client.Authentication.GetMeAsync();
                var profile = me.Data.AsJson()!.Value;
                Console.WriteLine($"Signed in as {profile.GetProperty("userName").GetString()} of {profile.GetProperty("companyName").GetString()} (API {me.ApiVersion}).");
                await client.SignOutAsync();
            }
            else
            {
                var apiClient = await client.Authentication.GetApiClientAsync();
                Console.WriteLine($"API key acts for company {apiClient.Data.AsJson()!.Value.GetProperty("companyId")}.");
            }
        }

        /// <summary>List the grids, then read every row of one (jobs are listed through the grid endpoints).</summary>
        public static async Task PageGridAsync(TransynqClient client, string gridId)
        {
            foreach (var grid in (await client.Grids.ListGridsAsync()).Data)
                Console.WriteLine($"grid {grid.Id}: {grid.Title}");

            var definition = (await client.Grids.GetGridAsync(gridId)).Data;
            Console.WriteLine($"Reading grid {definition.Id}");
            var request = new GridQueryRequest { Columns = null, Filters = null, Sort = null, Search = null, Summaries = null, PageSize = 200 };
            var count = 0;
            await foreach (var row in client.QueryGridRowsAsync(gridId, request))
            {
                if (++count <= 5) Console.WriteLine(row);
            }
            Console.WriteLine($"{count} rows");

            // a classic paged collection with auto-paging
            await foreach (var reason in client.ReferenceData.ListJobStatusReasonsAutoPagingAsync(new() { ActiveOnly = true, PageSize = 100 }))
                Console.WriteLine($"{reason.ReasonType}: {reason.Name}");
        }

        /// <summary>Create a job (booking) idempotently: the client-generated id makes a retry return the same record.</summary>
        public static async Task CreateBookingAsync(TransynqClient client, Guid customerId, Guid senderId, Guid recipientId)
        {
            var booking = new BookingRequestDto
            {
                Id = Ids.NewId(),
                CustomerId = customerId,
                SenderId = senderId,
                RecipientId = recipientId,
                BookDate = DateTimeOffset.UtcNow,
                InvoicingOnly = false,
                SpecialInstructions = "Call before delivery",
                Products = [],
                Legs = [],
                Pricings = [],
                Notes = [],
            };
            var created = await client.Bookings.CreateBookingAsync(booking);
            Console.WriteLine($"Booking {booking.Id} saved ({created.StatusCode}); correlation id {created.CorrelationId}.");
        }

        /// <summary>Update with optimistic concurrency: read the ETag, send it as If-Match, and retry on a stale version.</summary>
        public static async Task UpdatePublicHolidayAsync(TransynqClient client, Guid holidayId)
        {
            var saved = await Concurrency.ReadModifyWriteAsync(
                ct => client.ReferenceData.GetPublicHolidayAsync(holidayId, null, ct),
                (current, options, ct) => client.ReferenceData.UpdatePublicHolidayAsync(holidayId, new PublicHolidayRequest
                {
                    Id = current.Id,
                    Name = current.Name.Trim(),
                    Date = current.Date,
                    CountryId = current.CountryId,
                    States = current.States,
                }, options, ct));
            Console.WriteLine($"Saved; new ETag {saved.ETag}.");
        }

        /// <summary>List sales-ledger invoices of the last 30 days.</summary>
        public static async Task ListInvoicesAsync(TransynqClient client)
        {
            var response = await client.SalesLedger.GetSalesLedgerInvoicesAsync(new GetSalesLedgerInvoicesQuery
            {
                From = DateTimeOffset.UtcNow.AddDays(-30),
                To = DateTimeOffset.UtcNow,
                Take = 50,
            });
            Console.WriteLine(response.Data.AsText());
        }

        /// <summary>Upload a proof of delivery to a job.</summary>
        public static async Task UploadPodAsync(TransynqClient client, Guid jobId, string path)
        {
            var added = await client.Jobs.AddJobPodsAsync(jobId,
                new AddJobPodsForm { Files = [FileUpload.FromFile(path, "application/pdf")] });
            Console.WriteLine($"Added POD(s): {string.Join(", ", added.Data.Ids)}");
        }

        /// <summary>Queue a background import and wait for the long operation to finish.</summary>
        public static async Task ImportZonesAsync(TransynqClient client, string workbookPath)
        {
            var accepted = await client.Imports.ImportZonesInBackgroundAsync(new ImportZonesInBackgroundForm
            {
                File = FileUpload.FromFile(workbookPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            });
            try
            {
                var done = await client.PollLongOperation(accepted).WaitAsync(
                    pollInterval: TimeSpan.FromSeconds(2),
                    onProgress: op => Console.WriteLine($"{op.Kind}: {op.Status}"));
                Console.WriteLine($"Result: {done.Result}");
            }
            catch (LongOperationFailedException failed)
            {
                Console.Error.WriteLine($"Import failed: {failed.Operation?.Error}");
            }
        }
    }
}
