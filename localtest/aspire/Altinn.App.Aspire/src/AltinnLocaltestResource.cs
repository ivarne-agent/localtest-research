using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>
/// The localtest platform: the parent of its containers in the dashboard. It is running once every container is
/// healthy, so an app can <c>WaitFor</c> it.
/// </summary>
public sealed class AltinnLocaltestResource : Resource
{
    internal AltinnLocaltestResource(string name, LocaltestStack stack)
        : base(name)
    {
        Stack = stack;
    }

    internal LocaltestStack Stack { get; }

    /// <summary>The enabled optional groups, sorted.</summary>
    public IReadOnlyCollection<string> Groups => Stack.EnabledGroups;

    /// <summary>The public origin with a trailing slash: <c>http://local.altinn.cloud:8000/</c>.</summary>
    public Uri PlatformUri => Manifest.Topology.PlatformUri;

    /// <summary>The package's data directory on the host (the certificate, the apps' secrets).</summary>
    public string DataDirectory => Stack.DataDirectory;

    /// <summary>The host directory holding the shared test certificate.</summary>
    public string CertificateDirectory => Stack.CertificateDirectory;
}
