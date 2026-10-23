#nullable enable

namespace Altinn.Notifications.Localtest;

/// <summary>Settings for the localtest Notifications fakes, bound from <c>Localtest:Notifications</c>.</summary>
public sealed class LocaltestNotificationsOptions
{
    /// <summary>The platform origin; the host's own links and the OpenID metadata are derived from it (the services are called by name).</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>
    /// Empty the notificationsdb data tables and the capture when the component starts
    /// (<c>fakes/Startup/reset-on-start.sql</c>). Off by default: the data persists like Storage's.
    /// </summary>
    public bool ResetOnStart { get; set; }

    /// <summary>Pause, in milliseconds, between the notification leaving <c>New</c> (the claim committed) and the send result.</summary>
    public int ResultDelayMs { get; set; } = 100;

    /// <summary>Pause, in milliseconds, between the send result (<c>Succeeded</c> or <c>Accepted</c>) and the delivery report.</summary>
    public int DeliveryReportDelayMs { get; set; } = 500;

    /// <summary>How long the responder waits for a captured notification's claim to commit before it applies the result anyway.</summary>
    public int ClaimCommitTimeoutSeconds { get; set; } = 30;
}
