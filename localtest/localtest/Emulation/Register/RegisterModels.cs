namespace Localtest.Emulated;

// The Register's v1 models as app-lib reads them (Altinn.Platform.Models: camelCase, PartyType as a number).

public sealed record Party(
    int PartyId,
    Guid PartyUuid,
    int PartyTypeName,
    string? OrgNumber,
    string? Ssn,
    string? UnitType,
    string Name,
    bool IsDeleted,
    bool OnlyHierarchyElementWithNoAccess,
    Person? Person,
    Organization? Organization,
    IReadOnlyList<Party>? ChildParties);

public sealed record Person(
    string Ssn,
    string Name,
    string? FirstName,
    string? MiddleName,
    string LastName,
    string? TelephoneNumber,
    string? MobileNumber,
    string? MailingAddress,
    string? MailingPostalCode,
    string? MailingPostalCity,
    string? AddressMunicipalNumber,
    string? AddressMunicipalName,
    string? AddressStreetName,
    string? AddressHouseNumber,
    string? AddressHouseLetter,
    string? AddressPostalCode,
    string? AddressCity,
    DateTime? DateOfDeath);

public sealed record Organization(
    string OrgNumber,
    string Name,
    string? UnitType,
    string? TelephoneNumber,
    string? MobileNumber,
    string? FaxNumber,
    string? EMailAddress,
    string? InternetAddress,
    string? MailingAddress,
    string? MailingPostalCode,
    string? MailingPostalCity,
    string? BusinessAddress,
    string? BusinessPostalCode,
    string? BusinessPostalCity,
    string? UnitStatus);

/// <summary>The body of <c>parties/lookup</c>.</summary>
public sealed record PartyLookup(string? Ssn, string? OrgNo);

/// <summary>The body of the URN queries: <c>{"data": ["urn:…"]}</c>.</summary>
public sealed record UrnQuery(string[]? Data);

/// <summary>The answer of the URN queries: the parties found, with the fields app-lib reads (<c>partyId</c>, <c>partyUuid</c>).</summary>
public sealed record PartyQueryResult(IReadOnlyList<PartyQueryItem> Data);

public sealed record PartyQueryItem(Guid PartyUuid, int PartyId, string Type, string DisplayName, PartyQueryUser? User);

public sealed record PartyQueryUser(int UserId);

/// <summary>The Register fake's <c>test-users</c> entry, the part the picker and tools read.</summary>
public sealed record TestUserEntry(
    int UserId,
    int PartyId,
    Guid PartyUuid,
    string Ssn,
    string DisplayName,
    string? UserName,
    string? Email,
    string? Language,
    IReadOnlyList<TestUserPartyEntry> Parties,
    string PartyType,
    IReadOnlyList<string> SelfRoleCodes,
    IReadOnlyList<ActingForEntry> ActingFor,
    bool Reserved);

public sealed record TestUserPartyEntry(int PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, string? UnitType, IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AccessPackages);

public sealed record ActingForEntry(int PartyId, Guid PartyUuid, string PartyType, string? OrgNumber, string? Ssn, string DisplayName, string? UnitType, int? ParentPartyId, IReadOnlyList<string> RoleCodes);

/// <summary>The Register fake's <c>organizations</c> entry.</summary>
public sealed record TestOrgEntry(int PartyId, Guid PartyUuid, string OrgNumber, string DisplayName, int? ParentPartyId, string? UnitType, IReadOnlyList<TestNotificationAddressEntry> NotificationAddresses);

public sealed record TestNotificationAddressEntry(string? Email, string? MobileNumber);

/// <summary>How the emulated services turn the test data into the Register's models.</summary>
public static class RegisterModels
{
    /// <summary>A party; with <paramref name="withSubUnits"/> an organisation carries its sub-units as <c>childParties</c>.</summary>
    public static Party ToParty(TestDataSnapshot data, TestParty party, bool withSubUnits = false)
    {
        var person = party.Person;
        var org = party.Org;
        var children = withSubUnits && party.Kind == PartyKind.Organisation
            ? data.SubUnitsOf(party.PartyId).Select(s => ToParty(data, s)).ToList()
            : null;
        return new Party(
            party.PartyId,
            party.PartyUuid,
            party.PartyTypeNumber,
            party.OrgNumber,
            party.Ssn,
            party.UnitType,
            party.Name,
            IsDeleted: false,
            OnlyHierarchyElementWithNoAccess: false,
            party.Kind == PartyKind.Person && person is not null ? ToPerson(party, person) : null,
            party.Kind == PartyKind.Organisation && org is not null ? ToOrganization(party, org) : null,
            children is { Count: > 0 } ? children : null);
    }

    public static Person ToPerson(TestParty party, Altinn.Register.Localtest.TestData.TestPerson p)
        => new(
            p.Ssn,
            party.Name,
            p.FirstName,
            p.MiddleName,
            p.LastName,
            p.TelephoneNumber,
            p.MobileNumber,
            p.MailingAddress,
            p.MailingPostalCode,
            p.MailingPostalCity,
            p.AddressMunicipalNumber,
            p.AddressMunicipalName,
            p.AddressStreetName,
            p.AddressHouseNumber,
            p.AddressHouseLetter,
            p.AddressPostalCode,
            p.AddressCity,
            null);

    public static Organization ToOrganization(TestParty party, Altinn.Register.Localtest.TestData.TestOrg o)
        => new(
            o.OrgNumber,
            party.Name,
            party.UnitType,
            o.TelephoneNumber,
            o.MobileNumber,
            o.FaxNumber,
            o.EMailAddress,
            o.InternetAddress,
            o.MailingAddress,
            o.MailingPostalCode,
            o.MailingPostalCity,
            o.BusinessAddress,
            o.BusinessPostalCode,
            o.BusinessPostalCity,
            o.UnitStatus ?? "N");

    /// <summary>The Register fake's party type names.</summary>
    public static string TypeName(TestParty party) => party.Kind switch
    {
        PartyKind.Organisation => "organization",
        PartyKind.SelfIdentified => "self-identified",
        PartyKind.SystemUser => "system-user",
        _ => "person",
    };
}
