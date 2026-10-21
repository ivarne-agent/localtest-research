using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.PEP.Constants;
using Altinn.Common.PEP.Helpers;
using Altinn.Correspondence.Common.Constants;
using Altinn.Correspondence.Common.Helpers;

namespace Altinn.Correspondence.Integrations.Altinn.Authorization;

/// <summary>
/// Factory for building XACML JSON profile categories used in authorization requests.
/// </summary>
public static class XacmlRequestFactory
{
    private const string DefaultType = "string";

    public static XacmlJsonCategory CreateResourceCategory(string resourceId, string party, string? instanceId, string issuer)
    {
        XacmlJsonCategory resourceCategory = new() { Attribute = new List<XacmlJsonAttribute>() };
        resourceCategory.Attribute.Add(DecisionHelper.CreateXacmlJsonAttribute(AltinnXacmlUrns.ResourceId, resourceId, DefaultType, issuer));
        var partyWithoutPrefix = party.WithoutPrefix();
        if (party.StartsWith(AltinnXacmlUrns.OrganizationNumber) || partyWithoutPrefix.IsOrganizationNumber())
        {
            resourceCategory.Attribute.Add(
                DecisionHelper.CreateXacmlJsonAttribute(UrnConstants.OrganizationNumberAttribute, partyWithoutPrefix, DefaultType, issuer));
        }
        else if (party.StartsWith(AltinnXacmlUrns.PersonId) || partyWithoutPrefix.IsSocialSecurityNumber())
        {
            resourceCategory.Attribute.Add(
                DecisionHelper.CreateXacmlJsonAttribute(UrnConstants.PersonIdAttribute, partyWithoutPrefix, DefaultType, issuer));
        }
#if !LOCALTEST
        else if (party.StartsWith(AltinnXacmlUrns.PartyId) || partyWithoutPrefix.IsPartyId())
#else
        // localtest: the test data's party ids have 6 digits; production accepts only 8 or 10 (IsPartyId), so any positive 64-bit integer passes here.
        else if (party.StartsWith(AltinnXacmlUrns.PartyId) || (long.TryParse(partyWithoutPrefix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var localtestPartyId) && localtestPartyId > 0))
#endif
        {
            resourceCategory.Attribute.Add(
                DecisionHelper.CreateXacmlJsonAttribute(UrnConstants.Party, partyWithoutPrefix, DefaultType, issuer));
        }
        else
        {
            throw new InvalidOperationException("RecipientId is not a valid organization number, person number or party id: " + party);
        }

        if (instanceId is not null)
        {
            resourceCategory.Attribute.Add(
                DecisionHelper.CreateXacmlJsonAttribute(AltinnXacmlUrns.ResourceInstance, instanceId, DefaultType, issuer));
        }

        return resourceCategory;
    }
}
