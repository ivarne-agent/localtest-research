namespace Localtest.Provisioning;

/// <summary>What the provisioner asks its host: the Studio token and whether Dialogporten runs.</summary>
public sealed class LocaltestProvisionerOptions
{
    /// <summary>
    /// The token Storage and the PDP accept from Designer, fetched once per provisioning. The localtest app mints
    /// Designer's <c>studio</c>/<c>studio.designer</c> platform access token in-process and exchanges it through the
    /// production <c>exchange/altinnstudio</c> (<see cref="Home.StudioToken"/>).
    /// </summary>
    public required Func<CancellationToken, Task<string>> StudioToken { get; init; }

    /// <summary>
    /// Whether Dialogporten runs, asked before each Dialogporten sync (step 5). The localtest app answers whether
    /// <c>dialogporten-adapter</c> has an address (<c>services__dialogporten-adapter__http__0</c>). Default: never.
    /// </summary>
    public Func<bool> DialogportenRunning { get; init; } = static () => false;
}
