using Microsoft.Extensions.Http.Resilience;

namespace Altinn.Register.Utils;

/// <summary>
/// Extensions methods to help with HTTP resiliency.
/// </summary>
internal static class HttpReciliencyExtensions
{
    /// <summary>
    /// Configures the standard resilience handler for the HTTP client.
    /// </summary>
    /// <param name="builder">A <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configure">A configuration delegate.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHttpClientBuilder ReplaceResilienceHandler(this IHttpClientBuilder builder, Action<HttpStandardResilienceOptions> configure)
    {
        builder.ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
        {
            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                if (handlers[i] is ResilienceHandler)
                {
                    handlers.RemoveAt(i);
                }
            }
        });

        builder.AddStandardResilienceHandler(configure);

        return builder;
    }
}
