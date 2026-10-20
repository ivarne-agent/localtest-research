using System.Text.Json.Serialization;
using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// The four contact point lookups Altinn Notifications calls on Profile (<c>profile/api/v1/</c>), answered from the
/// register's test data in the shapes Notifications deserialises (<c>ProfileClient</c>, <c>contactPointsList</c> in
/// every answer). All four are anonymous, as Profile's internal controllers are (<c>UserContactPointController</c>,
/// <c>UnitContactPointController</c>, <c>OrgNotificationAddressController</c>); Notifications sends no credential.
/// Numbers and addresses are returned exactly as the seed stores them (nothing is normalised; Notifications decides what
/// it accepts). Nobody is reserved, and the seed has no official organisation addresses.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiVersionNeutral]
[Route("profile/api/v1")]
public sealed class LocaltestProfileContactPointsController(RegisterStoreProvider provider) : ControllerBase
{
    private const string IdPortenEmailPrefix = "urn:altinn:person:idporten-email:";

    /// <summary>Persons by national identity number: one entry per person found, none for unknown numbers.</summary>
    [HttpPost("users/contactpoint/lookup")]
    public ActionResult<ContactPointsList<UserContactPoints>> LookupUsers([FromBody] UserLookup lookup)
    {
        var store = provider.Current;
        var found = lookup.NationalIdentityNumbers
            .Select(ssn => store.UserBySsn(ssn))
            .OfType<TestUser>()
            .Select(ToUserContactPoints);
        return Ok(new ContactPointsList<UserContactPoints>([.. found]));
    }

    /// <summary>Self-identified users by <c>urn:altinn:person:idporten-email:{email}</c>: their e-mail, never a mobile number.</summary>
    [HttpPost("users/contactpoint/lookupsi")]
    public ActionResult<ContactPointsList<ExternalIdentityContactPoints>> LookupSelfIdentified([FromBody] ExternalIdentityLookup lookup)
    {
        var store = provider.Current;
        var found = new List<ExternalIdentityContactPoints>();
        foreach (var identity in lookup.ExternalIdentities)
        {
            if (!identity.StartsWith(IdPortenEmailPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var email = identity[IdPortenEmailPrefix.Length..];
            var user = store.Users.FirstOrDefault(u =>
                string.IsNullOrEmpty(u.Person.Ssn) && string.Equals(u.Person.Email, email, StringComparison.OrdinalIgnoreCase));
            if (user is not null)
            {
                found.Add(new ExternalIdentityContactPoints(identity, user.Person.Email ?? string.Empty, string.Empty));
            }
        }

        return Ok(new ContactPointsList<ExternalIdentityContactPoints>(found));
    }

    /// <summary>The official notification addresses of organisations. The seed has none: every organisation found answers with empty lists.</summary>
    [HttpPost("organizations/notificationaddresses/lookup")]
    public ActionResult<ContactPointsList<OrganizationContactPoints>> LookupOrganizations([FromBody] OrganizationLookup lookup)
        => Ok(new ContactPointsList<OrganizationContactPoints>([.. OrganizationEntries(lookup.OrganizationNumbers, withUsers: false, resourceId: null)]));

    /// <summary>
    /// The contact points users registered for an organisation and a resource. The seed has no such registrations, so
    /// this is every test user that holds something for the organisation (roles or access packages), with e-mail and
    /// mobile number; Notifications then filters them by the PDP's decision for the resource, as in production.
    /// </summary>
    [HttpPost("units/contactpoint/lookup")]
    public ActionResult<ContactPointsList<OrganizationContactPoints>> LookupUnits([FromBody] UnitLookup lookup)
        => Ok(new ContactPointsList<OrganizationContactPoints>([.. OrganizationEntries(lookup.OrganizationNumbers, withUsers: true, lookup.ResourceId)]));

    private IEnumerable<OrganizationContactPoints> OrganizationEntries(IEnumerable<string> organizationNumbers, bool withUsers, string? resourceId)
    {
        _ = resourceId; // The PDP, not Profile, decides per resource; the seed holds no per-resource registrations.
        var store = provider.Current;
        foreach (var organizationNumber in organizationNumbers)
        {
            if (!store.ByOrganizationIdentifier.TryGetValue(organizationNumber, out var party))
            {
                continue;
            }

            var users = withUsers
                ? store.Users
                    .Where(u => u.Grants.Any(g => g.Party.PartyId.Value == party.PartyId.Value))
                    .Select(ToUserContactPoints)
                    .ToList()
                : [];
            yield return new OrganizationContactPoints(organizationNumber, checked((int)party.PartyId.Value), [], [], users);
        }
    }

    private static UserContactPoints ToUserContactPoints(TestUser user)
    {
        var person = user.Person;
        return new UserContactPoints(
            person.UserId,
            person.Ssn,
            false,
            person.MobileNumber ?? person.TelephoneNumber,
            person.Email);
    }

    public sealed record ContactPointsList<T>([property: JsonPropertyName("contactPointsList")] IReadOnlyList<T> Items);

    public sealed record UserContactPoints(int UserId, string NationalIdentityNumber, bool IsReserved, string? MobileNumber, string? Email);

    public sealed record ExternalIdentityContactPoints(string ExternalIdentity, string Email, string MobileNumber);

    public sealed record OrganizationContactPoints(
        string OrganizationNumber,
        int PartyId,
        IReadOnlyList<string> MobileNumberList,
        IReadOnlyList<string> EmailList,
        IReadOnlyList<UserContactPoints> UserContactPoints);

    public sealed class UserLookup
    {
        public List<string> NationalIdentityNumbers { get; set; } = [];

        public bool UseStaleContactInfo { get; set; }
    }

    public sealed class ExternalIdentityLookup
    {
        public List<string> ExternalIdentities { get; set; } = [];
    }

    public sealed class OrganizationLookup
    {
        public List<string> OrganizationNumbers { get; set; } = [];
    }

    public sealed class UnitLookup
    {
        public List<string> OrganizationNumbers { get; set; } = [];

        public string ResourceId { get; set; } = string.Empty;
    }
}
