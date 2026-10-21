namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The public host names the manifest's <c>{{host:...}}</c> and <c>{{url:...}}</c> placeholders name: studioctl's
/// default topology (<c>localtest/studioctl/src/cli/internal/envtopology/topology.yaml</c>) at its default ingress port.
/// The manifest names topology components but does not hold their hosts, so they are repeated here; the golden test
/// fails when the two disagree.
/// </summary>
internal sealed class LocaltestTopology
{
    /// <summary>The topology component of the public origin, which <c>{{platform}}</c> names.</summary>
    public const string App = "app";

    /// <summary>The default topology: <c>http://local.altinn.cloud:8000</c>.</summary>
    public static LocaltestTopology Default { get; } = new("http", 8000);

    private static readonly IReadOnlyDictionary<string, string> _hosts = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        [App] = "local.altinn.cloud",
        ["platform"] = "local.altinn.cloud",
        ["pdf"] = "pdf.local.altinn.cloud",
        ["pgadmin"] = "pgadmin.local.altinn.cloud",
        ["workflowEngine"] = "workflow-engine.local.altinn.cloud",
        ["frontendDevServer"] = "app-frontend.local.altinn.cloud",
        ["arbeidsflate"] = "af.local.altinn.cloud",
    };

    /// <summary>
    /// The host names every container that maps the ingress hosts resolves to the host gateway, in studioctl's order
    /// (<c>envtopology.Local.LocaltestIngressHosts</c>).
    /// </summary>
    public static IReadOnlyList<string> IngressHostIds { get; } =
    [App, "pdf", "workflowEngine", "pgadmin", "frontendDevServer", "arbeidsflate"];

    private LocaltestTopology(string scheme, int ingressPort)
    {
        Scheme = scheme;
        IngressPort = ingressPort;
    }

    /// <summary>The public scheme.</summary>
    public string Scheme { get; }

    /// <summary>The front door's port, on the host and in every public URL.</summary>
    public int IngressPort { get; }

    /// <summary>The public host of a topology component, or null when it is unknown.</summary>
    public string? Host(string id) => _hosts.GetValueOrDefault(id);

    /// <summary>The public origin of a topology component without a trailing slash, or null.</summary>
    public string? Url(string id) => Host(id) is { } host ? $"{Scheme}://{host}:{IngressPort}" : null;

    /// <summary>The ingress host names in studioctl's order.</summary>
    public IReadOnlyList<string> IngressHosts => [.. IngressHostIds.Select(id => _hosts[id])];
}
