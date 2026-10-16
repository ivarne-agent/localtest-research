using System.Globalization;
using Altinn.Urn;

namespace Altinn.Register.Contracts;

/// <summary>
/// A unique reference to a party in the form of an URN.
/// </summary>
[KeyValueUrn]
public abstract partial record OrganizationUrn
{
    /// <summary>
    /// Try to get the urn as a party id.
    /// </summary>
    /// <param name="partyId">The resulting party id.</param>
    /// <returns><see langword="true"/> if this party reference is a party id, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:party:id", Canonical = true)]
    [UrnKey("altinn:partyid")]
    public partial bool IsPartyId(out uint partyId);

    /// <summary>
    /// Try to get the urn as a party uuid.
    /// </summary>
    /// <param name="partyUuid">The resulting party uuid.</param>
    /// <returns><see langword="true"/> if this party reference is a party uuid, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:party:uuid", Canonical = true)]
    [UrnKey("altinn:organization:uuid")]
    public partial bool IsPartyUuid(out Guid partyUuid);

    /// <summary>
    /// Try to get the urn as an organization number.
    /// </summary>
    /// <param name="organizationIdentifier">The resulting organization identifier.</param>
    /// <returns><see langword="true"/> if this party reference is an organization identifier, otherwise <see langword="false"/>.</returns>
    [UrnKey("altinn:organization:identifier-no", Canonical = true)]
    public partial bool IsOrganizationId(out OrganizationIdentifier organizationIdentifier);

    // Manually overridden to disallow negative party ids
    private static bool TryParsePartyId(ReadOnlySpan<char> segment, IFormatProvider? provider, out uint value)
        => uint.TryParse(segment, NumberStyles.None, provider, out value);
}
