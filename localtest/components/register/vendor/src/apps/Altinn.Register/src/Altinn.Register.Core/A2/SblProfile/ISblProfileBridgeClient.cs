using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.A2.SblProfile;

/// <summary>
/// Thin client over the SBL Bridge profile API.
/// </summary>
/// <remarks>
/// Used by the iteration-1 proxy implementation of the register users endpoint. Will be removed
/// when the endpoint switches to writing directly to the register database (iteration 2).
/// </remarks>
public interface ISblProfileBridgeClient
{
    /// <summary>
    /// Looks up an existing user profile by SBL "external identity" string
    /// (either an SSN or an issuer-prefixed external identity such as
    /// <c>urn:altinn:person:idporten-email:&lt;encoded-email&gt;</c>).
    /// </summary>
    /// <param name="externalIdentity">The bridge-shaped external identity string.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A lookup result. <see cref="SblUserLookup.Found"/> indicates whether the user existed.
    /// A problem result indicates the bridge call failed.
    /// </returns>
    Task<Result<SblUserLookup>> LookupUser(string externalIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user profile via the SBL Bridge.
    /// </summary>
    /// <param name="user">The user profile to create.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The created user profile, or a problem if creation failed.</returns>
    Task<Result<SblUserProfile>> CreateUser(SblUserProfile user, CancellationToken cancellationToken = default);
}
