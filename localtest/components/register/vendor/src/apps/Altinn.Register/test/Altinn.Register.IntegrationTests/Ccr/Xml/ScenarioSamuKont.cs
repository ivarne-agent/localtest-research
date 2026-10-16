using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// SAMU KONT: Removes all contact persons (kontaktperson) of an organization, including those with specific roles such as contact person for auditor (kontaktperson-revisor), contact person for municipality (kontaktperson-kommune), contact person for NUF (kontaktperson-nuf), and contact person for ADOS (kontaktperson-ados).
/// </summary>
public class ScenarioSamuKont
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personKONT = null!;
    private PersonRecord _personLedeOld = null!;
    private PersonRecord _personSREVA = null!;
    private PersonRecord _personKOMK = null!;
    private PersonRecord _personKNUF = null!;
    private PersonRecord _personKEMN = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _personLedeOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _personKONT = await uow.CreatePerson(
            name: PersonName.Create("Ny", "kontaktperson"),
            cancellationToken: cancellationToken);

        _personSREVA = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "kontaktperson-revisor"),
            cancellationToken: cancellationToken);

        _personKOMK = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "kontaktperson-kommune"),
            cancellationToken: cancellationToken);

        _personKNUF = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "kontaktperson-nuf"),
            cancellationToken: cancellationToken);

        _personKEMN = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "kontaktperson-ados"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLedeOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "kontaktperson", from: _org.PartyUuid.Value, to: _personKONT.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "kontaktperson-revisor", from: _org.PartyUuid.Value, to: _personSREVA.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "kontaktperson-kommune", from: _org.PartyUuid.Value, to: _personKOMK.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "kontaktperson-nuf", from: _org.PartyUuid.Value, to: _personKNUF.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "kontaktperson-ados", from: _org.PartyUuid.Value, to: _personKEMN.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringUtgaar felttype="SAMU">
                <samendringstype>KONT</samendringstype>
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
