namespace Altinn.Register.Contracts.V1;

/// <summary>
/// Represents the components of a person's name.
/// </summary>
public record PersonNameComponents
{
    /// <summary>
    /// Gets or sets the first name.
    /// </summary>
    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    /// <summary>
    /// Gets or sets the middle name.
    /// </summary>
    [JsonPropertyName("middleName")]
    public string? MiddleName { get; init; }

    /// <summary>
    /// Gets or sets the sure name.
    /// </summary>
    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }
}
