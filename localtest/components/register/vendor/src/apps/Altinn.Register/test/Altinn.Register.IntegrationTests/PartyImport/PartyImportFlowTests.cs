using System.Net;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.PartyImport;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.TestUtils.MassTransit;
using Altinn.Register.TestUtils.TestData;
using Altinn.Urn;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.IntegrationTests.PartyImport;

public partial class PartyImportFlowTests
    : IntegrationTestBase
{
    [Fact]
    public async Task Person_ActiveProfile()
    {
        var person = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var person = await uow.CreatePerson(
                user: new PartyUserRecord(ids[0], "test-user"),
                cancellationToken: ct);

            return person;
        });

        var userId = person.UserIds.CurrentValue.Value;

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", userId.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{userId}},
                  "UserUUID": null,
                  "UserType": 1,
                  "UserName": "",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{person.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 1,
                    "SSN": "{{person.PersonIdentifier.Value}}",
                    "OrgNumber": "",
                    "Person": {
                      "SSN": "{{person.PersonIdentifier.Value}}",
                      "Name": "{{person.ShortName.Value}}",
                      "FirstName": "{{person.FirstName.Value}}",
                      "MiddleName": "{{person.MiddleName.Value}}",
                      "LastName": "{{person.LastName.Value}}",
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
                    "PartyId": {{person.PartyId.Value}},
                    "PartyUUID": "{{person.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2009-06-06T15:12:18.787+02:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{person.ShortName.Value}}",
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

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", person.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 1,
                    "SSN": "{{person.PersonIdentifier.Value}}",
                    "OrgNumber": "",
                    "Person": {
                        "SSN": "{{person.PersonIdentifier.Value}}",
                        "Name": "{{person.ShortName.Value}}",
                        "FirstName": "{{person.FirstName.Value}}",
                        "MiddleName": "{{person.MiddleName.Value}}",
                        "LastName": "{{person.LastName.Value}}",
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
                    "PartyId": {{person.PartyId.Value}},
                    "PartyUUID": "{{person.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2009-06-06T15:12:18.787+02:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{person.ShortName.Value}}",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        FakeHttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/personer/{personIdentifier}")
            .WithRouteValue("personIdentifier", person.PersonIdentifier.Value!.ToString())
            .Respond(HttpStatusCode.NotFound);

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = userId,
            PartyUuid = person.PartyUuid.Value,
            OwnerPartyUuid = person.PartyUuid.Value,
            IsDeleted = false,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var sagaCompletedEvt = await conversation.Events.OfType<SagaCompletedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var evt = await conversation.Events.Completed.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var rolesAddedEvts = await conversation.Events.Completed.OfType<ExternalRoleAssignmentAddedEvent>().ToListAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(person.PartyUuid.Value);

        rolesAddedEvts.ShouldBeEmpty();

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var updated = await persistence.GetPartyById(person.PartyUuid.Value, PartyFieldIncludes.User, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.UserIds.CurrentValue.ShouldHaveValue();
            updated.Usernames.CurrentValue.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Person_WithGuardians()
    {
        var (person, guardians) = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var person = await uow.CreatePerson(
                user: new PartyUserRecord(ids[0], "test-user"),
                cancellationToken: ct);

            var guardians = await uow.CreatePeople(2, ct);

            return (person, guardians);
        });

        var userId = person.UserIds.CurrentValue.Value;

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", userId.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{userId}},
                  "UserUUID": null,
                  "UserType": 1,
                  "UserName": "",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{person.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 1,
                    "SSN": "{{person.PersonIdentifier.Value}}",
                    "OrgNumber": "",
                    "Person": {
                      "SSN": "{{person.PersonIdentifier.Value}}",
                      "Name": "{{person.ShortName.Value}}",
                      "FirstName": "{{person.FirstName.Value}}",
                      "MiddleName": "{{person.MiddleName.Value}}",
                      "LastName": "{{person.LastName.Value}}",
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
                    "PartyId": {{person.PartyId.Value}},
                    "PartyUUID": "{{person.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2009-06-06T15:12:18.787+02:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{person.ShortName.Value}}",
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

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", person.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 1,
                    "SSN": "{{person.PersonIdentifier.Value}}",
                    "OrgNumber": "",
                    "Person": {
                        "SSN": "{{person.PersonIdentifier.Value}}",
                        "Name": "{{person.ShortName.Value}}",
                        "FirstName": "{{person.FirstName.Value}}",
                        "MiddleName": "{{person.MiddleName.Value}}",
                        "LastName": "{{person.LastName.Value}}",
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
                    "PartyId": {{person.PartyId.Value}},
                    "PartyUUID": "{{person.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2009-06-06T15:12:18.787+02:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{person.ShortName.Value}}",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        FakeHttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/personer/{personIdentifier}")
            .WithRouteValue("personIdentifier", person.PersonIdentifier.Value!.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "identifikasjonsnummer": [
                    {
                      "ajourholdstidspunkt": "2020-12-22T17:09:36.294+01:00",
                      "erGjeldende": true,
                      "kilde": "KILDE_DSF",
                      "status": "iBruk",
                      "foedselsEllerDNummer": "{{person.PersonIdentifier.Value}}",
                      "identifikatortype": "foedselsnummer"
                    }
                  ],
                  "status": [
                    {
                      "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                      "erGjeldende": true,
                      "kilde": "Synutopia",
                      "aarsak": "Vergemål",
                      "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                      "status": "bosatt"
                    }
                  ],
                  "foedsel": [
                    {
                      "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                      "erGjeldende": true,
                      "kilde": "Synutopia",
                      "aarsak": "Vergemål",
                      "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                      "foedselsdato": "{{person.DateOfBirth.Value:yyyy-MM-dd}}"
                    }
                  ],
                  "navn": [
                    {
                      "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                      "erGjeldende": true,
                      "kilde": "Synutopia",
                      "aarsak": "Vergemål",
                      "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                      "fornavn": "{{person.FirstName.Value}}",
                      "mellomnavn": "{{person.MiddleName.Value}}",
                      "etternavn": "{{person.LastName.Value}}",
                      "forkortetNavn": "{{person.ShortName.Value}}"
                    }
                  ],
                  "vergemaalEllerFremtidsfullmakt": [
                    {
                      "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                      "erGjeldende": true,
                      "kilde": "Synutopia",
                      "aarsak": "Vergemål",
                      "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                      "vergemaaltype": "voksen",
                      "embete": "statsforvaltarenIMoereOgRomsdal",
                      "verge": {
                        "foedselsEllerDNummer": "{{guardians[0].PersonIdentifier.Value}}",
                        "tjenesteomraade": [
                          {
                            "vergeTjenestevirksomhet": "nav",
                            "vergeTjenesteoppgave": "arbeid"
                          }
                        ]
                      }
                    },
                    {
                      "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                      "erGjeldende": true,
                      "kilde": "Synutopia",
                      "aarsak": "Vergemål",
                      "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                      "vergemaaltype": "voksen",
                      "embete": "statsforvaltarenIMoereOgRomsdal",
                      "verge": {
                        "foedselsEllerDNummer": "{{guardians[1].PersonIdentifier.Value}}",
                        "tjenesteomraade": [
                          {
                            "vergeTjenestevirksomhet": "nav",
                            "vergeTjenesteoppgave": "arbeid"
                          },
                          {
                            "vergeTjenestevirksomhet": "kartverket",
                            "vergeTjenesteoppgave": "arvPrivatSkifteOgUskifte"
                          },
                          {
                            "vergeTjenestevirksomhet": "oevrige",
                            "vergeTjenesteoppgave": "avslutningAvHusleiekontrakter"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = userId,
            PartyUuid = person.PartyUuid.Value,
            OwnerPartyUuid = person.PartyUuid.Value,
            IsDeleted = false,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var sagaCompletedEvt = await conversation.Events.OfType<SagaCompletedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var evt = await conversation.Events.Completed.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var rolesAddedEvts = await conversation.Events.Completed.OfType<ExternalRoleAssignmentAddedEvent>().ToListAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(person.PartyUuid.Value);

        rolesAddedEvts.Count.ShouldBe(4);
        var fromParty = new PartyReference(person.PartyUuid.Value);
        var to0 = new PartyReference(guardians[0].PartyUuid.Value);
        var to1 = new PartyReference(guardians[1].PartyUuid.Value);

        rolesAddedEvts.ShouldAllBe(e => e.From == fromParty);
        rolesAddedEvts.ShouldContain(e => e.To == to0 && e.Role.Identifier == "nav-arbeid");
        rolesAddedEvts.ShouldContain(e => e.To == to1 && e.Role.Identifier == "nav-arbeid");
        rolesAddedEvts.ShouldContain(e => e.To == to1 && e.Role.Identifier == "kartverket-arv-privat-skifte-uskifte");
        rolesAddedEvts.ShouldContain(e => e.To == to1 && e.Role.Identifier == "ovrige-avslutning-husleiekontrakter");

        await Check(async (uow, ct) =>
        {
            var parties = uow.GetPartyPersistence();
            var updated = await parties.GetPartyById(person.PartyUuid.Value, PartyFieldIncludes.User, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.UserIds.CurrentValue.ShouldHaveValue();
            updated.Usernames.CurrentValue.ShouldBeNull();

            var roles = uow.GetPartyExternalRolePersistence();
            var assignedRoles = await roles.GetExternalRoleAssignmentsFromParty(person.PartyUuid.Value, cancellationToken: ct).ToListAsync(ct);
            assignedRoles.Count.ShouldBe(4);
            assignedRoles.ShouldContain(r => r.ToParty == to0.PartyUuid && r.Identifier == "nav-arbeid");
            assignedRoles.ShouldContain(r => r.ToParty == to1.PartyUuid && r.Identifier == "nav-arbeid");
            assignedRoles.ShouldContain(r => r.ToParty == to1.PartyUuid && r.Identifier == "kartverket-arv-privat-skifte-uskifte");
            assignedRoles.ShouldContain(r => r.ToParty == to1.PartyUuid && r.Identifier == "ovrige-avslutning-husleiekontrakter");
        });
    }

    [Fact]
    public async Task SelfIdentified_ActiveProfile()
    {
        var siUser = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var siUser = await uow.CreateSelfIdentifiedUser(
                user: new PartyUserRecord(ids[0], "TestSelfIdentifiedUser"),
                type: SelfIdentifiedUserType.Legacy,
                cancellationToken: ct);

            return siUser;
        });

        var userId = siUser.UserIds.CurrentValue.Value;

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", userId.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{userId}},
                  "UserUUID": "{{siUser.PartyUuid}}",
                  "UserType": 2,
                  "UserName": "updated-name",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{siUser.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "updated-name",
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

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "updated-name",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = userId,
            PartyUuid = siUser.PartyUuid.Value,
            OwnerPartyUuid = siUser.PartyUuid.Value,
            IsDeleted = false,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(siUser.PartyUuid.Value);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var updated = await persistence.GetPartyById(siUser.PartyUuid.Value, PartyFieldIncludes.User | PartyFieldIncludes.Party, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.UserIds.CurrentValue.ShouldHaveValue();
            updated.Usernames.CurrentValue.ShouldBe("updated-name");
            updated.DisplayName.ShouldBe("updated-name");
            updated.IsDeleted.ShouldBe(false);
        });
    }

    [Fact]
    public async Task SelfIdentified_InactiveProfile()
    {
        var siUser = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var siUser = await uow.CreateSelfIdentifiedUser(
                user: new PartyUserRecord(ids[0], "TestSelfIdentifiedUser"),
                type: SelfIdentifiedUserType.Legacy,
                cancellationToken: ct);

            return siUser;
        });

        var userId = siUser.UserIds.CurrentValue.Value;

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", userId.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{userId}},
                  "UserUUID": "{{siUser.PartyUuid}}",
                  "UserType": 2,
                  "UserName": "updated-name",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": false,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{siUser.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.00+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "updated-name",
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

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.00+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "updated-name",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = userId,
            PartyUuid = siUser.PartyUuid.Value,
            OwnerPartyUuid = siUser.PartyUuid.Value,
            IsDeleted = true,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(siUser.PartyUuid.Value);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var updated = await persistence.GetPartyById(siUser.PartyUuid.Value, PartyFieldIncludes.User | PartyFieldIncludes.Party, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.ShouldSatisfyAllConditions(
                u => u.UserIds.CurrentValue.ShouldHaveValue(),
                u => u.Usernames.CurrentValue.ShouldBe("updated-name"),
                u => u.DisplayName.ShouldBe("updated-name"),
                u => u.IsDeleted.ShouldBe(true),
                u => u.DeletedAt.ShouldBe(new DateTimeOffset(2010, 03, 02, 0, 53, 44, TimeSpan.Zero)));
        });
    }

    [Fact]
    public async Task SelfIdentified_EducationalUser()
    {
        var siUser = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var userIds = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            return await testDataGenerator.GetSelfIdentifiedUserData(
                type: SelfIdentifiedUserType.Educational,
                user: new PartyUserRecord(userIds[0], "TestEducationalUser"),
                cancellationToken: ct);
        });

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{siUser.DisplayName.Value}}",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users")
            .WithQuery("userUUID", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{siUser.UserIds.CurrentValue.Value}},
                  "UserUUID": "{{siUser.PartyUuid}}",
                  "UserType": 2,
                  "UserName": "{{siUser.Usernames.CurrentValue.Value}}",
                  "ExternalIdentity": "some-random-gibberish",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{siUser.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{siUser.DisplayName.Value}}",
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

        var cmd = new ImportA2PartyCommand
        {
            PartyUuid = siUser.PartyUuid.Value,
            ChangeId = 1,
            ChangedTime = TimeProvider.GetUtcNow(),
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(siUser.PartyUuid.Value);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var created = await persistence.GetPartyById(siUser.PartyUuid.Value, PartyFieldIncludes.User | PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, ct).FirstOrDefaultAsync(ct);
            created.ShouldNotBeNull();
            created.UserIds.CurrentValue.ShouldHaveValue();
            created.Usernames.CurrentValue.ShouldBe(siUser.Usernames.CurrentValue);
            created.DisplayName.ShouldBe(siUser.DisplayName);
            created.IsDeleted.ShouldBe(false);

            var createdSi = created.ShouldBeOfType<SelfIdentifiedUserRecord>();
            createdSi.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.Educational);
            createdSi.ExtRef.ShouldBe("some-random-gibberish");
        });
    }

    [Fact]
    public async Task SelfIdentified_EmailUser()
    {
        var email = "ALL_CAPS_LOVER@example.com";
        var siUser = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var userIds = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            return await testDataGenerator.GetSelfIdentifiedUserData(
                type: SelfIdentifiedUserType.IdPortenEmail,
                email: email,
                user: new PartyUserRecord(userIds[0], "TestEmailUser"),
                cancellationToken: ct);
        });

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{siUser.DisplayName.Value}}",
                    "IsDeleted": false,
                    "OnlyHierarchyElementWithNoAccess": false,
                    "ChildParties": null
                }
                """);

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users")
            .WithQuery("userUUID", siUser.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{siUser.UserIds.CurrentValue.Value}},
                  "UserUUID": "{{siUser.PartyUuid}}",
                  "UserType": 2,
                  "UserName": "{{siUser.Usernames.CurrentValue.Value}}",
                  "ExternalIdentity": "{{PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(email))}}",
                  "IsReserved": false,
                  "IsActive": true,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{siUser.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 3,
                    "SSN": "",
                    "OrgNumber": "",
                    "Person": null,
                    "Organization": null,
                    "PartyId": {{siUser.PartyId.Value}},
                    "PartyUUID": "{{siUser.PartyUuid.Value}}",
                    "UnitType": null,
                    "LastChangedInAltinn": "2010-03-02T01:53:44.87+01:00",
                    "LastChangedInExternalRegister": null,
                    "Name": "{{siUser.DisplayName.Value}}",
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

        var cmd = new ImportA2PartyCommand
        {
            PartyUuid = siUser.PartyUuid.Value,
            ChangeId = 1,
            ChangedTime = TimeProvider.GetUtcNow(),
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(siUser.PartyUuid.Value);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var created = await persistence.GetPartyById(siUser.PartyUuid.Value, PartyFieldIncludes.User | PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, ct).FirstOrDefaultAsync(ct);
            created.ShouldNotBeNull();
            created.UserIds.CurrentValue.ShouldHaveValue();
            created.Usernames.CurrentValue.ShouldBe(siUser.Usernames.CurrentValue);
            created.DisplayName.ShouldBe(email.ToLowerInvariant());
            created.IsDeleted.ShouldBe(false);

            var createSi = created.ShouldBeOfType<SelfIdentifiedUserRecord>();
            createSi.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
            createSi.Email.ShouldBe(email.ToLowerInvariant());
        });
    }

    [Fact]
    public async Task Enterprise_ActiveProfile()
    {
        var uuid = Guid.NewGuid();
        var (org, id) = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var org = await uow.CreateOrg(cancellationToken: ct);

            return (org, ids[0]);
        });

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", id.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{id}},
                  "UserUUID": "{{uuid}}",
                  "UserType": 3,
                  "UserName": "enterprise-user-name",
                  "ExternalIdentity": "",
                  "IsActive": true,
                  "IsReserved": false,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{org.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 2,
                    "SSN": "",
                    "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
                    "Person": null,
                    "Organization": {
                      "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
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
                    "PartyId": {{org.PartyId.Value}},
                    "PartyUUID": "{{org.PartyUuid.Value}}",
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

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = id,
            PartyUuid = uuid,
            OwnerPartyUuid = org.PartyUuid.Value,
            IsDeleted = false,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(uuid);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var updated = await persistence.GetPartyById(uuid, PartyFieldIncludes.User | PartyFieldIncludes.Party, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.ShouldBeOfType<EnterpriseUserRecord>();
            updated.OwnerUuid.ShouldBe(org.PartyUuid.Value);
            updated.UserIds.CurrentValue.ShouldHaveValue();
            updated.Usernames.CurrentValue.ShouldBe("enterprise-user-name");
            updated.DisplayName.ShouldBe("enterprise-user-name");
            updated.IsDeleted.ShouldBe(false);
        });
    }

    [Fact]
    public async Task Enterprise_InactiveProfile()
    {
        var uuid = Guid.NewGuid();
        var (org, id) = await Setup(async (uow, ct) =>
        {
            var testDataGenerator = uow.GetRequiredService<RegisterTestDataGenerator>();
            var tracking = uow.GetRequiredService<IImportJobTracker>();

            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = 100 }, ct);

            var ids = await testDataGenerator.GetNextUserIds(1, cancellationToken: ct);
            var org = await uow.CreateOrg(cancellationToken: ct);

            return (org, ids[0]);
        });

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/{userId}")
            .WithRouteValue("userId", id.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "UserId": {{id}},
                  "UserUUID": "{{uuid}}",
                  "UserType": 3,
                  "UserName": "enterprise-user-name",
                  "ExternalIdentity": "",
                  "IsReserved": false,
                  "IsActive": false,
                  "PhoneNumber": null,
                  "Email": null,
                  "PartyId": {{org.PartyId.Value}},
                  "Party": {
                    "PartyTypeName": 2,
                    "SSN": "",
                    "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
                    "Person": null,
                    "Organization": {
                      "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
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
                    "PartyId": {{org.PartyId.Value}},
                    "PartyUUID": "{{org.PartyUuid.Value}}",
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

        var cmd = new ImportA2UserProfileCommand
        {
            UserId = id,
            PartyUuid = uuid,
            OwnerPartyUuid = org.PartyUuid.Value,
            IsDeleted = true,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        evt.Party.PartyUuid.ShouldBe(uuid);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var updated = await persistence.GetPartyById(uuid, PartyFieldIncludes.User | PartyFieldIncludes.Party, ct).FirstOrDefaultAsync(ct);
            updated.ShouldNotBeNull();
            updated.ShouldBeOfType<EnterpriseUserRecord>();
            updated.OwnerUuid.ShouldBe(org.PartyUuid.Value);
            updated.UserIds.CurrentValue.ShouldHaveValue();
            updated.Usernames.CurrentValue.ShouldBe("enterprise-user-name");
            updated.DisplayName.ShouldBe("enterprise-user-name");
            updated.IsDeleted.ShouldBe(true);
        });
    }

    [Fact]
    public async Task UpsertPartyCommand_UpsertsParty()
    {
        await Setup(async (uow, ct) =>
        {
            await GetRequiredService<IImportJobTracker>()
                .TrackQueueStatus("test", new() { EnqueuedMax = 100, SourceMax = null }, ct);
        });

        var partyUuid = Guid.CreateVersion7();
        var partyId = 1U;

        var person = new PersonRecord
        {
            PartyUuid = partyUuid,
            PartyId = partyId,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("25871999336")),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = PersonIdentifier.Parse("25871999336"),
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,

            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = new DateOnly(1919, 7, 25),
            DateOfDeath = FieldValue.Null,
        };

        var cmd1 = new UpsertPartyCommand
        {
            Party = person,
            Tracking = new("test", 10),
        };

        await CommandSender.Send(cmd1, TestContext.Current.CancellationToken);
        var conversation = await TestHarness.Conversation(cmd1, TestContext.Current.CancellationToken);
        var updateEvent = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        updateEvent.ShouldNotBeNull();
        updateEvent.Party.PartyUuid.ShouldBe(partyUuid);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var fromDb = await persistence.GetPartyById(partyUuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, ct).FirstAsync(ct);

            fromDb.ShouldBe(person with { VersionId = fromDb!.VersionId });
        });

        var personUpdated = person with
        {
            Address = new StreetAddressRecord
            {
                StreetName = "Testveien",
                HouseNumber = "1",
                PostalCode = "1234",
                City = "Testby",
            },
            DateOfDeath = new DateOnly(2018, 4, 12),
        };

        var cmd2 = new UpsertPartyCommand
        {
            Party = personUpdated,
            Tracking = new("test", 20),
        };

        await CommandSender.Send(cmd2, TestContext.Current.CancellationToken);
        conversation = await TestHarness.Conversation(cmd2, TestContext.Current.CancellationToken);
        updateEvent = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        updateEvent.ShouldNotBeNull();
        updateEvent.Party.PartyUuid.ShouldBe(partyUuid);

        await Check(async (uow, ct) =>
        {
            var persistence = uow.GetPartyPersistence();
            var fromDb = await persistence.GetPartyById(partyUuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, ct).FirstAsync(ct);
            fromDb.ShouldBe(personUpdated with { VersionId = fromDb!.VersionId });
        });
    }

    [Fact]
    public async Task Batches_With_Errors()
    {
        var person1 = new PersonRecord
        {
            PartyUuid = Guid.NewGuid(),
            PartyId = 1,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("25871999336")),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = PersonIdentifier.Parse("25871999336"),
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,

            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = new DateOnly(1919, 7, 25),
            DateOfDeath = FieldValue.Null,
        };

        var person2 = person1 with { PartyUuid = Guid.NewGuid(), PartyId = 2 };

        IReadOnlyList<UpsertPartyCommand> cmds = [
            new UpsertPartyCommand { Party = person1 },
            new UpsertPartyCommand { Party = person2 },
        ];

        await CommandSender.Send(cmds, TestContext.Current.CancellationToken);

        var evts = TestHarness.Published.SelectAsync<PartyUpdatedEvent>(TestContext.Current.CancellationToken);
        var updateEvent = await evts.FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        updateEvent.ShouldNotBeNull();

        await Check(async (uow, ct) =>
        {
            var insertedPerson = updateEvent.Context.Message.Party.PartyUuid == person1.PartyUuid ? person1 : person2;
            var persistence = uow.GetPartyPersistence();
            var fromDb = await persistence.GetPartyById(insertedPerson.PartyUuid.Value, PartyFieldIncludes.Party | PartyFieldIncludes.Person, ct).FirstAsync(ct);
            fromDb.ShouldBe(insertedPerson with { VersionId = fromDb!.VersionId });
        });
    }

    private async Task<PartyUpdatedEvent> UpsertParty<T>(T cmd)
        where T : CommandBase
    {
        await CommandSender.Send(cmd, cancellationToken: CancellationToken);
        var conversation = await TestHarness.Conversation(cmd, CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(CancellationToken);

        evt.ShouldNotBeNull($"{nameof(PartyUpdatedEvent)} was not published");
        return evt;
    }
}
