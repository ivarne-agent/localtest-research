namespace Localtest.Proxy;

/// <summary>
/// How a call to a service by name fails. Service discovery runs with the configuration provider only (no
/// pass-through to DNS), so a name without <c>services__{name}__http__0</c> fails before any connection, with an
/// <see cref="InvalidOperationException"/>, and an address that does not answer fails as usual, with an
/// <see cref="HttpRequestException"/>.
/// </summary>
public static class ServiceCalls
{
    // The configuration-only resolver's failure for a name without an entry: the round-robin selector throws on an empty
    // endpoint collection (Microsoft.Extensions.ServiceDiscovery 10.8.0, as the service-discovery probe recorded); the
    // resolvers' own message for a name they could not resolve at all is the other.
    private const string NoEndpointsMessage = "The endpoint collection contains no endpoints";
    private const string UnresolvedPrefix = "Unable to resolve endpoint";

    /// <summary>Whether the call failed because service discovery has no address for the name.</summary>
    public static bool NoAddress(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException { Message: var message }
                && (message == NoEndpointsMessage || message.StartsWith(UnresolvedPrefix, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a call to a service failed for want of the service: no address, or no answer.</summary>
    public static bool Unreachable(Exception exception) => exception is HttpRequestException || NoAddress(exception);
}
