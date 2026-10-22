namespace Altinn.App.Aspire;

/// <summary>Options of <see cref="AltinnAppExtensions.AddAltinnApp{TProject}" />.</summary>
public sealed class AltinnAppOptions
{
    /// <summary>
    /// Whether the app exports OpenTelemetry (<c>AppSettings__UseOpenTelemetry=true</c>), so its traces, metrics and
    /// logs reach the dashboard through the OTLP endpoint Aspire gives every project. Default true.
    /// </summary>
    public bool OpenTelemetry { get; set; } = true;

    /// <summary>
    /// Whether the app is provisioned on the platform (test users, metadata, texts, policy) every time it is ready.
    /// Default true; the <c>provision</c> command runs it on demand either way.
    /// </summary>
    public bool ProvisionWhenReady { get; set; } = true;
}
