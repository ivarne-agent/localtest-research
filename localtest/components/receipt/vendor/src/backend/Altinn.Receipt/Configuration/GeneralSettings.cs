namespace Altinn.Platform.Receipt.Configuration;

/// <summary>
/// Settings for accessing bridge functionality
/// </summary>
public class GeneralSettings
{
    /// <summary>
    /// Open Id Connect Well known endpoint
    /// </summary>
    public string OpenIdWellKnownEndpoint { get; set; }

    /// <summary>
    /// Hostname
    /// </summary>
    public string Hostname { get; set; }

    /// <summary>
    /// Name of the cookie for runtime
    /// </summary>
    public string RuntimeCookieName { get; set; }

    /// <summary>
    /// The attachment groups to hide
    /// </summary>
    public string AttachmentGroupsToHide { get; set; }
}
