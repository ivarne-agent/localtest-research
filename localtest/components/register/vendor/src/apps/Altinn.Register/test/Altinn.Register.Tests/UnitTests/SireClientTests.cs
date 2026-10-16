using System.Net;
using System.Text;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Location;
using Altinn.Register.Integrations.Sire;
using Altinn.Register.TestUtils;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SireClient"/> covering each branch of <c>GetOrganization</c>:
/// 404, non-success status, malformed JSON, null body, and the happy path.
/// </summary>
public class SireClientTests
    : HostTestBase
{
    private static readonly OrganizationIdentifier TestOrgId
        = OrganizationIdentifier.Parse("090090003");

    // ILocationLookupProvider isn't registered in HostTestBase, so we stub it. The validator
    // only consults the lookup for international-address country resolution, which none of
    // these tests exercise, so an empty lookup is safe.
    private static ILocationLookupProvider CreateLookupProvider()
    {
        var provider = new Mock<ILocationLookupProvider>();

        provider
            .Setup(p => p.GetLocationLookup(It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Mock.Of<ILocationLookup>()));
        return provider.Object;
    }

    // Don't override BaseAddress — FakeHttpMessageHandler.CreateClient() sets its own
    // (FakeHttpEndpoint.HttpsUri); replacing it makes the handler miss the request.
    private SireClient CreateClient(FakeHttpMessageHandler handler)
        => new(handler.CreateClient(), CreateLookupProvider(), TimeProvider);

    [Fact]
    public async Task GetOrganization_ApiReturns404_ReturnsOrganizationNotFound()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"/v1/digdir/{TestOrgId}")
            .Respond(() => HttpStatusCode.NotFound);

        var client = CreateClient(handler);

        var result = await client.GetOrganization(TestOrgId, CancellationToken);

        Assert.True(result.IsProblem);
        Assert.Equal(Problems.OrganizationNotFound.ErrorCode, result.Problem.ErrorCode);
    }

    [Fact]
    public async Task GetOrganization_ApiReturns500_ReturnsPartyFetchFailed()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"/v1/digdir/{TestOrgId}")
            .Respond(() => HttpStatusCode.InternalServerError);

        var client = CreateClient(handler);

        var result = await client.GetOrganization(TestOrgId, CancellationToken);

        Assert.True(result.IsProblem);
        Assert.Equal(HttpStatusCode.InternalServerError, result.Problem.StatusCode);
        Assert.Equal(Problems.PartyFetchFailed.ErrorCode, result.Problem.ErrorCode);
        Assert.Contains("SIRE API responded with status code InternalServerError", result.Problem.Detail);
    }

    [Fact]
    public async Task GetOrganization_MalformedJson_ReturnsPartyFetchFailed()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"/v1/digdir/{TestOrgId}")
            .Respond(() => new StringContent("{ this is not valid json", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var result = await client.GetOrganization(TestOrgId, CancellationToken);

        Assert.True(result.IsProblem);
        Assert.Equal(Problems.PartyFetchFailed.ErrorCode, result.Problem.ErrorCode);
        Assert.Equal("Response deserialization failed", result.Problem.Detail);
    }

    [Fact]
    public async Task GetOrganization_NullJsonBody_ReturnsPartyFetchFailed()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"/v1/digdir/{TestOrgId}")
            .Respond(() => new StringContent("null", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var result = await client.GetOrganization(TestOrgId, CancellationToken);

        Assert.True(result.IsProblem);
        Assert.Equal(Problems.PartyFetchFailed.ErrorCode, result.Problem.ErrorCode);
        Assert.Equal("Response deserialization resulted in null", result.Problem.Detail);
    }

    [Fact]
    public async Task GetOrganization_ValidResponse_ReturnsMappedSireOrganization()
    {
        const string responseBody = """
            {
              "identifikator": "090090003",
              "selskapetsNavn": "Test AS",
              "organisasjonsform": "kommandittselskap",
              "stiftelsesdato": "2020-01-01",
              "virksomhetsrelasjon": [
                {
                  "ajourholdstidspunkt": "2024-01-15T10:00:00+01:00",
                  "gyldighetstidspunkt": "2024-01-15T10:00:00+01:00",
                  "relasjonstype": "dagligLederAdministrerendeDirektoer",
                  "relatertIdentifikator": {
                    "identifikatortype": "taxIdentificationNumber",
                    "verdi": "25871999336",
                    "landkode": "NO"
                  }
                }
              ],
              "postadresse": {
                "ajourholdstidspunkt": "2024-01-15T10:00:00+01:00",
                "norskAdresse": {
                  "adressetekst": ["Storgata 1"],
                  "postnummer": "0155",
                  "poststedsnavn": "OSLO"
                }
              }
            }
            """;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, $"/v1/digdir/{TestOrgId}")
            .Respond(() => new StringContent(responseBody, Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var result = await client.GetOrganization(TestOrgId, CancellationToken);

        Assert.False(result.IsProblem);
        var org = result.Value;
        Assert.Equal(TestOrgId, org.OrganizationIdentifier);
        Assert.Equal("Test AS", org.Name);
        Assert.Equal("KS", org.UnitType);
        Assert.Null(org.DeletedAt);
        Assert.Equal("E", org.UnitStatus);
        Assert.NotNull(org.MailingAddress);
        Assert.Equal("Storgata 1 0155 OSLO", org.MailingAddress!.Address);
        Assert.Equal("0155", org.MailingAddress.PostalCode);
        Assert.Equal("OSLO", org.MailingAddress.City);
        Assert.Single(org.BusinessRelationships);
        var rel = org.BusinessRelationships[0];
        Assert.Equal("daglig-leder", rel.RoleIdentifier);
        Assert.Equal(PersonIdentifier.Parse("25871999336"), rel.RelatedPersonIdentifier);
        Assert.Null(rel.RelatedOrganizationIdentifier);
    }
}
