using System.Text.Json;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Persistence.ImportJobs;

namespace Altinn.Register.Tests.UnitTests;

public class PartyExternalRoleAssignmentsUpdateTests
{
    private static readonly JsonSerializerOptions _options = PostgresSagaStatePersistence.JsonSerializerOptions;

    [Fact]
    public async Task Full_Empty_RoundTrips()
    {
        await VerifyRoundTrip(PartyExternalRoleAssignmentsUpdate.Full.Empty);
    }

    [Fact]
    public async Task Full_WithUuidAssignments_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Full
        {
            Assignments =
            [
                Assignment(
                    "daglig-leder",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("11111111-1111-1111-1111-111111111111") }),
                Assignment(
                    "styremedlem",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("22222222-2222-2222-2222-222222222222") }),
            ],
        });
    }

    [Fact]
    public async Task Full_WithMixedPartyRefs_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Full
        {
            Assignments =
            [
                Assignment(
                    "guardian",
                    new PartyExternalRoleAssignmentPartyRef.Person
                    {
                        PersonIdentifier = PersonIdentifier.Parse("25871999336"),
                        Name = PersonName.Create("Ada", "Lovelace"),
                        MailingAddress = new MailingAddressRecord
                        {
                            Address = "Example Street 1",
                            PostalCode = "0150",
                            City = "Oslo",
                        },
                    }),
                Assignment(
                    "accountant",
                    new PartyExternalRoleAssignmentPartyRef.Organization
                    {
                        OrganizationIdentifier = OrganizationIdentifier.Parse("123456785"),
                    }),
                Assignment(
                    "contact",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("33333333-3333-3333-3333-333333333333") }),
            ],
        });
    }

    [Fact]
    public async Task Patch_Empty_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Patch
        {
            AbsentByIdentifier = [],
            Absent = [],
            Present = [],
        });
    }

    [Fact]
    public async Task Patch_WithAbsentByIdentifierOnly_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Patch
        {
            AbsentByIdentifier = ["daglig-leder", "styreleder"],
            Absent = [],
            Present = [],
        });
    }

    [Fact]
    public async Task Patch_WithUuidAssignments_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Patch
        {
            AbsentByIdentifier = ["obsolete-role"],
            Absent =
            [
                Assignment(
                    "styremedlem",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("44444444-4444-4444-4444-444444444444") }),
            ],
            Present =
            [
                Assignment(
                    "daglig-leder",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("55555555-5555-5555-5555-555555555555") }),
            ],
        });
    }

    [Fact]
    public async Task Patch_WithMixedPartyRefs_RoundTrips()
    {
        await VerifyRoundTrip(new PartyExternalRoleAssignmentsUpdate.Patch
        {
            AbsentByIdentifier = ["legacy-role"],
            Absent =
            [
                Assignment(
                    "guardian",
                    new PartyExternalRoleAssignmentPartyRef.Person
                    {
                        PersonIdentifier = PersonIdentifier.Parse("01056261032"),
                        Name = null,
                        MailingAddress = null,
                    }),
                Assignment(
                    "accountant",
                    new PartyExternalRoleAssignmentPartyRef.Organization
                    {
                        OrganizationIdentifier = OrganizationIdentifier.Parse("311654306"),
                    }),
            ],
            Present =
            [
                Assignment(
                    "contact",
                    new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = Guid.Parse("66666666-6666-6666-6666-666666666666") }),
                Assignment(
                    "successor",
                    new PartyExternalRoleAssignmentPartyRef.Organization
                    {
                        OrganizationIdentifier = OrganizationIdentifier.Parse("987654325"),
                    }),
            ],
        });
    }

    private static PartyExternalRoleAssignment Assignment(string identifier, PartyExternalRoleAssignmentPartyRef toParty)
        => new()
        {
            ExternalRoleIdentifier = identifier,
            ToParty = toParty,
        };

    private static async Task VerifyRoundTrip(PartyExternalRoleAssignmentsUpdate update)
    {
        var json = JsonSerializer.Serialize(update, _options);
        var deserialized = JsonSerializer.Deserialize<PartyExternalRoleAssignmentsUpdate>(json, _options);

        deserialized.ShouldBe(update);
        await VerifyJson(json);
    }
}
