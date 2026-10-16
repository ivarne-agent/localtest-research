using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Defines the possible registration statuses for a person in the Norwegian National Population Register (Folkeregisteret).
/// </summary>
[StringEnumConverter]
public enum PersonStatus
    : byte
{
    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som oppholder seg lovlig i en
    /// norsk kommune i minst seks måneder.
    /// </summary>
    [JsonStringEnumMemberName("bosatt")]
    Resident = 1,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som er vedtatt utflyttet fra
    /// Norge iht. kriterier i Folkeregisterloven § 4-3
    /// </summary>
    [JsonStringEnumMemberName("utflyttet")]
    Emigrated,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som er blitt borte i forbindelse
    /// med ulykke, naturkatastrofe, forbrytelse eller er savnet på sjøen, i fjellet eller
    /// lignende
    /// </summary>
    [JsonStringEnumMemberName("forsvunnet")]
    Missing,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som er erklært død av lege
    /// eller domstolen
    /// </summary>
    [JsonStringEnumMemberName("doed")]
    Deceased,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som er tildelt fødsels-
    /// eller dnummer i Folkeregisteret, hvor identifikatoren ikke lenger er gyldig
    /// for identiteten
    /// </summary>
    [JsonStringEnumMemberName("opphoert")]
    Expired,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som er født i Norge, og
    /// oppfyller kravet til tildeling av fødselsnummer, men som ikke skal
    /// bostedsregistreres
    /// </summary>
    [JsonStringEnumMemberName("foedselsregistrert")]
    BirthRegistered,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person med midlertidig tilknytning til
    /// Norge
    /// </summary>
    [JsonStringEnumMemberName("midlertidig")]
    Temporary,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person med d-nummer som har hatt
    /// status=aktiv i 5 år eller mer. Personen med d-nummer som blir inaktiv etter 5 år
    /// kan reaktiveres igjen.
    /// </summary>
    [JsonStringEnumMemberName("inaktiv")]
    Inactive,

    /// <summary>
    /// registreringsstatus i Folkeregisteret for en person som ikke er offisielt bosatt i
    /// Norge
    /// </summary>
    [JsonStringEnumMemberName("ikkeBosatt")]
    NonRegistered,

    /// <summary>
    /// oppgitt personstatus fra Folkeregisteret til konsumenter uten hjemmel til
    /// taushetsbelagt informasjon for personer med personstatus forsvunnet,
    /// fødselsregistrert, midlertidig og ikkeBosatt
    /// </summary>
    [JsonStringEnumMemberName("aktiv")]
    Active,
}
