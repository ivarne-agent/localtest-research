using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// SAMU ESGR: Removes all electronic signature rights (elektronisk-signeringsrettig) of an organization, including those with individual authority (elektronisk-signeringsrettig) and those with authority to assign electronic signature rights (elektronisk-signeringsrett-tildeler).
/// </summary>
public class ScenarioSamuEsgr
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personLedeOld = null!;
    private PersonRecord _personESGR = null!;
    private PersonRecord _personETDL = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _personLedeOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _personETDL = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "elektronisk-signeringsrett-tildeler"),
            cancellationToken: cancellationToken);

        _personESGR = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "elektronisk-signeringsrettig"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLedeOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "elektronisk-signeringsrett-tildeler", from: _org.PartyUuid.Value, to: _personETDL.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "elektronisk-signeringsrettig", from: _org.PartyUuid.Value, to: _personESGR.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringUtgaar felttype="SAMU">
                <samendringstype>ESGR</samendringstype>
            </samendringUtgaar>
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

        roleAssignments.Count.ShouldBe(1);
        roleAssignments[0].Identifier.ShouldBe("styreleder");

        updatedOrg.ShouldNotBeNull();
    }
}
