namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>Where the platform is and what the provisioner asks its runner.</summary>
public sealed class LocaltestProvisionerOptions
{
    /// <summary>The platform's public origin, for example <c>http://local.altinn.cloud:8000/</c>.</summary>
    public required Uri PlatformUri { get; init; }

    /// <summary>
    /// The token Storage and the PDP accept from Designer. <c>null</c>: the localtest app's
    /// <c>GET {PlatformUri}/Home/GetTestStudioToken</c> (Designer's <c>studio</c>/<c>studio.designer</c> platform access
    /// token exchanged through the production <c>exchange/altinnstudio</c>), fetched once per provisioning.
    /// </summary>
    public Func<CancellationToken, Task<string>>? StudioToken { get; init; }

    /// <summary>
    /// Whether the optional <c>dialogporten</c> group is up, asked before each Dialogporten sync (step 5).
    /// studioctl-server reads its route table; an Aspire host knows which groups it added. Default: never.
    /// </summary>
    public Func<bool> DialogportenRunning { get; init; } = static () => false;
}
