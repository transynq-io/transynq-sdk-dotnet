using System.Globalization;
using System.Text.Json;
using Transynq.Sdk.Http;
using Transynq.Sdk.Models;
using Transynq.Sdk.Serialization;

namespace Transynq.Sdk.Credentials;

/// <summary>How requests authenticate (docs/design.md section 3). Implementations must be thread-safe.</summary>
public interface ITransynqCredential
{
    /// <summary>Adds the credential's headers to a request about to be sent (signing in first when needed).</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels sign-in.</param>
    /// <returns>A task.</returns>
    ValueTask ApplyAsync(CredentialRequest request, CancellationToken cancellationToken);

    /// <summary>Called when a request answered 401. Returns true when sending it again with fresh credentials is worthwhile.</summary>
    /// <param name="request">The request that failed (as passed to <see cref="ApplyAsync"/>).</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>Whether to retry once.</returns>
    ValueTask<bool> RefreshAfterUnauthorizedAsync(CredentialRequest request, CancellationToken cancellationToken);
}

/// <summary>Sends sign-in calls for credentials (no credential applied, no retries; error answers raise <see cref="ApiException"/>s).</summary>
public interface ISignInTransport
{
    /// <summary>POSTs JSON to an API path.</summary>
    /// <param name="path">Path such as <c>/api/v1/auth/login</c>.</param>
    /// <param name="body">UTF-8 JSON.</param>
    /// <param name="headers">Extra headers.</param>
    /// <param name="operationId">Name for logs and errors.</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The answer.</returns>
    Task<SignInResponse> PostJsonAsync(string path, byte[] body, IReadOnlyDictionary<string, string> headers, string operationId, CancellationToken cancellationToken);
}

/// <summary>A successful sign-in answer.</summary>
/// <param name="StatusCode">HTTP status.</param>
/// <param name="Body">UTF-8 JSON body.</param>
/// <param name="SetCookies">The <c>Set-Cookie</c> header values.</param>
public sealed record SignInResponse(int StatusCode, byte[] Body, IReadOnlyList<string> SetCookies);

/// <summary>A request as a credential sees it.</summary>
public sealed class CredentialRequest
{
    private readonly HttpRequestMessage _message;

    internal CredentialRequest(HttpRequestMessage message, ISignInTransport transport)
    {
        _message = message;
        Transport = transport;
    }

    /// <summary>The HTTP method.</summary>
    public string Method => _message.Method.Method;

    /// <summary>The request URI.</summary>
    public Uri? Uri => _message.RequestUri;

    /// <summary>True for POST, PUT, PATCH and DELETE (the verbs that need a CSRF token with a cookie session).</summary>
    public bool IsUnsafe => Method is not ("GET" or "HEAD" or "OPTIONS");

    /// <summary>Sends sign-in calls.</summary>
    public ISignInTransport Transport { get; }

    /// <summary>What the credential applied (compared on a 401 to know whether the state is already fresh).</summary>
    public object? AppliedState { get; set; }

    /// <summary>Sets (replaces) a header.</summary>
    /// <param name="name">Name.</param>
    /// <param name="value">Value.</param>
    public void SetHeader(string name, string value)
    {
        _message.Headers.Remove(name);
        _message.Headers.TryAddWithoutValidation(name, value);
    }
}

/// <summary>An API client key, sent as <c>X-Transynq-APIKey</c>. The key's company is the tenant; no cookie, no CSRF.</summary>
public sealed class ApiKeyCredential : ITransynqCredential
{
    private readonly string _key;

