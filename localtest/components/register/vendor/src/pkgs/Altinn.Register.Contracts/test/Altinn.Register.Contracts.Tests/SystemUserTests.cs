using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Tests;

public class SystemUserTests
    : PartyTests
{
    [Fact]
    public async Task MinimalSystemUser()
    {
        await ValidateParty(
            new SystemUser
            {
                Uuid = Uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.SystemUserUuid.Create(Uuid)),
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                Owner = FieldValue.Unset,
                VersionId = VersionId,
                SystemUserType = FieldValue.Unset,
            },
            """
            {
              "partyType": "system-user",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:systemuser:uuid:00000000-0000-0000-0000-000000000001"
            }
            """);
    }

    [Fact]
    public async Task MaximalSystemUser()
    {
        await ValidateParty(
            new SystemUser
            {
                Uuid = Uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.SystemUserUuid.Create(Uuid)),
                PartyId = PartyId,
                DisplayName = "Display Name",
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                User = FieldValue.Null,
                Owner = OwnerRef,
                VersionId = VersionId,
                SystemUserType = NonExhaustiveEnum.Create(SystemUserType.FirstPartySystemUser),
            },
            """
            {
              "partyType": "system-user",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:systemuser:uuid:00000000-0000-0000-0000-000000000001",
              "partyId": 12345678,
              "displayName": "Display Name",
              "createdAt": "2020-01-02T03:04:05+00:00",
              "modifiedAt": "2022-05-06T07:08:09+00:00",
              "isDeleted": false,
              "deletedAt": null,
              "user": null,
              "owner": {
                "partyUuid": "00000000-0000-0000-0000-000000000002",
                "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000002"
              },
              "systemUserType": "first-party-system-user"
            }
            """);
    }

    [Fact]
    public async Task MaximalSystemUser_Agent()
    {
        await ValidateParty(
            new SystemUser
            {
                Uuid = Uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.SystemUserUuid.Create(Uuid)),
                PartyId = PartyId,
                DisplayName = "Display Name",
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                User = FieldValue.Null,
                Owner = OwnerRef,
                VersionId = VersionId,
                SystemUserType = NonExhaustiveEnum.Create(SystemUserType.ClientPartySystemUser),
            },
            """
            {
              "partyType": "system-user",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:systemuser:uuid:00000000-0000-0000-0000-000000000001",
              "partyId": 12345678,
              "displayName": "Display Name",
              "createdAt": "2020-01-02T03:04:05+00:00",
              "modifiedAt": "2022-05-06T07:08:09+00:00",
              "isDeleted": false,
              "deletedAt": null,
              "user": null,
              "owner": {
                "partyUuid": "00000000-0000-0000-0000-000000000002",
                "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000002"
              },
              "systemUserType": "client-party-system-user"
            }
            """);
    }
}
