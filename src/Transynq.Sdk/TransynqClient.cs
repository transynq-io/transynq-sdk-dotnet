using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Transynq.Sdk.Credentials;
using Transynq.Sdk.Http;
using Transynq.Sdk.Models;

namespace Transynq.Sdk;

/// <summary>
/// The Transynq TMS API client. One property per API area (<see cref="Jobs"/>, <see cref="Customers"/>, ...), one method per operation.
/// Thread-safe; create one per base URI and credential and reuse it.
/// </summary>
public sealed partial class TransynqClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly HttpPipeline _pipeline;

    /// <summary>Creates a client with its own <see cref="HttpClient"/> (cookies off, decompression on).</summary>
    /// <param name="options">Settings; <see cref="TransynqClientOptions.BaseUri"/> is required.</param>
    public TransynqClient(TransynqClientOptions options) : this(options, null)
    {
    }

    /// <summary>Creates a client over a given <see cref="HttpClient"/> (from <c>IHttpClientFactory</c>, or with a test handler).</summary>
    /// <param name="options">Settings; <see cref="TransynqClientOptions.BaseUri"/> is required.</param>
    /// <param name="httpClient">The HTTP client; not disposed by this client. Null creates one.</param>
    public TransynqClient(TransynqClientOptions options, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
        if (httpClient is null)
        {
            _http = new HttpClient(CreateDefaultHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
        _pipeline = new HttpPipeline(_http, options);
        InitializeServices(_pipeline);
    }

    /// <summary>The settings.</summary>
    public TransynqClientOptions Options { get; }

    /// <summary>The <c>User-Agent</c> this client sends.</summary>
    public string UserAgent => _pipeline.UserAgent;

    internal HttpPipeline Pipeline => _pipeline;

    /// <summary>The handler a client creates for itself: the SDK keeps the session cookie, so the handler's cookie jar is off.</summary>
    /// <returns>A handler.</returns>
    public static HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    /// <summary>A poller for the long operation a 202 answer started (its <c>Location</c>, else its body's <c>id</c>).</summary>
    /// <param name="accepted">The 202 answer.</param>
    /// <returns>The poller.</returns>
    public LongOperationPoller PollLongOperation(ApiResponse<ResponseBody> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (LongOperationPoller.IdFromLocation(accepted.Location) is { } fromLocation) return new LongOperationPoller(this, fromLocation);
        if (accepted.Data.AsJson() is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid))
            return new LongOperationPoller(this, guid);
        throw new ArgumentException("The response is not a long-operation answer (no Location and no id).", nameof(accepted));
    }

    /// <summary>A poller for a long operation by id.</summary>
    /// <param name="operationId">The operation id.</param>
    /// <returns>The poller.</returns>
    public LongOperationPoller PollLongOperation(Guid operationId) => new(this, operationId);

    /// <summary>
    /// Every row of a grid query (<c>POST /api/v1/grids/{gridId}/query</c>), page by page from <c>request.Page</c> (default 1) with
    /// <c>request.PageSize</c> (default 500, the maximum).
    /// </summary>
    /// <param name="gridId">The grid.</param>
    /// <param name="request">Columns, filters, sort, search.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the iteration.</param>
    /// <returns>The rows, as JSON objects keyed by column.</returns>
    public async IAsyncEnumerable<JsonElement> QueryGridRowsAsync(string gridId, GridQueryRequest request, RequestOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = request.Page ?? 1;
        var size = request.PageSize ?? 500;
        while (true)
        {
            var response = await Grids.QueryGridAsync(gridId, request with { Page = page, PageSize = size }, options, cancellationToken).ConfigureAwait(false);
            var data = response.Data;
            if (data is null || data.Rows.Count == 0) yield break;
            foreach (var row in data.Rows) yield return row;
            if ((long)data.Page * data.PageSize >= data.Total) yield break;
            page++;
        }
    }

    /// <summary>Ends a session or driver sign-in (<c>POST /api/v1/auth/logout</c> or <c>/api/v1/driver/logout</c>) and forgets it locally.</summary>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>A task.</returns>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        switch (Options.Credential)
        {
            case SessionCredential session when session.SessionExpiresUtc is not null:
                await Authentication.LogoutAsync(null, cancellationToken).ConfigureAwait(false);
                session.Reset();
                break;
            case DriverLoginCredential driver when driver.Session is not null:
                await DriverApp.DriverLogoutAsync(null, cancellationToken).ConfigureAwait(false);
                driver.Reset();
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

/// <summary>SDK identity.</summary>
public static class SdkInfo
{
    /// <summary>The SDK version (semantic, independent of the API version).</summary>
    public const string Version = "1.0.3";
}

/// <summary>Client-generated identifiers.</summary>
public static class Ids
{
    /// <summary>A new id for an idempotent create (<c>POST</c> with <c>id</c> in the body: sending it again returns the same record).</summary>
    /// <returns>A random GUID.</returns>
    public static Guid NewId() => Guid.NewGuid();

    /// <summary>A new <c>Idempotency-Key</c> value.</summary>
    /// <returns>32 hex characters.</returns>
    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    /// <summary>A new <c>X-Correlation-Id</c> value.</summary>
    /// <returns>32 hex characters.</returns>
    public static string NewCorrelationId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}

/// <summary>Optimistic concurrency helpers (ETag / If-Match, docs/api-conventions.md section 5).</summary>
public static class Concurrency
{
    /// <summary>
    /// Loads a record (with its ETag), lets <paramref name="save"/> write it with <c>If-Match</c>, and on <see cref="StaleVersionException"/>
    /// loads it again and repeats, up to <paramref name="maxAttempts"/> times.
    /// </summary>
    /// <typeparam name="T">The loaded type.</typeparam>
    /// <typeparam name="TResult">The saved type.</typeparam>
    /// <param name="load">Reads the record.</param>
    /// <param name="save">Writes the change; receives the record and options carrying its ETag as <c>If-Match</c>.</param>
    /// <param name="maxAttempts">Attempts before the stale-version error is raised.</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The save's answer.</returns>
    public static async Task<ApiResponse<TResult>> ReadModifyWriteAsync<T, TResult>(
        Func<CancellationToken, Task<ApiResponse<T>>> load,
        Func<T, RequestOptions, CancellationToken, Task<ApiResponse<TResult>>> save,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        for (var attempt = 1; ; attempt++)
        {
            var current = await load(cancellationToken).ConfigureAwait(false);
            var etag = current.ETag ?? throw new InvalidOperationException("The loaded record has no ETag.");
            try
            {
                return await save(current.Data, RequestOptions.WithIfMatch(etag), cancellationToken).ConfigureAwait(false);
            }
            catch (StaleVersionException) when (attempt < maxAttempts)
            {
                // someone else saved first: read their version and apply the change again
            }
        }
    }
}

/// <summary>Follows a long-running operation (<c>GET /api/v1/long-operations/{id}</c>) to its end.</summary>
public sealed class LongOperationPoller
{
    private readonly TransynqClient _client;

    internal LongOperationPoller(TransynqClient client, Guid id)
    {
        _client = client;
        Id = id;
    }

    /// <summary>The operation id.</summary>
    public Guid Id { get; }

    /// <summary>The last state read, or null.</summary>
    public LongOperationResponse? Last { get; private set; }

    /// <summary>Reads the current state once.</summary>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The state.</returns>
    public async Task<LongOperationResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.LongOperations.GetLongOperationAsync(Id, null, cancellationToken).ConfigureAwait(false);
        Last = response.Data;
        return response.Data;
    }

    /// <summary>
    /// Polls until the operation is <c>Succeeded</c> (returns it) or <c>Failed</c> (throws <see cref="LongOperationFailedException"/>).
    /// </summary>
    /// <param name="pollInterval">Time between polls (default 2 s).</param>
    /// <param name="timeout">Gives up after this long with <see cref="TimeoutException"/> (default 30 min).</param>
    /// <param name="onProgress">Called with every state read.</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The succeeded operation (its <c>Result</c> holds the outcome).</returns>
    public async Task<LongOperationResponse> WaitAsync(TimeSpan? pollInterval = null, TimeSpan? timeout = null, Action<LongOperationResponse>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromSeconds(2);
        var limit = timeout ?? TimeSpan.FromMinutes(30);
        var waited = TimeSpan.Zero;
        while (true)
        {
            var state = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            onProgress?.Invoke(state);
            if (string.Equals(state.Status, LongOperationStatus.Succeeded, StringComparison.OrdinalIgnoreCase)) return state;
            if (string.Equals(state.Status, LongOperationStatus.Failed, StringComparison.OrdinalIgnoreCase))
                throw new LongOperationFailedException($"Long operation {Id} ({state.Kind}) failed: {state.Error ?? "no reason given"}.") { Operation = state };
            if (waited >= limit) throw new TimeoutException($"Long operation {Id} did not finish within {limit}.");
            await _client.Options.Delay(interval, cancellationToken).ConfigureAwait(false);
            waited += interval;
        }
    }

    /// <summary>Downloads the operation's result file (a workbook or PDF), once it has succeeded.</summary>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The streamed file (dispose it).</returns>
    public async Task<FileDownload> DownloadResultAsync(CancellationToken cancellationToken = default)
        => (await _client.LongOperations.GetLongOperationFileAsync(Id, null, cancellationToken).ConfigureAwait(false)).Data;

    internal static Guid? IdFromLocation(string? location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        var path = location.Split('?', 2)[0].TrimEnd('/');
        var last = path[(path.LastIndexOf('/') + 1)..];
        return path.Contains("/long-operations/", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(last, out var id) ? id : null;
    }
}

/// <summary>The states of a long operation (open set: compare as strings).</summary>
public static class LongOperationStatus
{
    /// <summary>Waiting to start.</summary>
    public const string Queued = "Queued";

    /// <summary>Running.</summary>
    public const string Running = "Running";

    /// <summary>Finished; <c>result</c> holds the outcome.</summary>
    public const string Succeeded = "Succeeded";

    /// <summary>Finished; <c>error</c> says why.</summary>
    public const string Failed = "Failed";
}
