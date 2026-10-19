namespace Localtest.IdPorten;

/// <summary>
/// The old localtest's authentication levels 0-4 and the <c>acr</c> each signs in with. The same table is
/// Authentication's <c>OidcProviders:idporten:AuthLevels</c> default
/// (<c>components/authentication/fakes/Login/LocaltestAuthLevels.cs</c>), which turns the acr into
/// <c>urn:altinn:authlevel</c>; keep the two in step.
/// </summary>
public static class AuthLevels
{
    public sealed record Level(int Number, string Acr, string Label);

    public static readonly Level[] All =
    [
        new(0, "level0", "0 (self-identified)"),
        new(1, "level1", "1 (static password)"),
        new(2, "level2", "2 (pin code)"),
        new(3, "idporten-loa-substantial", "3 (substantial)"),
        new(4, "idporten-loa-high", "4 (high)"),
    ];

    /// <summary>The level the picker preselects when the request asked for none it knows.</summary>
    public const int Default = 3;

    public static Level? ForNumber(int number) => All.FirstOrDefault(l => l.Number == number);

    public static Level? ForAcr(string? acr) => All.FirstOrDefault(l => l.Acr == acr);

    /// <summary>The highest known level among space-separated <c>acr_values</c>.</summary>
    public static Level? Highest(string? acrValues)
        => (acrValues ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(ForAcr)
            .OfType<Level>()
            .MaxBy(l => l.Number);
}
