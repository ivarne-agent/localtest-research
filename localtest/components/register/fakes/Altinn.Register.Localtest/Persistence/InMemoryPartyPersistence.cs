using System.Runtime.CompilerServices;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// Read-only <see cref="IPartyPersistence"/> over the <see cref="RegisterStore"/>.
/// Writes (imports, self-identified users, set-username) are not supported: edit <c>testData.json</c> instead.
/// </summary>
public sealed class InMemoryPartyPersistence(RegisterStore store)
    : IPartyPersistence
{
    private const string ReadOnlyMessage = "The localtest register is read-only; change testData.json and restart.";

    public IAsyncEnumerable<PartyRecord> GetPartyById(Guid partyUuid, PartyFieldIncludes include = PartyFieldIncludes.Party, CancellationToken cancellationToken = default)
        => Single(store.ByUuid.GetValueOrDefault(partyUuid), include);

    public IAsyncEnumerable<PartyRecord> GetPartyById(uint partyId, PartyFieldIncludes include = PartyFieldIncludes.Party, CancellationToken cancellationToken = default)
        => Single(store.ByPartyId.GetValueOrDefault(partyId), include);

    public IAsyncEnumerable<OrganizationRecord> GetOrganizationByIdentifier(OrganizationIdentifier identifier, PartyFieldIncludes include = PartyFieldIncludes.Party, CancellationToken cancellationToken = default)
        => Single(store.ByOrganizationIdentifier.GetValueOrDefault(identifier.ToString()), include).OfType<OrganizationRecord>();

    public IAsyncEnumerable<PersonRecord> GetPersonByIdentifier(PersonIdentifier identifier, PartyFieldIncludes include = PartyFieldIncludes.Party, CancellationToken cancellationToken = default)
        => Single(store.ByPersonIdentifier.GetValueOrDefault(identifier.ToString()), include).OfType<PersonRecord>();

    public IAsyncEnumerable<PartyRecord> GetPartyByUserId(uint userId, PartyFieldIncludes include = PartyFieldIncludes.Party, CancellationToken cancellationToken = default)
        => Single(store.ByUserId.GetValueOrDefault(userId), include);

    public async IAsyncEnumerable<PartyRecord> LookupParties(
        IReadOnlyList<Guid>? partyUuids = null,
        IReadOnlyList<uint>? partyIds = null,
        IReadOnlyList<PartyExternalRefUrn>? externalUrns = null,
        IReadOnlyList<OrganizationIdentifier>? organizationIdentifiers = null,
        IReadOnlyList<PersonIdentifier>? personIdentifiers = null,
        IReadOnlyList<uint>? userIds = null,
        IReadOnlyList<string>? usernames = null,
        IReadOnlyList<string>? selfIdentifiedEmails = null,
        PartyFieldIncludes include = PartyFieldIncludes.Party,
        PartyListTransforms transforms = PartyListTransforms.None,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var matched = new HashSet<Guid>();

        void Add(PartyRecord? record)
        {
            if (record is not null)
            {
                matched.Add(record.PartyUuid.Value);
            }
        }

        foreach (var uuid in partyUuids ?? [])
        {
            Add(store.ByUuid.GetValueOrDefault(uuid));
        }

        foreach (var id in partyIds ?? [])
        {
            Add(store.ByPartyId.GetValueOrDefault(id));
        }

        foreach (var urn in externalUrns ?? [])
        {
            Add(store.Parties.FirstOrDefault(p => p.ExternalUrn.HasValue && p.ExternalUrn.Value == urn));
        }

        foreach (var orgId in organizationIdentifiers ?? [])
        {
            Add(store.ByOrganizationIdentifier.GetValueOrDefault(orgId.ToString()));
        }

        foreach (var personId in personIdentifiers ?? [])
        {
            Add(store.ByPersonIdentifier.GetValueOrDefault(personId.ToString()));
        }

        foreach (var userId in userIds ?? [])
        {
            Add(store.ByUserId.GetValueOrDefault(userId));
        }

        foreach (var username in usernames ?? [])
        {
            Add(store.Parties.FirstOrDefault(p => p.Usernames.HasValue && p.Usernames.Value.HasCurrentValue
                && string.Equals(p.Usernames.Value.CurrentValue.Value, username, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var email in selfIdentifiedEmails ?? [])
        {
            Add(store.Parties.FirstOrDefault(p => p is SelfIdentifiedUserRecord si && si.Email.HasValue
                && string.Equals(si.Email.Value, email, StringComparison.OrdinalIgnoreCase)));
        }

        IEnumerable<PartyRecord> results = store.Parties.Where(p => matched.Contains(p.PartyUuid.Value));

        if (transforms.HasFlag(PartyListTransforms.ReplaceWithMainUnits))
        {
            // Production replaces every matched party with its main unit(s); parties without one are dropped.
            results = results.SelectMany(p => store.MainUnitsOf(p.PartyUuid.Value)).Distinct().OrderBy(p => p.VersionId.Value);
        }

        foreach (var party in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return FieldMask.Mask(party, include);

            if (transforms.HasFlag(PartyListTransforms.IncludeSubUnits))
            {
                foreach (var child in store.SubUnitsOf(party.PartyUuid.Value))
                {
                    if (FieldMask.Mask(child, include) is OrganizationRecord childOrg)
                    {
                        yield return childOrg with { ParentOrganizationUuid = party.PartyUuid.Value };
                    }
                }
            }
        }
    }

    public IAsyncEnumerable<PartyRecord> GetPartyStream(ulong fromExclusive, ushort limit, PartyFieldIncludes include = PartyFieldIncludes.Party, IReadOnlySet<PartyRecordType>? filterByPartyType = null, CancellationToken cancellationToken = default)
        => store.Parties
            .Where(p => p.VersionId.Value > fromExclusive)
            .Where(p => filterByPartyType is null || filterByPartyType.Contains(p.PartyType.Value))
            .Take(limit)
            .Select(p => FieldMask.Mask(p, include))
            .ToAsyncEnumerable();

    public IAsyncEnumerable<ExternalRoleAssignmentEvent> GetExternalRoleAssignmentStream(ulong fromExclusive, ushort limit, CancellationToken cancellationToken = default)
        => store.RoleEvents.Where(e => e.VersionId > fromExclusive).Take(limit).ToAsyncEnumerable();

    public Task<ulong> GetMaxPartyVersionId(CancellationToken cancellationToken)
        => Task.FromResult(store.MaxPartyVersionId);

    public Task<ulong> GetMaxExternalRoleAssignmentVersionId(CancellationToken cancellationToken)
        => Task.FromResult((ulong)store.RoleEvents.Length);

    public Task<Result<PartyRecord>> UpsertParty(PartyRecord party, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(ReadOnlyMessage);

    public IAsyncEnumerable<Result<PartyRecord>> UpsertParties(IAsyncEnumerable<PartyRecord> parties, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(ReadOnlyMessage);

    public Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEmailUser(string email, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(ReadOnlyMessage);

    public Task<Result<NewOrExisting<SelfIdentifiedUserRecord>>> GetOrCreateSelfIdentifiedEduUser(string extRef, string username, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(ReadOnlyMessage);

    private static IAsyncEnumerable<PartyRecord> Single(PartyRecord? record, PartyFieldIncludes include)
        => record is null
            ? AsyncEnumerable.Empty<PartyRecord>()
            : AsyncEnumerable.Repeat(FieldMask.Mask(record, include), 1);
}
