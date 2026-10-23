#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Shared.ServiceDiscovery;

/// <summary>
/// For a production client whose one base URL serves several services and is also handed out in links or used as an
/// issuer (Dialogporten's <c>Infrastructure:Altinn:BaseUri</c>, Correspondence's <c>AltinnOptions:PlatformGatewayUrl</c>):
/// the base stays the public origin, and this handler, outermost on those clients, moves each request on it to the service
/// that owns its path prefix, as the front door would route it (<c>localtest/localtest/Proxy/PlatformRoutes.cs</c>):
/// <c>{origin}/resourceregistry/api/v1/…</c> becomes <c>http://resource-registry/resourceregistry/api/v1/…</c>, which the
/// service-discovery handler behind it then resolves from <c>services__resource-registry__http__0</c>. A request on another
/// host, or under a prefix no service owns here, is left as it is.
/// </summary>
public sealed class LocaltestPrefixRouting(Uri publicBase) : DelegatingHandler
{
    /// <summary>
    /// The front door's path prefixes and their services, the longer prefix of a service inside another's first. Not
    /// <c>/dialogporten/graphql</c>: the front door rewrites it, so it stays on the public origin.
    /// </summary>
    private static readonly (string Prefix, string Service)[] Prefixes =
    [
        ("/dialogporten", "dialogporten-webapi"),
        ("/storage/dialogporten", "dialogporten-adapter"),
        ("/storage", "storage"),
        ("/register", "register"),
        ("/profile", "profile"),
        ("/authentication", "authentication"),
        ("/authorization", "authorization"),
        ("/accessmanagement", "access-management"),
        ("/events", "events"),
        ("/resourceregistry", "resource-registry"),
        ("/notifications", "notifications"),
        ("/receipt", "receipt"),
        ("/correspondence", "correspondence"),
    ];

    /// <summary>
    /// Adds the handler, outermost, to the named (or typed: the interface's short name) clients. Call it after every
    /// production registration, so the service-discovery handler is in their lists already and this one goes in front.
    /// </summary>
    public static IServiceCollection AddTo(IServiceCollection services, Func<IServiceProvider, Uri> publicBase, params string[] clientNames)
    {
        foreach (var name in clientNames)
        {
            services.AddHttpClient(name).ConfigureAdditionalHttpMessageHandlers((handlers, sp) => handlers.Insert(0, new LocaltestPrefixRouting(publicBase(sp))));
        }

        return services;
    }

    /// <summary>The service-name URI for <paramref name="uri"/>, or null when it is not on the public base's origin or no service owns its path.</summary>
    public static Uri? ServiceUri(Uri uri, Uri publicBase)
    {
        if (!uri.IsAbsoluteUri
            || Uri.Compare(uri, publicBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0
            || uri.AbsolutePath.StartsWith("/dialogporten/graphql", StringComparison.Ordinal))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var owner = Prefixes
            .Where(p => path.StartsWith(p.Prefix, StringComparison.Ordinal) && (path.Length == p.Prefix.Length || path[p.Prefix.Length] == '/'))
            .OrderByDescending(p => p.Prefix.Length)
            .Select(p => p.Service)
            .FirstOrDefault();
        return owner is null ? null : new Uri($"http://{owner}{uri.PathAndQuery}");
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && ServiceUri(uri, publicBase) is { } service)
        {
            request.RequestUri = service;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
