using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire.Organization;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Focused unit tests for <see cref="OrganizationDocumentValidator"/> covering the
/// <c>opphoerstidspunkt</c> (validTo) handling semantics: SIRE treats any non-null
/// validTo as "terminated, drop this entry", and a validTo sitting more than
/// <c>FutureDateGrace</c> in the future is flagged as bad data rather than silently
/// carried forward.
/// </summary>
/// <remarks>
/// The golden-file <see cref="SireOrganizationParsingTests"/> exercises the validator
/// end-to-end on real SIRE responses; these tests use synthetic in-memory documents to
/// pin down the exact validity branches around a deterministic <c>now</c>.
/// </remarks>
public class OrganizationDocumentValidatorTests
{
    private static readonly DateTimeOffset Now
        = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan WithinGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BeyondGrace = TimeSpan.FromHours(1);

    private const string TestOrgNo = "090090003";
    private const string TestFnr = "25871999336";

    private static OrganizationDocumentValidator CreateValidator()
        => new(Mock.Of<ILocationLookup>(), Now);

    private static (SireOrganization? Validated, bool HasError) Run(OrganizationDocument document)
    {
        var validator = CreateValidator();
        ValidationProblemBuilder builder = default;
        builder.TryValidate(path: "/", document, validator, out SireOrganization? validated);
        var hasError = builder.TryBuild(out _);
        return (validated, hasError);
    }

    private static OrganizationDocument DocWithPostalAddress(DateTimeOffset? validTo)
        => new()
        {
            Identifier = TestOrgNo,
            CompanyName = "Test AS",
            OrganizationForm = "indreSelskap",
            PostalAddress = new PostalAddress
            {
                ValidTo = validTo,
                NorwegianAddress = new NorwegianAddress
                {
                    AddressLines = ["Testgata 1"],
                    PostalCode = "0001",
                    City = "OSLO",
                },
            },
            BusinessRelationships = [],
        };

    private static OrganizationDocument DocWithBothAddresses()
        => new()
        {
            Identifier = TestOrgNo,
            CompanyName = "Test AS",
            OrganizationForm = "indreSelskap",
            PostalAddress = new PostalAddress
            {
                ValidTo = null,
                NorwegianAddress = new NorwegianAddress
                {
                    AddressLines = ["Testgata 1"],
                    PostalCode = "0001",
                    City = "OSLO",
                },
                InternationalAddress = new InternationalAddress
                {
                    AddressLines = ["1600 Pennsylvania Avenue"],
                    PostalCode = "20500",
                    City = "Washington",
                    CountryCode = "US",
                },
            },
            BusinessRelationships = [],
        };

    private static OrganizationDocument DocWithBusinessRelationship(DateTimeOffset? validTo)
        => new()
        {
            Identifier = TestOrgNo,
            CompanyName = "Test AS",
            OrganizationForm = "indreSelskap",
            PostalAddress = null,
            BusinessRelationships =
            [
                new BusinessRelationship
                {
                    RelationshipType = "styremedlem",
                    ValidTo = validTo,
                    RelatedIdentifier = new RelatedIdentifier
                    {
                        IdentifierType = "taxIdentificationNumber",
                        Value = TestFnr,
                    },
                },
            ],
        };

    private static OrganizationDocument DocWithDeletedDate(string? deletedDate)
        => new()
        {
            Identifier = TestOrgNo,
            CompanyName = "Test AS",
            OrganizationForm = "indreSelskap",
            DeletedDate = deletedDate,
            PostalAddress = null,
            BusinessRelationships = [],
        };

    private static OrganizationDocument DocWithCompanyName(string? companyName)
        => new()
        {
            Identifier = TestOrgNo,
            CompanyName = companyName,
            OrganizationForm = "indreSelskap",
            PostalAddress = null,
            BusinessRelationships = [],
        };

