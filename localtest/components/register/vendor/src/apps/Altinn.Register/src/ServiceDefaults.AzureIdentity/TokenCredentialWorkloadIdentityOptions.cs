namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Options for configuring workload identity credential support. Enabled by default.
/// </summary>
public sealed class TokenCredentialWorkloadIdentityOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether support for workload identity credentials should be enabled.
    /// </summary>
    public bool? Enable { get; set; }
}
