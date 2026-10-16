using System.Text.Json;
using Altinn.Register.Contracts;
using Altinn.Register.PartyImport;

namespace Altinn.Register.Tests.UnitTests;

public class ImportPartyIdentifierTests
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PersonIdentifier_RoundTrips()
    {
        VerifyRoundTrip(
            PersonIdentifier.Parse("25871999336"),
            "25871999336");
    }

    [Fact]
    public void OrganizationIdentifier_RoundTrips()
    {
        VerifyRoundTrip(
            OrganizationIdentifier.Parse("123456785"),
            "123456785");
    }

    [Fact]
    public void PartyUuid_RoundTrips()
    {
        VerifyRoundTrip(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Default_RoundTrips_AsNull()
    {
        var defaultIdentifier = default(ImportPartyIdentifier);

        var defaultJson = JsonSerializer.Serialize(defaultIdentifier, _options);

        var deserializedDefault = JsonSerializer.Deserialize<ImportPartyIdentifier>(defaultJson, _options);

        deserializedDefault.ShouldBe(defaultIdentifier);
        defaultIdentifier.HasValue.ShouldBeFalse();
        defaultJson.ShouldBe("null");
    }

    private static void VerifyRoundTrip(ImportPartyIdentifier identifier, string value)
    {
        var json = JsonSerializer.Serialize(identifier, _options);
        var deserialized = JsonSerializer.Deserialize<ImportPartyIdentifier>(json, _options);

        deserialized.ShouldBe(identifier);
        json.ShouldBe($"\"{value}\"");
    }
}
