using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Integrations.Sire.Organization;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SireOrganizationFormMapper"/>.
/// </summary>
public class SireOrganizationFormMapperTests
{
    /// <summary>
    /// Every known SIRE <c>organisasjonsform</c> must map to its expected SL-code.
    /// </summary>
    [Theory]
    [MemberData(nameof(KnownOrganizationFormMappings))]
    public void TryValidate_MapsKnownValue(string? organisasjonsform, string expectedSlCode)
    {
        ValidationProblemBuilder builder = default;
        var ok = builder.TryValidate(
            path: "/organisasjonsform",
            organisasjonsform,
            default(SireOrganizationFormMapper),
            out string? result);

        Assert.True(ok);
        Assert.Equal(expectedSlCode, result);
        Assert.False(builder.TryBuild(out _));
    }

    /// <summary>
    /// Missing/whitespace <c>organisasjonsform</c> is treated as a Required-error rather
    /// than silently falling back to a default SL-code. SIRE may omit the field for some
    /// orgs, but we want to surface that as data we can't act on instead of guessing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryValidate_MissingInput_AddsRequiredError(string? organisasjonsform)
    {
        ValidationProblemBuilder builder = default;
        var ok = builder.TryValidate(
            path: "/organisasjonsform",
            organisasjonsform,
            default(SireOrganizationFormMapper),
            out string? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Anything outside the kodeliste produces a validation error rather than throwing.
    /// The default only applies to the missing case (covered by
    /// <see cref="TryValidate_MissingInput_ReturnsDefault"/>), never as a silent fallback
    /// for unrecognised non-empty values.
    /// </summary>
    [Theory]
    [InlineData("AS")] // common Norwegian form, but not in this kodeliste
    [InlineData("notAForm")]
    [InlineData("KS")] // SL-code (output of the mapper, mistakenly fed back in)
    [InlineData("KOMMANDITTSELSKAP")] // case mismatch
    [InlineData("kommandittselskap ")] // trailing whitespace
    public void TryValidate_AddsProblemForUnknown(string? organisasjonsform)
    {
        ValidationProblemBuilder builder = default;
        var ok = builder.TryValidate(
            path: "/organisasjonsform",
            organisasjonsform,
            default(SireOrganizationFormMapper),
            out string? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Authoritative table of the 9 SIRE organisasjonsform technical names → SL-codes from
    /// Skatteetaten's <c>organisasjonsformHovedenhetType.xml</c> kodeliste.
    /// </summary>
    public static TheoryData<string, string> KnownOrganizationFormMappings => new()
    {
        { "ansvarligSelskapMedSolidariskAnsvar", "ANS" },
        { "ansvarligSelskapMedDeltAnsvar",       "DA" },
        { "indreSelskap",                        "IS" },
        { "kommandittselskap",                   "KS" },
        { "norskkontrollertUtenlandskSelskap",   "NOKUS" },
        { "partrederi",                          "PRE" },
        { "tingsrettsligSameie",                 "SAM" },
        { "utenlandskAnsvarligSelskap",          "UTLANS" },
        { "virksomhetDrevetIFellesskap",         "VIFE" },
    };
}
