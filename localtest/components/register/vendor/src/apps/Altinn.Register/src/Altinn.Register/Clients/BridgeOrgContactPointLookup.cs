using System.Text.Json.Serialization;

namespace Altinn.Register.Models;

/// <summary>
/// A class describing the query model for contact points for organizations
/// </summary>
public class BridgeOrgContactPointLookup
{
    /// <summary>
    /// Gets or sets the list of organization numbers to lookup contact points for
    /// </summary>
    [JsonPropertyName("organisationNumbers")]
    public List<string> OrganisationNumbers { get; set; } = [];
}
