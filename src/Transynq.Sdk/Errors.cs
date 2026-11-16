using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Transynq.Sdk;

/// <summary>Base of every error the SDK raises.</summary>
public class TransynqException : Exception
{
    /// <summary>Creates the error.</summary>
    public TransynqException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public TransynqException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public TransynqException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>No answer: the connection failed (DNS, TLS, reset). Retried when the request is safe to repeat.</summary>
public class TransynqConnectionException : TransynqException
{
    /// <summary>Creates the error.</summary>
    public TransynqConnectionException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public TransynqConnectionException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public TransynqConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>No answer within <see cref="TransynqClientOptions.Timeout"/>.</summary>
public class TransynqTimeoutException : TransynqConnectionException
{
    /// <summary>Creates the error.</summary>
    public TransynqTimeoutException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public TransynqTimeoutException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public TransynqTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The API answered with an error (ProblemDetails, docs/api-conventions.md section 3).</summary>
public class ApiException : TransynqException
{
    /// <summary>Creates the error.</summary>
    public ApiException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public ApiException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public ApiException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>HTTP status.</summary>
    public int StatusCode { get; internal set; }

    /// <summary>The stable problem code (<c>validation</c>, <c>not-found</c>, a rule code such as <c>id-in-use</c>...), when sent.</summary>
    public string? Code { get; internal set; }

    /// <summary>The problem title (in the request's language).</summary>
    public string? Title { get; internal set; }

    /// <summary>The problem detail (a business rule's sentence).</summary>
    public string? Detail { get; internal set; }

    /// <summary>The problem type URI.</summary>
    public string? Type { get; internal set; }

    /// <summary>The correlation id (from the body, else the <c>X-Correlation-Id</c> header).</summary>
    public string? CorrelationId { get; internal set; }

    /// <summary>The raw response body (may be empty).</summary>
    public string RawBody { get; internal set; } = "";

    /// <summary>Response headers.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; internal set; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The operation that failed.</summary>
    public string? OperationId { get; internal set; }
}

/// <summary>400: malformed input; <see cref="Errors"/> lists the messages per field.</summary>
public class ValidationException : ApiException
{
    /// <summary>Creates the error.</summary>
    public ValidationException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public ValidationException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public ValidationException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Messages per field (<c>errors</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; internal set; } = new Dictionary<string, IReadOnlyList<string>>();
}

/// <summary>401: not signed in, session ended, invalid key or token.</summary>
public class AuthenticationException : ApiException
{
    /// <summary>Creates the error.</summary>
    public AuthenticationException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public AuthenticationException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>403: signed in but not allowed (or a missing CSRF token).</summary>
public class PermissionDeniedException : ApiException
{
    /// <summary>Creates the error.</summary>
    public PermissionDeniedException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public PermissionDeniedException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public PermissionDeniedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>404: missing, or outside the caller's scope.</summary>
public class NotFoundException : ApiException
{
    /// <summary>Creates the error.</summary>
    public NotFoundException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public NotFoundException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>409: the data was changed by someone else (concurrency token).</summary>
public class ConflictException : ApiException
{
    /// <summary>Creates the error.</summary>
    public ConflictException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public ConflictException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public ConflictException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>412: <c>If-Match</c> does not match the current ETag; reload and try again (see <see cref="Concurrency"/>).</summary>
public class StaleVersionException : ApiException
{
    /// <summary>Creates the error.</summary>
    public StaleVersionException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public StaleVersionException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public StaleVersionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>422: a business rule refused a well-formed request; <see cref="ApiException.Code"/> is the rule.</summary>
public class BusinessRuleException : ApiException
{
    /// <summary>Creates the error.</summary>
    public BusinessRuleException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public BusinessRuleException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public BusinessRuleException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>429: rate limited; <see cref="RetryAfter"/> says when to try again.</summary>
public class RateLimitException : ApiException
{
    /// <summary>Creates the error.</summary>
    public RateLimitException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public RateLimitException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public RateLimitException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The <c>Retry-After</c> delay, when sent.</summary>
    public TimeSpan? RetryAfter { get; internal set; }
}

/// <summary>5xx: the API failed (quote <see cref="ApiException.CorrelationId"/> to support).</summary>
public class ServerException : ApiException
{
    /// <summary>Creates the error.</summary>
    public ServerException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public ServerException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public ServerException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>A long operation ended in <c>Failed</c>.</summary>
public class LongOperationFailedException : TransynqException
{
    /// <summary>Creates the error.</summary>
    public LongOperationFailedException() { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    public LongOperationFailedException(string message) : base(message) { }

    /// <summary>Creates the error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public LongOperationFailedException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The final state of the operation.</summary>
    public Models.LongOperationResponse? Operation { get; internal set; }
}

/// <summary>Builds the typed error from an error answer.</summary>
internal static class ErrorFactory
{
    public static ApiException Create(int status, byte[] body, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string operationId, TimeSpan? retryAfter)
    {
        string? code = null, title = null, detail = null, type = null, correlationId = null;
        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var raw = body.Length == 0 ? "" : Encoding.UTF8.GetString(body);
        if (body.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    code = Str(root, "code");
                    title = Str(root, "title");
                    detail = Str(root, "detail");
                    type = Str(root, "type");
                    correlationId = Str(root, "correlationId");
                    if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in e.EnumerateObject())
                        {
                            errors[field.Name] = field.Value.ValueKind == JsonValueKind.Array
                                ? field.Value.EnumerateArray().Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : m.GetRawText()).ToList()
                                : [field.Value.ToString()];
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // not ProblemDetails (a proxy page): keep the raw body only
            }
        }
        correlationId ??= headers.TryGetValue("X-Correlation-Id", out var c) && c.Count > 0 ? c[0] : null;
        var message = string.Create(CultureInfo.InvariantCulture,
            $"{operationId} failed with {status}{(code is null ? "" : $" ({code})")}: {title ?? "no title"}{(detail is null ? "" : $" - {detail}")}{(correlationId is null ? "" : $" [correlation id {correlationId}]")}");
        ApiException error = status switch
        {
            400 => new ValidationException(message) { Errors = errors },
            401 => new AuthenticationException(message),
            403 => new PermissionDeniedException(message),
            404 => new NotFoundException(message),
            409 => new ConflictException(message),
            412 => new StaleVersionException(message),
            422 => new BusinessRuleException(message),
            429 => new RateLimitException(message) { RetryAfter = retryAfter },
            >= 500 => new ServerException(message),
            _ => new ApiException(message),
        };
        error.StatusCode = status;
        error.Code = code;
        error.Title = title;
        error.Detail = detail;
        error.Type = type;
        error.CorrelationId = correlationId;
        error.RawBody = raw;
        error.Headers = headers;
        error.OperationId = operationId;
        return error;
    }

    private static string? Str(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText(),
        } : null;
}
