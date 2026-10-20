#nullable enable
using System.Collections.Generic;
using Altinn.Platform.Authentication.Enum;

namespace Altinn.Platform.Authentication.Localtest.Login;

/// <summary>
/// The old localtest's authentication levels 0-4 and the acr value each one signs in with. The acr values are the
/// ones production's built-in ID-porten table (<c>OidcAuthLevelDefaults.IdPorten</c>) uses for the same
/// <see cref="SecurityLevel"/>, so the production claim shaping turns each into the matching <c>urn:altinn:authlevel</c>.
/// The table is also the <c>OidcProviders:idporten:AuthLevels</c> default (see <see cref="ConfigurationDefaults"/>).
/// The localtest app's fake ID-porten has the same levels and acr values (<c>localtest/localtest/IdPorten/AuthLevels.cs</c>);
/// keep the two in step.
/// </summary>
public static class LocaltestAuthLevels
{
    public sealed record Entry(int Level, string Acr, SecurityLevel SecurityLevel, string[] ClaimValues);

    public static readonly IReadOnlyList<Entry> All =
    [
        new(0, "level0", SecurityLevel.SelfIdentifed, ["level0"]),
        new(1, "level1", SecurityLevel.NotSensitive, ["level1"]),
        new(2, "level2", SecurityLevel.QuiteSensitive, ["level2"]),
        new(3, "idporten-loa-substantial", SecurityLevel.Sensitive, ["idporten-loa-substantial", "level3"]),
        new(4, "idporten-loa-high", SecurityLevel.VerySensitive, ["idporten-loa-high", "level4"]),
    ];

    /// <summary>The <c>OidcProviders:idporten:AuthLevels</c> configuration entries.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> ConfigurationDefaults(string prefix)
    {
        for (var i = 0; i < All.Count; i++)
        {
            var entry = All[i];
            yield return new($"{prefix}:{i}:Acr", entry.Acr);
            yield return new($"{prefix}:{i}:Level", entry.SecurityLevel.ToString());
            yield return new($"{prefix}:{i}:UpstreamAcrValues", entry.Acr);
            for (var j = 0; j < entry.ClaimValues.Length; j++)
            {
                yield return new($"{prefix}:{i}:ClaimValues:{j}", entry.ClaimValues[j]);
            }
        }
    }
}
