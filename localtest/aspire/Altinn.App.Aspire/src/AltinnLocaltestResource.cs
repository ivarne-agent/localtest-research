using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>
/// The localtest platform: the parent of its component containers and its front door in the dashboard. It is running
/// once every child is healthy, so an app can <c>WaitFor</c> it.
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

    /// <summary>The public origin with a trailing slash, the value of every <c>...PlatformEndpoint</c> setting.</summary>
    public Uri PlatformUri => Stack.PlatformUri;

    /// <summary>The package's data directory on the host (the certificate, the apps' secrets).</summary>
    public string DataDirectory => Stack.DataDirectory;

    /// <summary>The host directory holding the shared test certificate.</summary>
    public string CertificateDirectory => Stack.CertificateDirectory;
}
