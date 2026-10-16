using Altinn.Urn;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents an external Uniform Resource Name (URN) reference to a party, such as a person, organization, or system
/// user, in the Altinn platform.
/// </summary>
[KeyValueUrn]
public abstract partial record PartyExternalRefUrn
{
    /// <summary>
    /// Try to get the urn as a person identifier.
    /// </summary>
    /// <param name="personIdentifier">The resulting person identifier.</param>
    /// <returns><see langword="true"/> if this party reference is an person identifier, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:person:identifier-no")]
    public partial bool IsPersonId(out PersonIdentifier personIdentifier);

    /// <summary>
    /// Try to get the urn as an organization number.
    /// </summary>
    /// <param name="organizationIdentifier">The resulting organization identifier.</param>
    /// <returns><see langword="true"/> if this party reference is an organization identifier, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:organization:identifier-no")]
    public partial bool IsOrganizationId(out OrganizationIdentifier organizationIdentifier);

    /// <summary>
    /// Try to get the urn as a username.
    /// </summary>
    /// <param name="username">The resulting username.</param>
    /// <returns><see langword="true"/> if this party reference is a username, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:person:legacy-selfidentified")]
    public partial bool IsLegacySelfIdentifiedUsername(out UrnEncoded username);

    /// <summary>
    /// Try to get the urn as a party uuid.
    /// </summary>
    /// <param name="systemUserUuid">The resulting party uuid.</param>
    /// <returns><see langword="true"/> if this party reference is a party uuid, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:systemuser:uuid")]
    public partial bool IsSystemUserUuid(out Guid systemUserUuid);

    /// <summary>
    /// Try to get the urn as an ID-Porten email.
    /// </summary>
    /// <param name="email">The resulting email.</param>
    /// <returns><see langword="true"/> if this party reference is a ID-Porten email, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:person:idporten-email")]
    public partial bool IsIDPortenEmail(out UrnEncoded email);
}
