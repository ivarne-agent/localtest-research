using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
///  foersteOverfoering of new Org, should create the org, and also all the persons and role connections.
/// </summary>
public class Scenario24A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _oldOrg = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _oldOrg = await uow.CreateOrg(
            unitType: "ENK",
            emailAddress: "does.not@work.nope",
            telephoneNumber: "+4799999999",
            name: "Test Selskap AS",
            cancellationToken: cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_oldOrg.OrganizationIdentifier.Value}}" organisasjonsform="ENK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20080102" datoSistEndret="20260504">
            <samendringer data="D" felttype="DAGL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>1</rolleRekkefoelge>
              <rolleFoedselsnr>26917031273</rolleFoedselsnr>
              <fornavn>Erik</fornavn>
              <mellomnavn>Test</mellomnavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 9</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <infotype felttype="EPOS" endringstype="N">
              <opplysning>elise.testperson@example.com</opplysning>
            </infotype>
            <samendringer data="D" felttype="INNH" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>1</rolleRekkefoelge>
              <rolleFoedselsnr>12864421537</rolleFoedselsnr>
              <fornavn>Elise</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 29</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <infotype felttype="MTLF" endringstype="N">
              <opplysning>+4799999999</opplysning>
            </infotype>
            <infotype felttype="NAVN" endringstype="N">
              <navn1>ELISE TESTPERSON</navn1>
              <rednavn>ELISE TESTPERSON</rednavn>
            </infotype>
            <infotype felttype="paategning" endringstype="U" />
            <infotype felttype="TFON" endringstype="U" />
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var newOrg = await parties.GetOrganizationByIdentifier(_oldOrg.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        newOrg.ShouldNotBeNull();
        newOrg.DisplayName.Value?.Contains("ELISE TESTPERSON").ShouldBeTrue();
        newOrg.UnitType.Value.ShouldBe("ENK");
        newOrg.EmailAddress.Value?.Contains("elise.testperson@example.com").ShouldBeTrue();
        newOrg.MobileNumber.Value?.Contains("+4799999999").ShouldBeTrue();
        newOrg.TelephoneNumber.Value.ShouldBeNull();

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(
            partyUuid: newOrg.PartyUuid.Value,
            cancellationToken: cancellationToken).
            ToListAsync(cancellationToken);
        roleAssignments.Count.ShouldBe(2);

        var regnskapsforerFound = roleAssignments.Where(r => r.Identifier == "daglig-leder").ToList();
        regnskapsforerFound.Count.ShouldBe(1);

        var debitorFound = roleAssignments.Where(r => r.Identifier == "innehaver").ToList();
        debitorFound.Count.ShouldBe(1);
    }
}
