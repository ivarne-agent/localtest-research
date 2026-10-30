namespace Localtest;

/// <summary>
/// Settings several features share. <see cref="PlatformEndpoint"/> is the public origin of the stack (what the browser
/// and the apps use), with a trailing slash: <c>Localtest:PlatformEndpoint</c>, default <c>http://local.altinn.cloud:8000/</c>;
/// the public host and port are its own (<see cref="PublicHostName"/>, <see cref="PublicPort"/>).
/// <see cref="PublicSubdomains"/> are the dedicated subdomains of the public host the runner lists
/// (<c>Localtest:PublicSubdomains</c>, comma-separated, from the manifest's public section; default every one in
/// <see cref="KnownSubdomains"/>): a known one not listed is off, an unknown one is an error at start-up.
/// <see cref="AccessTokenCertificatePath"/> is the shared test certificate every app and component signs platform access
/// tokens with (<c>Localtest:AccessTokenCertificatePath</c>, default <c>/certs/localtest-accesstoken.pfx</c>).
/// </summary>
public sealed record LocaltestSettings
{
    /// <summary>The PDF generator's subdomain: <c>pdf.{host}</c> belongs to pdf3.</summary>
    public const string PdfSubdomain = "pdf";

    /// <summary>The workflow engine's subdomain.</summary>
    public const string WorkflowEngineSubdomain = "workflow-engine";

    /// <summary>Arbeidsflate's (the inbox's) subdomain: its SPA and BFF use absolute paths.</summary>
    public const string ArbeidsflateSubdomain = "af";

    /// <summary>
    /// The Access Management GUI's subdomain, two labels as production's <c>am.ui.{env}</c>: it serves everything under
    /// <c>/accessmanagement/</c>, which is the Access Management API's prefix on the main host.
    /// </summary>
    public const string AccessManagementUiSubdomain = "am.ui";

    /// <summary>The frontend dev server's subdomain, routed while one is registered.</summary>
    public const string FrontendDevServerSubdomain = "app-frontend";

    /// <summary>The subdomains the front door has a route for, in the manifest's order.</summary>
    public static IReadOnlyList<string> KnownSubdomains { get; } =
        [PdfSubdomain, WorkflowEngineSubdomain, FrontendDevServerSubdomain, ArbeidsflateSubdomain, AccessManagementUiSubdomain];

    private const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public LocaltestSettings(string platformEndpoint, string accessTokenCertificatePath, IReadOnlyList<string>? publicSubdomains = null)
    {
        PlatformEndpoint = platformEndpoint.TrimEnd('/') + "/";
        AccessTokenCertificatePath = accessTokenCertificatePath;
        var unknown = (publicSubdomains ?? []).Where(s => !KnownSubdomains.Contains(s, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Localtest:PublicSubdomains names {string.Join(", ", unknown)}, which the front door has no route for (it knows {string.Join(", ", KnownSubdomains)}).");
        }

        PublicSubdomains = publicSubdomains ?? KnownSubdomains;
        var origin = new Uri(PlatformEndpoint);
        PublicHostName = origin.Host;
        PublicPort = origin.Port;
    }

    /// <summary>The public origin with a trailing slash.</summary>
    public string PlatformEndpoint { get; }

    public string AccessTokenCertificatePath { get; }

    /// <summary>The dedicated subdomains that are on.</summary>
    public IReadOnlyList<string> PublicSubdomains { get; }

    /// <summary>The public host name, without port: <c>local.altinn.cloud</c>.</summary>
    public string PublicHostName { get; }

    /// <summary>The public port: <c>8000</c>.</summary>
    public int PublicPort { get; }

    /// <summary>The public host and port, for the <c>Host</c> header of calls the localtest app makes to a component.</summary>
    public string PublicHost => new Uri(PlatformEndpoint).Authority;

    public static LocaltestSettings FromConfiguration(IConfiguration configuration)
        => new(
            configuration["Localtest:PlatformEndpoint"] is { Length: > 0 } platform ? platform : DefaultPlatformEndpoint,
            configuration["Localtest:AccessTokenCertificatePath"] is { Length: > 0 } path ? path : "/certs/localtest-accesstoken.pfx",
            configuration["Localtest:PublicSubdomains"] is { } subdomains
                ? subdomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : null);

    /// <summary>A subdomain's host name, <c>{sub}.{host}</c>, or null when the subdomain is off.</summary>
    public string? HostOf(string subdomain)
        => PublicSubdomains.Contains(subdomain, StringComparer.Ordinal) ? subdomain + "." + PublicHostName : null;

    /// <summary>
    /// A subdomain's origin without a trailing slash, <c>http://{sub}.{host}:{port}</c>, written whether or not the
    /// subdomain is on (a link or a redirect URI that names it).
    /// </summary>
    public string OriginOf(string subdomain)
        => new UriBuilder(new Uri(PlatformEndpoint)) { Host = subdomain + "." + PublicHostName, Path = "" }.Uri.GetLeftPart(UriPartial.Authority);

    /// <summary>Whether <paramref name="host"/> (without port) is the subdomain's host and the subdomain is on.</summary>
    public bool IsHostOf(string subdomain, string host)
        => HostOf(subdomain) is { } own && string.Equals(host, own, StringComparison.OrdinalIgnoreCase);
}
