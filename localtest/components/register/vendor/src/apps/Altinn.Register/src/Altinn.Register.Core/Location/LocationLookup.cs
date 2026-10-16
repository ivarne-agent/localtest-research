using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Core.Location;

/// <summary>
/// Defines a default implementation of <see cref="ILocationLookup"/> that uses in-memory collections for lookups.
/// </summary>
public sealed class LocationLookup
    : ILocationLookup
{
    /// <summary>
    /// Creates a new instance of <see cref="LocationLookup"/> with the given country and municipality collections.
    /// </summary>
    /// <param name="countries">The countries.</param>
    /// <param name="municipalities">The municipalities.</param>
    /// <returns>A new instance of <see cref="LocationLookup"/>.</returns>
    public static LocationLookup Create(
        IEnumerable<Country> countries,
        IEnumerable<Municipality> municipalities)
    {
        var countryDictionary = countries.ToFrozenDictionary(c => c.Code2, StringComparer.OrdinalIgnoreCase);
        var municipalityDictionary = municipalities.ToFrozenDictionary(m => (uint)m.Number);

        return new LocationLookup(countryDictionary, municipalityDictionary);
    }

    private readonly FrozenDictionary<string, Country> _countries;
    private readonly FrozenDictionary<uint, Municipality> _municipalities;

    private LocationLookup(
        FrozenDictionary<string, Country> countries,
        FrozenDictionary<uint, Municipality> municipalities)
    {
        _countries = countries;
        _municipalities = municipalities;
    }

    /// <inheritdoc/>
    public bool TryGetCountry(string countryCode2, [MaybeNullWhen(false)] out Country country)
        => _countries.TryGetValue(countryCode2, out country);

    /// <inheritdoc/>
    public bool TryGetMunicipality(uint municipalityNumber, [MaybeNullWhen(false)] out Municipality municipality)
        => _municipalities.TryGetValue(municipalityNumber, out municipality);
}
