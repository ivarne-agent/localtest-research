namespace Altinn.Register.Core.Location;

/// <summary>
/// Represents a country with its ISO codes and name.
/// </summary>
public sealed record Country
{
    /// <summary>
    /// Gets the ISO 3166-1 alpha-2 country code of the country.
    /// </summary>
    public required string Code2 { get; init; }

    /// <summary>
    /// Gets the ISO 3166-1 alpha-3 country code of the country.
    /// </summary>
    public required string Code3 { get; init; }

    /// <summary>
    /// Gets the name of the country.
    /// </summary>
    public required string Name { get; init; }
}
