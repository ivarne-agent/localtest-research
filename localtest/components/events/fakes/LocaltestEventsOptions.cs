#nullable enable

namespace Altinn.Platform.Events.Localtest;

/// <summary>Settings for the localtest Events fakes, bound from <c>Localtest:Events</c>.</summary>
public sealed class LocaltestEventsOptions
{
    /// <summary>The platform origin; its host and subdomains count as inside the stack.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>Which webhook POSTs leave the container. See <see cref="WebhookDelivery"/>.</summary>
    public WebhookDelivery Delivery { get; set; } = WebhookDelivery.LocalOnly;

    /// <summary>Empty every eventsdb data table when the component starts (<c>fakes/Startup/reset-on-start.sql</c>).</summary>
    public bool ResetOnStart { get; set; } = true;
}

public enum WebhookDelivery
{
    /// <summary>
    /// Deliver to the platform host, its subdomains, single-label hosts (compose services, <c>localhost</c>, which is the
    /// events container itself) and <c>host.docker.internal</c> (the developer's machine, for a receiver run there);
    /// capture every other endpoint with a synthetic <c>200</c>.
    /// </summary>
    LocalOnly,

    /// <summary>Capture every webhook POST with a synthetic <c>200</c>; nothing is sent.</summary>
    CaptureAll,

    /// <summary>Send every webhook POST, as production does. Still recorded.</summary>
    All,
}