    /// <summary>Creates the credential.</summary>
    /// <param name="apiKey">The key (keep it in a secret store).</param>
    public ApiKeyCredential(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _key = apiKey;
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.SetHeader(SdkHeaders.ApiKey, _key);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> RefreshAfterUnauthorizedAsync(CredentialRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    /// <inheritdoc />
    public override string ToString() => "ApiKeyCredential([redacted])";
}

/// <summary>An existing driver-app token (<c>Authorization: Bearer &lt;token&gt;</c>), valid for <c>/api/v1/driver/*</c>.</summary>
public sealed class DriverTokenCredential : ITransynqCredential
{
    private readonly string _token;

    /// <summary>Creates the credential.</summary>
    /// <param name="token">The token (a GUID issued by <c>POST /api/v1/driver/login</c>).</param>
    public DriverTokenCredential(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _token = token;
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.SetHeader("Authorization", "Bearer " + _token);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> RefreshAfterUnauthorizedAsync(CredentialRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    /// <inheritdoc />
    public override string ToString() => "DriverTokenCredential([redacted])";
}

/// <summary>Base of credentials that sign in and keep the result: single-flight sign-in, refresh before expiry and once after a 401.</summary>
/// <typeparam name="TState">What a sign-in yields.</typeparam>
public abstract class SignInCredential<TState> : ITransynqCredential, IDisposable where TState : class
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TState? _state;

    /// <summary>Signs in again this long before the state expires. Default 60 seconds.</summary>
    public TimeSpan RefreshMargin { get; init; } = TimeSpan.FromSeconds(60);

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>The current signed-in state, or null.</summary>
    protected TState? State => Volatile.Read(ref _state);

    /// <summary>Number of sign-ins performed (diagnostics).</summary>
    public int SignInCount { get; private set; }

    /// <summary>Forgets the signed-in state (after a sign-out); the next request signs in again.</summary>
    public void Reset() => Volatile.Write(ref _state, null);

    /// <inheritdoc />
    public async ValueTask ApplyAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await EnsureAsync(request.Transport, cancellationToken).ConfigureAwait(false);
        request.AppliedState = state;
        Apply(request, state);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RefreshAfterUnauthorizedAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // only drop the state the failed request used; a concurrent caller may already have signed in again
            if (ReferenceEquals(_state, request.AppliedState)) Volatile.Write(ref _state, null);
        }
        finally
        {
            _lock.Release();
        }
        return true;
    }

    private async ValueTask<TState> EnsureAsync(ISignInTransport transport, CancellationToken cancellationToken)
    {
        var state = State;
        if (state is not null && !NearExpiry(state)) return state;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state = State;
            if (state is not null && !NearExpiry(state)) return state;
            state = await SignInAsync(transport, cancellationToken).ConfigureAwait(false);
            SignInCount++;
            Volatile.Write(ref _state, state);
            return state;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool NearExpiry(TState state) => ExpiresUtc(state) is { } expires && expires - RefreshMargin <= Clock();

    /// <summary>Signs in.</summary>
    /// <param name="transport">Sends the sign-in call.</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <returns>The new state.</returns>
    protected abstract Task<TState> SignInAsync(ISignInTransport transport, CancellationToken cancellationToken);

    /// <summary>When the state stops being valid, if known.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The expiry.</returns>
    protected abstract DateTimeOffset? ExpiresUtc(TState state);

    /// <summary>Adds the state's headers.</summary>
    /// <param name="request">The request.</param>
    /// <param name="state">The state.</param>
    protected abstract void Apply(CredentialRequest request, TState state);

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A user session: <c>POST /api/v1/auth/login</c> with user name and password, then the session cookie on every request and
/// <c>X-CSRF-Token</c> on every unsafe one. Signs in again before the session expires and once after a 401 (idle timeout, revocation).
/// </summary>
public sealed class SessionCredential : SignInCredential<SessionCredential.Session>
{
    private readonly string _userName;
    private readonly string _password;

    /// <summary>Creates the credential (nothing is sent until the first request).</summary>
    /// <param name="userName">User name.</param>
    /// <param name="password">Password (keep it in a secret store).</param>
    public SessionCredential(string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        _userName = userName;
        _password = password;
    }

    /// <summary>The signed-in user (the login answer, <c>MeResponse</c>), or null before the first request.</summary>
    public JsonElement? Me => State?.Me;

    /// <summary>When the current session ends, or null.</summary>
    public DateTimeOffset? SessionExpiresUtc => State?.ExpiresUtc;

    /// <inheritdoc />
    protected override async Task<Session> SignInAsync(ISignInTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var body = TransynqJson.SerializeToUtf8Bytes(new LoginRequest { UserName = _userName, Password = _password });
        // without a session the CSRF check only needs a non-empty value (it forces a CORS preflight in browsers)
        var answer = await transport.PostJsonAsync("/api/v1/auth/login", body,
            new Dictionary<string, string> { [SdkHeaders.Csrf] = "transynq-sdk" }, "Login", cancellationToken).ConfigureAwait(false);
        var cookie = SessionCookie(answer.SetCookies)
                     ?? throw new AuthenticationException("Login answered without a session cookie.") { StatusCode = answer.StatusCode, OperationId = "Login" };
        using var doc = JsonDocument.Parse(answer.Body);
        var root = doc.RootElement;
        var csrf = root.TryGetProperty("csrfToken", out var c) ? c.GetString() : null;
        if (string.IsNullOrEmpty(csrf))
            throw new AuthenticationException("Login answered without a csrfToken.") { StatusCode = answer.StatusCode, OperationId = "Login" };
        DateTimeOffset? expires = root.TryGetProperty("sessionExpiresUtc", out var e) && e.ValueKind == JsonValueKind.String
                                  && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var x)
            ? x : null;
        return new Session(cookie, csrf, expires, root.Clone());
    }

    /// <inheritdoc />
    protected override DateTimeOffset? ExpiresUtc(Session state) => state?.ExpiresUtc;

    /// <inheritdoc />
    protected override void Apply(CredentialRequest request, Session state)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        request.SetHeader("Cookie", state.Cookie);
        if (request.IsUnsafe) request.SetHeader(SdkHeaders.Csrf, state.CsrfToken);
    }

    /// <summary>The <c>name=value</c> of the session cookie (<c>tq_session</c> or <c>__Host-tq_session</c>).</summary>
    /// <param name="setCookies">Set-Cookie values.</param>
    /// <returns>The pair or null.</returns>
    internal static string? SessionCookie(IEnumerable<string> setCookies)
    {
        foreach (var header in setCookies)
        {
            var pair = header.Split(';', 2)[0].Trim();
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            var name = pair[..eq];
            if (name.EndsWith("tq_session", StringComparison.Ordinal) && eq < pair.Length - 1) return pair;
        }
        return null;
    }

    /// <inheritdoc />
    public override string ToString() => $"SessionCredential({_userName}, [redacted])";

    /// <summary>A signed-in session.</summary>
    /// <param name="Cookie">The session cookie pair.</param>
    /// <param name="CsrfToken">The CSRF token bound to the session.</param>
    /// <param name="ExpiresUtc">The absolute end of the session.</param>
    /// <param name="Me">The login answer.</param>
    public sealed record Session(string Cookie, string CsrfToken, DateTimeOffset? ExpiresUtc, JsonElement Me)
    {
        /// <inheritdoc />
        public override string ToString() => $"Session(expires {ExpiresUtc:O}, [redacted])";
    }
}

/// <summary>
/// Driver-app sign-in: <c>POST /api/v1/driver/login</c> (company code, PIN, device), then <c>Authorization: Bearer &lt;token&gt;</c>.
/// Signs in again before <c>expiresUtc</c> and once after a 401.
/// </summary>
public sealed class DriverLoginCredential : SignInCredential<DriverLoginCredential.DriverToken>
{
    private readonly DriverLoginRequest _login;

