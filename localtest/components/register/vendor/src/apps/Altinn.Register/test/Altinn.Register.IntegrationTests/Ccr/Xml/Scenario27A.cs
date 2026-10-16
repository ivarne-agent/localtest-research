using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Regression test for <c>&lt;samendringer data="T" type="K"&gt;</c> — supplementary free-text
/// connection entries that previously caused <c>CcrXmlProcessor</c> to throw
/// <c>"unknown samendring type ..."</c>. The element should now be consumed silently with no
/// effect on persisted role assignments or the organization itself. Free-text role
/// (<c>type="R"</c>) coverage lives in a separate scenario.
/// </summary>
public class Scenario27A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "FLI",
            name: "FRITEKST KNYTNING TEST",
            cancellationToken: cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260530" kjoerenr="04991" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="FLI" hovedsakstype="N" undersakstype="NY" foersteOverfoering="J" datoFoedt="20260530" datoSistEndret="20260530">
            <infotype felttype="NAVN" endringstype="N">
              <navn1>FRITEKST KNYTNING TEST</navn1>
              <rednavn>FRITEKST KNYTNING TEST</rednavn>
            </infotype>
            <samendringer data="T" felttype="KONT" endringstype="N" type="K">
              <knytningfritOrganisasjonsnummer>999888777</knytningfritOrganisasjonsnummer>
              <knytningfritTekstlinje>kontaktperson via annet selskap.</knytningfritTekstlinje>
            </samendringer>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();
        var roles = uow.GetPartyExternalRolePersistence();

        var updatedOrg = await parties
            .GetOrganizationByIdentifier(_org.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        updatedOrg.ShouldNotBeNull();
        updatedOrg.DisplayName.Value.ShouldBe("FRITEKST KNYTNING TEST");

        // The free-text connection samendring is supplementary text only - no structured K/R
        // siblings are present, so no role assignments should exist for this org. Crucially,
        // the fake knytningfritOrganisasjonsnummer above (999888777) is NOT looked up: if it
        // were, the import would fail because that org doesn't exist in the test DB.
        var roleAssignments = await roles
            .GetExternalRoleAssignmentsFromParty(partyUuid: _org.PartyUuid.Value, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        roleAssignments.ShouldBeEmpty();
    }
}
