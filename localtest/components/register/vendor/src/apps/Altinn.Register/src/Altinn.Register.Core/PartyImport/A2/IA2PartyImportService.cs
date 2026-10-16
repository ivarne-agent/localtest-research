using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Service for importing parties from Altinn 2.
/// </summary>
public interface IA2PartyImportService
{
    /// <summary>
    /// Gets the changes that have occurred in parties since the given change id.
    /// </summary>
    /// <param name="fromExclusive">The previously imported change id. Defaults to <c>0</c>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="A2PartyChangePage"/>s.</returns>
    IAsyncEnumerable<A2PartyChangePage> GetChanges(uint fromExclusive = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the changes that have occurred in user profiles since the given change id.
    /// </summary>
    /// <param name="fromExclusive">The previously imported change id. Defaults to <c>0</c>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="A2UserProfileChangePage"/>s.</returns>
    IAsyncEnumerable<A2UserProfileChangePage> GetUserProfileChanges(uint fromExclusive = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all external role assignments from the party with the given id.
    /// </summary>
    /// <param name="fromPartyId">The party id of providing party.</param>
    /// <param name="fromPartyUuid">The party uuid of the providing party.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A read only list of all role assignments from the given party.</returns>
    IAsyncEnumerable<A2PartyExternalRoleAssignment> GetExternalRoleAssignmentsFrom(uint fromPartyId, Guid fromPartyUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a party by its UUID.
    /// </summary>
    /// <param name="partyUuid">The party UUID.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="PartyRecord"/>.</returns>
    Task<Result<PartyRecord>> GetParty(Guid partyUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user information for a person.
    /// </summary>
    /// <param name="partyUuid">The party UUID of the person.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>User information for the party.</returns>
    Task<Result<A2ProfileRecord>> GetOrCreatePersonUser(Guid partyUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user information for a party.
    /// </summary>
    /// <param name="partyUuid">The party UUID.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>User information for the party.</returns>
    Task<Result<A2ProfileRecord>> GetPartyUser(Guid partyUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets profile information based on user id.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>User profile.</returns>
    Task<Result<A2ProfileRecord>> GetProfile(ulong userId, CancellationToken cancellationToken = default);
}
