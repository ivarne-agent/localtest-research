namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Options for configuring environment credential support. Disabled by default.
/// </summary>
public sealed class TokenCredentialEnvironmentOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether support for environment credentials should be enabled.
    /// </summary>
    public bool? Enable { get; set; }
}
