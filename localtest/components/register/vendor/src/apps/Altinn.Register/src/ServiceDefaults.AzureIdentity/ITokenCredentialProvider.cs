using Azure.Core;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Abstraction for providing <see cref="TokenCredential"/> instances based on named configuration.
/// </summary>
public interface ITokenCredentialProvider
{
    /// <summary>
    /// Constructs a <see cref="TokenCredential"/> instance using the configuration that corresponds
    /// to the logical name specified by <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The logical name of the token credential to use.</param>
    /// <returns>A <see cref="TokenCredential"/> instance.</returns>
    public TokenCredential GetCredential(string name);
}
