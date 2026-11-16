using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Transynq.Sdk.Credentials;
using Transynq.Sdk.Serialization;

namespace Transynq.Sdk.Http;

/// <summary>
/// Sends requests: credentials, standard headers, timeouts, retries with back-off (docs/design.md section 6), error mapping (section 5).
/// </summary>
internal sealed partial class HttpPipeline : ISignInTransport
{
    internal const string UserAgentPrefix = "transynq-sdk-dotnet/";

    private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", SdkHeaders.ApiKey, SdkHeaders.Csrf, "Proxy-Authorization",
    };

    private static readonly HashSet<int> RetryableStatus = [408, 429, 500, 502, 503, 504];

    private readonly HttpClient _http;
    private readonly TransynqClientOptions _options;
    private readonly string _userAgent;

    public HttpPipeline(HttpClient http, TransynqClientOptions options)
    {
        _http = http;
        _options = options;
        _userAgent = UserAgentPrefix + SdkInfo.Version + (string.IsNullOrWhiteSpace(options.UserAgentSuffix) ? "" : " " + options.UserAgentSuffix.Trim());
    }

    public TransynqClientOptions Options => _options;

    public string UserAgent => _userAgent;

    // ------------------------------------------------------------------------------------------------------------
    // typed entry points used by generated services

    public async Task<ApiResponse<T>> SendJsonAsync<T>(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var data = bytes.Length == 0 ? default! : TransynqJson.Deserialize<T>(bytes)!;
        return new ApiResponse<T>((int)response.StatusCode, data, HeadersOf(response));
    }

    public async Task<ApiResponse<ResponseBody>> SendRawAsync(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, options, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var body = new ResponseBody(bytes, response.Content.Headers.ContentType?.MediaType, FileNameOf(response));
        return new ApiResponse<ResponseBody>((int)response.StatusCode, body, HeadersOf(response));
    }

    public async Task<ApiResponse<FileDownload>> SendDownloadAsync(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, options, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ApiResponse<FileDownload>((int)response.StatusCode, new FileDownload(response, stream), HeadersOf(response));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // the loop

    public async Task<HttpResponseMessage> SendAsync(ApiRequest request, RequestOptions? options, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        var baseUri = _options.BaseUri!;
        var uri = request.BuildUri(baseUri);
        var correlationId = options?.CorrelationId ?? Ids.NewCorrelationId();
        var maxRetries = Math.Max(0, options?.MaxRetries ?? _options.MaxRetries);
        var timeout = options?.Timeout ?? _options.Timeout;
        var credential = _options.Credential;
        var retrySafe = IsRetrySafe(request, options);
        var authRetried = false;

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
            message.Content = request.Json is { } json
                ? new ByteArrayContent(json) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } } }
                : request.Form?.CreateContent();
            ApplyStandardHeaders(message, correlationId, options);
            var credentialRequest = new CredentialRequest(message, this);
            if (credential is not null) await credential.ApplyAsync(credentialRequest, cancellationToken).ConfigureAwait(false);

            var info = new TransynqRequestInfo(request.Method, uri, request.OperationId, attempt, Redact(message.Headers, message.Content?.Headers));
            if (_options.OnRequest is { } onRequest) await onRequest(info).ConfigureAwait(false);

            HttpResponseMessage response;
            var started = Stopwatch.GetTimestamp();
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (timeout != Timeout.InfiniteTimeSpan) timeoutSource.CancelAfter(timeout);
                try
                {
                    response = await _http.SendAsync(message, completion, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    LogFailure(request, attempt, "timeout", correlationId);
                    if (retrySafe && attempt <= maxRetries)
                    {
                        await _options.Delay(BackOff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new TransynqTimeoutException($"{request.OperationId} timed out after {timeout.TotalSeconds:0.#} s [correlation id {correlationId}].", ex);
                }
                catch (HttpRequestException ex)
                {
                    LogFailure(request, attempt, ex.HttpRequestError.ToString(), correlationId);
                    if (retrySafe && attempt <= maxRetries)
                    {
                        await _options.Delay(BackOff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new TransynqConnectionException($"{request.OperationId} could not reach the API: {ex.Message} [correlation id {correlationId}].", ex);
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            var status = (int)response.StatusCode;
            LogAttempt(request, attempt, status, elapsed, correlationId);
            if (_options.OnResponse is { } onResponse)
                await onResponse(new TransynqResponseInfo(info, status, elapsed, Redact(response.Headers, response.Content.Headers))).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return response;

            if (status == 401 && credential is not null && !authRetried
                && await credential.RefreshAfterUnauthorizedAsync(credentialRequest, cancellationToken).ConfigureAwait(false))
            {
                authRetried = true;
                response.Dispose();
                attempt--;
                continue;
            }

            var retryAfter = RetryAfterOf(response);
            if (RetryableStatus.Contains(status) && (retrySafe || status == 429) && attempt <= maxRetries
                && (retryAfter is null || retryAfter <= _options.MaxRetryAfter))
            {
                response.Dispose();
                await _options.Delay(retryAfter ?? BackOff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                throw ErrorFactory.Create(status, body, HeadersOf(response), request.OperationId, retryAfter);
            }
        }
    }

    /// <summary>Sign-in calls made by credentials: no credential, no retries, errors mapped.</summary>
    public async Task<SignInResponse> PostJsonAsync(string path, byte[] body, IReadOnlyDictionary<string, string> headers, string operationId, CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseUri!.GetLeftPart(UriPartial.Path).TrimEnd('/') + path, UriKind.Absolute);
        var correlationId = Ids.NewCorrelationId();
        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } } },
        };
        ApplyStandardHeaders(message, correlationId, null);
        foreach (var (name, value) in headers) message.Headers.TryAddWithoutValidation(name, value);
        var info = new TransynqRequestInfo("POST", uri, operationId, 1, Redact(message.Headers, message.Content.Headers));
        if (_options.OnRequest is { } onRequest) await onRequest(info).ConfigureAwait(false);

        HttpResponseMessage response;
        var started = Stopwatch.GetTimestamp();
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            if (_options.Timeout != Timeout.InfiniteTimeSpan) timeoutSource.CancelAfter(_options.Timeout);
            try
            {
                response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TransynqTimeoutException($"{operationId} timed out [correlation id {correlationId}].", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new TransynqConnectionException($"{operationId} could not reach the API: {ex.Message} [correlation id {correlationId}].", ex);
            }
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var elapsed = Stopwatch.GetElapsedTime(started);
            LogAttempt(operationId, 1, status, elapsed, correlationId);
            if (_options.OnResponse is { } onResponse)
                await onResponse(new TransynqResponseInfo(info, status, elapsed, Redact(response.Headers, response.Content.Headers))).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var headersOf = HeadersOf(response);
            if (!response.IsSuccessStatusCode) throw ErrorFactory.Create(status, bytes, headersOf, operationId, RetryAfterOf(response));
            var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
            return new SignInResponse(status, bytes, cookies);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // helpers

    internal static bool IsRetrySafe(ApiRequest request, RequestOptions? options)
    {
        if (request.Form is { IsReplayable: false }) return false;
        return request.Method is "GET" or "HEAD" or "OPTIONS" or "PUT" or "DELETE"
               || !string.IsNullOrEmpty(options?.IdempotencyKey)
               || request.HasClientId;
    }

    internal TimeSpan BackOff(int attempt)
    {
        var exponential = _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(_options.MaxRetryDelay.TotalMilliseconds, exponential);
        return TimeSpan.FromMilliseconds(capped * _options.Jitter());
    }

    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }

    private void ApplyStandardHeaders(HttpRequestMessage message, string correlationId, RequestOptions? options)
    {
        message.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation(SdkHeaders.CorrelationId, correlationId);
        if (options is null) return;
        if (!string.IsNullOrEmpty(options.IfMatch)) message.Headers.TryAddWithoutValidation("If-Match", options.IfMatch);
        if (!string.IsNullOrEmpty(options.IdempotencyKey)) message.Headers.TryAddWithoutValidation(SdkHeaders.IdempotencyKey, options.IdempotencyKey);
        if (options.Headers is { } extra)
        {
            foreach (var (name, value) in extra)
            {
                message.Headers.Remove(name);
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> HeadersOf(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers) headers[name] = values.ToList();
        foreach (var (name, values) in response.Content.Headers) headers[name] = values.ToList();
        return headers;
    }

    internal static string? FileNameOf(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        return name?.Trim('"');
    }

    internal static IReadOnlyDictionary<string, string> Redact(HttpHeaders headers, HttpHeaders? content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in headers) result[name] = Redacted.Contains(name) ? "[redacted]" : string.Join(", ", values);
        if (content is not null)
            foreach (var (name, values) in content) result[name] = string.Join(", ", values);
        return result;
    }

    private void LogAttempt(ApiRequest request, int attempt, int status, TimeSpan elapsed, string correlationId)
        => LogAttempt(request.OperationId, attempt, status, elapsed, correlationId);

    private void LogAttempt(string operationId, int attempt, int status, TimeSpan elapsed, string correlationId)
    {
        if (_options.Logger is { } logger) Attempted(logger, operationId, attempt, status, elapsed.TotalMilliseconds, correlationId);
    }

    private void LogFailure(ApiRequest request, int attempt, string reason, string correlationId)
    {
        if (_options.Logger is { } logger) AttemptFailed(logger, request.OperationId, attempt, reason, correlationId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Transynq {OperationId} attempt {Attempt} -> {Status} in {ElapsedMs:0} ms (correlation id {CorrelationId})")]
    private static partial void Attempted(ILogger logger, string operationId, int attempt, int status, double elapsedMs, string correlationId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Transynq {OperationId} attempt {Attempt} failed: {Reason} (correlation id {CorrelationId})")]
    private static partial void AttemptFailed(ILogger logger, string operationId, int attempt, string reason, string correlationId);
}

/// <summary>Header names the API defines.</summary>
public static class SdkHeaders
{
    /// <summary>The API key header .</summary>
    public const string ApiKey = "X-Transynq-APIKey";

    /// <summary>The CSRF token header every unsafe cookie-session request carries.</summary>
    public const string Csrf = "X-CSRF-Token";

    /// <summary>The correlation id header (request and response).</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>The contract version header on every response.</summary>
    public const string ApiVersion = "X-Api-Version";

    /// <summary>The idempotency key header for commands.</summary>
    public const string IdempotencyKey = "Idempotency-Key";
}
