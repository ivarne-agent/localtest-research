using System.Diagnostics.CodeAnalysis;
using Azure.Core;
using Azure.Identity;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Base class for builders that construct <see cref="TokenCredential"/> instances for use in the <see cref="ITokenCredentialProvider"/> infrastructure.
/// </summary>
public abstract class TokenCredentialBuilder
{
    /// <summary>
    /// Gets or sets the name of the <see cref="TokenCredential"/> being created.
    /// </summary>
    /// <remarks>
    /// The <see cref="Name"/> is set by the <see cref="ITokenCredentialProvider"/> infrastructure
    /// and is public for unit testing purposes only. Setting the <see cref="Name"/> outside of
    /// testing scenarios may have unpredictable results.
    /// </remarks>
    [DisallowNull]
    public abstract string? Name { get; set; }

    /// <summary>
    /// Gets a list of <see cref="TokenCredential"/> instances used to configure an
    /// <see cref="ChainedTokenCredential"/> pipeline.
    /// </summary>
    public abstract IList<TokenCredential> Credentials { get; }

    /// <summary>
    /// Creates a <see cref="TokenCredential"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="TokenCredential"/> built from the <see cref="Credentials"/>.
    /// </returns>
    /// <remarks>
    /// The default implementation of <see cref="Build"/> only creates a
    /// <see cref="ChainedTokenCredential"/> if there are multiple credentials
    /// in the <see cref="Credentials"/> list. If there is only one credential,
    /// it is returned directly.
    /// </remarks>
    public abstract TokenCredential Build();
}
