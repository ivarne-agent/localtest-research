using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Adds one new REGN and the other leaves.
/// </summary>
public class Scenario06A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private OrganizationRecord _oldRegn = null!;
    private OrganizationRecord _newRegn = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _oldRegn = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN AS",
            cancellationToken: cancellationToken);

        _newRegn = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN2 AS",
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "regnskapsforer", from: _org.PartyUuid.Value, to: _oldRegn.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="AS" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20210125" datoSistEndret="20260503">
            <samendringer data="D" felttype="REGN" endringstype="N" type="K">
              <knytningFratraadt>F</knytningFratraadt>
              <knytningOrganisasjonsnummer>{{_oldRegn.OrganizationIdentifier.Value}}</knytningOrganisasjonsnummer>
              <knytningRekkefoelge>1</knytningRekkefoelge>
              <korrektOrganisasjonsnummer>000000000</korrektOrganisasjonsnummer>
            </samendringer>
            <samendringer data="D" felttype="REGN" endringstype="N" type="K">
              <knytningFratraadt>N</knytningFratraadt>
              <knytningOrganisasjonsnummer>{{_newRegn.OrganizationIdentifier.Value}}</knytningOrganisasjonsnummer>
              <korrektOrganisasjonsnummer>000000000</korrektOrganisasjonsnummer>
            </samendringer>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var updatedOrg = await parties.GetOrganizationByIdentifier(
            _org.OrganizationIdentifier.Value!,
            PartyFieldIncludes.Party | PartyFieldIncludes.Organization,
            cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        var roleAssignments = await roles.GetExternalRoleAssignmentsFromParty(
            partyUuid: _org.PartyUuid.Value,
            cancellationToken: cancellationToken).
            ToListAsync(cancellationToken);

        roleAssignments.Count.ShouldBe(1);

        updatedOrg.ShouldNotBeNull();
        var regnskapsforerFound = roleAssignments.Where(r => r.Identifier == "regnskapsforer").ToList();

        regnskapsforerFound.Count.ShouldBe(1);
        regnskapsforerFound.FirstOrDefault(r => r.ToParty == _newRegn.PartyUuid.Value).ShouldNotBeNull();
    }
}
