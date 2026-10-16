using Altinn.Register.Contracts.V1;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Service for interfacing with party entities.
/// </summary>
public interface IV1PartyService
{
    /// <summary>
    /// Get a party by party id.
    /// </summary>
    /// <param name="partyId">The party id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="V1Models.Party"/> if it was found, otherwise <see langword="null"/>.</returns>
    Task<V1Models.Party?> GetPartyById(int partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a party by party uuid.
    /// </summary>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="V1Models.Party"/> if it was found, otherwise <see langword="null"/>.</returns>
    Task<V1Models.Party?> GetPartyById(Guid partyUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lookup a party by social security number or organization number.
    /// </summary>
    /// <param name="lookupValue">The ssn/org.nr.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="V1Models.Party"/> if it was found, otherwise <see langword="null"/>.</returns>
    Task<V1Models.Party?> LookupPartyBySSNOrOrgNo(string lookupValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lookup parties by social security number or organization number.
    /// </summary>
    /// <param name="lookupValues">The set of ssn/org.nr.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="V1Models.Party"/>.</returns>
    IAsyncEnumerable<V1Models.Party> LookupPartiesBySSNOrOrgNos(IEnumerable<string> lookupValues, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lookup party names by social security number or organization number.
    /// </summary>
    /// <param name="lookupValues">The set of ssn/org.nr.</param>
    /// <param name="partyComponentOption">Specifies the components that should be included when retrieving party's information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="PartyName"/>.</returns>
    IAsyncEnumerable<PartyName> LookupPartyNames(IEnumerable<PartyLookup> lookupValues, PartyComponentOptions partyComponentOption, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get parties by party ids.
    /// </summary>
    /// <param name="partyIds">The party ids.</param>
    /// <param name="fetchSubUnits">Flag indicating whether subunits should be included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="V1Models.Party"/>.</returns>
    IAsyncEnumerable<V1Models.Party> GetPartiesById(IEnumerable<int> partyIds, bool fetchSubUnits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get parties by party ids.
    /// </summary>
    /// <param name="partyIds">The party ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="V1Models.Party"/>.</returns>
    IAsyncEnumerable<V1Models.Party> GetPartiesById(IEnumerable<int> partyIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get parties by party ids.
    /// </summary>
    /// <param name="partyIds">The party ids.</param>
    /// <param name="fetchSubUnits">Flag indicating whether subunits should be included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="V1Models.Party"/>.</returns>
    IAsyncEnumerable<V1Models.Party> GetPartiesById(IEnumerable<Guid> partyIds, bool fetchSubUnits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get parties by party ids.
    /// </summary>
    /// <param name="partyIds">The party ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of <see cref="V1Models.Party"/>.</returns>
    IAsyncEnumerable<V1Models.Party> GetPartiesById(IEnumerable<Guid> partyIds, CancellationToken cancellationToken = default);
}
