using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Register.Localtest.TestData;

/// <summary>
/// The <c>testData.json</c> contract (test-users.schema.v1): persons, organisations and systems.
/// </summary>
public sealed class TestDataFile
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<TestPerson> Persons { get; set; } = [];

    public List<TestOrg> Orgs { get; set; } = [];

    public List<TestSystem> Systems { get; set; } = [];

    public static TestDataFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TestDataFile>(stream, _jsonOptions)
            ?? throw new InvalidOperationException($"'{path}' did not contain test data.");
    }
}

public sealed class TestPerson
{
    public int UserId { get; set; }

    public int PartyId { get; set; }

    public Guid? PartyUuid { get; set; }

    [JsonPropertyName("ssn")]
    public string Ssn { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? Language { get; set; }

    public string? MobileNumber { get; set; }

    public string? TelephoneNumber { get; set; }

    public string? AddressCity { get; set; }

    public string? AddressHouseLetter { get; set; }

    public string? AddressHouseNumber { get; set; }

    public string? AddressMunicipalName { get; set; }

    public string? AddressMunicipalNumber { get; set; }

    public string? AddressPostalCode { get; set; }

    public string? AddressStreetName { get; set; }

    public string? MailingAddress { get; set; }

    public string? MailingPostalCity { get; set; }

    public string? MailingPostalCode { get; set; }

    /// <summary>Roles the person holds, keyed by the party id of the organisation the role is for.</summary>
    public Dictionary<string, List<TestRole>> PartyRoles { get; set; } = [];

    /// <summary>
    /// Access packages ("tilgangspakker") the person holds, keyed by the party id of the organisation they are
    /// held for. Values are package identifiers such as <c>skattegrunnlag</c> or full URNs
    /// <c>urn:altinn:accesspackage:skattegrunnlag</c>. Register does not use them; the localtest endpoint
    /// exposes them for the Access Management and PDP fakes. Needs test-users.schema v2.
    /// </summary>
    public Dictionary<string, List<string>> AccessPackages { get; set; } = [];

    public List<TestClaim> CustomClaims { get; set; } = [];
}

public sealed class TestRole
{
    public string? Type { get; set; }

    public string Value { get; set; } = string.Empty;
}

public sealed class TestClaim
{
    public string Type { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? ValueType { get; set; }
}

public sealed class TestOrg
{
    public int PartyId { get; set; }

    public Guid? PartyUuid { get; set; }

    public string OrgNumber { get; set; } = string.Empty;

    public int? ParentPartyId { get; set; }

    public string? Name { get; set; }

    public string? UnitType { get; set; }

    public string? UnitStatus { get; set; }

    public string? BusinessAddress { get; set; }

    public string? BusinessPostalCity { get; set; }

    public string? BusinessPostalCode { get; set; }

    public string? MailingAddress { get; set; }

    public string? MailingPostalCity { get; set; }

    public string? MailingPostalCode { get; set; }

    public string? EMailAddress { get; set; }

    public string? InternetAddress { get; set; }

    public string? TelephoneNumber { get; set; }

    public string? MobileNumber { get; set; }

    public string? FaxNumber { get; set; }
}

public sealed class TestSystem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<TestSystemUser> SystemUsers { get; set; } = [];
}

public sealed class TestSystemUser
{
    public Guid Id { get; set; }

    public string OrgNumber { get; set; } = string.Empty;

    public List<string> Actions { get; set; } = [];
}
