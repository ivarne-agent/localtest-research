using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// SAMU REVI: Removes all auditors (revisor) of an organization, including those with individual responsibility (revisor-hver-for-seg) and contact person for auditor (kontaktperson-revisor).
/// </summary>
public class ScenarioSamuRevi
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personLedeOld = null!;
    private PersonRecord _personREVI = null!;
    private PersonRecord _personREAD = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _personLedeOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _personREAD = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "revisoradressat"),
            cancellationToken: cancellationToken);

        _personREVI = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "revisor"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLedeOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "revisoradressat", from: _org.PartyUuid.Value, to: _personREAD.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "revisor", from: _org.PartyUuid.Value, to: _personREVI.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringUtgaar felttype="SAMU">
                <samendringstype>REVI</samendringstype>
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
