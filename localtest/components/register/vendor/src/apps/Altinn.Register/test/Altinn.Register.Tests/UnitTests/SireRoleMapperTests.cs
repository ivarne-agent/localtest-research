using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Integrations.Sire.Organization;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SireRoleMapper"/>.
/// </summary>
public class SireRoleMapperTests
{
    /// <summary>
    /// Every known SIRE <c>relasjonstype</c> must map to the expected Altinn role identifier.
    /// The expected identifiers mirror the v0.18-role-identifiers migration; if either side
    /// changes, the other must follow.
    /// </summary>
    [Theory]
    [MemberData(nameof(KnownRelasjonstyper))]
    public void TryValidate_MapsKnownRelasjonstype(string relasjonstype, string expected)
    {
        ValidationProblemBuilder builder = default;
        var ok = builder.TryValidate(
            path: "/relasjonstype",
            relasjonstype,
            default(SireRoleMapper),
            out string? result);

        Assert.True(ok);
        Assert.Equal(expected, result);
        Assert.False(builder.TryBuild(out _));
    }

    /// <summary>
    /// Anything outside the kodeliste produces a validation error at the supplied path
    /// rather than throwing — that's the whole point of the IValidator refactor: multiple
    /// unknowns in a single document aggregate instead of failing on the first.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("notARealRelasjonstype")]
    [InlineData("KOMP")] // SL-code (caller passed the wrong layer's value)
    [InlineData("daglig-leder")] // role identifier (output, mistakenly fed back in)
    [InlineData("KOMPLEMENTAR")] // case mismatch — switch is case-sensitive
    [InlineData("komplementar ")] // trailing whitespace — caller forgot to trim
    public void TryValidate_AddsProblemForUnknown(string relasjonstype)
    {
        ValidationProblemBuilder builder = default;
        var ok = builder.TryValidate(
            path: "/relasjonstype",
            relasjonstype,
            default(SireRoleMapper),
            out string? result);

        Assert.False(ok);
        Assert.Null(result);
        Assert.True(builder.TryBuild(out _));
    }

    /// <summary>
    /// Authoritative table of all 52 SIRE relasjonstype → Altinn role identifier mappings.
    /// SL-codes (KOMP, DAGL, …) are noted in comments above each block for traceability against
    /// the Skatteetaten kodeliste.
    /// </summary>
    public static TheoryData<string, string> KnownRelasjonstyper => new()
    {
        // Single-entry letters
        { "administrativEnhetOffentligSektor", "administrativ-enhet-offentlig-sektor" }, // ADOS
        { "lederIPartietsUtoevendeOrgan",      "parti-organ-leder" },                     // HLED

        // b
        { "bestyrendeReder", "bestyrende-reder" }, // BEST
        { "bostyrer",        "bostyrer" },         // BOBE

        // d
        { "dagligLederAdministrerendeDirektoer", "daglig-leder" },          // DAGL
        { "deltakerMedProratariskAnsvar",        "deltaker-delt-ansvar" },  // DTPR
        { "deltakerMedSolidariskAnsvar",         "deltaker-fullt-ansvar" }, // DTSO
        { "denPersonligeKonkursenAngaar",        "personlige-konkurs" },    // KENK

        // e — kodeliste spells READ with capital E ("ErRevisoradresseFor")
        { "ErRevisoradresseFor",               "revisoradressat" },               // READ
        { "eierkommune",                        "eierkommune" },                   // EIKM
        { "erBedriftTil",                       "hovedenhet" },                    // BEDR
        { "erFrivilligRegistrertUtleiebyggFor", "utleiebygg" },                    // UTBG
        { "erRegnskapsfoereradresseFor",        "regnskapsforeradressat" },        // RFAD
        { "erSaerskiltOppdeltEnhetTil",         "saerskilt-oppdelt-enhet" },       // OPMV
        { "erVirksomhetDrevetIFellesskapAv",    "virksomhet-fellesskap-drifter" }, // VIFE

        // f
        { "forestaarAvvikling", "forestaar-avvikling" }, // AVKL
        { "forretningsfoerer",  "forretningsforer" },    // FFOR

        // h
        { "harSomDatterIKonsern",     "konsern-datter" },                 // KDAT
        { "harSomGrunnlagForKonsern", "konsern-grunnlag" },               // KGRL
        { "harSomMorIKonsern",        "konsern-mor" },                    // KMOR
        { "harSomRegistreringsenhet", "ikke-naeringsdrivende-hovedenhet" }, // AAFY
        { "helseforetak",             "helseforetak" },                   // HLSE

        // i
        { "innehaver",                     "innehaver" },             // INNH
        { "inngaarIFellesMvaRegistrering", "felles-registrert-med" }, // FEMV
        { "inngaarIForetaksgruppeMed",     "foretaksgruppe-med" },    // FGRP
        { "inngaarIKirkeligFellesraad",    "kirkelig-fellesraad" },   // KIRK
        { "inngaarIKontorfellesskap",      "kontorfelleskapmedlem" }, // KTRF

        // k
        { "komplementar",   "komplementar" },   // KOMP
        { "konkursdebitor", "konkursdebitor" }, // KDEB
        { "kontaktperson",  "kontaktperson" },  // KONT

        // n
        { "nestleder",                          "nestleder" },              // NEST
        { "nestlederIPartietsUtoevendeOrgan",   "parti-organ-nestleder" },  // HNST
        { "norskRepresentantForUtenlandskEnhet", "norsk-representant" },    // REPR

        // o
        { "observatoer",                       "observator" },                          // OBS
        { "opplysningerOmForetaketIHjemlandet", "hovedforetak" },                       // HFOR
        { "organisasjonsleddIOffentligSektor",  "organisasjonsledd-offentlig-sektor" }, // ORGL

        // p
        { "prokura",            "prokurist" },              // PROK
        { "prokuraHverForSeg",  "prokurist-hver-for-seg" }, // POHV
        { "prokuraIFellesskap", "prokurist-fellesskap" },   // POFE

        // r
        { "regnskapsfoerer", "regnskapsforer" }, // REGN
        { "revisor",         "revisor" },        // REVI

        // s
        { "sameiere",                           "sameier" },                  // SAM
        { "signatur",                           "signerer" },                 // SIGN
        { "signaturHverForSeg",                 "signerer-hver-for-seg" },    // SIHV
        { "signaturIFellesskap",                "signerer-fellesskap" },      // SIFE
        { "skalFisjoneresMed",                  "fisjonsovertaker" },         // FISJ
        { "skalFusjoneresMed",                  "fusjonsovertaker" },         // FUSJ
        { "styremedlem",                        "styremedlem" },              // MEDL
        { "styremedlemIPartietsUtoevendeOrgan", "parti-organ-styremedlem" },  // HMDL
        { "styretsLeder",                       "styreleder" },               // LEDE

        // v
        { "varamedlem",                       "varamedlem" },             // VARA
        { "varamedlemIPartietsUtoevendeOrgan", "parti-organ-varamedlem" }, // HVAR
    };
}
