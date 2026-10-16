using System.Text.Json.Serialization;

namespace Altinn.Register.Core.A2.SblProfile;

/// <summary>
/// Wire model matching the SBL Bridge profile API <c>UserProfile</c> payload.
/// </summary>
/// <remarks>
/// Mirrors a minimal subset of the bridge contract — only fields the proxy round-trip needs.
/// Removed once iteration 2 stops calling SBL.
/// </remarks>
public sealed class SblUserProfile
{
    /// <summary>Gets or sets the legacy numeric user id.</summary>
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>Gets or sets the user uuid.</summary>
    [JsonPropertyName("userUuid")]
    public Guid? UserUuid { get; set; }

    /// <summary>Gets or sets the username.</summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>Gets or sets the external identity string (SSN or iss:sub).</summary>
    [JsonPropertyName("externalIdentity")]
    public string? ExternalIdentity { get; set; }

    /// <summary>Gets or sets the legacy numeric party id.</summary>
    [JsonPropertyName("partyId")]
    public int PartyId { get; set; }

    /// <summary>
    /// Gets or sets the user type. <c>2</c> is <c>SelfIdentified</c> in SBL Bridge.
    /// </summary>
    [JsonPropertyName("userType")]
    public int UserType { get; set; }
}
