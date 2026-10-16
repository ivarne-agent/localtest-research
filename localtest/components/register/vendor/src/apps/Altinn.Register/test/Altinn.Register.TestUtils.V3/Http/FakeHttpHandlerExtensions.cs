using Altinn.Authorization.TestUtils.Http;

namespace Altinn.Register.TestUtils.Http;

public static class FakeHttpHandlerExtensions
{
    /// <summary>
    /// Adds a query filter to the request.
    /// </summary>
    /// <typeparam name="T">The request builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="platformToken">The platform token.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static T WithPlatformToken<T>(this T builder, string platformToken)
        where T : IFilterFakeRequest
    {
        builder.AddFilter(new PlatformTokenFilter(platformToken));

        return builder;
    }

    private sealed class PlatformTokenFilter(string platformToken)
        : IFakeRequestFilter
    {
        public string Description => $"has platform token '{platformToken}'";

        public bool Matches(FakeHttpRequestMessage request)
        {
            if (!request.Headers.TryGetValues("PlatformAccessToken", out var values))
            {
                return false;
            }

            if (!values.Contains(platformToken, StringComparer.Ordinal))
            {
                return false;
            }

            return true;
        }
    }
}