    /// <summary>Creates the credential (nothing is sent until the first request).</summary>
    /// <param name="login">Company code, PIN, device id, push token, app version and platform.</param>
    public DriverLoginCredential(DriverLoginRequest login)
    {
        ArgumentNullException.ThrowIfNull(login);
        _login = login;
    }

    /// <summary>The last driver session (driver, company, settings), or null.</summary>
    public DriverSession? Session => State?.Session;

    /// <inheritdoc />
    protected override async Task<DriverToken> SignInAsync(ISignInTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var answer = await transport.PostJsonAsync("/api/v1/driver/login", TransynqJson.SerializeToUtf8Bytes(_login),
            new Dictionary<string, string>(), "DriverLogin", cancellationToken).ConfigureAwait(false);
        var session = TransynqJson.Deserialize<DriverSession>(answer.Body);
        if (session?.Token is not { Length: > 0 } token)
            throw new AuthenticationException("DriverLogin answered without a token.") { StatusCode = answer.StatusCode, OperationId = "DriverLogin" };
        return new DriverToken(token, session.ExpiresUtc, session);
    }

    /// <inheritdoc />
    protected override DateTimeOffset? ExpiresUtc(DriverToken state) => state?.ExpiresUtc;

    /// <inheritdoc />
    protected override void Apply(CredentialRequest request, DriverToken state)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        request.SetHeader("Authorization", "Bearer " + state.Token);
    }

    /// <inheritdoc />
    public override string ToString() => "DriverLoginCredential([redacted])";

    /// <summary>A driver token.</summary>
    /// <param name="Token">The bearer token.</param>
    /// <param name="ExpiresUtc">When it ends.</param>
    /// <param name="Session">The login answer.</param>
    public sealed record DriverToken(string Token, DateTimeOffset? ExpiresUtc, DriverSession Session)
    {
        /// <inheritdoc />
        public override string ToString() => $"DriverToken(expires {ExpiresUtc:O}, [redacted])";
    }
}
