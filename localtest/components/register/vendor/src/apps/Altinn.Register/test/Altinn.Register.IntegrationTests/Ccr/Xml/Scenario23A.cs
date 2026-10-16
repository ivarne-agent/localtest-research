using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
///  foersteOverfoering = J of new Org, as a repair overwrite of any current entries in our db
/// </summary>
public class Scenario23A
    : CcrXmlUpdateTestBase
{
    private OrganizationIdentifier _orgId = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _orgId = await uow.GetNewOrgNumber(cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_orgId}}" organisasjonsform="UTLA" hovedsakstype="N" undersakstype="NY" foersteOverfoering="J" datoFoedt="20260504" datoSistEndret="20260504">
            <infotype felttype="FADR" endringstype="N">
              <landkode>FI</landkode>
              <poststed>012345 Helsinki</poststed>
              <adresse1>c/o Testifirma Oy</adresse1>
              <adresse2>Testikatu 5B</adresse2>
            </infotype>
            <infotype felttype="FORM" endringstype="N">
              <opplysning>datter i konsern</opplysning>
            </infotype>
            <infotype felttype="MÅL" endringstype="N">
              <opplysning>B</opplysning>
            </infotype>
            <infotype felttype="NAVN" endringstype="N">
              <navn1>TEST EIENDOM OY</navn1>
              <rednavn>TEST EIENDOM OY</rednavn>
            </infotype>
            <infotype felttype="PADR" endringstype="N">
              <postnr>1234</postnr>
              <landkode>NO</landkode>
              <kommunenr>0301</kommunenr>
              <adresse1>c/o Test Eiendom AS</adresse1>
              <adresse2>Testgata 2</adresse2>
            </infotype>
            <infotype felttype="underlagtHjemlandetsLovgivning" endringstype="N">
              <foretaksform>OY</foretaksform>
              <beskrivelseForetaksformHjemland>Yksityinen osakeyhtiö / Privat aktiebolag</beskrivelseForetaksformHjemland>
              <beskrivelseForetaksformNorsk>Aksjeselskap</beskrivelseForetaksformNorsk>
              <landkode>FI</landkode>
            </infotype>
            <infotype felttype="registrertHjemlandetsRegister" endringstype="N">
              <registernr>1234567-8</registernr>
              <registerNavn1>Patentti - Ja Rekisterihallitus</registerNavn1>
              <registerNavn2>Kaupparekisterijärjestelmä</registerNavn2>
              <landkode>FI</landkode>
              <utenlandskPoststed>00010 Helsinki</utenlandskPoststed>
              <postadresse1>Testikatu 1 A</postadresse1>
            </infotype>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var newOrg = await parties.GetOrganizationByIdentifier(_orgId!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        newOrg.ShouldNotBeNull();
        newOrg.DisplayName.Value?.Contains("TEST EIENDOM OY").ShouldBeTrue();
        newOrg.UnitType.Value.ShouldBe("UTLA");
        newOrg.MailingAddress.Value?.Address?.Contains("c/o Test Eiendom AS").ShouldBeTrue();
        newOrg.BusinessAddress.Value?.Address?.Contains("c/o Testifirma Oy").ShouldBeTrue();

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(
            partyUuid: newOrg.PartyUuid.Value,
            cancellationToken: cancellationToken).
            ToListAsync(cancellationToken);
        roleAssignments.Count.ShouldBe(0);
    }
}
