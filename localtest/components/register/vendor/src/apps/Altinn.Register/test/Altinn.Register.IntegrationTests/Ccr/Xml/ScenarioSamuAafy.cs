using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// SAMU AAFY: Removes the non-commercial main entity (ikke-naeringsdrivende-hovedenhet) role of an organization.
/// </summary>
public class ScenarioSamuAafy
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private OrganizationRecord _orgAAFY = null!;
    private PersonRecord _personLede = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "SAMU AAFY TEST AS",
            cancellationToken: cancellationToken);

        _personLede = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _orgAAFY = await uow.CreateOrg(
            unitType: "AS",
            name: "AAFY AS",
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLede.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "ikke-naeringsdrivende-hovedenhet", from: _org.PartyUuid.Value, to: _orgAAFY.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringUtgaar felttype="SAMU">
                <samendringstype>AAFY</samendringstype>
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
