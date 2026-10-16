using System.Text;
using System.Text.Json;
using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a party in Altinn Register.
/// </summary>
[PolymorphicFieldValueRecord(IsRoot = true)]
[PolymorphicDerivedType(typeof(Person), PartyType.Person)]
[PolymorphicDerivedType(typeof(Organization), PartyType.Organization)]
[PolymorphicDerivedType(typeof(SelfIdentifiedUser), PartyType.SelfIdentifiedUser)]
[PolymorphicDerivedType(typeof(SystemUser), PartyType.SystemUser)]
[PolymorphicDerivedType(typeof(EnterpriseUser), PartyType.EnterpriseUser)]
public record Party
    : IHasExtensionData
{
    // CS0649: this field is populated by serializers via [JsonExtensionData], not by direct assignments in code.
#pragma warning disable CS0649
    [JsonExtensionData]
    private readonly JsonExtensionData _extensionData;
#pragma warning restore CS0649

    private readonly Guid _uuid;
    private readonly PartyUrn.PartyUuid _urn = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="Party"/> class.
    /// </summary>
    /// <param name="partyType">The type of the party.</param>
    public Party(NonExhaustiveEnum<PartyType> partyType)
    {
        Type = partyType;
    }

    /// <summary>
    /// Gets the UUID of the party.
    /// </summary>
    [JsonPropertyName("partyUuid")]
    public required Guid Uuid
    {
        get => _uuid;
        init
        {
            Guard.IsNotDefault(value);
            _uuid = value;
            _urn = PartyUrn.PartyUuid.Create(value);
        }
    }

    /// <summary>
    /// Gets the version ID of the party.
    /// </summary>
    [JsonPropertyName("versionId")]
    public required ulong VersionId { get; init; }

    /// <summary>
    /// Gets the canonical URN of the party.
    /// </summary>
    [JsonPropertyName("urn")]
    public PartyUrn.PartyUuid Urn
        => _urn;

    /// <summary>
    /// Gets the external reference of the party.
    /// </summary>
    [JsonPropertyName("externalUrn")]
    public FieldValue<NonExhaustive<PartyExternalRefUrn>> ExternalUrn { get; init; }

    /// <summary>
    /// Gets the ID of the party.
    /// </summary>
    [JsonPropertyName("partyId")]
    public required FieldValue<uint> PartyId { get; init; }

    /// <summary>
    /// Gets the type of the party.
    /// </summary>
    [JsonPropertyName("partyType")]
    [PolymorphicDiscriminatorProperty]
    public NonExhaustiveEnum<PartyType> Type { get; }

    /// <summary>
    /// Gets the display-name of the party.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required FieldValue<string> DisplayName { get; init; }

    /// <summary>
    /// Gets when the party was created in Altinn 3.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public required FieldValue<DateTimeOffset> CreatedAt { get; init; }

    /// <summary>
    /// Gets when the party was last modified in Altinn 3.
    /// </summary>
    [JsonPropertyName("modifiedAt")]
    public required FieldValue<DateTimeOffset> ModifiedAt { get; init; }

    /// <summary>
    /// Gets whether the party is deleted.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public required FieldValue<bool> IsDeleted { get; init; }

    /// <summary>
    /// Gets when the party was deleted.
    /// </summary>
    /// <remarks>
    /// This will only have a value if the party is actually deleted (see <see cref="IsDeleted"/>).
    /// Though, if <see cref="IsDeleted"/> is not requested, it may still be unset.
    /// </remarks>
    [JsonPropertyName("deletedAt")]
    public required FieldValue<DateTimeOffset> DeletedAt { get; init; }

    /// <summary>
    /// Gets user information for the party.
    /// </summary>
    [JsonPropertyName("user")]
    public required FieldValue<PartyUser> User { get; init; }

    /// <inheritdoc/>
    JsonElement IHasExtensionData.JsonExtensionData
        => _extensionData;

    /// <summary>
    /// Prints the members of the party to a <see cref="StringBuilder"/>.
    /// </summary>
    /// <param name="builder">The string builder.</param>
    /// <returns><see langword="true"/>.</returns>
    /// <remarks>This implements the record <see cref="ToString"/> protocol.</remarks>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        if (GetType() == typeof(Party))
        {
            // only print the party-type if this is the base class
            builder.Append("PartyType = ").Append(Type);
        }

        builder.Append(", Uuid = ").Append(Uuid);
        builder.Append(", VersionId = ").Append(VersionId);
        builder.Append(", PartyId = ").Append(PartyId);
        builder.Append(", DisplayName = ").Append(DisplayName);
        builder.Append(", CreatedAt = ").Append(CreatedAt);
        builder.Append(", ModifiedAt = ").Append(ModifiedAt);
        builder.Append(", IsDeleted = ").Append(IsDeleted);
        builder.Append(", DeletedAt = ").Append(DeletedAt);
        builder.Append(", User = ").Append(User);

        IHasExtensionData ext = this;
        if (ext.HasJsonExtensionData)
        {
            builder.Append(", [has extension data]");
        }

        return true;
    }
}
