using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;
using CommunityToolkit.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Default implementation of <see cref="TokenCredentialBuilder"/>.
/// </summary>
internal sealed class DefaultTokenCredentialBuilder
    : TokenCredentialBuilder
{
    /// <inheritdoc/>
    [DisallowNull]
    public override string? Name
    {
        get => field;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <inheritdoc/>
    public override IList<TokenCredential> Credentials { get; } = [];

    /// <inheritdoc/>
    public override TokenCredential Build()
    {
        return Credentials switch
        {
            [] => ThrowHelper.ThrowInvalidOperationException<TokenCredential>("At least one credential must be configured."),
            [var single] => single,
            _ => new ChainedTokenCredential(Credentials.ToArray()),
        };
    }
}
