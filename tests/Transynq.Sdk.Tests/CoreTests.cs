using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Transynq.Sdk.Credentials;
using Transynq.Sdk.Models;
using Transynq.Sdk.Serialization;

namespace Transynq.Sdk.Tests;

public class ContractTests
{
    private static readonly Guid Id = Guid.Parse("6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b");

    [Fact]
    public async Task Get_by_id_builds_method_path_and_standard_headers()
    {
        var (client, http, _) = TestClients.Create(o => o.UserAgentSuffix = "acme/1.0");
        http.Enqueue(HttpStatusCode.OK, """{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","name":"Anzac Day","date":"2026-04-25","countryId":1,"states":[]}""",
            ("ETag", "\"v1\""), ("X-Api-Version", "1.0.0"), ("X-Correlation-Id", "abc12345"));

        var response = await client.ReferenceData.GetPublicHolidayAsync(Id);

        var r = Assert.Single(http.Requests);
        Assert.Equal("GET", r.Method);
        Assert.Equal("https://api.test.local/api/v1/public-holidays/6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b", r.Uri.ToString());
        Assert.Equal($"transynq-sdk-dotnet/{SdkInfo.Version} acme/1.0", r.Header("User-Agent"));
        Assert.Equal("application/json", r.Header("Accept"));
        Assert.Matches("^[0-9a-f]{32}$", r.Header("X-Correlation-Id"));
        Assert.Equal(new DateOnly(2026, 4, 25), response.Data.Date);
        Assert.Equal("\"v1\"", response.ETag);
        Assert.Equal("1.0.0", response.ApiVersion);
        Assert.Equal("abc12345", response.CorrelationId);
        PublicHolidayResponse data = response;   // implicit conversion
        Assert.Equal("Anzac Day", data.Name);
    }

    [Fact]
    public async Task Query_parameters_are_formatted_invariantly_and_nulls_skipped()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, """{"items":[],"page":2,"pageSize":20,"total":0}""");

        await client.ReferenceData.ListPublicHolidaysAsync(new ListPublicHolidaysQuery { CountryId = 1, Year = 2026, Page = 2, PageSize = 20, Sort = "-date,name", Q = "a b" });

        Assert.Equal("/api/v1/public-holidays?countryId=1&year=2026&page=2&pageSize=20&sort=-date%2Cname&q=a%20b", http.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Instants_in_queries_are_sent_as_utc()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, "[]");

        await client.DriverApp.DriverLegsAsync(new DriverLegsQuery { From = new DateTimeOffset(2026, 10, 2, 13, 4, 5, TimeSpan.FromHours(10)) });

        Assert.Equal("/api/v1/driver/legs?from=2026-10-02T03%3A04%3A05Z", http.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Put_sends_json_body_and_if_match()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, """{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","name":"X","date":"2026-01-26","countryId":1,"states":["NSW"]}""", ("ETag", "\"v2\""));

        var response = await client.ReferenceData.UpdatePublicHolidayAsync(Id,
            new PublicHolidayRequest { Id = Id, Name = "X", Date = new DateOnly(2026, 1, 26), CountryId = 1, States = ["NSW"] },
            RequestOptions.WithIfMatch("\"v1\""));

        var r = http.Requests[0];
        Assert.Equal("PUT", r.Method);
        Assert.Equal("\"v1\"", r.Header("If-Match"));
        Assert.StartsWith("application/json", r.ContentType);
        Assert.Equal("""{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","name":"X","date":"2026-01-26","countryId":1,"states":["NSW"]}""", r.BodyText);
        Assert.Equal("\"v2\"", response.ETag);
    }

    [Fact]
    public async Task Multipart_upload_sends_file_and_text_parts()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, """{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b"}""");

        var response = await client.Jobs.UploadJobDocumentAsync(Id, new UploadJobDocumentForm { File = FileUpload.FromBytes("%PDF-1.7"u8.ToArray(), "pod.pdf", "application/pdf") });

        var r = http.Requests[0];
        Assert.Equal("POST", r.Method);
        Assert.StartsWith("multipart/form-data", r.ContentType);
        Assert.Contains("name=file", r.BodyText);
        Assert.Contains("filename=pod.pdf", r.BodyText);
        Assert.Contains("%PDF-1.7", r.BodyText);
        Assert.Equal(Id, response.Data.AsJson()!.Value.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Download_streams_the_file_with_its_name()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentType = new("application/pdf");
            response.Content.Headers.ContentDisposition = new("attachment") { FileName = "pod.pdf" };
            return response;
        });

        await using var file = (await client.Jobs.DownloadJobDocumentAsync(Id)).Data;

        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("pod.pdf", file.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, await file.ReadAllBytesAsync());
    }

    [Fact]
    public void Generated_surface_covers_the_whole_spec()
    {
        Assert.Equal(705, SpecInfo.OperationCount);
        Assert.Equal(64, SpecInfo.Sha256.Length);
        var (client, _, _) = TestClients.Create();
        var services = typeof(TransynqClient).GetProperties().Where(p => p.PropertyType.Namespace == "Transynq.Sdk.Services").ToList();
        Assert.Equal(52, services.Count);
        Assert.All(services, p => Assert.NotNull(p.GetValue(client)));
    }
}

