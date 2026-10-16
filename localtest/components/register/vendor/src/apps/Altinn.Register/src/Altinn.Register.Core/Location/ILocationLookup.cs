using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Altinn.Register.Core.Location;

/// <summary>
/// Defines methods for looking up location information such as countries and municipalities.
/// </summary>
public interface ILocationLookup
{
    /// <summary>
    /// Tries to get the country information by the given ISO 3166-1 alpha-2 country code.
    /// </summary>
    /// <param name="countryCode2">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="country">The country information.</param>
    /// <returns><see langword="true"/> if the country was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetCountry(string countryCode2, [MaybeNullWhen(false)] out Country country);

    /// <summary>
    /// Tries to get the municipality information by the given municipality number.
    /// </summary>
    /// <param name="municipalityNumber">The municipality number.</param>
    /// <param name="municipality">The municipality.</param>
    /// <returns><see langword="true"/> if the municipality was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetMunicipality(uint municipalityNumber, [MaybeNullWhen(false)] out Municipality municipality);

    /// <summary>
    /// Tries to get the municipality information by the given municipality number.
    /// </summary>
    /// <param name="municipalityNumber">The municipality number.</param>
    /// <param name="municipality">The municipality.</param>
    /// <returns><see langword="true"/> if the municipality was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetMunicipality(string municipalityNumber, [MaybeNullWhen(false)] out Municipality municipality)
    {
        if (!uint.TryParse(municipalityNumber, style: NumberStyles.None, provider: null, out uint number))
        {
            municipality = default;
            return false;
        }

        return TryGetMunicipality(number, out municipality);
    }
}
