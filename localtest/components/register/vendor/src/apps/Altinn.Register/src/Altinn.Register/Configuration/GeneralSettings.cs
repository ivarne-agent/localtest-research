namespace Altinn.Register.Configuration;

/// <summary>
/// General configuration settings
/// </summary>
public class GeneralSettings
{
    /// <summary>
    /// Gets or sets the bridge api endpoint
    /// </summary>
    public string BridgeApiEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Open Id Connect Well known endpoint
    /// </summary>
    public string OpenIdWellKnownEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Name of the cookie for where JWT is stored
    /// </summary>
    public string JwtCookieName { get; set; } = string.Empty;
}
