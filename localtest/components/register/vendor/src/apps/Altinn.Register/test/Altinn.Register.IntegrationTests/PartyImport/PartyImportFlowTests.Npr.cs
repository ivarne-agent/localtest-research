using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.TestUtils.MassTransit;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.IntegrationTests.PartyImport;

public partial class PartyImportFlowTests
{
    [Fact]
    public async Task NprImportFlow_CanCreatePerson()
    {
        var (birthDate, personIdentifier, guardian) = await Setup(async (uow, ct) =>
        {
            var tracking = uow.GetRequiredService<IImportJobTracker>();
            await tracking.TrackQueueStatus("test", new() { EnqueuedMax = 1, SourceMax = null }, ct);

            var gen = uow.GetRequiredService<RegisterTestDataGenerator>();
            var birthDate = gen.GetRandomBirthDate();
            var personIdentifier = await gen.GetNewPersonIdentifier(birthDate: birthDate, cancellationToken: ct);

            var guardian = await uow.CreatePerson(cancellationToken: ct);

            return (birthDate, personIdentifier, guardian);
        });

        FakeHttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/personer/{personIdentifier}")
            .WithRouteValue("personIdentifier", personIdentifier.ToString())
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
                            "foedselsEllerDNummer": "{{personIdentifier}}",
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
                            "foedselsdato": "{{birthDate:yyyy-MM-dd}}"
                        }
                    ],
                    "navn": [
                        {
                            "ajourholdstidspunkt": "2023-11-20T14:00:18.540646+01:00",
                            "erGjeldende": true,
                            "kilde": "Synutopia",
                            "aarsak": "Vergemål",
                            "gyldighetstidspunkt": "2021-11-28T00:00:00+01:00",
                            "fornavn": "Fornavn",
                            "mellomnavn": "Mellomnavn",
                            "etternavn": "Etternavn",
                            "forkortetNavn": "Forkortet Navn"
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
                                "foedselsEllerDNummer": "{{guardian.PersonIdentifier.Value}}",
                                "tjenesteomraade": [
                                    {
                                        "vergeTjenestevirksomhet": "nav",
                                        "vergeTjenesteoppgave": "arbeid"
                                    },
                                    {
                                        "vergeTjenestevirksomhet": "kartverket",
                                        "vergeTjenesteoppgave": "arvPrivatSkifteOgUskifte"
                                    }
                                ]
                            }
                        }
                    ]
                }
                """);

        var cmd = new ImportNprPartyCommand
        {
            PersonIdentifier = personIdentifier,
            ChangeId = 1,
            ChangedTime = TimeProvider.GetUtcNow(),
            Tracking = new("test", 1),
        };

        await CommandSender.Send(cmd, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(cmd, TestContext.Current.CancellationToken);
        var sagaCompletedEvt = await conversation.Events.OfType<SagaCompletedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var evt = await conversation.Events.Completed.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var rolesAddedEvts = await conversation.Events.Completed.OfType<ExternalRoleAssignmentAddedEvent>().ToListAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        rolesAddedEvts.Count.ShouldBe(2);

        await Check(async (uow, ct) =>
        {
            var person = await uow.GetPartyPersistence()
                .GetPersonByIdentifier(
                    personIdentifier,
                    include: PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.User,
                    cancellationToken: ct)
                .FirstOrDefaultAsync(ct);

            person.ShouldNotBeNull();
            person.PersonIdentifier.ShouldBe(personIdentifier);
            person.PartyUuid.ShouldBe(evt.Party.PartyUuid);
            person.DisplayName.ShouldBe("Fornavn Mellomnavn Etternavn");
            person.FirstName.ShouldBe("Fornavn");
            person.MiddleName.ShouldBe("Mellomnavn");
            person.LastName.ShouldBe("Etternavn");
            person.ShortName.ShouldBe("Forkortet Navn");
            person.DateOfBirth.ShouldBe(birthDate);

            var partyId = person.PartyId.ShouldHaveValue();
            person.UserIds.CurrentValue.ShouldBe(partyId);
            person.Usernames.CurrentValue.ShouldBeNull();
        });
    }
}