    /// <summary>
    /// Null <c>opphoerstidspunkt</c> means the address is still in force, so it should
    /// be carried through to <c>MailingAddress</c>.
    /// </summary>
    [Fact]
    public void PostalAddress_NullValidTo_IsKept()
    {
        var (validated, hasError) = Run(DocWithPostalAddress(validTo: null));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.NotNull(validated.MailingAddress);
        Assert.Equal("Testgata 1 0001 OSLO", validated.MailingAddress.Address);
    }

    /// <summary>
    /// A past <c>opphoerstidspunkt</c> is the canonical "terminated" case — the address
    /// is silently dropped and no error is added.
    /// </summary>
    [Fact]
    public void PostalAddress_PastValidTo_IsDropped()
    {
        var (validated, hasError) = Run(DocWithPostalAddress(validTo: Now - TimeSpan.FromDays(1)));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Null(validated.MailingAddress);
    }

    /// <summary>
    /// A <c>validTo</c> a few minutes in the future stays within the clock-skew grace
    /// window — the address is dropped (treat as terminated) but no error is added.
    /// </summary>
    [Fact]
    public void PostalAddress_WithinGrace_IsDropped()
    {
        var (validated, hasError) = Run(DocWithPostalAddress(validTo: Now + WithinGrace));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Null(validated.MailingAddress);
    }

    /// <summary>
    /// A <c>validTo</c> meaningfully in the future is treated as bad data: SIRE
    /// shouldn't be sending future-dated terminations and we don't know how to safely
    /// process them. The validator surfaces a validation error rather than silently
    /// choosing an interpretation.
    /// </summary>
    [Fact]
    public void PostalAddress_FarFuture_AddsValidationError()
    {
        var (validated, hasError) = Run(DocWithPostalAddress(validTo: Now + BeyondGrace));

        Assert.True(hasError);
        Assert.Null(validated);
    }

    /// <summary>
    /// When a SIRE postadresse carries both <c>norskAdresse</c> and <c>utenlandskAdresse</c>
    /// the Norwegian address wins — the international one is dropped. Skatt's data
    /// occasionally retains a stale international address alongside a newer Norwegian
    /// one (e.g. after a foreign company re-registers domestically); the Norwegian
    /// address is treated as the authoritative current location. This test pins down
    /// that precedence so a future refactor of <c>NormalizeAddress</c> can't silently
    /// flip it.
    /// </summary>
    [Fact]
    public void PostalAddress_BothNorwegianAndInternational_NorwegianWins()
    {
        var (validated, hasError) = Run(DocWithBothAddresses());

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.NotNull(validated.MailingAddress);

        // Norwegian branch produces: "Testgata 1 0001 OSLO" with PostalCode="0001"
        // and City="OSLO". Asserting on the structured PostalCode/City fields keeps
        // the test resilient to harmless changes in the line-formatting logic.
        Assert.Equal("0001", validated.MailingAddress.PostalCode);
        Assert.Equal("OSLO", validated.MailingAddress.City);

        // The international address must NOT have leaked into the result — no
        // "Washington", "Pennsylvania", or US postal code anywhere.
        Assert.NotNull(validated.MailingAddress.Address);
        Assert.DoesNotContain("Washington", validated.MailingAddress.Address);
        Assert.DoesNotContain("Pennsylvania", validated.MailingAddress.Address);
        Assert.DoesNotContain("20500", validated.MailingAddress.Address);
    }

    /// <summary>
    /// Null <c>opphoerstidspunkt</c> on a relationship means it's still in force, so it
    /// should appear in <c>BusinessRelationships</c>.
    /// </summary>
    [Fact]
    public void BusinessRelationship_NullValidTo_IsKept()
    {
        var (validated, hasError) = Run(DocWithBusinessRelationship(validTo: null));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Single(validated.BusinessRelationships);
    }

