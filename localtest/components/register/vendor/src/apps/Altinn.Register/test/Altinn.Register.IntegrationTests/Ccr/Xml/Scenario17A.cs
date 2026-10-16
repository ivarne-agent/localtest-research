using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// We ignore the status rows, but the parser should not fail.
/// </summary>
public class Scenario17A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personDagl = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "BEDR",
            emailAddress: "old@example.com",
            internetAddress: FieldValue.Null,
            mobileNumber: "12345678",
            cancellationToken: cancellationToken);

        _personDagl = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Dagl"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "daglig-leder", from: _org.PartyUuid.Value, to: _personDagl.PartyUuid.Value, cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="AS" hovedsakstype="E" undersakstype="OPPL" foersteOverfoering="N" datoFoedt="20170201" datoSistEndret="20260504">
            <samendringer data="D" felttype="DAGL" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personDagl.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringUtgaar felttype="SAMU">
              <samendringstype>DAGL</samendringstype>
            </samendringUtgaar>
            <status felttype="OPPL" endringstype="N" />
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var updatedOrg = await parties.GetOrganizationByIdentifier(_org.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(partyUuid: _org.PartyUuid.Value, cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        roleAssignments.Count.ShouldBe(0);

        updatedOrg.ShouldNotBeNull();
    }
}
