using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Swaps out the REGN and REVI roles for an organization,
/// replacing them with new ones. Tests that the old roles are removed and the new ones are added correctly.
/// </summary>
public class Scenario01A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personRegn = null!;
    private PersonRecord _personRevi = null!;
    private PersonRecord _personRegnOld = null!;
    private PersonRecord _personReviOld = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        // gammel Regnskapsfører
        _personRegnOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Regnskapsfører"),
            cancellationToken: cancellationToken);

        // gammel Revisor
        _personReviOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Revisor"),
            cancellationToken: cancellationToken);

        // ny Regnskapsfører
        _personRegn = await uow.CreatePerson(
            name: PersonName.Create("CECILIE", "CHRISTIANSEN"),
            cancellationToken: cancellationToken);

        // ny Revisor
        _personRevi = await uow.CreatePerson(
            name: PersonName.Create("DAVID", "DANIELSEN"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "regnskapsforer", from: _org.PartyUuid.Value, to: _personRegnOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "revisor", from: _org.PartyUuid.Value, to: _personReviOld.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260102" kjoerenr="00091" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="AS" hovedsakstype="E" undersakstype="NY" foersteOverfoering="N" datoFoedt="20260101" datoSistEndret="20260102">
            <samendringer data="D" felttype="REGN" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personRegnOld.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringer data="D" felttype="REGN" endringstype="N" type="R">
              <rolleFoedselsnr>{{_personRegn.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>CECILIE</fornavn>
              <slektsnavn>CHRISTIANSEN</slektsnavn>
            </samendringer>
            <samendringer data="D" felttype="REVI" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personReviOld.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringer data="D" felttype="REVI" endringstype="N" type="R">
              <rolleFoedselsnr>{{_personRevi.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>DAVID</fornavn>
              <slektsnavn>DANIELSEN</slektsnavn>
            </samendringer>
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

        roleAssignments.Count.ShouldBe(2);

        updatedOrg.ShouldNotBeNull();
        var revisorFound = roleAssignments.Where(r => r.Identifier == "revisor").ToList();
        var regnskapsforerFound = roleAssignments.Where(r => r.Identifier == "regnskapsforer").ToList();

        regnskapsforerFound.ShouldHaveSingleItem().ToParty.ShouldBe(_personRegn.PartyUuid.Value);
        revisorFound.ShouldHaveSingleItem().ToParty.ShouldBe(_personRevi.PartyUuid.Value);
    }
}
