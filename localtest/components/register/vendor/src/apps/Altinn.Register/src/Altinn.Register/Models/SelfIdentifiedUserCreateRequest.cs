using System.Text.Json.Serialization;
using Altinn.Register.Contracts;

namespace Altinn.Register.Models;

/// <summary>
/// Request body for the get-or-create self-identified user endpoint.
/// </summary>
/// <remarks>
/// The caller (altinn-authentication) owns identity construction: it builds the bridge-shape
/// <see cref="ExternalIdentity"/> (e.g. <c>uidp-anonym:&lt;hex&gt;</c> for OIDC, or
/// <c>urn:altinn:person:idporten-email:&lt;base64-email&gt;</c> for idporten-email) and the
/// <see cref="UserName"/>, then sends them verbatim to this endpoint. Register does not
/// rebuild or validate the shape of either field beyond non-empty.
/// </remarks>
public sealed class SelfIdentifiedUserCreateRequest
{
    /// <summary>
    /// Gets or sets the type of self-identified user to create.
    /// </summary>
    [JsonPropertyName("selfIdentifiedUserType")]
    [JsonRequired]
    public required SelfIdentifiedUserType SelfIdentifiedUserType { get; set; }

    /// <summary>
    /// Gets or sets the bridge-shape external identity (pre-built by the caller). Required.
    /// </summary>
    [JsonPropertyName("externalIdentity")]
    public string? ExternalIdentity { get; set; }

    /// <summary>
    /// Gets or sets the username to assign on create. Required. The caller (altinn-authentication)
    /// owns username generation; register does not transform it.
    /// </summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the email of the user. Required for
    /// <see cref="Contracts.SelfIdentifiedUserType.IdPortenEmail"/>; ignored otherwise.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
