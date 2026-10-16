namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// Represents an envelope for CCR updates, containing authentication information and the CCR XML payload.
/// </summary>
public sealed record CcrUpdateEnvelope
{
    /// <summary>
    /// Gets the username.
    /// </summary>
    public required string? UserName { get; init; }

    /// <summary>
    /// Gets the password.
    /// </summary>
    public required string? Password { get; init; }

    /// <summary>
    /// Gets the payload containing the CCR XML data.
    /// </summary>
    public required string? Payload { get; init; }
}
