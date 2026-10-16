using System.Text.Json;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Tests;

public class ExternalRoleAssignmentTests
    : JsonModelTests
{
    [Fact]
    public async Task ExternalRoleAssignment_KnownSource()
    {
        var assignment = new ExternalRoleAssignment
        {
            Role = new ExternalRoleRef
            {
                Source = ExternalRoleSource.CentralCoordinatingRegister,
                Identifier = "daglig-leder",
            },
            FromParty = new PartyRef
            {
                Uuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            },
            ToParty = new PartyRef
            {
                Uuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            },
        };

        await ValidateJson(
            assignment,
            """
            {
                "role": {
                    "source": "ccr",
                    "identifier": "daglig-leder",
                    "urn": "urn:altinn:external-role:ccr:daglig-leder"
                },
                "from": {
                    "partyUuid": "00000000-0000-0000-0000-000000000001",
                    "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001"
                },
                "to": {
                    "partyUuid": "00000000-0000-0000-0000-000000000002",
                    "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000002"
                }
            }
            """);
    }

    [Fact]
    public async Task ExternalRoleAssignment_UnknownSource()
    {
        var source = JsonSerializer.Deserialize<NonExhaustiveEnum<ExternalRoleSource>>(@"""new-source""", Options);

        var assignment = new ExternalRoleAssignment
        {
            Role = new ExternalRoleRef
            {
                Source = source,
                Identifier = "daglig-leder",
            },
            FromParty = new PartyRef
            {
                Uuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            },
            ToParty = new PartyRef
            {
                Uuid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            },
        };

        await ValidateJson(
            assignment,
            """
            {
                "role": {
                    "source": "new-source",
                    "identifier": "daglig-leder",
                    "urn": "urn:altinn:external-role:new-source:daglig-leder"
                },
                "from": {
                    "partyUuid": "00000000-0000-0000-0000-000000000001",
                    "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000001"
                },
                "to": {
                    "partyUuid": "00000000-0000-0000-0000-000000000002",
                    "urn": "urn:altinn:party:uuid:00000000-0000-0000-0000-000000000002"
                }
            }
            """);
    }
}
