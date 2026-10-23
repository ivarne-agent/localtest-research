using Digdir.Domain.Dialogporten.Application.Externals;
using Digdir.Domain.Dialogporten.Application.Externals.AltinnAuthorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// The four production clients that share <c>Infrastructure:Altinn:BaseUri</c> (the Resource Registry, Access
/// Management's metadata, the Register's party names, the PDP and Access Management's authorized parties) call the
/// services by name. Upstream uses one base for all of them, and <c>MetadataLinkProvider</c> builds the links it hands
/// to clients from the same base, so the base stays the public origin and this handler, outermost on those four clients,
/// moves each request on it to the service that owns its path prefix: <c>{BaseUri}resourceregistry/api/v1/…</c> becomes
/// <c>http://resource-registry/resourceregistry/api/v1/…</c>, which the service-discovery handler then resolves from
/// <c>services__resource-registry__http__0</c>. A path with another prefix is left on the public origin.
/// </summary>
public sealed class LocaltestServiceNames(Uri publicBase) : DelegatingHandler
{
    /// <summary>The first path segment on the public origin → the service that owns it.</summary>
    private static readonly Dictionary<string, string> Owners = new(StringComparer.Ordinal)
    {
        ["resourceregistry"] = "resource-registry",
        ["accessmanagement"] = "access-management",
        ["register"] = "register",
        ["authorization"] = "authorization",
        ["events"] = "events",
    };

    /// <summary>Adds the handler, outermost, to the four clients on <c>Infrastructure:Altinn:BaseUri</c>.</summary>
    public static IServiceCollection Apply(IServiceCollection services)
    {
        // The typed clients' names are their interfaces' short names (AddHttpClient<TClient, TImplementation>).
        foreach (var name in new[] { nameof(IResourceRegistry), nameof(IAccessManagementMetadata), nameof(IPartyNameRegistry), nameof(IAltinnAuthorization) })
        {
            // Registered after every production registration (LocaltestOverrides), so the service-discovery handler is
            // already in the list; index 0 puts this one in front of it.
            services.AddHttpClient(name).ConfigureAdditionalHttpMessageHandlers((handlers, sp) =>
                handlers.Insert(0, new LocaltestServiceNames(sp.GetRequiredService<IOptions<InfrastructureSettings>>().Value.Altinn.BaseUri)));
        }

        return services;
    }

    /// <summary>The service-name URI for <paramref name="uri"/>, or null when it is not on the public base or no service owns its prefix.</summary>
    public static Uri? ServiceUri(Uri uri, Uri publicBase)
    {
        if (!uri.IsAbsoluteUri
            || Uri.Compare(uri, publicBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var end = path.IndexOf('/', 1);
        var first = end < 0 ? path[1..] : path[1..end];
        return Owners.TryGetValue(first, out var service)
            ? new Uri($"http://{service}{uri.PathAndQuery}")
            : null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && ServiceUri(uri, publicBase) is { } service)
        {
            request.RequestUri = service;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
