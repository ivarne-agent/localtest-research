using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Tests;

public class EnterpriseUserTests
    : PartyTests
{
    [Fact]
    public async Task MinimalEnterpriseUser()
    {
        await ValidateParty(
            new EnterpriseUser
            {
                Uuid = Uuid,
                ExternalUrn = FieldValue.Null,
                PartyId = FieldValue.Unset,
                DisplayName = FieldValue.Unset,
                CreatedAt = FieldValue.Unset,
                ModifiedAt = FieldValue.Unset,
                IsDeleted = FieldValue.Unset,
                DeletedAt = FieldValue.Unset,
                User = FieldValue.Unset,
                Owner = FieldValue.Unset,
                VersionId = VersionId,
            },
            """
            {
              "partyType": "enterprise-user",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": null
            }
            """);
    }

    [Fact]
    public async Task MaximalEnterpriseUser()
    {
        await ValidateParty(
            new EnterpriseUser
            {
                Uuid = Uuid,
                ExternalUrn = FieldValue.Null,
                PartyId = PartyId,
                DisplayName = "Display Name",
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                User = FullUser,
                Owner = OwnerRef,
                VersionId = VersionId,
            },
            """
            {
              "partyType": "enterprise-user",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": null,
              "partyId": 12345678,
              "displayName": "Display Name",
              "createdAt": "2020-01-02T03:04:05+00:00",
              "modifiedAt": "2022-05-06T07:08:09+00:00",
              "isDeleted": false,
              "deletedAt": null,
              "user": {
                "userId": 50,
                "username": "username",
                "userIds": [ 50, 30, 1 ],
                "usernames": [ "username" ]
              },
              "owner": {
                "partyUuid": "00000000-0000-0000-0000-000000000002",
                "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000002"
              }
            }
            """);
    }
}
