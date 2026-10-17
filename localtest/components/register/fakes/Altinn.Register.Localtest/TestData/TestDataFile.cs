using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Register.Localtest.TestData;

/// <summary>
/// The <c>testData.json</c> contract: persons, organisations and systems. Schema v1 plus the v2 fields
/// (<c>accessPackages</c>, profile preferences and <c>userType</c> on persons), all optional.
/// </summary>
public sealed class TestDataFile
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TestDataFile Empty => new();

    public List<TestPerson> Persons { get; set; } = [];

    public List<TestOrg> Orgs { get; set; } = [];

    public List<TestSystem> Systems { get; set; } = [];

    public static TestDataFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TestDataFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"'{path}' did not contain test data.");
    }

    public static TestDataFile Parse(string json, string source)
        => JsonSerializer.Deserialize<TestDataFile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"'{source}' did not contain test data.");

    /// <summary>Canonical JSON of one entry, used to tell identical duplicates from conflicting ones.</summary>
    internal static string Canonical<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);
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

    /// <summary>Profile language preference (<c>nb</c>, <c>nn</c> or <c>en</c>).</summary>
    public string? Language { get; set; }

    /// <summary>Profile: the party preselected in the party picker (schema v2).</summary>
    public int? PreSelectedPartyId { get; set; }

    /// <summary>Profile: skip the party picker and use the preselected or own party (schema v2).</summary>
    public bool? DoNotPromptForParty { get; set; }

    /// <summary>
    /// Profile user type, an <c>Altinn.Platform.Profile.Enums.UserType</c> name or number (schema v2). Defaults to
    /// <c>SSNIdentified</c> for persons with <c>ssn</c> and <c>SelfIdentified</c> without.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProfileUserType? UserType { get; set; }

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

    /// <summary>
    /// Roles the person holds, keyed by the party id the role is for (an organisation, or the person's own party).
    /// Values are Altinn role codes (<c>DAGL</c>, <c>REGNA</c>, <c>PRIV</c>, ...) or CCR role identifiers. CCR codes for
    /// organisations become external role assignments in Register; every code is also kept verbatim for the PDP fake
    /// (<c>urn:altinn:rolecode</c>). An empty list means "may act for this party" without any role.
    /// </summary>
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

/// <summary>The values of <c>Altinn.Platform.Profile.Enums.UserType</c>; the profile endpoints return the number.</summary>
public enum ProfileUserType
{
    None = 0,
    SSNIdentified = 1,
    SelfIdentified = 2,
    EnterpriseIdentified = 3,
    AgencyUser = 4,
    PSAN = 5,
    PSA = 6,
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