    /// <summary>
    /// A past <c>opphoerstidspunkt</c> on a relationship — silently dropped, no error.
    /// </summary>
    [Fact]
    public void BusinessRelationship_PastValidTo_IsDropped()
    {
        var (validated, hasError) = Run(DocWithBusinessRelationship(validTo: Now - TimeSpan.FromDays(1)));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Empty(validated.BusinessRelationships);
    }

    /// <summary>
    /// Within the clock-skew grace window — dropped, no error.
    /// </summary>
    [Fact]
    public void BusinessRelationship_WithinGrace_IsDropped()
    {
        var (validated, hasError) = Run(DocWithBusinessRelationship(validTo: Now + WithinGrace));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Empty(validated.BusinessRelationships);
    }

    /// <summary>
    /// Far-future <c>opphoerstidspunkt</c> on a relationship — validation error.
    /// </summary>
    [Fact]
    public void BusinessRelationship_FarFuture_AddsValidationError()
    {
        var (validated, hasError) = Run(DocWithBusinessRelationship(validTo: Now + BeyondGrace));

        Assert.True(hasError);
        Assert.Null(validated);
    }

    /// <summary>
    /// Null, empty, or whitespace <c>slettetdato</c> means the organization is not
    /// deleted — no error, <c>DeletedAt</c> is null.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeletedDate_MissingInput_NotDeleted(string? deletedDate)
    {
        var (validated, hasError) = Run(DocWithDeletedDate(deletedDate));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Null(validated.DeletedAt);
        Assert.Equal("E", validated.UnitStatus);
    }

    /// <summary>
    /// A well-formed ISO date (<c>yyyy-MM-dd</c>) parses into <c>DeletedAt</c> at
    /// midnight UTC and flips <c>UnitStatus</c> to "S".
    /// </summary>
    [Fact]
    public void DeletedDate_ValidIsoDate_ParsesToMidnightUtc()
    {
        var (validated, hasError) = Run(DocWithDeletedDate("2025-09-09"));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Equal(new DateTimeOffset(2025, 9, 9, 0, 0, 0, TimeSpan.Zero), validated.DeletedAt);
        Assert.Equal("S", validated.UnitStatus);
    }

    /// <summary>
    /// Anything that doesn't match the strict <c>yyyy-MM-dd</c> format is rejected with
    /// an <see cref="ValidationErrors.InvalidDate"/> error rather than silently being
    /// treated as "not deleted" or coerced via a more lenient parse. This mirrors NPR's
    /// pattern for DateOfBirth/DateOfDeath: pin the format, surface the bad value.
    /// </summary>
    [Theory]
    [InlineData("2025/09/09")] // wrong separator
    [InlineData("09-09-2025")] // wrong order (dd-MM-yyyy)
    [InlineData("2025-09-09T00:00:00Z")] // includes time component
    [InlineData("2025-9-9")] // missing leading zeros
    [InlineData("2025-13-01")] // invalid month
    [InlineData("notADate")]
    public void DeletedDate_InvalidFormat_AddsValidationError(string deletedDate)
    {
        var (validated, hasError) = Run(DocWithDeletedDate(deletedDate));

        Assert.True(hasError);
        Assert.Null(validated);
    }

    /// <summary>
    /// <c>selskapetsNavn</c> is core data we can't reasonably default — null, empty,
    /// or whitespace input must surface as a <see cref="StdValidationErrors.Required"/>
    /// error rather than producing a record with a blank name.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CompanyName_MissingInput_AddsRequiredError(string? companyName)
    {
        var (validated, hasError) = Run(DocWithCompanyName(companyName));

        Assert.True(hasError);
        Assert.Null(validated);
    }

    /// <summary>
    /// A present <c>selskapetsNavn</c> flows through unchanged to <c>SireOrganization.Name</c>.
    /// </summary>
    [Fact]
    public void CompanyName_Present_FlowsThrough()
    {
        var (validated, hasError) = Run(DocWithCompanyName("Real Company AS"));

        Assert.False(hasError);
        Assert.NotNull(validated);
        Assert.Equal("Real Company AS", validated.Name);
    }
}
