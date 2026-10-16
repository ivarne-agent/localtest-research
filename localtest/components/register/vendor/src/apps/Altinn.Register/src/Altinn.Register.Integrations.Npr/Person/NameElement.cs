using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a historical name entry for a person.
/// </summary>
/// <source>folkeregisteret.tilgjengeliggjoering.person.v1.Folkeregisterpersonnavn</source>
public sealed record NameElement
    : HistoricalElement
{
    /// <summary>
    /// Gets the first name.
    /// </summary>
    [JsonPropertyName("fornavn")]
    public string? FirstName { get; init; }

    /// <summary>
    /// Gets the middle name.
    /// </summary>
    [JsonPropertyName("mellomnavn")]
    public string? MiddleName { get; init; }

    /// <summary>
    /// Gets the last name.
    /// </summary>
    [JsonPropertyName("etternavn")]
    public string? LastName { get; init; }

    /// <summary>
    /// Gets the short form of the name.
    /// </summary>
    [JsonPropertyName("forkortetNavn")]
    public string? ShortName { get; init; }

    /// <summary>
    /// Gets the original name as registered.
    /// </summary>
    [JsonPropertyName("originaltNavn")]
    public Personnavn? OriginalName { get; init; }

    /// <summary>
    /// Represents the original name as registered in NPR.
    /// </summary>
    public sealed record Personnavn
    {
        /// <summary>
        /// Gets the first name.
        /// </summary>
        [JsonPropertyName("fornavn")]
        public string? FirstName { get; init; }

        /// <summary>
        /// Gets the middle name.
        /// </summary>
        [JsonPropertyName("mellomnavn")]
        public string? MiddleName { get; init; }

        /// <summary>
        /// Gets the last name.
        /// </summary>
        [JsonPropertyName("etternavn")]
        public string? LastName { get; init; }
    }
}
