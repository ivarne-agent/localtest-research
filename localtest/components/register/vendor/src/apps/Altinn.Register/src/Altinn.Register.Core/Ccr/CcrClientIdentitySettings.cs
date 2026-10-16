using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Options for a CCR client, used for validating incoming CCR update requests.
/// </summary>
public sealed class CcrClientIdentitySettings
{
    /// <summary>
    /// The password hash for the client. Generated using <see cref="Cryptography.PasswordHash"/>.
    /// </summary>
    [Required]
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Allowed source networks for the client. If empty, requests from any network are rejected.
    /// </summary>
    [MinLength(1, ErrorMessage = "At least one allowed source network must be specified.")]
    public List<IPNetwork> AllowedSourceNetworks { get; set; } = [];

    /// <summary>
    /// Whether updates from this client should be federated to other Altinn instances. If <see langword="null"/>, the default behavior is to federate.
    /// </summary>
    public bool? Federate { get; set; }
}
