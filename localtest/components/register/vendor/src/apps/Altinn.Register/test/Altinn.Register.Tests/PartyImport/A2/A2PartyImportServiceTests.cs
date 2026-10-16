using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;

namespace Altinn.Register.Tests.PartyImport.A2;

[SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "It's more readable")]
public class A2PartyImportServiceTests
    : HostTestBase
{
    [Fact]
    public async Task GetParty_Calls_Correct_Endpoint_AndMapsOrganizationData()
    {
        var partyId = 50004216U;
        var party = await TestDataLoader.Load<Contracts.V1.Party>(partyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(party);

        var partyUuid = party.PartyUuid;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", partyUuid.ToString())
            .Respond(() => TestDataParty(partyId));

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);

        var result = await client.GetParty(partyUuid, CancellationToken);
        var partyRecord = result.ShouldHaveValue();

        partyRecord.ShouldNotBeNull();
        partyRecord.PartyId.ShouldBe(partyId);
        partyRecord.PartyUuid.ShouldBe(partyUuid);

        var orgRecord = partyRecord.ShouldBeOfType<OrganizationRecord>();
        orgRecord.OrganizationIdentifier.ShouldHaveValue().ShouldBe(OrganizationIdentifier.Parse("311654306"));
        orgRecord.DisplayName.ShouldHaveValue().ShouldBe("TYNSET OG OPPDAL");
        orgRecord.UnitType.ShouldHaveValue().ShouldBe("ANS");
        orgRecord.UnitStatus.ShouldHaveValue().ShouldBe("N");
        orgRecord.TelephoneNumber.ShouldHaveValue().ShouldBe("22077000");
        orgRecord.MobileNumber.ShouldHaveValue().ShouldBe("99000000");
        orgRecord.FaxNumber.ShouldHaveValue().ShouldBe("22077108");
        orgRecord.EmailAddress.ShouldHaveValue().ShouldBe("tynset_og_oppdal@example.com");
        orgRecord.InternetAddress.ShouldHaveValue().ShouldBe("tynset-og-oppdal.example.com");
        orgRecord.MailingAddress.ShouldHaveValue().ShouldBe(new MailingAddressRecord
        {
            Address = "Postboks 6662 St. Bergens plass",
            PostalCode = "1666",
            City = "Bergen",
        });
        orgRecord.BusinessAddress.ShouldHaveValue().ShouldBe(new MailingAddressRecord
        {
            Address = "Postboks 6662 St. Olavs plass",
            PostalCode = "0555",
            City = "Oslo",
        });
    }

    [Fact]
    public async Task GetParty_Calls_Correct_Endpoint_AndMapsPersonData()
    {
        var partyId = 50012345U;
        var party = await TestDataLoader.Load<Contracts.V1.Party>(partyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(party);

        var partyUuid = party.PartyUuid;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", partyUuid.ToString())
            .Respond(() => TestDataParty(partyId));

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);

        var result = await client.GetParty(partyUuid, CancellationToken);
        var partyRecord = result.ShouldHaveValue();

        partyRecord.ShouldNotBeNull();
        partyRecord.PartyId.ShouldBe(partyId);
        partyRecord.PartyUuid.ShouldBe(partyUuid);

        var persRecord = partyRecord.ShouldBeOfType<PersonRecord>();
        persRecord.PersonIdentifier.ShouldHaveValue().ShouldBe(PersonIdentifier.Parse("25871999336"));
        persRecord.DisplayName.ShouldHaveValue().ShouldBe("Ola Bla Nordmann");
        persRecord.FirstName.ShouldHaveValue().ShouldBe("Ola");
        persRecord.MiddleName.ShouldHaveValue().ShouldBe("Bla");
        persRecord.LastName.ShouldHaveValue().ShouldBe("Nordmann");
        persRecord.ShortName.ShouldHaveValue().ShouldBe("Ola Bla Nordmann");
        persRecord.MailingAddress.ShouldHaveValue().ShouldBe(new MailingAddressRecord
        {
            Address = "Blåbæreveien 7 8450 Stokmarknes",
            PostalCode = "8450",
            City = "Stokmarknes",
        });
        persRecord.Address.ShouldHaveValue().ShouldBe(new StreetAddressRecord
        {
            MunicipalNumber = "1866",
            MunicipalName = "Hadsel",
            StreetName = "Blåbærveien",
            HouseNumber = "7",
            HouseLetter = "G",
            PostalCode = "8450",
            City = "Stokarknes",
        });
    }

    [Theory]
    [MemberData(nameof(GetPartyProblemStatuses))]
    public async Task GetParty_Problems(HttpStatusCode httpStatus, string errorCode)
    {
        var expected = JsonSerializer.Deserialize<ErrorCode>(JsonSerializer.Serialize(errorCode));

        var partyId = 50012345;
        var party = await TestDataLoader.Load<Contracts.V1.Party>(partyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(party);

        var partyUuid = party.PartyUuid;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", partyUuid.ToString())
            .Respond(() => httpStatus);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);

        var result = await client.GetParty(partyUuid, CancellationToken);
        result.ShouldBeProblem(expected);
    }

    public static TheoryData<HttpStatusCode, string> GetPartyProblemStatuses()
        => new()
        {
            { HttpStatusCode.Gone, Problems.PartyGone.ErrorCode.ToString()! },
            { HttpStatusCode.NotFound, Problems.ReferencedPartyNotFound.ErrorCode.ToString()! },
            { HttpStatusCode.BadRequest, Problems.PartyFetchFailed.ErrorCode.ToString()! },
            { HttpStatusCode.InternalServerError, Problems.PartyFetchFailed.ErrorCode.ToString()! },
        };

    [Fact]
    public async Task GetExternalRoleAssignmentsFrom_Calls_Correct_Endpoint_AndMapsData()
    {
        var partyId = 50012345U;
        var party = await TestDataLoader.Load<Contracts.V1.Party>(partyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(party);

        var partyUuid = party.PartyUuid;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties/partyroles/{fromPartyId}")
            .WithRouteValue("fromPartyId", partyId.ToString())
            .Respond(() => new StringContent(
                """
                [
                    {"PartyId": "1", "PartyUuid": "00000000-0000-0000-0000-000000000001", "PartyRelation": "Role", "RoleCode": "DAGL"},
                    {"PartyId": "2", "PartyUuid": "00000000-0000-0000-0000-000000000002", "PartyRelation": "Role", "RoleCode": "REVI"}
                ]
                """,
                MediaTypeHeaderValue.Parse("application/json; encoding=utf-8")));

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);

        var roleAssignments = await client.GetExternalRoleAssignmentsFrom(partyId, partyUuid, CancellationToken).ToListAsync(CancellationToken);
        Assert.NotNull(roleAssignments);

        roleAssignments.Count.ShouldBe(2);

        var firstRoleAssignment = roleAssignments[0];
        firstRoleAssignment.ToPartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        firstRoleAssignment.RoleCode.ShouldBe("DAGL");

        var secondRoleAssignment = roleAssignments[1];
        secondRoleAssignment.ToPartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        secondRoleAssignment.RoleCode.ShouldBe("REVI");
    }

    [Fact]
    public async Task GetExternalRoleAssignmentsFrom_Calls_Correct_Endpoint_AndMapsData_KONT_Roles()
    {
        var partyId = 50012345U;
        var party = await TestDataLoader.Load<Contracts.V1.Party>(partyId.ToString(CultureInfo.InvariantCulture));
        Assert.NotNull(party);

        var partyUuid = party.PartyUuid;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties/partyroles/{fromPartyId}")
            .WithRouteValue("fromPartyId", partyId.ToString())
            .Respond(() => new StringContent(
                """
                [
                    {"PartyId": "1", "PartyUuid": "00000000-0000-0000-0000-000000000001", "PartyRelation": "Role", "RoleCode": "DAGL"},
                    {"PartyId": "2", "PartyUuid": "00000000-0000-0000-0000-000000000002", "PartyRelation": "Role", "RoleCode": "REVI"},
                    {"PartyId": "3", "PartyUuid": "00000000-0000-0000-0000-000000000003", "PartyRelation": "Role", "RoleCode": "KONT"},
                    {"PartyId": "4", "PartyUuid": "00000000-0000-0000-0000-000000000004", "PartyRelation": "Role", "RoleCode": "KOMK"},
                    {"PartyId": "5", "PartyUuid": "00000000-0000-0000-0000-000000000005", "PartyRelation": "Role", "RoleCode": "SREVA"},
                    {"PartyId": "6", "PartyUuid": "00000000-0000-0000-0000-000000000006", "PartyRelation": "Role", "RoleCode": "KNUF"},
                    {"PartyId": "7", "PartyUuid": "00000000-0000-0000-0000-000000000007", "PartyRelation": "Role", "RoleCode": "KEMN"}
                ]
                """,
                MediaTypeHeaderValue.Parse("application/json; encoding=utf-8")));

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);

        var roleAssignments = await client.GetExternalRoleAssignmentsFrom(partyId, partyUuid, CancellationToken).ToListAsync(CancellationToken);
        Assert.NotNull(roleAssignments);

        roleAssignments.Count.ShouldBe(11);

        var party3Assignments = roleAssignments.Where(ra => ra.ToPartyUuid == Guid.Parse("00000000-0000-0000-0000-000000000003")).ToArray();
        party3Assignments.Length.ShouldBe(1);
        party3Assignments.ShouldContain(ra => ra.RoleCode == "KONT");

        var party4Assignments = roleAssignments.Where(ra => ra.ToPartyUuid == Guid.Parse("00000000-0000-0000-0000-000000000004")).ToArray();
        party4Assignments.Length.ShouldBe(2);
        party4Assignments.ShouldContain(ra => ra.RoleCode == "KOMK");
        party4Assignments.ShouldContain(ra => ra.RoleCode == "KONT");

        var party5Assignments = roleAssignments.Where(ra => ra.ToPartyUuid == Guid.Parse("00000000-0000-0000-0000-000000000005")).ToArray();
        party5Assignments.Length.ShouldBe(2);
        party5Assignments.ShouldContain(ra => ra.RoleCode == "SREVA");
        party5Assignments.ShouldContain(ra => ra.RoleCode == "KONT");

        var party6Assignments = roleAssignments.Where(ra => ra.ToPartyUuid == Guid.Parse("00000000-0000-0000-0000-000000000006")).ToArray();
        party6Assignments.Length.ShouldBe(2);
        party6Assignments.ShouldContain(ra => ra.RoleCode == "KNUF");
        party6Assignments.ShouldContain(ra => ra.RoleCode == "KONT");
    }

    [Fact]
    public async Task GetChanges_NoChanges()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/0")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [],
                    "LastAvailableChange": 0,
                    "LastChangeInSegment": 0
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var changes = await client.GetChanges(cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChanges_SinglePage()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/0")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                        {
                            "ChangeId": 1,
                            "PartyId": 1,
                            "PartyUuid": "00000000-0000-0000-0000-000000000001",
                            "LastChangedTime": "2020-01-01T00:00:00Z"
                        },
                        {
                            "ChangeId": 2,
                            "PartyId": 2,
                            "PartyUuid": "00000000-0000-0000-0000-000000000002",
                            "LastChangedTime": "2020-01-02T00:00:00Z"
                        }
                    ],
                    "LastAvailableChange": 2,
                    "LastChangeInSegment": 2
                }
                """);

        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/2")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                    ],
                    "LastAvailableChange": 2,
                    "LastChangeInSegment": 0
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var changes = await client.GetChanges(cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var page = changes.ShouldHaveSingleItem();
        page.LastKnownChangeId.ShouldBe(2U);
        page.Count.ShouldBe(2);

        page[0].ChangeId.ShouldBe(1U);
        page[0].PartyId.ShouldBe(1);
        page[0].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        page[0].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        page[1].ChangeId.ShouldBe(2U);
        page[1].PartyId.ShouldBe(2);
        page[1].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        page[1].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetChanges_MultiplePages()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/0")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                        {
                            "ChangeId": 1,
                            "PartyId": 1,
                            "PartyUuid": "00000000-0000-0000-0000-000000000001",
                            "LastChangedTime": "2020-01-01T00:00:00Z"
                        },
                        {
                            "ChangeId": 2,
                            "PartyId": 2,
                            "PartyUuid": "00000000-0000-0000-0000-000000000002",
                            "LastChangedTime": "2020-01-02T00:00:00Z"
                        }
                    ],
                    "LastAvailableChange": 3,
                    "LastChangeInSegment": 2
                }
                """);

        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/2")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                        {
                            "ChangeId": 3,
                            "PartyId": 3,
                            "PartyUuid": "00000000-0000-0000-0000-000000000003",
                            "LastChangedTime": "2020-01-03T00:00:00Z"
                        },
                        {
                            "ChangeId": 4,
                            "PartyId": 4,
                            "PartyUuid": "00000000-0000-0000-0000-000000000004",
                            "LastChangedTime": "2020-01-04T00:00:00Z"
                        }
                    ],
                    "LastAvailableChange": 5,
                    "LastChangeInSegment": 4
                }
                """);

        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/4")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                        {
                            "ChangeId": 5,
                            "PartyId": 5,
                            "PartyUuid": "00000000-0000-0000-0000-000000000005",
                            "LastChangedTime": "2020-01-05T00:00:00Z"
                        }
                    ],
                    "LastAvailableChange": 5,
                    "LastChangeInSegment": 5
                }
                """);

        handler.Expect(HttpMethod.Get, "/register/api/parties/partychanges/5")
            .Respond(
                contentType: "application/json",
                """
                {
                    "PartyChangeList": [
                    ],
                    "LastAvailableChange": 5,
                    "LastChangeInSegment": 0
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var changes = await client.GetChanges(cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        changes.Count.ShouldBe(3);
        var firstPage = changes[0];
        firstPage.LastKnownChangeId.ShouldBe(3U);
        firstPage.Count.ShouldBe(2);

        firstPage[0].ChangeId.ShouldBe(1U);
        firstPage[0].PartyId.ShouldBe(1);
        firstPage[0].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        firstPage[0].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        firstPage[1].ChangeId.ShouldBe(2U);
        firstPage[1].PartyId.ShouldBe(2);
        firstPage[1].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        firstPage[1].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var secondPage = changes[1];
        secondPage.LastKnownChangeId.ShouldBe(5U);
        secondPage.Count.ShouldBe(2);

        secondPage[0].ChangeId.ShouldBe(3U);
        secondPage[0].PartyId.ShouldBe(3);
        secondPage[0].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000003"));
        secondPage[0].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 3, 0, 0, 0, TimeSpan.Zero));

        secondPage[1].ChangeId.ShouldBe(4U);
        secondPage[1].PartyId.ShouldBe(4);
        secondPage[1].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000004"));
        secondPage[1].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 4, 0, 0, 0, TimeSpan.Zero));

        var thirdPage = changes[2];
        thirdPage.LastKnownChangeId.ShouldBe(5U);
        thirdPage.Count.ShouldBe(1);

        thirdPage[0].ChangeId.ShouldBe(5U);
        thirdPage[0].PartyId.ShouldBe(5);
        thirdPage[0].PartyUuid.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000005"));
        thirdPage[0].ChangeTime.ShouldBe(new DateTimeOffset(2020, 1, 5, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetProfile_Person_MapsCorrectly()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "profile/api/users/{userId}")
            .WithRouteValue("userId", "20002097")
            .Respond(
                contentType: "application/json",
                """
                {
                  "UserId": 20002097,
                  "UserUUID": "76bc6f6e-8090-4ca6-8fd0-57054ffe1daf",
                  "UserType": 1,
                  "UserName": "",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": 50004205,
                  "Party": {
                    "PartyTypeName": 1,
                    "SSN": "63074821024",
                    "OrgNumber": "",
                    "Person": {
                      "SSN": "63074821024",
                      "Name": "SAMIRA AASGAARD",
                      "FirstName": "SAMIRA",
                      "MiddleName": "",
                      "LastName": "AASGAARD",
                      "TelephoneNumber": "",
                      "MobileNumber": "",
                      "MailingAddress": "Amalie Jessens vei 26",
                      "MailingPostalCode": "3182",
                      "MailingPostalCity": "HORTEN",
                      "AddressMunicipalNumber": "",
                      "AddressMunicipalName": "",
                      "AddressStreetName": "",
                      "AddressHouseNumber": "",
                      "AddressHouseLetter": "",
                      "AddressPostalCode": "3182",
                      "AddressCity": "HORTEN",
                      "DateOfDeath": null
                    },
                    "Organization": null,
                    "PartyId": 50004205,
                    "PartyUUID": "76bc6f6e-8090-4ca6-8fd0-57054ffe1daf",
                    "UnitType": null,
                    "LastChangedInAltinn": "2009-06-06T15:12:18.787+02:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "SAMIRA AASGAARD",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                  },
                  "ProfileSettingPreference": {
                    "Language": "nb",
                    "PreSelectedPartyId": 0,
                    "DoNotPromptForParty": false
                  }
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var profileResult = await client.GetProfile(20002097, CancellationToken);

        var profile = profileResult.ShouldHaveValue();
        profile.PartyId.ShouldBe(50004205U);
        profile.PartyUuid.ShouldBe(Guid.Parse("76bc6f6e-8090-4ca6-8fd0-57054ffe1daf"));
        profile.UserId.ShouldBe(20002097U);
        profile.UserUuid.ShouldBe(Guid.Parse("76bc6f6e-8090-4ca6-8fd0-57054ffe1daf"));
        profile.UserName.ShouldBeNull();
        profile.IsActive.ShouldBeTrue();
        profile.ProfileType.ShouldBe(A2UserProfileType.Person);
    }

    [Fact]
    public async Task GetProfile_SelfIdentified_MapsCorrectly()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "profile/api/users/{userId}")
            .WithRouteValue("userId", "20002139")
            .Respond(
                contentType: "application/json",
                """
                {
                  "UserId": 20002139,
                  "UserUUID": "4fe860c4-bc65-4d2f-a288-f825b460f26b",
                  "UserType": 2,
                  "UserName": "TestSelfIdentifiedUser",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": 50006237,
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": 50006237,
                    "PartyUUID": "4fe860c4-bc65-4d2f-a288-f825b460f26b",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "TestSelfIdentifiedUser",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                  },
                  "ProfileSettingPreference": {
                    "Language": "nb",
                    "PreSelectedPartyId": 0,
                    "DoNotPromptForParty": false
                  }
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var profileResult = await client.GetProfile(20002139, CancellationToken);

        var profile = profileResult.ShouldHaveValue();
        profile.PartyId.ShouldBe(50006237U);
        profile.PartyUuid.ShouldBe(Guid.Parse("4fe860c4-bc65-4d2f-a288-f825b460f26b"));
        profile.UserId.ShouldBe(20002139U);
        profile.UserUuid.ShouldBe(Guid.Parse("4fe860c4-bc65-4d2f-a288-f825b460f26b"));
        profile.UserName.ShouldBe("TestSelfIdentifiedUser");
        profile.IsActive.ShouldBeTrue();
        profile.ProfileType.ShouldBe(A2UserProfileType.SelfIdentifiedUser);
    }

    [Fact]
    public async Task GetProfile_Enterprise_MapsCorrectly()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, "profile/api/users/{userId}")
            .WithRouteValue("userId", "20005073")
            .Respond(
                contentType: "application/json",
                """
                {
                  "UserId": 20005073,
                  "UserUUID": "869a3a5f-02db-402c-823a-94584afea394",
                  "UserType": 3,
                  "UserName": "PKQHFDZGLDRIRGJU",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": 50066480,
                  "Party": {
                    "PartyTypeName": 2,
                    "SSN": "",
                    "OrgNumber": "910514318",
                    "Person": null,
                    "Organization": {
                      "OrgNumber": "910514318",
                      "Name": "KYSTBASEN ÅGOTNES OG ILSENG",
                      "UnitType": "ASA",
                      "TelephoneNumber": "12345678",
                      "MobileNumber": "99999999",
                      "FaxNumber": "12345679",
                      "EMailAddress": "test@test.test",
                      "InternetAddress": null,
                      "MailingAddress": "Markalléen 19",
                      "MailingPostalCode": "1368",
                      "MailingPostalCity": "STABEKK",
                      "BusinessAddress": "Markalléen 19",
                      "BusinessPostalCode": "1368",
                      "BusinessPostalCity": "STABEKK",
                      "UnitStatus": "N",
                      "Established": "2017-09-11T00:00:00+02:00"
                    },
                    "PartyId": 50066480,
                    "PartyUUID": "ec061efa-4c2a-4dbd-87f5-bcb59cdeaf91",
                    "UnitType": "ASA",
                    "LastChangedInAltinn": "2023-02-08T10:22:30.973+01:00",
                    "LastChangedInExternalRegister": "2017-09-11T00:00:00+02:00",
                    "Name": "KYSTBASEN ÅGOTNES OG ILSENG",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                  },
                  "ProfileSettingPreference": {
                    "Language": "nb",
                    "PreSelectedPartyId": 0,
                    "DoNotPromptForParty": false
                  }
                }
                """);

        var logger = GetRequiredService<ILogger<A2PartyImportService>>();
        var client = new A2PartyImportService(handler.CreateClient(), TimeProvider, logger);
        var profileResult = await client.GetProfile(20005073, CancellationToken);

        var profile = profileResult.ShouldHaveValue();
        profile.PartyId.ShouldBe(50066480U);
        profile.PartyUuid.ShouldBe(Guid.Parse("ec061efa-4c2a-4dbd-87f5-bcb59cdeaf91"));
        profile.UserId.ShouldBe(20005073U);
        profile.UserUuid.ShouldBe(Guid.Parse("869a3a5f-02db-402c-823a-94584afea394"));
        profile.UserName.ShouldBe("PKQHFDZGLDRIRGJU");
        profile.IsActive.ShouldBeTrue();
        profile.ProfileType.ShouldBe(A2UserProfileType.EnterpriseUser);
    }

    private static async Task<SequenceHttpContent> TestDataParty(uint id)
    {
        Sequence<byte>? content = null;

        try
        {
            content = await TestDataLoader.LoadContent(id.ToString(CultureInfo.InvariantCulture));

            var httpContent = new SequenceHttpContent(content);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            content = null;
            return httpContent;
        }
        finally
        {
            content?.Dispose();
        }
    }
}