public class AuthTests
{
    [Fact]
    public async Task Api_key_is_sent_in_the_legacy_header_and_no_csrf()
    {
        var (client, http, _) = TestClients.Create(o => o.Credential = new ApiKeyCredential("secret-key"));
        http.Enqueue(HttpStatusCode.OK, """{"companyId":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","description":"ERP"}""");
        http.Enqueue(HttpStatusCode.OK, "{}");

        await client.Authentication.GetApiClientAsync();
        await client.ReferenceData.DeletePublicHolidayAsync(Guid.NewGuid(), RequestOptions.WithIfMatch("\"x\""));

        Assert.All(http.Requests, r => Assert.Equal("secret-key", r.Header("X-Transynq-APIKey")));
        Assert.All(http.Requests, r => Assert.Null(r.Header("X-CSRF-Token")));
        Assert.DoesNotContain("secret-key", new ApiKeyCredential("secret-key").ToString(), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Login(string session = "s1", string csrf = "csrf-1", string expires = "2099-01-01T00:00:00Z")
    {
        var response = MockHttp.Json(HttpStatusCode.OK, $$"""{"userName":"ann","csrfToken":"{{csrf}}","sessionExpiresUtc":"{{expires}}"}""");
        response.Headers.TryAddWithoutValidation("Set-Cookie", $"__Host-tq_session={session}; path=/; secure; samesite=strict; httponly");
        return response;
    }

    [Fact]
    public async Task Session_signs_in_once_then_sends_cookie_and_csrf_on_unsafe_verbs_only()
    {
        var credential = new SessionCredential("ann", "pw");
        var (client, http, _) = TestClients.Create(o => o.Credential = credential);
        http.Enqueue(_ => Login());
        http.Enqueue(HttpStatusCode.OK, "{}");
        http.Enqueue(HttpStatusCode.OK, "{}");

        await client.Authentication.GetMeAsync();
        await client.ReferenceData.DeletePublicHolidayAsync(Guid.NewGuid(), RequestOptions.WithIfMatch("\"x\""));

        var login = http.Requests[0];
        Assert.Equal("/api/v1/auth/login", login.Uri.AbsolutePath);
        Assert.Equal("""{"userName":"ann","password":"pw"}""", login.BodyText);
        Assert.False(string.IsNullOrEmpty(login.Header("X-CSRF-Token")));
        Assert.Equal("__Host-tq_session=s1", http.Requests[1].Header("Cookie"));
        Assert.Null(http.Requests[1].Header("X-CSRF-Token"));
        Assert.Equal("csrf-1", http.Requests[2].Header("X-CSRF-Token"));
        Assert.Equal(1, credential.SignInCount);
        Assert.Equal("ann", credential.Me!.Value.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Session_signs_in_again_once_after_a_401()
    {
        var credential = new SessionCredential("ann", "pw");
        var (client, http, _) = TestClients.Create(o => o.Credential = credential);
        http.Enqueue(_ => Login("s1", "c1"));
        http.Enqueue(HttpStatusCode.Unauthorized);
        http.Enqueue(_ => Login("s2", "c2"));
        http.Enqueue(HttpStatusCode.OK, "{}");

        await client.Authentication.GetMeAsync();

        Assert.Equal(4, http.Requests.Count);
        Assert.Equal("__Host-tq_session=s2", http.Requests[3].Header("Cookie"));
        Assert.Equal(2, credential.SignInCount);
    }

    [Fact]
    public async Task A_second_401_is_raised_as_authentication_error()
    {
        var (client, http, _) = TestClients.Create(o => o.Credential = new SessionCredential("ann", "pw"));
        http.Enqueue(_ => Login());
        http.Enqueue(HttpStatusCode.Unauthorized);
        http.Enqueue(_ => Login());
        http.Enqueue(HttpStatusCode.Unauthorized, """{"title":"Sign in again.","status":401}""");

        await Assert.ThrowsAsync<AuthenticationException>(() => client.Authentication.GetMeAsync());
        Assert.Equal(4, http.Requests.Count);
    }

    [Fact]
    public async Task Failed_login_raises_authentication_error_without_retrying()
    {
        var (client, http, _) = TestClients.Create(o => o.Credential = new SessionCredential("ann", "bad"));
        http.Enqueue(HttpStatusCode.Unauthorized, """{"title":"Invalid user name or password.","status":401}""");

        var error = await Assert.ThrowsAsync<AuthenticationException>(() => client.Authentication.GetMeAsync());
        Assert.Equal("Invalid user name or password.", error.Title);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_sign_in()
    {
        var credential = new SessionCredential("ann", "pw");
        var (client, http, _) = TestClients.Create(o => o.Credential = credential);
        http.Fallback = r => r.Uri.AbsolutePath == "/api/v1/auth/login" ? Login() : MockHttp.Json(HttpStatusCode.OK, "{}");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.Authentication.GetMeAsync()));

        Assert.Equal(1, credential.SignInCount);
        Assert.Single(http.Requests, r => r.Uri.AbsolutePath == "/api/v1/auth/login");
    }

    [Fact]
    public async Task Session_is_renewed_before_it_expires()
    {
        var credential = new SessionCredential("ann", "pw");
        var now = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        credential.Clock = () => now;
        var (client, http, _) = TestClients.Create(o => o.Credential = credential);
        http.Fallback = r => r.Uri.AbsolutePath == "/api/v1/auth/login" ? Login(expires: "2026-11-16T01:00:00Z") : MockHttp.Json(HttpStatusCode.OK, "{}");

        await client.Authentication.GetMeAsync();
        now = now.AddMinutes(59.5);
        await client.Authentication.GetMeAsync();

        Assert.Equal(2, credential.SignInCount);
    }

    [Fact]
    public async Task Driver_login_uses_the_bearer_token()
    {
        var credential = new DriverLoginCredential(new DriverLoginRequest { CompanyCode = "ACME", Pin = "1234", DeviceId = "d", PushToken = "p", AppVersion = "1", Platform = "android" });
        var (client, http, _) = TestClients.Create(o => o.Credential = credential);
        http.Enqueue(HttpStatusCode.OK, """{"token":"0b7c9f8e-1111-2222-3333-444455556666","expiresUtc":"2099-01-01T00:00:00Z","driver":{},"company":{},"settings":{}}""");
        http.Enqueue(HttpStatusCode.OK, "{}");

        await client.DriverApp.DriverMeAsync();

        Assert.Equal("/api/v1/driver/login", http.Requests[0].Uri.AbsolutePath);
        Assert.Null(http.Requests[0].Header("Authorization"));
        Assert.Equal("Bearer 0b7c9f8e-1111-2222-3333-444455556666", http.Requests[1].Header("Authorization"));
    }

    [Fact]
    public async Task Hooks_never_see_secrets()
    {
        var seen = new List<TransynqRequestInfo>();
        var (client, http, _) = TestClients.Create(o =>
        {
            o.Credential = new ApiKeyCredential("secret-key");
            o.OnRequest = info => { seen.Add(info); return ValueTask.CompletedTask; };
        });
        http.Enqueue(HttpStatusCode.OK, "{}");

        await client.Authentication.GetApiClientAsync();

        Assert.Equal("[redacted]", seen[0].Headers["X-Transynq-APIKey"]);
    }
}

public class RetryTests
{
    [Fact]
    public async Task Get_is_retried_on_503_with_exponential_back_off()
    {
        var (client, http, delays) = TestClients.Create();
        http.Enqueue(HttpStatusCode.ServiceUnavailable).Enqueue(HttpStatusCode.BadGateway).Enqueue(HttpStatusCode.OK, "[]");

        await client.ReferenceData.ListCountriesAsync();

        Assert.Equal(3, http.Requests.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)], delays);
        Assert.Single(http.Requests.Select(r => r.Header("X-Correlation-Id")).Distinct());
    }

    [Fact]
    public async Task Retries_stop_after_max_retries()
    {
        var (client, http, _) = TestClients.Create(o => o.MaxRetries = 1);
        http.Fallback = _ => MockHttp.Json(HttpStatusCode.InternalServerError, """{"title":"An unexpected error occurred.","status":500,"code":"unexpected","correlationId":"c-1"}""");

        var error = await Assert.ThrowsAsync<ServerException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal("unexpected", error.Code);
        Assert.Equal("c-1", error.CorrelationId);
    }

    [Fact]
    public async Task Post_without_idempotency_is_not_retried()
    {
        var (client, http, _) = TestClients.Create();
        http.Fallback = _ => MockHttp.Json(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<ServerException>(() => client.Jobs.RepriceJobsAsync(new JobRepriceRequest { Grid = new GridQueryRequest() }));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Post_with_idempotency_key_is_retried_with_the_same_key()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.ServiceUnavailable).Enqueue(HttpStatusCode.OK, "{}");

        await client.Jobs.RepriceJobsAsync(new JobRepriceRequest { Grid = new GridQueryRequest() }, new RequestOptions { IdempotencyKey = "key-1" });

        Assert.Equal(2, http.Requests.Count);
        Assert.All(http.Requests, r => Assert.Equal("key-1", r.Header("Idempotency-Key")));
    }

    [Fact]
    public async Task Create_with_client_id_is_retried_but_not_with_an_empty_id()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.ServiceUnavailable).Enqueue(HttpStatusCode.OK, "{}");
        await client.ReferenceData.CreatePublicHolidayAsync(new PublicHolidayRequest { Id = Ids.NewId(), Name = "X", Date = new DateOnly(2026, 1, 1), CountryId = 1 });
        Assert.Equal(2, http.Requests.Count);

