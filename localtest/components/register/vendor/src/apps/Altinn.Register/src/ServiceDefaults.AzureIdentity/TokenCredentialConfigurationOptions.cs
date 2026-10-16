using Azure.Identity;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Options for configuring the default <see cref="ITokenCredentialProvider"/>.
/// </summary>
public sealed class TokenCredentialConfigurationOptions
{
    /// <summary>
    /// Options for configuring <see cref="EnvironmentCredential"/>.
    /// </summary>
    public TokenCredentialEnvironmentOptions Environment { get; } = new();

    /// <summary>
    /// Options for configuring <see cref="WorkloadIdentityCredential"/>.
    /// </summary>
    public TokenCredentialWorkloadIdentityOptions WorkloadIdentity { get; } = new();

    /// <summary>
    /// Options for configuring <see cref="ManagedIdentityCredential"/>.
    /// </summary>
    public TokenCredentialManagedIdentityOptions ManagedIdentity { get; } = new();
}
