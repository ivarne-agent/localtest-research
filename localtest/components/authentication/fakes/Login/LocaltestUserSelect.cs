#nullable enable
using System.Globalization;

namespace Altinn.Platform.Authentication.Localtest.Login;

/// <summary>The old localtest's <c>UserSelect</c> value: <c>userId.partyId</c>, or just <c>userId</c> for the user's own party.</summary>
public static class LocaltestUserSelect
{
    public static bool TryParse(string? value, out int userId, out int? partyId)
    {
        userId = 0;
        partyId = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('.');
        if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var party))
            {
                return false;
            }

            partyId = party;
        }

        return true;
    }
}
