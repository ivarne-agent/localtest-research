using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>
/// The workflow engine's dashboard links a workflow's trace to the Aspire dashboard's trace page, as studioctl links it
/// to Grafana while its monitoring group runs (the group the package ignores).
/// </summary>
internal static class DashboardTraceLinks
{
    /// <summary>The workflow engine's manifest name.</summary>
    public const string WorkflowEngine = "workflow-engine";

    /// <summary>
    /// The engine's setting: an absolute URL with <c>{traceId}</c>, which wins over its Grafana settings. An engine
    /// that does not know it ignores it.
    /// </summary>
    public const string TraceUrlTemplateVariable = "EngineSettings__Dashboard__TraceUrlTemplate";

    /// <summary>
    /// The dashboard's resource, which Aspire adds to the model before the start (<c>KnownResourceNames.AspireDashboard</c>
    /// in Aspire.Hosting, internal), with an <c>https</c> or an <c>http</c> endpoint on its browser address, from
    /// <c>ASPNETCORE_URLS</c> or allocated. Aspire's own dashboard link reads the same endpoint.
    /// </summary>
    public const string DashboardResource = "aspire-dashboard";

    /// <summary>
    /// The trace URL template, the dashboard's trace detail page (<c>/traces/detail/{traceId}</c>, Aspire 13.6) on its
    /// browser address, or null when the AppHost runs no dashboard
    /// (<c>DisableDashboard</c>, publish mode). The endpoint is read on the localhost network, as a browser on the host
    /// opens it, not as the container would reach the host.
    /// </summary>
    public static ReferenceExpression? TraceUrlTemplate(IEnumerable<IResource> resources)
    {
        if (
            resources.FirstOrDefault(r => string.Equals(r.Name, DashboardResource, StringComparison.OrdinalIgnoreCase))
            is not IResourceWithEndpoints dashboard
        )
        {
            return null;
        }
        var https = dashboard.GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork);
        var endpoint = https.Exists ? https : dashboard.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork);
        if (!endpoint.Exists)
        {
            return null;
        }
        // The interpolation handler escapes the literal braces of {traceId}.
        return ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Url)}/traces/detail/{{traceId}}");
    }
}
