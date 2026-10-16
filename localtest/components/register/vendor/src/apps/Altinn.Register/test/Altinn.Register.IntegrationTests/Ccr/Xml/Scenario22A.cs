using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
///  foersteOverfoering of new Org, should create the org, and also all the persons and role connections.
/// </summary>
public class Scenario22A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _orgKDEB = null!;
    private OrganizationIdentifier _orgId = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _orgKDEB = await uow.CreateOrg(
            unitType: "BEDR",
            name: "Den Andre Bedriften AS",
            cancellationToken: cancellationToken);

        _orgId = await uow.GetNewOrgNumber(cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_orgId}}" organisasjonsform="KBO" hovedsakstype="N" undersakstype="NY" foersteOverfoering="J" datoFoedt="20260504" datoSistEndret="20260504">
            <samendringer data="D" felttype="BOBE" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>10</rolleRekkefoelge>
              <rolleFoedselsnr>28816327530</rolleFoedselsnr>
              <fornavn>Frida</fornavn>
              <mellomnavn>Test</mellomnavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 62 B</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <infotype felttype="FADR" endringstype="N">
              <postnr>5678</postnr>
              <landkode>NO</landkode>
              <kommunenr>0301</kommunenr>
              <adresse1>Testgata 33B</adresse1>
            </infotype>
            <infotype felttype="FORM" endringstype="N">
              <opplysning>Konkursbo.</opplysning>
            </infotype>
            <samendringer data="D" felttype="KDEB" endringstype="N" type="K">
              <knytningFratraadt>N</knytningFratraadt>
              <knytningOrganisasjonsnummer>{{_orgKDEB.OrganizationIdentifier.Value}}</knytningOrganisasjonsnummer>
              <knytningRekkefoelge>30</knytningRekkefoelge>
              <korrektOrganisasjonsnummer>000000000</korrektOrganisasjonsnummer>
            </samendringer>
            <infotype felttype="MÅL" endringstype="N">
              <opplysning>B</opplysning>
            </infotype>
            <infotype felttype="NAVN" endringstype="N">
              <navn1>TESTSELSKAP AS KONKURSBO</navn1>
              <rednavn>TESTSELSKAP AS KONKURSBO</rednavn>
            </infotype>
            <infotype felttype="PADR" endringstype="N">
              <postnr>1234</postnr>
              <landkode>NO</landkode>
              <kommunenr>0301</kommunenr>
              <adresse1>v/Adv. Frida Test Testperson</adresse1>
              <adresse2>Postboks 1234</adresse2>
            </infotype>
            <infotype felttype="naeringskode" endringstype="N">
              <naeringskode>53.200</naeringskode>
              <hjelpeenhet>N</hjelpeenhet>
            </infotype>
            <infotype felttype="STID" endringstype="N">
              <opplysning>20260504</opplysning>
            </infotype>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var newOrg = await parties.GetOrganizationByIdentifier(_orgId, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        newOrg.ShouldNotBeNull();
        newOrg.DisplayName.Value?.Contains("TESTSELSKAP AS KONKURSBO").ShouldBeTrue();
        newOrg.UnitType.Value.ShouldBe("KBO");
        newOrg.MailingAddress.Value?.Address?.Contains("v/Adv. Frida Test Testperson").ShouldBeTrue();
        newOrg.BusinessAddress.Value?.Address?.Contains("Testgata 33B").ShouldBeTrue();

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(
            partyUuid: newOrg.PartyUuid.Value,
            cancellationToken: cancellationToken).
            ToListAsync(cancellationToken);
        roleAssignments.Count.ShouldBe(2);

        var regnskapsforerFound = roleAssignments.Where(r => r.Identifier == "bostyrer").ToList();
        regnskapsforerFound.Count.ShouldBe(1);

        var debitorFound = roleAssignments.Where(r => r.Identifier == "konkursdebitor").ToList();
        debitorFound.Count.ShouldBe(1);
    }
}
