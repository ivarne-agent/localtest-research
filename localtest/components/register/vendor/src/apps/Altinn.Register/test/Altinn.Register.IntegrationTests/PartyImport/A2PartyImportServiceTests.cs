using System.Net;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.PartyImport.A2;
using Altinn.Register.PartyImport.SystemUser;
using Altinn.Register.TestUtils.Http;

namespace Altinn.Register.IntegrationTests.PartyImport;

public class A2PartyImportServiceTests
    : IntegrationTestBase
{
    [Fact]
    public async Task Does_Not_Retry()
    {
        var partyUuid = Guid.CreateVersion7();

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", partyUuid.ToString())
            .Respond(() => HttpStatusCode.InternalServerError);

        var service = GetRequiredService<IA2PartyImportService>();

        var result = await service.GetParty(partyUuid, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.ErrorCode.ShouldBe(Problems.PartyFetchFailed.ErrorCode);
    }

    public class GetUserPartyTests
        : IntegrationTestBase
    {
        protected virtual IFakeRequestBuilder GetRequestBuilder(FakeHttpMessageHandler handler, Guid partyUuid)
            => handler.Expect(HttpMethod.Get, "/profile/api/users")
                .WithQuery("userUUID", partyUuid.ToString());

        protected virtual Task<Result<A2ProfileRecord>> GetUserParty(IA2PartyImportService service, Guid partyUuid)
            => service.GetPartyUser(partyUuid, TestContext.Current.CancellationToken);

        [Fact]
        public async Task Gone_Returns_PartyGoneProblem()
        {
            var partyUuid = Guid.CreateVersion7();

            GetRequestBuilder(FakeHttpHandlers.For<IA2PartyImportService>(), partyUuid)
                .Respond(() => HttpStatusCode.Gone);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await GetUserParty(service, partyUuid);

            result.IsProblem.ShouldBeTrue();
            result.Problem.ErrorCode.ShouldBe(Problems.PartyGone.ErrorCode);
        }

        [Fact]
        public async Task NotFound_Returns_PartyNotFound()
        {
            var partyUuid = Guid.CreateVersion7();

            GetRequestBuilder(FakeHttpHandlers.For<IA2PartyImportService>(), partyUuid)
                .Respond(() => HttpStatusCode.NotFound);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await GetUserParty(service, partyUuid);

            result.IsProblem.ShouldBeTrue();
            result.Problem.ErrorCode.ShouldBe(Problems.ReferencedPartyNotFound.ErrorCode);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task UnsuccessfulStatus_Returns_PartyFetchFailed(HttpStatusCode statusCode)
        {
            var partyUuid = Guid.CreateVersion7();

            GetRequestBuilder(FakeHttpHandlers.For<IA2PartyImportService>(), partyUuid)
                .Respond(() => statusCode);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await GetUserParty(service, partyUuid);

            result.IsProblem.ShouldBeTrue();
            result.Problem.ErrorCode.ShouldBe(Problems.PartyFetchFailed.ErrorCode);
        }

        [Fact]
        public async Task ValidResponse_ReturnsPartyUserRecord()
        {
            var partyUuid = Guid.Parse("b049a483-dfea-437e-bb89-261a5390f207");

            GetRequestBuilder(FakeHttpHandlers.For<IA2PartyImportService>(), partyUuid)
                .Respond(
                "application/json",
                """
                {
                  "UserId": 20002571,
                  "UserUUID": "b049a483-dfea-437e-bb89-261a5390f207",
                  "UserType": 2,
                  "UserName": "AdvancedSettingsTest",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": "AdvancedSettingsTest@AdvancedSettingsTest.no",
                  "PartyId": 50068492,
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": 50068492,
                    "PartyUUID": "b049a483-dfea-437e-bb89-261a5390f207",
                    "UnitType": null,
                    "LastChangedInAltinn": "2021-02-08T05:07:09.677+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "AdvancedSettingsTest",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                  },
                  "ProfileSettingPreference": {
                    "Language": "nb",
                    "PreSelectedPartyId": 0,
                    "DoNotPromptForParty": true
                  }
                }
                """);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await GetUserParty(service, partyUuid);

            result.IsProblem.ShouldBeFalse();
            result.Value.ShouldNotBeNull();
            result.Value.ShouldBe(new A2ProfileRecord
            {
                UserId = 20002571,
                UserUuid = Guid.Parse("b049a483-dfea-437e-bb89-261a5390f207"),
                ExternalAuthenticationReference = null,
                ProfileType = A2UserProfileType.SelfIdentifiedUser,
                IsActive = true,
                UserName = "AdvancedSettingsTest",
                PartyUuid = Guid.Parse("b049a483-dfea-437e-bb89-261a5390f207"),
                PartyId = 50068492,
                LastChangedAt = DateTimeOffset.Parse("2021-02-08T05:07:09.677+01:00"),
            });
        }
    }

    public class GetOrCreatePersonUserTests
        : GetUserPartyTests
    {
        protected override IFakeRequestBuilder GetRequestBuilder(FakeHttpMessageHandler handler, Guid partyUuid)
            => handler.Expect(HttpMethod.Get, "profile/api/users/getorcreate/{userUuid:guid}")
                .WithRouteValue("userUuid", partyUuid.ToString());

        protected override Task<Result<A2ProfileRecord>> GetUserParty(IA2PartyImportService service, Guid partyUuid)
            => service.GetOrCreatePersonUser(partyUuid, TestContext.Current.CancellationToken);
    }

    public class GetUserProfileChangesTests
        : IntegrationTestBase
    {
        [Fact]
        public async Task NoChanges()
        {
            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "1")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "sequenceMax": 0
                        },
                        "links": {
                            "next": null
                        },
                        "data": []
                    }
                    """);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await service.GetUserProfileChanges(cancellationToken: CancellationToken)
                .ToListAsync(CancellationToken);

            result.ShouldNotBeNull();
            result.ShouldBeEmpty();
        }

        [Fact]
        public async Task NoMoreChanges()
        {
            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "251")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "sequenceMax": 250
                        },
                        "links": {
                            "next": null
                        },
                        "data": []
                    }
                    """);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await service.GetUserProfileChanges(250, cancellationToken: CancellationToken)
                .ToListAsync(CancellationToken);

            result.ShouldNotBeNull();
            result.ShouldBeEmpty();
        }

        [Fact]
        public async Task Pages()
        {
            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "1")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "pageStart": 1,
                            "pageEnd": 2,
                            "sequenceMax": 3
                        },
                        "links": {
                            "next": "foo/bar?bat"
                        },
                        "data": [
                            {
                                "userChangeEventId": 1,
                                "userUuid": "453f2415-ce87-4c8f-a9de-8664109d599a",
                                "userId": 20000000,
                                "ownerPartyUuid": "453f2415-ce87-4c8f-a9de-8664109d599a",
                                "ownerPartyId": 50002108,
                                "userName": null,
                                "userType": "SSNIdentified",
                                "isDeleted": false
                            },
                            {
                                "userChangeEventId": 2,
                                "userUuid": "69f34a20-9228-4fda-b585-4ccf0eb8af60",
                                "userId": 20000001,
                                "ownerPartyUuid": "69f34a20-9228-4fda-b585-4ccf0eb8af60",
                                "ownerPartyId": 50002109,
                                "userName": "null",
                                "userType": "SelfIdentified",
                                "isDeleted": false
                            }
                        ]
                    }
                    """);

            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "3")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "pageStart": 3,
                            "pageEnd": 4,
                            "sequenceMax": 4
                        },
                        "links": {
                            "next": "foo/bar?bat"
                        },
                        "data": [
                            {
                                "userChangeEventId": 3,
                                "userUuid": "6e75fe3a-e1a7-4288-8c28-b1bc699cdcae",
                                "userId": 20000002,
                                "ownerPartyUuid": "6e75fe3a-e1a7-4288-8c28-b1bc699cdcae",
                                "ownerPartyId": 50002110,
                                "userName": null,
                                "userType": "SSNIdentified",
                                "isDeleted": false
                            },
                            {
                                "userChangeEventId": 4,
                                "userUuid": "8686c09c-8fa5-48c2-bde8-2d73446d2f75",
                                "userId": 20000003,
                                "ownerPartyUuid": "f8c9a346-b996-4250-9333-ae6e9fe37256",
                                "ownerPartyId": 50002111,
                                "userName": "FOOBARBAT",
                                "userType": "EnterpriseIdentified",
                                "isDeleted": false
                            }
                        ]
                    }
                    """);

            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "5")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "pageStart": 5,
                            "pageEnd": 5,
                            "sequenceMax": 5
                        },
                        "links": {
                            "next": "foo/bar?bat"
                        },
                        "data": [
                            {
                                "userChangeEventId": 5,
                                "userUuid": "5f7ca26d-40ed-409e-bcf9-880914803b2c",
                                "userId": 20000004,
                                "ownerPartyUuid": "5f7ca26d-40ed-409e-bcf9-880914803b2c",
                                "ownerPartyId": 50002112,
                                "userName": null,
                                "userType": "SSNIdentified",
                                "isDeleted": true
                            }
                        ]
                    }
                    """);

            FakeHttpHandlers.For<IA2PartyImportService>()
                .Expect(HttpMethod.Get, "profile/api/userprofileevents")
                .WithQuery("eventId", "6")
                .Respond(
                    "application/json",
                    """
                    {
                        "stats": {
                            "sequenceMax": 5
                        },
                        "links": {
                            "next": null
                        },
                        "data": []
                    }
                    """);

            var service = GetRequiredService<IA2PartyImportService>();

            var result = await service.GetUserProfileChanges(cancellationToken: CancellationToken)
                .ToListAsync(CancellationToken);

            result.ShouldNotBeNull();
            result.Count.ShouldBe(3);

            var page1 = result[0].ShouldNotBeNull();
            var page2 = result[1].ShouldNotBeNull();
            var page3 = result[2].ShouldNotBeNull();

            page1.LastKnownChangeId.ShouldBe(3U);
            page1.Count.ShouldBe(2);
            page1[0].ShouldSatisfyAllConditions(
                change => change.ChangeId.ShouldBe(1U),
                change => change.UserUuid.ShouldBe(Guid.Parse("453f2415-ce87-4c8f-a9de-8664109d599a")),
                change => change.OwnerPartyUuid.ShouldBe(Guid.Parse("453f2415-ce87-4c8f-a9de-8664109d599a")),
                change => change.UserName.ShouldBeNull(),
                change => change.IsDeleted.ShouldBeFalse(),
                change => change.ProfileType.ShouldBe(A2UserProfileType.Person));
            page1[1].ShouldSatisfyAllConditions(
                change => change.ChangeId.ShouldBe(2U),
                change => change.UserUuid.ShouldBe(Guid.Parse("69f34a20-9228-4fda-b585-4ccf0eb8af60")),
                change => change.OwnerPartyUuid.ShouldBe(Guid.Parse("69f34a20-9228-4fda-b585-4ccf0eb8af60")),
                change => change.UserName.ShouldBe("null"),
                change => change.IsDeleted.ShouldBeFalse(),
                change => change.ProfileType.ShouldBe(A2UserProfileType.SelfIdentifiedUser));

            page2.LastKnownChangeId.ShouldBe(4U);
            page2.Count.ShouldBe(2);
            page2[0].ShouldSatisfyAllConditions(
                change => change.ChangeId.ShouldBe(3U),
                change => change.UserUuid.ShouldBe(Guid.Parse("6e75fe3a-e1a7-4288-8c28-b1bc699cdcae")),
                change => change.OwnerPartyUuid.ShouldBe(Guid.Parse("6e75fe3a-e1a7-4288-8c28-b1bc699cdcae")),
                change => change.UserName.ShouldBeNull(),
                change => change.IsDeleted.ShouldBeFalse(),
                change => change.ProfileType.ShouldBe(A2UserProfileType.Person));
            page2[1].ShouldSatisfyAllConditions(
                change => change.ChangeId.ShouldBe(4U),
                change => change.UserUuid.ShouldBe(Guid.Parse("8686c09c-8fa5-48c2-bde8-2d73446d2f75")),
                change => change.OwnerPartyUuid.ShouldBe(Guid.Parse("f8c9a346-b996-4250-9333-ae6e9fe37256")),
                change => change.UserName.ShouldBe("FOOBARBAT"),
                change => change.IsDeleted.ShouldBeFalse(),
                change => change.ProfileType.ShouldBe(A2UserProfileType.EnterpriseUser));

            page3.LastKnownChangeId.ShouldBe(5U);
            page3.Count.ShouldBe(1);
            page3[0].ShouldSatisfyAllConditions(
                change => change.ChangeId.ShouldBe(5U),
                change => change.UserUuid.ShouldBe(Guid.Parse("5f7ca26d-40ed-409e-bcf9-880914803b2c")),
                change => change.OwnerPartyUuid.ShouldBe(Guid.Parse("5f7ca26d-40ed-409e-bcf9-880914803b2c")),
                change => change.UserName.ShouldBeNull(),
                change => change.IsDeleted.ShouldBeTrue(),
                change => change.ProfileType.ShouldBe(A2UserProfileType.Person));
        }
    }
}

public class SystemUserImportServiceTests
    : IntegrationTestBase
{
    [Fact]
    public async Task GetStream_Empty()
    {
        FakeHttpHandlers.For<SystemUserImportService>()
            .Expect(HttpMethod.Get, "api/v1/systemuser/internal/systemusers/stream")
            .WithPlatformToken("platform:register")
            .Respond(
                "application/json",
                """
                {
                  "stats": {
                    "pageStart": 0,
                    "pageEnd": 0,
                    "sequenceMax": 0
                  },
                  "links": {},
                  "data": []
                }
                """);

        var service = GetRequiredService<SystemUserImportService>();
        var result = await service.GetStream(cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(1);
        result[0].SequenceMax.ShouldBe(0UL);
        result[0].Count.ShouldBe(0);
    }

    [Fact]
    public async Task Multiple_Page()
    {
        FakeHttpHandlers.For<SystemUserImportService>()
            .Expect(HttpMethod.Get, "api/v1/systemuser/internal/systemusers/stream")
            .WithPlatformToken("platform:register")
            .Respond(
                "application/json",
                $$"""
                {
                  "stats": {
                    "pageStart": 1,
                    "pageEnd": 2,
                    "sequenceMax": 2
                  },
                  "links": {
                    "next": "{{FakeHttpEndpoint.HttpsUri}}api/v1/systemuser/internal/systemusers/stream?token=fake-2"
                  },
                  "data": [
                    {
                      "id": "2cacc11b-6960-413f-9894-c330f99ed7e4",
                      "partyOrgNo": "",
                      "partyId": "50692553",
                      "integrationTitle": "56a981cf-d7a2-4727-a3a4-f314530ff9cd",
                      "isDeleted": true,
                      "lastChanged": "2024-11-14T14:17:19.644093Z",
                      "created": "2024-11-14T14:17:19.644093Z",
                      "sequenceNo": 1,
                      "systemUserType": "standard"
                    },
                    {
                      "id": "704013ee-e82a-433e-83c5-a40e6e00d746",
                      "partyOrgNo": "313775429",
                      "partyId": "51655537",
                      "integrationTitle": "SmartCloud",
                      "isDeleted": false,
                      "lastChanged": "2024-11-15T09:36:50.451886Z",
                      "created": "2024-11-15T09:36:50.451886Z",
                      "sequenceNo": 2,
                      "systemUserType": "standard"
                    }
                  ]
                }
                """);

        FakeHttpHandlers.For<SystemUserImportService>()
            .Expect(HttpMethod.Get, "api/v1/systemuser/internal/systemusers/stream")
            .WithPlatformToken("platform:register")
            .WithQuery("token", "fake-2")
            .Respond(
                "application/json",
                """
                {
                  "stats": {
                    "pageStart": 3,
                    "pageEnd": 3,
                    "sequenceMax": 3
                  },
                  "links": {},
                  "data": []
                }
                """);

        var service = GetRequiredService<SystemUserImportService>();
        var result = await service.GetStream(cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(2);
        result[0].SequenceMax.ShouldBe(2UL);
        result[0].Count.ShouldBe(2);

        var item1 = result[0][0];
        var item2 = result[0][1];

        item1.ShouldSatisfyAllConditions(
            s => s.Id.ShouldBe(Guid.Parse("2cacc11b-6960-413f-9894-c330f99ed7e4")),
            s => s.IsDeleted.ShouldBe(true),
            s => s.Name.ShouldBe("56a981cf-d7a2-4727-a3a4-f314530ff9cd"),
            s => s.OwnerPartyId.ShouldBe((SystemUserItem.GuidOrUint)50692553));

        item2.ShouldSatisfyAllConditions(
            s => s.Id.ShouldBe(Guid.Parse("704013ee-e82a-433e-83c5-a40e6e00d746")),
            s => s.IsDeleted.ShouldBe(false),
            s => s.Name.ShouldBe("SmartCloud"),
            s => s.OwnerPartyId.ShouldBe((SystemUserItem.GuidOrUint)51655537));

        result[1].SequenceMax.ShouldBe(3UL);
        result[1].Count.ShouldBe(0);
    }
}
