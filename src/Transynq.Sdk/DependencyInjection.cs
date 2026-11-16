using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Transynq.Sdk;

/// <summary>Registers <see cref="TransynqClient"/> with <c>IHttpClientFactory</c>.</summary>
public static class TransynqServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="TransynqClient"/> as a typed HTTP client. The options (and so the credential and its session) are a singleton shared by
    /// every client instance; the factory manages the handlers (cookie jar off, decompression on).
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Sets the options (base URI, credential, ...).</param>
    /// <returns>The HTTP client builder, to add handlers or policies.</returns>
    public static IHttpClientBuilder AddTransynqClient(this IServiceCollection services, Action<TransynqClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<TransynqClientOptions>().Configure(configure);
        var builder = services
            .AddHttpClient(nameof(TransynqClient))
            .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan)   // the SDK applies its own per-attempt timeout
            .ConfigurePrimaryHttpMessageHandler(TransynqClient.CreateDefaultHandler);
        builder.AddTypedClient<TransynqClient>((http, provider) =>
            new TransynqClient(provider.GetRequiredService<IOptions<TransynqClientOptions>>().Value, http));
        return builder;
    }
}
