using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>
/// What the two forms of the localtest platform share: the full stack (<see cref="AltinnLocaltestResource" />, from
/// <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />) and minimal mode
/// (<see cref="AltinnLocaltestMinimalResource" />, from
/// <see cref="AltinnLocaltestMinimalExtensions.AddAltinnLocaltestMinimal" />). What both support takes a builder of
/// this type (<c>AddAltinnApp</c>, <c>WithFrontendDevServer</c>); what only the full stack supports (the optional
/// groups) takes <see cref="AltinnLocaltestResource" />'s, which a minimal builder does not convert to, since neither
/// leaf derives from the other. The parent of the containers in the dashboard; it is running once every container is
/// healthy, so an app can <c>WaitFor</c> it.
/// </summary>
public abstract class AltinnLocaltestResourceBase : Resource
{
    // Private protected: the package's two sealed leaves are the only resources the methods taking this type expect.
    private protected AltinnLocaltestResourceBase(string name, LocaltestStack stack)
        : base(name)
    {
        Stack = stack;
    }

    internal LocaltestStack Stack { get; }

    /// <summary>
    /// The public origin with a trailing slash: <c>http://local.altinn.cloud:8000/</c> by default, the options'
    /// <c>PublicHost</c> and <c>PublicPort</c> otherwise. With <c>PublicPort = 0</c> it is known once Aspire has allocated
    /// the port, when the AppHost runs; before that it throws.
    /// </summary>
    public Uri PlatformUri => Stack.PlatformUri;

    /// <summary>The package's data directory on the host (the certificate, the apps' secrets).</summary>
    public string DataDirectory => Stack.DataDirectory;

    /// <summary>The host directory holding the shared test certificate.</summary>
    public string CertificateDirectory => Stack.CertificateDirectory;
}
