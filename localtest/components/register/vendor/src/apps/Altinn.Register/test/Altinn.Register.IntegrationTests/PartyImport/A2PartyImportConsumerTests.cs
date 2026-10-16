using System.Net;
using System.Net.Http.Json;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.TestUtils.MassTransit;
using Altinn.Register.TestUtils.TestData;
using Xunit.Sdk;
using V1Models = Altinn.Register.Contracts.V1;

namespace Altinn.Register.IntegrationTests.PartyImport;

public class A2PartyImportConsumerTests
    : IntegrationTestBase
{
    [Fact]
    public async Task ImportA2PartyCommand_FetchesParty_AndUpsertsParty()
    {
        var (org, person1, person2) = await Setup(async (uow, ct) =>
        {
            var org = await uow.CreateOrg(isDeleted: false, cancellationToken: ct);
            var person1 = await uow.CreatePerson(cancellationToken: ct);
            var person2 = await uow.CreatePerson(cancellationToken: ct);

            var roles = uow.GetPartyExternalRolePersistence();
            await roles.UpsertExternalRolesFromPartyBySource(
                commandId: Guid.CreateVersion7(),
                partyUuid: org.PartyUuid.Value,
                roleSource: ExternalRoleSource.CentralCoordinatingRegister,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                    new("styreleder", person2.PartyUuid.Value),
                ]),
                cancellationToken: ct);

            return (org, person1, person2);
        });

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", org.PartyUuid.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                {
                  "PartyTypeName": 2,
                  "SSN": "",
                  "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
                  "Person": null,
                  "Organization": {
                    "OrgNumber": "{{org.OrganizationIdentifier.Value}}",
                    "Name": "{{org.DisplayName.Value}}",
                    "UnitType": "{{org.UnitType.Value}}",
                    "UnitStatus": "{{org.UnitStatus.Value}}",
                    "TelephoneNumber": "{{org.TelephoneNumber.Value}}",
                    "MobileNumber": "{{org.MobileNumber.Value}}",
                    "FaxNumber": "{{org.FaxNumber.Value}}",
                    "EMailAddress": "{{org.EmailAddress.Value}}",
                    "InternetAddress": "{{org.InternetAddress.Value}}",
                    "MailingAddress": "{{org.MailingAddress.SelectFieldValue(m => FieldValue.Create(m.Address)).Value}}",
                    "MailingPostalCode": "{{org.MailingAddress.SelectFieldValue(m => FieldValue.Create(m.PostalCode)).Value}}",
                    "MailingPostalCity": "{{org.MailingAddress.SelectFieldValue(m => FieldValue.Create(m.City)).Value}}",
                    "BusinessAddress": "{{org.BusinessAddress.SelectFieldValue(m => FieldValue.Create(m.Address)).Value}}",
                    "BusinessPostalCode": "{{org.BusinessAddress.SelectFieldValue(m => FieldValue.Create(m.PostalCode)).Value}}",
                    "BusinessPostalCity": "{{org.BusinessAddress.SelectFieldValue(m => FieldValue.Create(m.City)).Value}}"
                  },
                  "PartyId": {{org.PartyId.Value}},
                  "PartyUuid": "{{org.PartyUuid.Value}}",
                  "UnitType": "{{org.UnitType.Value}}",
                  "Name": "{{org.DisplayName.Value}}",
                  "IsDeleted": false,
                  "OnlyHierarchyElementWithNoAccess": false,
                  "ChildParties": null
                }
                """);

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties/partyroles/{fromPartyId}")
            .WithRouteValue("fromPartyId", org.PartyId.Value.ToString())
            .Respond(
                "application/json",
                $$"""
                    [
                        {"PartyId": "{{person1.PartyId.Value}}", "PartyUuid": "{{person1.PartyUuid.Value}}", "PartyRelation": "Role", "RoleCode": "DAGL"},
                        {"PartyId": "{{person2.PartyId.Value}}", "PartyUuid": "{{person2.PartyUuid.Value}}", "PartyRelation": "Role", "RoleCode": "MEDL"}
                    ]
                    """);

        var msg = new ImportA2PartyCommand { PartyUuid = org.PartyUuid.Value, ChangeId = 1, ChangedTime = TimeProvider.GetUtcNow() };
        await CommandSender.Send(msg, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(msg, TestContext.Current.CancellationToken);
        var orgEvent = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        var roleAddedEvents = await conversation.Events.OfType<ExternalRoleAssignmentAddedEvent>().Take(2).ToListAsync(TestContext.Current.CancellationToken);
        var roleRemovedEvents = await conversation.Events.OfType<ExternalRoleAssignmentRemovedEvent>().Take(1).ToListAsync(TestContext.Current.CancellationToken);

        orgEvent.ShouldNotBeNull();
        roleAddedEvents.Count.ShouldBe(2);
        roleRemovedEvents.Count.ShouldBe(1);

        await Check(async (uow, ct) =>
        {
            var parties = uow.GetPartyPersistence();
            var roles = uow.GetPartyExternalRolePersistence();
            var party = await parties.GetPartyById(orgEvent.Party.PartyUuid, include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization, ct).FirstOrDefaultAsync(ct);

            var actual = party.ShouldBeOfType<OrganizationRecord>();
            actual.ShouldSatisfyAllConditions([
                (p) => p.PartyId.ShouldBe(org.PartyId),
                (p) => p.PartyUuid.ShouldBe(org.PartyUuid),
                (p) => p.DisplayName.ShouldBe(org.DisplayName),
                (p) => p.UnitType.ShouldBe(org.UnitType),
            ]);

            var assignments = await roles.GetExternalRoleAssignmentsFromParty(
                orgEvent.Party.PartyUuid,
                cancellationToken: ct)
                .ToListAsync(ct);

            assignments.Count.ShouldBe(2);
            assignments.ShouldContain(a => a.FromParty == party.PartyUuid && a.ToParty == person1.PartyUuid && a.Identifier.Value == "daglig-leder" && a.Source.Value == ExternalRoleSource.CentralCoordinatingRegister);
            assignments.ShouldContain(a => a.FromParty == party.PartyUuid && a.ToParty == person2.PartyUuid && a.Identifier.Value == "styremedlem" && a.Source.Value == ExternalRoleSource.CentralCoordinatingRegister);
        });
    }

    [Theory]
    [MemberData(nameof(PartyNameHandlingData))]
    public async Task ImportA2PartyCommand_NameHandling(PersonNameInput input)
    {
        var userId = 1;
        var party = new V1Models.Party
        {
            PartyId = 50012345,
            PartyTypeName = V1Models.PartyType.Person,
            OrgNumber = null,
            SSN = "25871999336",
            UnitType = null,
            Name = "Ola Bla Nordmann",
            IsDeleted = false,
            PartyUuid = Guid.Parse("195BD1DC-F136-48FE-9BA4-959949A1B067"),
            OnlyHierarchyElementWithNoAccess = false,
            Person = new V1Models.Person
            {
                SSN = "25871999336",
                Name = "Ola Bla Nordmann",
                FirstName = "Ola",
                MiddleName = "Bla",
                LastName = "Nordmann",
                TelephoneNumber = "12345678",
                MobileNumber = "87654321",
                MailingAddress = "Blåbæreveien 7 8450 Stokmarknes",
                MailingPostalCode = "8450",
                MailingPostalCity = "Stokmarknes",
                AddressMunicipalNumber = "1866",
                AddressMunicipalName = "Hadsel",
                AddressStreetName = "Blåbærveien",
                AddressHouseNumber = "7",
                AddressHouseLetter = "G",
                AddressPostalCode = "8450",
                AddressCity = "Stokarknes"
            },
            Organization = null,
            ChildParties = null,
        };

        party.Name = input.Name!;
        party.Person.Name = input.Name!;
        party.Person.FirstName = input.FirstName;
        party.Person.MiddleName = input.MiddleName;
        party.Person.LastName = input.LastName;

        var partyUuid = party.PartyUuid;
        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/register/api/parties")
            .WithQuery("partyuuid", partyUuid.ToString())
            .Respond(() => JsonContent.Create(party));

        FakeHttpHandlers.For<IA2PartyImportService>()
            .Expect(HttpMethod.Get, "/profile/api/users/getorcreate/{uuid}")
            .WithRouteValue("uuid", partyUuid.ToString())
            .Respond(() => JsonContent.Create(new
            {
                UserId = userId,
                UserUUID = partyUuid,
                UserType = 1,
                UserName = (string?)null,
                ExternalIdentity = (string?)null,
                IsReserved = false,
                IsActive = false,
                PhoneNumber = (string?)null,
                Email = (string?)null,
                PartyId = party.PartyId,
                Party = party,
                ProfileSettingPreference = new
                {
                    Language = "nb",
                    PreSelectedPartyId = 0,
                    DoNotPromptForParty = false,
                }
            }));

        FakeHttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/personer/{personIdentifier}")
            .WithRouteValue("personIdentifier", "25871999336")
            .Respond(HttpStatusCode.NotFound);

        var msg = new ImportA2PartyCommand { PartyUuid = partyUuid, ChangeId = 1, ChangedTime = TimeProvider.GetUtcNow() };
        await CommandSender.Send(msg, TestContext.Current.CancellationToken);

        var conversation = await TestHarness.Conversation(msg, TestContext.Current.CancellationToken);
        var evt = await conversation.Events.OfType<PartyUpdatedEvent>().FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        evt.ShouldNotBeNull();
        await Check(async (uow, ct) =>
        {
            var parties = uow.GetPartyPersistence();
            var party = await parties.GetPartyById(evt.Party.PartyUuid, include: PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.User, ct).FirstOrDefaultAsync(ct);

            var person = party.ShouldBeOfType<PersonRecord>();
            person.ShouldSatisfyAllConditions([
                (p) => p.PartyId.ShouldBe((uint)party.PartyId),
                (p) => p.PartyUuid.ShouldBe(partyUuid),
                (p) => p.DisplayName.ShouldBe(input.ExpectedName),
                (p) => p.FirstName.ShouldBe(input.ExpectedFirstName),
                (p) => p.MiddleName.ShouldBe(input.ExpectedMiddleName),
                (p) => p.LastName.ShouldBe(input.ExpectedLastName),
            ]);
        });
    }

    public static TheoryData<PersonNameInput> PartyNameHandlingData
        => [
        new PersonNameInput()
        {
            Name = null,
            FirstName = null,
            LastName = null,
            ExpectedName = "Mangler Navn",
            ExpectedFirstName = "Mangler",
            ExpectedLastName = "Navn",
        },

        new PersonNameInput()
        {
            Name = "Gul-Brun Sint Tiger",
            FirstName = "Sint Tiger",
            LastName = "Gul-Brun",
            ExpectedName = "Sint Tiger Gul-Brun",
        },

        new PersonNameInput()
        {
            Name = "Inserted By ER Import",
            FirstName = null,
            LastName = null,
            ExpectedFirstName = "Mangler",
            ExpectedLastName = "Navn",
            ExpectedName = "Mangler Navn",
        },

        new PersonNameInput()
        {
            Name = "Ikke i Altinn register",
            FirstName = null,
            LastName = null,
            ExpectedFirstName = "Mangler",
            ExpectedLastName = "Navn",
            ExpectedName = "Mangler Navn",
        },

        new PersonNameInput()
        {
            Name = "Inserted By FReg Import",
            FirstName = null,
            LastName = null,
            ExpectedFirstName = "Mangler",
            ExpectedLastName = "Navn",
            ExpectedName = "Mangler Navn",
        },

        new PersonNameInput()
        {
            Name = "Gul-Brun Sint Tiger",
            FirstName = null,
            LastName = null,
            ExpectedFirstName = "Sint Tiger",
            ExpectedLastName = "Gul-Brun",
            ExpectedName = "Sint Tiger Gul-Brun",
        },

        new PersonNameInput()
        {
            Name = null,
            FirstName = "Sint Tiger",
            LastName = "Gul-Brun",
            ExpectedName = "Sint Tiger Gul-Brun",
        },

        new PersonNameInput()
        {
            Name = null,
            FirstName = "Sint Tiger",
            MiddleName = "Jr.",
            LastName = "Gul-Brun",
            ExpectedName = "Sint Tiger Jr. Gul-Brun",
        },

        // Test that Name is built up from FirstName, MiddleName and LastName
        new PersonNameInput()
        {
            Name = "Gul-Brun S. Tiger",
            FirstName = "Sint Tiger",
            LastName = "Gul-Brun",
            ExpectedName = "Sint Tiger Gul-Brun",
        },

        new PersonNameInput()
        {
            Name = "Gul-Grønn Sint T. R.",
            FirstName = "Sint Tiger",
            MiddleName = "Rød",
            LastName = "von Gul-Grønn",
            ExpectedName = "Sint Tiger Rød von Gul-Grønn",
        }
    ];

    public sealed record PersonNameInput
        : IXunitSerializable
    {
        private string? _name;
        private string? _firstName;
        private string? _lastName;
        private string? _middleName;
        private string? _expectedName;
        private string? _expectedFirstName;
        private string? _expectedMiddleName;
        private string? _expectedLastName;

        public required string? Name
        {
            get => _name;
            init => _name = value;
        }

        public required string? FirstName
        {
            get => _firstName;
            init => _firstName = value;
        }

        public string? MiddleName
        {
            get => _middleName;
            init => _middleName = value;
        }

        public required string? LastName
        {
            get => _lastName;
            init => _lastName = value;
        }

        public string? ExpectedName
        {
            get => _expectedName ?? Name;
            init => _expectedName = value;
        }

        public string? ExpectedFirstName
        {
            get => _expectedFirstName ?? FirstName;
            init => _expectedFirstName = value;
        }

        public string? ExpectedMiddleName
        {
            get => _expectedMiddleName ?? MiddleName;
            init => _expectedMiddleName = value;
        }

        public string? ExpectedLastName
        {
            get => _expectedLastName ?? LastName;
            init => _expectedLastName = value;
        }

        public PersonNameInput()
        {
        }

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            _name = info.GetValue<string>(nameof(Name));
            _firstName = info.GetValue<string>(nameof(FirstName));
            _middleName = info.GetValue<string>(nameof(MiddleName));
            _lastName = info.GetValue<string>(nameof(LastName));
            _expectedName = info.GetValue<string>(nameof(ExpectedName));
            _expectedFirstName = info.GetValue<string>(nameof(ExpectedFirstName));
            _expectedMiddleName = info.GetValue<string>(nameof(ExpectedMiddleName));
            _expectedLastName = info.GetValue<string>(nameof(ExpectedLastName));
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Name), Name, typeof(string));
            info.AddValue(nameof(FirstName), FirstName, typeof(string));
            info.AddValue(nameof(MiddleName), MiddleName, typeof(string));
            info.AddValue(nameof(LastName), LastName, typeof(string));
            info.AddValue(nameof(ExpectedName), ExpectedName, typeof(string));
            info.AddValue(nameof(ExpectedFirstName), ExpectedFirstName, typeof(string));
            info.AddValue(nameof(ExpectedMiddleName), ExpectedMiddleName, typeof(string));
            info.AddValue(nameof(ExpectedLastName), ExpectedLastName, typeof(string));
        }
    }
}
