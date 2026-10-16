namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Options for configuring managed identity credential support. Enabled by default.
/// </summary>
public sealed class TokenCredentialManagedIdentityOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether support for managed identity credentials should be enabled.
    /// </summary>
    public bool? Enable { get; set; }
}
