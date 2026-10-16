using System.Text.Json.Serialization;
using Altinn.Register.Contracts;

namespace Altinn.Register.Models;

/// <summary>
/// Request body for the set-username endpoint, which allows changing the username of an existing user.
/// </summary>
public sealed record SetUsernameRequest
{
    /// <summary>
    /// Gets the party to set the username for. Supports URNs from <see cref="PartyExternalRefUrn"/>.
    /// </summary>
    /// <remarks>
    /// As of now, we only support setting the username on a person-party. This might change in the future.
    /// </remarks>
    [JsonPropertyName("party")]
    public string? Party { get; init; }

    /// <summary>
    /// Gets the new username to set. Use <see langword="null"/> to clear the username. Empty or whitespace-only usernames are not allowed.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }
}
