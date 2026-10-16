using System.Text.Json;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.TestUtils.Shouldly;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.TestUtils;

namespace Altinn.Register.Tests.UnitTests;

public class PartyRecordTests
    : HostTestBase
{
    private static readonly JsonSerializerOptions _options
        = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static PersonRecord ToExpectedPersonRecord(PartyRecord record)
        => new()
        {
            PartyUuid = record.PartyUuid,
            PartyId = record.PartyId,
            ExternalUrn = record.ExternalUrn,
            DisplayName = record.DisplayName,
            PersonIdentifier = record.PersonIdentifier,
            OrganizationIdentifier = record.OrganizationIdentifier,
            CreatedAt = record.CreatedAt,
            ModifiedAt = record.ModifiedAt,
            IsDeleted = record.IsDeleted,
            DeletedAt = record.DeletedAt,
            UserIds = record.UserIds,
            Usernames = record.Usernames,
            VersionId = record.VersionId,
            OwnerUuid = record.OwnerUuid,
            Source = FieldValue.Unset,
            FirstName = FieldValue.Unset,
            MiddleName = FieldValue.Unset,
            LastName = FieldValue.Unset,
            ShortName = FieldValue.Unset,
            Address = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            DateOfBirth = FieldValue.Unset,
            DateOfDeath = FieldValue.Unset,
        };

    private static OrganizationRecord ToExpectedOrganizationRecord(PartyRecord record)
        => new()
        {
            PartyUuid = record.PartyUuid,
            PartyId = record.PartyId,
            ExternalUrn = record.ExternalUrn,
            DisplayName = record.DisplayName,
            PersonIdentifier = record.PersonIdentifier,
            OrganizationIdentifier = record.OrganizationIdentifier,
            CreatedAt = record.CreatedAt,
            ModifiedAt = record.ModifiedAt,
            IsDeleted = record.IsDeleted,
            DeletedAt = record.DeletedAt,
            UserIds = record.UserIds,
            Usernames = record.Usernames,
            VersionId = record.VersionId,
            OwnerUuid = record.OwnerUuid,
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
        };

    private static SelfIdentifiedUserRecord ToExpectedSelfIdentifiedUserRecord(PartyRecord record)
        => new()
        {
            PartyUuid = record.PartyUuid,
            PartyId = record.PartyId,
            ExternalUrn = record.ExternalUrn,
            DisplayName = record.DisplayName,
            PersonIdentifier = record.PersonIdentifier,
            OrganizationIdentifier = record.OrganizationIdentifier,
            CreatedAt = record.CreatedAt,
            ModifiedAt = record.ModifiedAt,
            IsDeleted = record.IsDeleted,
            DeletedAt = record.DeletedAt,
            UserIds = record.UserIds,
            Usernames = record.Usernames,
            VersionId = record.VersionId,
            OwnerUuid = record.OwnerUuid,
            SelfIdentifiedUserType = FieldValue.Unset,
            Email = FieldValue.Unset,
            ExtRef = FieldValue.Unset,
        };

    [Fact]
    public void Serialize_PartyRecord_AllUnset()
    {
        PartyRecord record = new PartyRecord(FieldValue.Unset)
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Unset,
        };

        var json = JsonSerializer.Serialize(record, _options);
        json.ShouldBe("""{}""");

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<PartyRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_PartyRecord_TypeUnset()
    {
        PartyRecord record = new PartyRecord(FieldValue.Unset)
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Unset,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([1U, 2U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 50,
            OwnerUuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyUuid": "00000000-0000-0000-0000-000000000001",
                "partyId": 1,
                "displayName": "1",
                "createdAt": "2000-01-01T00:00:00+00:00",
                "modifiedAt": "2000-01-01T00:00:00+00:00",
                "isDeleted": false,
                "deletedAt": null,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1, 2]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId": 50,
                "ownerUuid": "00000000-0000-0000-0000-000000000002"
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<PartyRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_PartyRecord_PersonType()
    {
        PartyRecord record = new PartyRecord(PartyRecordType.Person)
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("25871999336")),
            DisplayName = "1",
            PersonIdentifier = PersonIdentifier.Parse("25871999336"),
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = true,
            DeletedAt = TimeProvider.GetUtcNow(),
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "person",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": "urn:altinn:person:identifier-no:25871999336",
                "displayName":"1",
                "personIdentifier":"25871999336",
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":true,
                "deletedAt":"2000-01-01T00:00:00+00:00",
                "versionId":42,
                "ownerUuid": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        var person = deserialized.ShouldBeOfType<PersonRecord>();
        person.ShouldBeEquivalentTo(ToExpectedPersonRecord(record));
    }

    [Fact]
    public void Serialize_PartyRecord_OrganizationType()
    {
        PartyRecord record = new PartyRecord(PartyRecordType.Organization)
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier.Parse("123456785")),
            DisplayName = "1",
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = OrganizationIdentifier.Parse("123456785"),
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "organization",
                "partyUuid": "00000000-0000-0000-0000-000000000001",
                "partyId": 1,
                "externalUrn": "urn:altinn:organization:identifier-no:123456785",
                "displayName": "1",
                "organizationIdentifier": "123456785",
                "createdAt": "2000-01-01T00:00:00+00:00",
                "modifiedAt": "2000-01-01T00:00:00+00:00",
                "isDeleted": false,
                "deletedAt": null,
                "versionId": 42,
                "ownerUuid": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        var organization = deserialized.ShouldBeOfType<OrganizationRecord>();
        organization.ShouldBeEquivalentTo(ToExpectedOrganizationRecord(record));
    }

    [Fact]
    public void Serialize_PartyRecord_SIType()
    {
        PartyRecord record = new PartyRecord(PartyRecordType.SelfIdentifiedUser)
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Null,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = true,
            DeletedAt = TimeProvider.GetUtcNow(),
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":true,
                "deletedAt":"2000-01-01T00:00:00+00:00",
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId":42,
                "ownerUuid": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        var selfIdentified = deserialized.ShouldBeOfType<SelfIdentifiedUserRecord>();
        selfIdentified.ShouldBeEquivalentTo(ToExpectedSelfIdentifiedUserRecord(record));
    }

    [Fact]
    public void Serialize_PersonRecord_Empty()
    {
        PartyRecord record = new PersonRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Unset,

            Source = FieldValue.Unset,
            FirstName = FieldValue.Unset,
            MiddleName = FieldValue.Unset,
            LastName = FieldValue.Unset,
            ShortName = FieldValue.Unset,
            Address = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            DateOfBirth = FieldValue.Unset,
            DateOfDeath = FieldValue.Unset,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "person"
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<PersonRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_PersonRecord_OnlyPartyFields()
    {
        PartyRecord record = new PersonRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("25871999336")),
            DisplayName = "1",
            PersonIdentifier = PersonIdentifier.Parse("25871999336"),
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,

            Source = FieldValue.Unset,
            FirstName = FieldValue.Unset,
            MiddleName = FieldValue.Unset,
            LastName = FieldValue.Unset,
            ShortName = FieldValue.Unset,
            Address = FieldValue.Unset,
            MailingAddress = FieldValue.Unset,
            DateOfBirth = FieldValue.Unset,
            DateOfDeath = FieldValue.Unset,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "person",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": "urn:altinn:person:identifier-no:25871999336",
                "displayName":"1",
                "personIdentifier":"25871999336",
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "versionId":42,
                "ownerUuid": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<PersonRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_PersonRecord_Full()
    {
        PartyRecord record = new PersonRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("25871999336")),
            DisplayName = "1",
            PersonIdentifier = PersonIdentifier.Parse("25871999336"),
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow() + TimeSpan.FromDays(1),
            IsDeleted = true,
            DeletedAt = TimeProvider.GetUtcNow() + TimeSpan.FromDays(2),
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,

            Source = PersonSource.NationalPopulationRegister,
            FirstName = "First",
            MiddleName = null,
            LastName = "Last",
            ShortName = "Short",
            Address = new StreetAddressRecord
            {
                MunicipalNumber = "1",
                MunicipalName = "2",
                StreetName = "3",
                HouseNumber = "4",
                HouseLetter = "5",
                PostalCode = "6",
                City = "7",
            },
            MailingAddress = new MailingAddressRecord
            {
                Address = "Address",
                PostalCode = "PostalCode",
                City = "City",
            },
            DateOfBirth = new DateOnly(1900, 01, 01),
            DateOfDeath = new DateOnly(2000, 02, 02),
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "person",
                "partyUuid": "00000000-0000-0000-0000-000000000001",
                "partyId": 1,
                "externalUrn": "urn:altinn:person:identifier-no:25871999336",
                "displayName": "1",
                "personIdentifier": "25871999336",
                "createdAt": "2000-01-01T00:00:00+00:00",
                "modifiedAt": "2000-01-02T00:00:00+00:00",
                "deletedAt": "2000-01-03T00:00:00+00:00",
                "isDeleted": true,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId": 42,
                "ownerUuid": null,
                "source": "npr",
                "firstName": "First",
                "middleName": null,
                "lastName": "Last",
                "shortName": "Short",
                "address": {
                    "municipalNumber": "1",
                    "municipalName": "2",
                    "streetName": "3",
                    "houseNumber": "4",
                    "houseLetter": "5",
                    "postalCode": "6",
                    "city": "7"
                },
                "mailingAddress": {
                    "address": "Address",
                    "postalCode": "PostalCode",
                    "city": "City"
                },
                "dateOfBirth": "1900-01-01",
                "dateOfDeath": "2000-02-02"
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<PersonRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_OrganizationRecord_Empty()
    {
        PartyRecord record = new OrganizationRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Unset,

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
        };
        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "organization"
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<OrganizationRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_OrganizationRecord_Full()
    {
        PartyRecord record = new OrganizationRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier.Parse("123456785")),
            DisplayName = "1",
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = OrganizationIdentifier.Parse("123456785"),
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Null,
            Usernames = FieldValue.Null,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,

            Source = OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "status",
            UnitType = "type",
            TelephoneNumber = "telephone",
            MobileNumber = "mobile",
            FaxNumber = "fax",
            EmailAddress = "email",
            InternetAddress = "internet",
            MailingAddress = new MailingAddressRecord
            {
                Address = "mailing address",
                PostalCode = "mailing postal",
                City = "mailing city",
            },
            BusinessAddress = new MailingAddressRecord
            {
                Address = "business address",
                PostalCode = "business postal",
                City = "business city",
            },
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "organization",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": "urn:altinn:organization:identifier-no:123456785",
                "displayName":"1",
                "organizationIdentifier":"123456785",
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "userIds": null,
                "usernames": null,
                "versionId":42,
                "ownerUuid": null,
                "source": "ccr",
                "unitStatus":"status",
                "unitType":"type",
                "telephoneNumber":"telephone",
                "mobileNumber":"mobile",
                "faxNumber":"fax",
                "emailAddress":"email",
                "internetAddress":"internet",
                "mailingAddress": {
                    "address": "mailing address",
                    "postalCode": "mailing postal",
                    "city": "mailing city"
                },
                "businessAddress": {
                    "address": "business address",
                    "postalCode": "business postal",
                    "city": "business city"
                }
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, _options);
        deserialized.ShouldBeOfType<OrganizationRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_OrganizationRecord_ParentOrganizationUuid_IsIgnored()
    {
        PartyRecord record = new OrganizationRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = FieldValue.Unset,
            DisplayName = FieldValue.Unset,
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = FieldValue.Unset,
            CreatedAt = FieldValue.Unset,
            ModifiedAt = FieldValue.Unset,
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Unset,

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

            ParentOrganizationUuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        };
        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "organization"
            }
            """);

        var json2 =
            """
            {
                "partyType": "organization",
                "parentOrganizationUuid": "00000000-0000-0000-0000-000000000002"
            }
            """;

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json2, _options);
        var org = deserialized.ShouldBeOfType<OrganizationRecord>();
        org.ParentOrganizationUuid.ShouldBeUnset();
    }

    [Fact]
    public void Serialize_SIRecord()
    {
        SelfIdentifiedUserRecord record = new SelfIdentifiedUserRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Null,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = FieldValue.Null,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "organizationIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId":42,
                "ownerUuid": null,
                "selfIdentifiedUserType": null,
                "email": null,
                "extRef": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<SelfIdentifiedUserRecord>(json, _options);
        deserialized.ShouldBeOfType<SelfIdentifiedUserRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_SIRecord_Legacy()
    {
        SelfIdentifiedUserRecord record = new SelfIdentifiedUserRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Null,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "organizationIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId":42,
                "ownerUuid": null,
                "selfIdentifiedUserType": "legacy",
                "email": null,
                "extRef": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<SelfIdentifiedUserRecord>(json, _options);
        deserialized.ShouldBeOfType<SelfIdentifiedUserRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_SIRecord_Edu()
    {
        SelfIdentifiedUserRecord record = new SelfIdentifiedUserRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Null,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            Email = FieldValue.Null,
            ExtRef = "edu-ext-ref",
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "organizationIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId":42,
                "ownerUuid": null,
                "selfIdentifiedUserType": "edu",
                "email": null,
                "extRef": "edu-ext-ref"
            }
            """);

        var deserialized = JsonSerializer.Deserialize<SelfIdentifiedUserRecord>(json, _options);
        deserialized.ShouldBeOfType<SelfIdentifiedUserRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_SIRecord_Email()
    {
        SelfIdentifiedUserRecord record = new SelfIdentifiedUserRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = FieldValue.Null,
            DisplayName = "1",
            PersonIdentifier = FieldValue.Null,
            OrganizationIdentifier = FieldValue.Null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(1U),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            Email = "test@example.com",
            ExtRef = FieldValue.Null,
        };

        var json = JsonSerializer.SerializeToElement(record, _options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "organizationIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "userIds": {
                    "hasActiveValue": true,
                    "values": [1]
                },
                "usernames": {
                    "hasActiveValue": false,
                    "values": []
                },
                "versionId":42,
                "ownerUuid": null,
                "selfIdentifiedUserType": "idporten-email",
                "email": "test@example.com",
                "extRef": null
            }
            """);

        var deserialized = JsonSerializer.Deserialize<SelfIdentifiedUserRecord>(json, _options);
        deserialized.ShouldBeOfType<SelfIdentifiedUserRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Deserialize_SIRecord_LegacyUserObject()
    {
        var json =
            """
            {
                "partyType": "self-identified-user",
                "partyUuid":"00000000-0000-0000-0000-000000000001",
                "partyId":1,
                "externalUrn": null,
                "displayName":"1",
                "personIdentifier": null,
                "organizationIdentifier": null,
                "createdAt":"2000-01-01T00:00:00+00:00",
                "modifiedAt":"2000-01-01T00:00:00+00:00",
                "isDeleted":false,
                "deletedAt": null,
                "user": {
                    "userId": 1,
                    "username": "legacy-user",
                    "userIds": [1, 2]
                },
                "versionId":42,
                "ownerUuid": null,
                "selfIdentifiedUserType": "legacy",
                "email": null,
                "extRef": null
            }
            """;

        var deserialized = JsonSerializer.Deserialize<SelfIdentifiedUserRecord>(json, _options);

        deserialized.ShouldNotBeNull();
        deserialized.UserIds.CurrentValue.ShouldBe(1U);
        deserialized.UserIds.Values.ShouldBe([1U, 2U]);
        deserialized.Usernames.CurrentValue.ShouldBe("legacy-user");
        deserialized.Usernames.Values.ShouldBe(["legacy-user"]);
    }

    [Fact]
    public void Serialize_RespectsOptions()
    {
        var options = JsonSerializerOptions.Default;

        PartyRecord record = new OrganizationRecord
        {
            PartyUuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier.Parse("123456785")),
            DisplayName = "1",
            PersonIdentifier = FieldValue.Unset,
            OrganizationIdentifier = OrganizationIdentifier.Parse("123456785"),
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Null,
            Usernames = FieldValue.Null,
            VersionId = 42,
            OwnerUuid = FieldValue.Null,

            Source = OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "status",
            UnitType = "type",
            TelephoneNumber = "telephone",
            MobileNumber = "mobile",
            FaxNumber = "fax",
            EmailAddress = "email",
            InternetAddress = "internet",
            MailingAddress = new MailingAddressRecord
            {
                Address = "mailing address",
                PostalCode = "mailing postal",
                City = "mailing city",
            },
            BusinessAddress = new MailingAddressRecord
            {
                Address = "business address",
                PostalCode = "business postal",
                City = "business city",
            },
        };

        var json = JsonSerializer.SerializeToElement(record, options);
        json.ShouldBeStructurallyEquivalentTo(
            """
            {
                "PartyType": "organization",
                "PartyUuid":"00000000-0000-0000-0000-000000000001",
                "PartyId":1,
                "ExternalUrn": "urn:altinn:organization:identifier-no:123456785",
                "DisplayName":"1",
                "OrganizationIdentifier":"123456785",
                "CreatedAt":"2000-01-01T00:00:00+00:00",
                "ModifiedAt":"2000-01-01T00:00:00+00:00",
                "IsDeleted":false,
                "DeletedAt": null,
                "UserIds": null,
                "Usernames": null,
                "VersionId":42,
                "OwnerUuid": null,
                "Source": "ccr",
                "UnitStatus":"status",
                "UnitType":"type",
                "TelephoneNumber":"telephone",
                "MobileNumber":"mobile",
                "FaxNumber":"fax",
                "EmailAddress":"email",
                "InternetAddress":"internet",
                "MailingAddress": {
                    "Address": "mailing address",
                    "PostalCode": "mailing postal",
                    "City": "mailing city"
                },
                "BusinessAddress": {
                    "Address": "business address",
                    "PostalCode": "business postal",
                    "City": "business city"
                }
            }
            """);

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, options);
        deserialized.ShouldBeOfType<OrganizationRecord>();
        deserialized.ShouldBeEquivalentTo(record);
    }

    [Fact]
    public void Serialize_SupportsCaseInsensitive()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true,
        };

        var json =
            """
            {
                "pARTYtYPE": "organization",
                "DISPLAYNAME": "Test",
                "UNITTYPE": "type"
            }
            """;

        var deserialized = JsonSerializer.Deserialize<PartyRecord>(json, options);
        var org = deserialized.ShouldBeOfType<OrganizationRecord>();
        org.DisplayName.ShouldHaveValue().ShouldBe("Test");
        org.UnitType.ShouldHaveValue().ShouldBe("type");
    }

    [Fact]
    public void Deserialize_Subclasses()
    {
        JsonSerializer.Deserialize<PersonRecord>("""{}""", _options).ShouldNotBeNull();
        JsonSerializer.Deserialize<OrganizationRecord>("""{}""", _options).ShouldNotBeNull();
    }
}
