namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Options for configuring the behavior of a <see cref="ITokenCredentialProvider"/> implementation.
/// </summary>
public sealed class TokenCredentialOptions
{
    /// <summary>
    /// Gets a list of operations used to configure an <see cref="TokenCredentialBuilder"/>.
    /// </summary>
    public IList<Action<TokenCredentialBuilder>> TokenCredentialBuilderActions { get; } = [];
}
