using Microsoft.Extensions.Logging;
using Transynq.Sdk.Credentials;

namespace Transynq.Sdk;

/// <summary>Settings of a <see cref="TransynqClient"/>.</summary>
public sealed class TransynqClientOptions
{
    /// <summary>The API host, for example <c>https://api.transynq.example</c>. Paths such as <c>/api/v1/jobs</c> are appended to it.</summary>
    public Uri? BaseUri { get; set; }

    /// <summary>How requests authenticate: <see cref="ApiKeyCredential"/>, <see cref="SessionCredential"/>, <see cref="DriverTokenCredential"/> or <see cref="DriverLoginCredential"/>. Null sends anonymous requests.</summary>
    public ITransynqCredential? Credential { get; set; }

    /// <summary>Time allowed for one attempt (sending, and reading a buffered response). Default 60 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Retries after the first attempt for retryable failures (docs/design.md section 6). 0 turns retries off. Default 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>First back-off delay; doubled per attempt, with full jitter. Default 0.5 seconds.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound of a computed back-off delay. Default 8 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>A <c>Retry-After</c> longer than this is not waited for: the error is raised instead. Default 60 seconds.</summary>
    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Appended to the <c>User-Agent</c> (<c>transynq-sdk-dotnet/&lt;version&gt; &lt;suffix&gt;</c>), for example <c>my-integration/2.1</c>.</summary>
    public string? UserAgentSuffix { get; set; }

    /// <summary>Receives one debug line per attempt (method, path template, status, duration, correlation id). Never bodies or secrets.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Called before each attempt is sent (credential headers redacted).</summary>
    public Func<TransynqRequestInfo, ValueTask>? OnRequest { get; set; }

    /// <summary>Called after each attempt is answered (before error handling).</summary>
    public Func<TransynqResponseInfo, ValueTask>? OnResponse { get; set; }

    /// <summary>Waits between retries and polls; replaceable in tests.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, ct) => Task.Delay(delay, ct);

    /// <summary>Jitter source in [0, 1); replaceable in tests.</summary>
    internal Func<double> Jitter { get; set; } = () => Random.Shared.NextDouble();

    internal void Validate()
    {
        if (BaseUri is null) throw new ArgumentException("TransynqClientOptions.BaseUri is required.", nameof(BaseUri));
        if (!BaseUri.IsAbsoluteUri) throw new ArgumentException("TransynqClientOptions.BaseUri must be absolute.", nameof(BaseUri));
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries must be 0 or more.");
        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive.");
    }
}

/// <summary>Per-call options.</summary>
public sealed record RequestOptions
{
    /// <summary>Sent as <c>If-Match</c> (PUT and DELETE require it; take it from <see cref="ApiResponse{T}.ETag"/>).</summary>
    public string? IfMatch { get; init; }

    /// <summary>Sent as <c>Idempotency-Key</c>; also makes a POST retryable.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Extra request headers.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Overrides <see cref="TransynqClientOptions.Timeout"/> for this call.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Overrides <see cref="TransynqClientOptions.MaxRetries"/> for this call.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Overrides the generated <c>X-Correlation-Id</c> (8-64 characters of <c>[A-Za-z0-9._-]</c>).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Options carrying <c>If-Match: etag</c>.</summary>
    public static RequestOptions WithIfMatch(string etag) => new() { IfMatch = etag };

    /// <summary>Options carrying a new random <c>Idempotency-Key</c>.</summary>
    public static RequestOptions WithNewIdempotencyKey() => new() { IdempotencyKey = Ids.NewIdempotencyKey() };
}

/// <summary>What a hook sees of a request about to be sent.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Uri">Full URI.</param>
/// <param name="OperationId">The OpenAPI operation id (or an SDK-internal name such as <c>Login</c>).</param>
/// <param name="Attempt">1 for the first attempt.</param>
/// <param name="Headers">Request headers, with credentials redacted.</param>
public sealed record TransynqRequestInfo(string Method, Uri Uri, string OperationId, int Attempt, IReadOnlyDictionary<string, string> Headers);

/// <summary>What a hook sees of an answer.</summary>
/// <param name="Request">The request.</param>
/// <param name="StatusCode">HTTP status.</param>
/// <param name="Elapsed">Time to the response headers.</param>
/// <param name="Headers">Response headers, with cookies redacted.</param>
public sealed record TransynqResponseInfo(TransynqRequestInfo Request, int StatusCode, TimeSpan Elapsed, IReadOnlyDictionary<string, string> Headers);
