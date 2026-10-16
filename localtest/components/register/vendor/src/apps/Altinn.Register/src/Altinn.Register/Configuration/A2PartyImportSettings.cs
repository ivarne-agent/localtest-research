using System.ComponentModel.DataAnnotations;

namespace Altinn.Register.Configuration;

/// <summary>
/// Settings for A2 party import.
/// </summary>
public class A2PartyImportSettings
{
    /// <summary>
    /// Gets or sets the (root) bridge api endpoint for A2.
    /// </summary>
    [Required]
    public Uri? BridgeApiEndpoint { get; set; }
}
