using System.Text.Json;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Tests;

public class UnknownPartyTests
    : PartyTests
{
    protected static NonExhaustiveEnum<PartyType> UnknownPartyType { get; }
        = JsonSerializer.Deserialize<NonExhaustiveEnum<PartyType>>(
            "\"unknown-party\"");

    [Fact]
    public async Task MinimalUnknownParty()
    {
        await ValidateParty(
            new Party(UnknownPartyType)
            {
                Uuid = Uuid,
                ExternalUrn = JsonSerializer.Deserialize<NonExhaustive<PartyExternalRefUrn>>("\"urn:altinn:robot:clanker-id\"")!,
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                VersionId = VersionId,
            },
            """
            {
              "partyType": "unknown-party",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:robot:clanker-id"
            }
            """);
    }

    [Fact]
    public async Task UnknownPartyWithExtensions()
    {
        // note: this is not constructable without using JSON deserialization, hence we do this first
        var json =
            """
            {
              "partyType": "unknown-party",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:robot:clanker-id",
              "partyId": 12345678,
              "displayName": "Display Name",
              "createdAt": "2020-01-02T03:04:05+00:00",
              "modifiedAt": "2022-05-06T07:08:09+00:00",
              "isDeleted": false,
              "deletedAt": null,
              "user": {
                "userId": 50,
                "username": "username",
                "userIds": [ 50, 30, 1 ]
              },
              "custom-field": "custom value",
              "custom-array": [ "value1", "value2" ],
              "custom-tuple": [ "value1", 42, true ],
              "custom-object": {
                "nested-field": "nested value"
              }
            }
            """;

        var party = JsonSerializer.Deserialize<Party>(json);
        party.ShouldNotBeNull();
        party.ShouldBeOfType<Party>();

        var p2 = JsonSerializer.Deserialize<Party>(json)!;

        var p1Ext = ((IHasExtensionData)party).JsonExtensionData;
        var p2Ext = ((IHasExtensionData)p2).JsonExtensionData;

        await ValidateParty(party, json);
    }
}
