using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// A database record for a party.
/// </summary>
[PolymorphicFieldValueRecord(IsRoot = true)]
[PolymorphicDerivedType(typeof(PersonRecord), PartyRecordType.Person)]
[PolymorphicDerivedType(typeof(OrganizationRecord), PartyRecordType.Organization)]
[PolymorphicDerivedType(typeof(SelfIdentifiedUserRecord), PartyRecordType.SelfIdentifiedUser)]
[PolymorphicDerivedType(typeof(SystemUserRecord), PartyRecordType.SystemUser)]
[PolymorphicDerivedType(typeof(EnterpriseUserRecord), PartyRecordType.EnterpriseUser)]
public record PartyRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartyRecord"/> class.
    /// </summary>
    public PartyRecord(FieldValue<PartyRecordType> partyType)
    {
        PartyType = partyType;
    }

    /// <summary>
    /// Gets the UUID of the party.
    /// </summary>
    public required FieldValue<Guid> PartyUuid { get; init; }

    /// <summary>
    /// Gets the UUID of the owner party (if any).
    /// </summary>
    public required FieldValue<Guid> OwnerUuid { get; init; }

    /// <summary>
    /// Gets the ID of the party.
    /// </summary>
    public required FieldValue<uint> PartyId { get; init; }

    /// <summary>
    /// Gets the type of the party.
    /// </summary>
    [PolymorphicDiscriminatorProperty]
    public FieldValue<PartyRecordType> PartyType { get; private init; }

    /// <summary>
    /// Gets the external Uniform Resource Name (URN) associated with the party.
    /// </summary>
    public required FieldValue<PartyExternalRefUrn> ExternalUrn { get; init; }

    /// <summary>
    /// Gets the display-name of the party.
    /// </summary>
    public required FieldValue<string> DisplayName { get; init; }

    /// <summary>
    /// Gets the person identifier of the party, or <see langword="null"/> if the party is not a person.
    /// </summary>
    public required FieldValue<PersonIdentifier> PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the organization identifier of the party, or <see langword="null"/> if the party is not an organization.
    /// </summary>
    public required FieldValue<OrganizationIdentifier> OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets when the party was created in Altinn 3.
    /// </summary>
    public required FieldValue<DateTimeOffset> CreatedAt { get; init; }

    /// <summary>
    /// Gets when the party was last modified in Altinn 3.
    /// </summary>
    public required FieldValue<DateTimeOffset> ModifiedAt { get; init; }

    /// <summary>
    /// Gets the historical aggregate of user ids for the party.
    /// </summary>
    public required FieldValue<PartyHistoricalAggregate<uint>> UserIds { get; init; }

    /// <summary>
    /// Gets the historical aggregate of usernames for the party.
    /// </summary>
    public required FieldValue<PartyHistoricalAggregate<string>> Usernames { get; init; }

    /// <summary>
    /// Gets whether the party is deleted.
    /// </summary>
    public required FieldValue<bool> IsDeleted { get; init; }

    /// <summary>
    /// Gets when the party was deleted.
    /// </summary>
    public required FieldValue<DateTimeOffset> DeletedAt { get; init; }

    /// <summary>
    /// Gets the version ID of the party.
    /// </summary>
    public required FieldValue<ulong> VersionId { get; init; }

    /// <summary>
    /// Backwards-compatible for json deserialization only.
    /// </summary>
    /// <remarks>
    /// Added on 2026-06-08. Needs to be in prod for about 2 months before it can be removed.
    /// </remarks>
    [Obsolete("Use UserIds and Usernames instead.")]
    public FieldValue<PartyUserRecord> User
    {
        get => FieldValue.Unset;
        init
        {
            UserIds = value.SelectFieldValue(static u => u.UserIds).Select(static ids => PartyHistoricalAggregate<uint>.Create(ids, hasActiveValue: !ids.IsEmpty));
            Usernames = value.SelectFieldValue(static u => u.Username).Select(static name => PartyHistoricalAggregate<string>.Create([name], hasActiveValue: true));

            if (UserIds.IsNull)
            {
                UserIds = PartyHistoricalAggregate<uint>.Empty;
            }

            if (Usernames.IsNull)
            {
                Usernames = PartyHistoricalAggregate<string>.Empty;
            }
        }
    }
}
