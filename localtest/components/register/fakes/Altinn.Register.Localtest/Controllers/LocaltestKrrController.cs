using System.Text.Json.Serialization;
using Altinn.Register.Localtest.Persistence;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Register.Localtest.Controllers;

/// <summary>
/// A stand-in for KRR, the contact and reservation register (<c>kontaktregisteret.no</c>), at
/// <c>register/api/localtest/krr/rest/v2/personer</c>: the production client of Altinn Correspondence
/// (<c>ContactReservationRegistryService</c>, base URL <c>GeneralSettings:ContactReservationRegistryBaseUrl</c>) posts
/// <c>{ "personidentifikatorer": [...] }</c> and reads <c>personer[].reservasjon</c>. A person with <c>reserved: true</c> in
/// the test data answers <c>"JA"</c>, everyone else <c>"NEI"</c> (the flag Profile's <c>isReserved</c> mirrors), with the test
/// data's e-mail and mobile number as stored; national identity numbers not in the test data get no entry, which the client
/// reads as not reserved. Anonymous: production sends a raw Maskinporten token that nothing checks here.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiVersionNeutral]
[Route("register/api/localtest/krr")]
public sealed class LocaltestKrrController(RegisterStoreProvider provider) : ControllerBase
{
    [HttpPost("rest/v2/personer")]
    public ActionResult<KrrPersons> Persons([FromBody] KrrLookup lookup)
    {
        var store = provider.Current;
        var persons = lookup.Personidentifikatorer
            .Where(ssn => !string.IsNullOrWhiteSpace(ssn))
            .Distinct(StringComparer.Ordinal)
            .Select(ssn => store.UserBySsn(ssn))
            .OfType<TestUser>()
            .Select(user => new KrrPerson(
                user.Person.Ssn,
                user.Person.Reserved == true ? "JA" : "NEI",
                "AKTIV",
                user.Person.Reserved == true ? "KAN_IKKE_VARSLES" : "KAN_VARSLES",
                new KrrContactInformation(user.Person.Email ?? string.Empty, user.Person.MobileNumber ?? user.Person.TelephoneNumber ?? string.Empty)))
            .ToList();
        return Ok(new KrrPersons(persons));
    }

    public sealed class KrrLookup
    {
        [JsonPropertyName("personidentifikatorer")]
        public List<string> Personidentifikatorer { get; set; } = [];
    }

    public sealed record KrrPersons([property: JsonPropertyName("personer")] IReadOnlyList<KrrPerson> Personer);

    public sealed record KrrPerson(
        [property: JsonPropertyName("personidentifikator")] string Personidentifikator,
        [property: JsonPropertyName("reservasjon")] string Reservasjon,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("varslingsstatus")] string Varslingsstatus,
        [property: JsonPropertyName("kontaktinformasjon")] KrrContactInformation Kontaktinformasjon);

    public sealed record KrrContactInformation(
        [property: JsonPropertyName("epostadresse")] string Epostadresse,
        [property: JsonPropertyName("mobiltelefonnummer")] string Mobiltelefonnummer);
}
