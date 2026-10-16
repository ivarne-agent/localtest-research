using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// New registration of a Forening with 4 members, where the first member is the styreleder.
/// The organization has a FORM and ISEK infotype - which we ignore, but the FADR infotype sets the BusinessAddress of the organization.
/// The XML also contains a SIGN samendring with text describing the signing rules for the organization.
/// </summary>
public class Scenario11A
    : CcrXmlUpdateTestBase
{
    private string _orgNumber = "316289371";
    private PersonRecord _styreleder = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _styreleder = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Regnskapsfører"),
            cancellationToken: cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_orgNumber}}" organisasjonsform="FLI" hovedsakstype="N" undersakstype="NY" foersteOverfoering="J" datoFoedt="20260504" datoSistEndret="20260504">
            <infotype felttype="FADR" endringstype="N">
              <postnr>1234</postnr>
              <landkode>NO</landkode>
              <kommunenr>5001</kommunenr>
              <adresse1>c/o Berit Testperson</adresse1>
              <adresse2>Testveien 97</adresse2>
            </infotype>
            <infotype felttype="FORM" endringstype="N">
              <opplysning>Bevare og bruke Testfyrstasjon som offentlig tilgjengelig</opplysning>
            </infotype>
            <infotype felttype="FORM" endringstype="N">
              <opplysning>kulturminne gjennom frivillig drift, overnatting, kafé, omvisning og</opplysning>
            </infotype>
            <infotype felttype="FORM" endringstype="N">
              <opplysning>formidling av kystkultur.</opplysning>
            </infotype>
            <infotype felttype="ISEK" endringstype="N">
              <opplysning>7000</opplysning>
            </infotype>
            <samendringer data="D" felttype="LEDE" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>4</rolleRekkefoelge>
              <rolleFoedselsnr>{{_styreleder.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Berit</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 97</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>3</rolleRekkefoelge>
              <rolleFoedselsnr>03850672341</rolleFoedselsnr>
              <fornavn>Bjørn</fornavn>
              <mellomnavn>Test</mellomnavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testgata 55</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>2</rolleRekkefoelge>
              <rolleFoedselsnr>15837924771</rolleFoedselsnr>
              <fornavn>Geir</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>5678</postnr>
              <adresse1>Testbergvegen 11</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>1</rolleRekkefoelge>
              <rolleFoedselsnr>22880761599</rolleFoedselsnr>
              <fornavn>Birk</fornavn>
              <mellomnavn>Test</mellomnavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testgata 55</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <infotype felttype="MÅL" endringstype="N">
              <opplysning>B</opplysning>
            </infotype>
            <infotype felttype="NAVN" endringstype="N">
              <navn1>TESTBY FYRFORENING</navn1>
              <rednavn>TESTBY FYRFORENING</rednavn>
            </infotype>
            <samendringer data="T" felttype="SIGN" endringstype="N" type="S">
              <plassering>H</plassering>
              <samendringfritTekstlinje>Styrets leder alene eller to styremedlemmer i fellesskap.</samendringfritTekstlinje>
            </samendringer>
            <infotype felttype="STID" endringstype="N">
              <opplysning>20251118</opplysning>
            </infotype>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var newOrg = await parties.GetOrganizationByIdentifier(OrganizationIdentifier.Parse(_orgNumber), PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        newOrg.ShouldNotBeNull();
        newOrg.DisplayName.Value?.Contains("TESTBY FYRFORENING").ShouldBeTrue();
        newOrg.UnitType.Value.ShouldBe("FLI");
        newOrg.BusinessAddress.Value?.Address?.Contains("c/o Berit Testperson").ShouldBeTrue();

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(
            partyUuid: newOrg.PartyUuid.Value,
            cancellationToken: cancellationToken).
            ToListAsync(cancellationToken);
        roleAssignments.Count.ShouldBe(4);

        var styreleder = roleAssignments.Where(r => r.Identifier == "styreleder").ToList();
        styreleder.Count.ShouldBe(1);
        styreleder[0].ToParty.ShouldBe(_styreleder.PartyUuid);

        var medlemmer = roleAssignments.Where(r => r.Identifier == "styremedlem").ToList();
        medlemmer.Count.ShouldBe(3);
    }
}
