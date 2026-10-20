using System.Text.Json.Serialization;

namespace Localtest.Tokens;

/// <summary>An OAuth token endpoint error (RFC 6749 section 5.2), shared by the fake ID-porten and Maskinporten.</summary>
public sealed record TokenErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);
