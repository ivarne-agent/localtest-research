using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts.Testing;

namespace Altinn.Register.Contracts.Tests;

public class OrganizationTests
    : PartyTests
{
    protected static OrganizationIdentifier OrganizationIdentifier { get; }
        = OrganizationIdentifier.Parse("123456785");

    [Fact]
    public async Task MinimalOrganization()
    {
        var org = Organization.Minimal(OrganizationIdentifier, Uuid);
        org.ShouldBe(new Organization
        {
            Uuid = Uuid,
            ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier)),
            PartyId = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            User = FieldValue.Unset,
            VersionId = VersionId,

            OrganizationIdentifier = OrganizationIdentifier,
            Source = FieldValue.Unset,
            UnitStatus = FieldValue.Unset,
            UnitType = FieldValue.Unset,
            TelephoneNumber = FieldValue.Unset,
            MobileNumber = FieldValue.Unset,
            FaxNumber = FieldValue.Unset,
            EmailAddress = FieldValue.Unset,
            InternetAddress = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            BusinessAddress = FieldValue.Unset,
        });

        await ValidateParty(
            org,
            """
            {
              "partyType": "organization",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "organizationIdentifier": "123456785",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:organization:identifier-no:123456785"
            }
            """);
    }

    [Fact]
    public async Task MaximalOrganization()
    {
        await ValidateParty(
            new Organization
            {
                Uuid = Uuid,
                ExternalUrn = NonExhaustive.Create<PartyExternalRefUrn>(PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier)),
                PartyId = PartyId,
                DisplayName = "Display Name",
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                User = FieldValue.Null,
                VersionId = VersionId,

                OrganizationIdentifier = OrganizationIdentifier,
                Source = NonExhaustiveEnum.Create(OrganizationSource.CentralCoordinatingRegister),
                UnitStatus = "Unit Status",
                UnitType = "Unit Type",
                TelephoneNumber = "Telephone Number",
                MobileNumber = "Mobile Number",
                FaxNumber = "Fax Number",
                EmailAddress = "email@address.example.com",
                InternetAddress = "internet.address.example.com",
                MailingAddress = new MailingAddress
                {
                    Address = "Mailing Address Lines",
                    PostalCode = "1234",
                    City = "City Name",
                },
                BusinessAddress = new MailingAddress
                {
                    Address = "Business Address Lines",
                    PostalCode = "1234",
                    City = "City Name",
                },
            },
            """
            {
              "partyType": "organization",
              "partyUuid": "00000000-0000-0000-0000-000000000001",
              "organizationIdentifier": "123456785",
              "versionId": 1,
              "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001",
              "externalUrn": "urn:altinn:organization:identifier-no:123456785",
              "partyId": 12345678,
              "displayName": "Display Name",
              "createdAt": "2020-01-02T03:04:05+00:00",
              "modifiedAt": "2022-05-06T07:08:09+00:00",
              "isDeleted": false,
              "deletedAt": null,
              "user": null,
              "source": "ccr",
              "unitStatus": "Unit Status",
              "unitType": "Unit Type",
              "telephoneNumber": "Telephone Number",
              "mobileNumber": "Mobile Number",
              "faxNumber": "Fax Number",
              "emailAddress": "email@address.example.com",
              "internetAddress": "internet.address.example.com",
              "mailingAddress": {
                "address": "Mailing Address Lines",
                "postalCode": "1234",
                "city": "City Name"
              },
              "businessAddress": {
                "address": "Business Address Lines",
                "postalCode": "1234",
                "city": "City Name"
              }
            }
            """);
    }
}
