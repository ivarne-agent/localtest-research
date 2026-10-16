using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// SAMU STYR: Removes all board members (styremedlem) of an organization, including the chairperson (styreleder) and deputy members (varamedlem).
/// </summary>
public class ScenarioSamuStyr
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personLedeNew = null!;
    private PersonRecord _personLedeOld = null!;
    private PersonRecord _personMedlOld1 = null!;
    private PersonRecord _personMedlOld2 = null!;
    private PersonRecord _personMedlNew1 = null!;
    private PersonRecord _personMedlNew2 = null!;
    private PersonRecord _personVaraBlirMedl = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _personLedeOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _personLedeNew = await uow.CreatePerson(
            name: PersonName.Create("Ny", "Styreleder"),
            cancellationToken: cancellationToken);

        _personMedlOld1 = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Medlem 1"),
            cancellationToken: cancellationToken);

        _personMedlOld2 = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Medlem 2"),
            cancellationToken: cancellationToken);

        _personMedlNew1 = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "Medlem 1"),
            cancellationToken: cancellationToken);

        _personMedlNew2 = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "Medlem 2"),
            cancellationToken: cancellationToken);

        _personVaraBlirMedl = await uow.CreatePerson(
            name: PersonName.Create("Vara", "BlirMedlem"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLedeOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styremedlem", from: _org.PartyUuid.Value, to: _personMedlOld1.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styremedlem", from: _org.PartyUuid.Value, to: _personMedlOld2.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "varamedlem", from: _org.PartyUuid.Value, to: _personVaraBlirMedl.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringUtgaar felttype="SAMU">
                <samendringstype>STYR</samendringstype>
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

        roleAssignments.Count.ShouldBe(0);

        updatedOrg.ShouldNotBeNull();
    }
}
