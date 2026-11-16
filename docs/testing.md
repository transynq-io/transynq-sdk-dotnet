# Testing your integration

Unit tests should not call the real API. Answer the SDK's requests in memory:

```csharp
// Answer requests in memory: no network, no server.
sealed class FakeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":1,"name":"Australia"}]""", Encoding.UTF8, "application/json"),
        });
}

[Fact]
public async Task Lists_countries()
{
    using var client = new TransynqClient(
        new TransynqClientOptions { BaseUri = new Uri("https://api.example.com"), Credential = new ApiKeyCredential("test-key") },
        new HttpClient(new FakeHandler()));

    var countries = await client.ReferenceData.ListCountriesAsync();

    Assert.Equal("Australia", countries.Data[0].Name);
}
```

Your own code can depend on `TransynqClient` directly and give it an `HttpClient` over a fake handler, as above (this is how the SDK's own tests work), or wrap the few calls it makes behind an interface of yours and mock that.

Tips:

- Never put real API keys, passwords, hostnames or customer data in tests; use fake values such as `test-key` and
  `https://api.example.com`.
- Answer error cases with ProblemDetails bodies (`{"title": "...", "status": 422, "code": "id-in-use"}`) to exercise your error
  handling; the SDK maps them to the same typed errors as in production.
- Set the client's max retries to 0 in tests that answer 5xx, or retries will consume your queued answers.
