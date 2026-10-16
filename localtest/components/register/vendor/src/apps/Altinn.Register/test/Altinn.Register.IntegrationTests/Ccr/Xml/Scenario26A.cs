using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Regression test for <c>&lt;samendringer data="T" type="R"&gt;</c> — supplementary free-text
/// role entries that previously caused <c>CcrXmlProcessor</c> to throw
/// <c>"unknown samendring type ..."</c>. The element should now be consumed silently while the
/// structured <c>data="D"</c> sibling continues to apply normally. Free-text connection
/// (<c>type="K"</c>) coverage lives in a separate scenario.
/// </summary>
public class Scenario26A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personSife = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "FLI",
            name: "FRITEKST SAMENDRING TEST",
            cancellationToken: cancellationToken);

        _personSife = await uow.CreatePerson(
            name: PersonName.Create("Rik", "Kveld"),
            cancellationToken: cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260530" kjoerenr="04990" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="FLI" hovedsakstype="N" undersakstype="NY" foersteOverfoering="J" datoFoedt="20260530" datoSistEndret="20260530">
            <infotype felttype="NAVN" endringstype="N">
              <navn1>FRITEKST SAMENDRING TEST</navn1>
              <rednavn>FRITEKST SAMENDRING TEST</rednavn>
            </infotype>
            <samendringer data="D" felttype="SIFE" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>1</rolleRekkefoelge>
              <rolleFoedselsnr>{{_personSife.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Rik</fornavn>
              <slektsnavn>Kveld</slektsnavn>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="T" felttype="SIFE" endringstype="N" type="R">
              <rollefritFoedselsnr>{{_personSife.PersonIdentifier.Value}}</rollefritFoedselsnr>
              <rollefritTekstlinje>to av ovennevnte i fellesskap.</rollefritTekstlinje>
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

        // The free-text role/connection samendringer should not introduce any extra
        // assignments. The structured SIFE sibling should be the only one applied.
        var roleAssignments = await roles
            .GetExternalRoleAssignmentsFromParty(partyUuid: _org.PartyUuid.Value, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        roleAssignments.Count.ShouldBe(1);
        var sife = roleAssignments.ShouldHaveSingleItem();
        sife.Identifier.ShouldBe("signerer-fellesskap");
        sife.ToParty.ShouldBe(_personSife.PartyUuid.Value);

        updatedOrg.DisplayName.Value.ShouldBe("FRITEKST SAMENDRING TEST");
    }
}