        http.Requests.Clear();
        http.Enqueue(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<ServerException>(() => client.ReferenceData.CreatePublicHolidayAsync(new PublicHolidayRequest { Name = "X" }));
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Rate_limit_honours_retry_after_even_for_post()
    {
        var (client, http, delays) = TestClients.Create();
        http.Enqueue(HttpStatusCode.TooManyRequests, null, ("Retry-After", "3")).Enqueue(HttpStatusCode.OK, "{}");

        await client.Jobs.RepriceJobsAsync(new JobRepriceRequest { Grid = new GridQueryRequest() });

        Assert.Equal([TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task Long_retry_after_is_raised_not_waited()
    {
        var (client, http, delays) = TestClients.Create();
        http.Enqueue(HttpStatusCode.TooManyRequests, null, ("Retry-After", "600"));

        var error = await Assert.ThrowsAsync<RateLimitException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Equal(TimeSpan.FromSeconds(600), error.RetryAfter);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Network_errors_are_retried_then_raised_as_connection_errors()
    {
        var (client, http, _) = TestClients.Create(o => o.MaxRetries = 2);
        http.Fallback = _ => throw new HttpRequestException("connection reset");

        await Assert.ThrowsAsync<TransynqConnectionException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Timeouts_are_raised_as_timeout_errors()
    {
        var (client, http, _) = TestClients.Create(o => o.MaxRetries = 0);
        http.Fallback = _ => throw new TaskCanceledException("timeout");

        await Assert.ThrowsAsync<TransynqTimeoutException>(() => client.ReferenceData.ListCountriesAsync());
    }

    [Fact]
    public async Task Max_retries_zero_turns_retries_off()
    {
        var (client, http, _) = TestClients.Create(o => o.MaxRetries = 0);
        http.Fallback = _ => MockHttp.Json(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<ServerException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Not_implemented_is_not_retried()
    {
        var (client, http, _) = TestClients.Create();
        http.Fallback = _ => MockHttp.Json(HttpStatusCode.NotImplemented, """{"code":"not-implemented","status":501}""");

        await Assert.ThrowsAsync<ServerException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Single(http.Requests);
    }
}

public class ErrorTests
{
    [Fact]
    public async Task Validation_problem_exposes_field_errors()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.BadRequest, """{"type":"https://httpstatuses.io/400","title":"The request is not valid.","status":400,"code":"validation","correlationId":"c-9","errors":{"name":["Name is required."]}}""");

        var error = await Assert.ThrowsAsync<ValidationException>(() => client.ReferenceData.ListCountriesAsync());

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("validation", error.Code);
        Assert.Equal("c-9", error.CorrelationId);
        Assert.Equal(["Name is required."], error.Errors["name"]);
        Assert.Contains("\"validation\"", error.RawBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(403, typeof(PermissionDeniedException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(409, typeof(ConflictException))]
    [InlineData(412, typeof(StaleVersionException))]
    [InlineData(422, typeof(BusinessRuleException))]
    [InlineData(418, typeof(ApiException))]
    public async Task Status_maps_to_a_typed_error(int status, Type expected)
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue((HttpStatusCode)status, """{"title":"t","detail":"d","code":"year-exists"}""", ("X-Correlation-Id", "hdr-1"));

        var error = await Assert.ThrowsAnyAsync<ApiException>(() => client.ReferenceData.ListCountriesAsync());

        Assert.IsType(expected, error);
        Assert.Equal("year-exists", error.Code);
        Assert.Equal("d", error.Detail);
        Assert.Equal("hdr-1", error.CorrelationId);
        Assert.Equal("ListCountries", error.OperationId);
    }

    [Fact]
    public async Task A_non_json_error_body_is_kept_raw()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>nope</html>") });

        var error = await Assert.ThrowsAsync<NotFoundException>(() => client.ReferenceData.ListCountriesAsync());
        Assert.Equal("<html>nope</html>", error.RawBody);
        Assert.Null(error.Code);
    }
}

public class ConcurrencyPagingAndOperationsTests
{
    private static readonly Guid Id = Guid.Parse("6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b");

    [Fact]
    public async Task Read_modify_write_reloads_and_retries_on_stale_version()
    {
        var (client, http, _) = TestClients.Create();
        const string body = """{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","name":"A","date":"2026-01-01","countryId":1,"states":[]}""";
        http.Enqueue(HttpStatusCode.OK, body, ("ETag", "\"1\""))
            .Enqueue(HttpStatusCode.PreconditionFailed, """{"code":"stale-version","status":412}""")
            .Enqueue(HttpStatusCode.OK, body, ("ETag", "\"2\""))
            .Enqueue(HttpStatusCode.OK, body, ("ETag", "\"3\""));

        var saved = await Concurrency.ReadModifyWriteAsync(
            ct => client.ReferenceData.GetPublicHolidayAsync(Id, null, ct),
            (current, options, ct) => client.ReferenceData.UpdatePublicHolidayAsync(Id,
                new PublicHolidayRequest { Id = current.Id, Name = current.Name + "!", Date = current.Date, CountryId = current.CountryId, States = current.States }, options, ct));

        Assert.Equal("\"3\"", saved.ETag);
        Assert.Equal("\"1\"", http.Requests[1].Header("If-Match"));
        Assert.Equal("\"2\"", http.Requests[3].Header("If-Match"));
    }

    [Fact]
    public async Task Auto_paging_reads_until_total()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, """{"items":[{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b"},{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5c"}],"page":1,"pageSize":2,"total":3}""")
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5d"}],"page":2,"pageSize":2,"total":3}""");

        var all = new List<JobStatusReasonResponse>();
        await foreach (var item in client.ReferenceData.ListJobStatusReasonsAutoPagingAsync(new ListJobStatusReasonsQuery { PageSize = 2 })) all.Add(item);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("page=2", http.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grid_rows_are_iterated_across_pages()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, """{"rows":[{"a":1},{"a":2}],"page":1,"pageSize":2,"total":3,"summaries":{}}""")
            .Enqueue(HttpStatusCode.OK, """{"rows":[{"a":3}],"page":2,"pageSize":2,"total":3,"summaries":{}}""");

        var rows = new List<int>();
        await foreach (var row in client.QueryGridRowsAsync("jobs", new GridQueryRequest { Columns = ["a"], PageSize = 2 })) rows.Add(row.GetProperty("a").GetInt32());

        Assert.Equal([1, 2, 3], rows);
        Assert.Equal("/api/v1/grids/jobs/query", http.Requests[0].Uri.AbsolutePath);
        Assert.Contains("\"page\":2", http.Requests[1].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepted_long_operation_is_polled_to_success()
    {
        var (client, http, delays) = TestClients.Create();
        http.Enqueue(HttpStatusCode.Accepted, """{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","status":"Queued"}""", ("Location", "/api/v1/long-operations/6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b"));
        http.Enqueue(HttpStatusCode.OK, Op("Running"));
        http.Enqueue(HttpStatusCode.OK, Op("Succeeded", """{"created":3}"""));

        var accepted = await client.Imports.ImportZonesInBackgroundAsync(new ImportZonesInBackgroundForm { File = FileUpload.FromBytes([1], "z.xlsx") });
        Assert.True(accepted.IsAccepted);
        var seen = new List<string>();
        var done = await client.PollLongOperation(accepted).WaitAsync(TimeSpan.FromSeconds(1), onProgress: s => seen.Add(s.Status));

        Assert.Equal(["Running", "Succeeded"], seen);
        Assert.Equal(3, done.Result!.Value.GetProperty("created").GetInt32());
        Assert.Equal("/api/v1/long-operations/6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b", http.Requests[1].Uri.AbsolutePath);
        Assert.Equal([TimeSpan.FromSeconds(1)], delays);
    }

    [Fact]
    public async Task Failed_long_operation_raises()
    {
        var (client, http, _) = TestClients.Create();
        http.Enqueue(HttpStatusCode.OK, Op("Failed", error: "Bad file"));

        var error = await Assert.ThrowsAsync<LongOperationFailedException>(() => client.PollLongOperation(Id).WaitAsync());
        Assert.Equal("Bad file", error.Operation!.Error);
    }

    private static string Op(string status, string result = "null", string? error = null)
        => $$"""{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","kind":"zones","inputName":"z.xlsx","requestedBy":"ann","requestedUtc":"2026-11-16T00:00:00Z","status":"{{status}}","startedUtc":null,"finishedUtc":null,"result":{{result}},"error":{{(error is null ? "null" : $"\"{error}\"")}}}""";
}

public class SerializationTests
{
    [Fact]
    public void Decimals_keep_every_digit_and_numbers_may_arrive_as_strings()
    {
        var json = """{"ratePercentage":1234567890123456.78,"minPercentage":"0.10","decimalPlaces":"2","unknownFutureField":{"x":1}}""";
        var item = TransynqJson.Deserialize<ScheduleRateUpdateRequest>(json);
        Assert.NotNull(item);
        Assert.Equal(1234567890123456.78m, item.RatePercentage);
        Assert.Equal(0.10m, item.MinPercentage);
        Assert.Equal(2, item.DecimalPlaces);
        var back = TransynqJson.Serialize(item);
        Assert.Contains("\"ratePercentage\":1234567890123456.78", back, StringComparison.Ordinal);
    }

    [Fact]
    public void Instants_without_offset_are_utc_and_written_with_z()
    {
        var op = TransynqJson.Deserialize<LongOperationResponse>("""{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","kind":"k","requestedBy":"a","requestedUtc":"2026-10-02T03:04:05","status":"Queued"}""")!;
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 3, 4, 5, TimeSpan.Zero), op.RequestedUtc);
        Assert.Contains("\"requestedUtc\":\"2026-10-02T03:04:05Z\"", TransynqJson.Serialize(op), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_codes_and_properties_do_not_break_deserialisation()
    {
        var op = TransynqJson.Deserialize<LongOperationResponse>("""{"id":"6f1c7a52-3d6b-4b5e-9a3e-0c1d2e3f4a5b","kind":"brand-new-kind","requestedBy":"a","requestedUtc":"2026-10-02T03:04:05Z","status":"Paused","newField":[1,2]}""")!;
        Assert.Equal("Paused", op.Status);
    }

    [Fact]
    public void Nulls_are_omitted_and_dates_are_calendar_days()
    {
        var json = TransynqJson.Serialize(new PublicHolidayRequest { Id = Guid.Empty, Date = new DateOnly(2026, 12, 25) });
        Assert.Equal("""{"id":"00000000-0000-0000-0000-000000000000","date":"2026-12-25"}""", json);
    }

    [Fact]
    public void Di_registration_builds_a_client()
    {
        var services = new ServiceCollection();
        services.AddTransynqClient(o => { o.BaseUri = new Uri("https://api.test.local"); o.Credential = new ApiKeyCredential("k"); });
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<TransynqClient>();
        Assert.Equal("https://api.test.local/", client.Options.BaseUri!.ToString());
    }
}
