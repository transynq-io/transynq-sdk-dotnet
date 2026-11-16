using System.Net;
using System.Text;

namespace Transynq.Sdk.Tests;

/// <summary>A recorded request (the body read before the message is disposed).</summary>
public sealed record Recorded(string Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body, string? ContentType)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}

/// <summary>An in-memory HTTP handler: answers from a queue (or a function) and records every request. No network.</summary>
public sealed class MockHttp : HttpMessageHandler
{
    private readonly Queue<Func<Recorded, HttpResponseMessage>> _answers = new();
    private readonly object _gate = new();

    public List<Recorded> Requests { get; } = [];

    public Func<Recorded, HttpResponseMessage>? Fallback { get; set; }

    public MockHttp Enqueue(Func<Recorded, HttpResponseMessage> answer)
    {
        lock (_gate) _answers.Enqueue(answer);
        return this;
    }

    public MockHttp Enqueue(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
        => Enqueue(_ => Json(status, json, headers));

    public static HttpResponseMessage Json(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        if (json is not null) response.Content = new StringContent(json, Encoding.UTF8, status >= HttpStatusCode.BadRequest ? "application/problem+json" : "application/json");
        foreach (var (name, value) in headers)
            if (!response.Headers.TryAddWithoutValidation(name, value)) response.Content.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Headers) headers[name] = string.Join(name == "User-Agent" ? " " : ", ", values);
        byte[] body = [];
        string? contentType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType?.ToString();
            foreach (var (name, values) in request.Content.Headers) headers[name] = string.Join(", ", values);
        }
        var recorded = new Recorded(request.Method.Method, request.RequestUri!, headers, body, contentType);
        Func<Recorded, HttpResponseMessage>? answer;
        lock (_gate)
        {
            Requests.Add(recorded);
            answer = _answers.Count > 0 ? _answers.Dequeue() : Fallback;
        }
        if (answer is null) throw new InvalidOperationException($"No answer queued for {recorded.Method} {recorded.Uri}");
        var response = answer(recorded);
        response.RequestMessage = request;
        return response;
    }
}

public static class TestClients
{
    public static readonly Uri Base = new("https://api.test.local");

    public static (TransynqClient Client, MockHttp Http, List<TimeSpan> Delays) Create(Action<TransynqClientOptions>? configure = null)
    {
        var http = new MockHttp();
        var delays = new List<TimeSpan>();
        var options = new TransynqClientOptions
        {
            BaseUri = Base,
            Delay = (d, _) => { lock (delays) delays.Add(d); return Task.CompletedTask; },
            Jitter = () => 1.0,
        };
        configure?.Invoke(options);
        return (new TransynqClient(options, new HttpClient(http)), http, delays);
    }
}
