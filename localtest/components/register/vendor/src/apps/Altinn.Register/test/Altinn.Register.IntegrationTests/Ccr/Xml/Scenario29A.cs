using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Regression test for a konkursbo (<c>organisasjonsform="KBO"</c>) enhet whose only
/// <c>&lt;samendringer&gt;</c> child is a self-closing attribute-only element, e.g. a
/// free-text bostyrer update
/// <c>&lt;samendringer data="T" felttype="BOBE" endringstype="U" type="R" /&gt;</c>.
///
/// Previously <c>CcrXmlProcessor.ReadSamendring</c> called <c>ReadEndElement()</c>
/// unconditionally after <c>ReadStartElement</c>. Because <c>ReadEnhet</c> wraps each
/// samendringer in a <c>ReadSubtree</c>, consuming the self-closing start element also
/// exhausts the subtree — <c>MoveToContent</c> then returns <c>None</c> and the trailing
/// <c>ReadEndElement</c> throws <c>XmlException: 'None' is an invalid XmlNodeType.</c>
///
/// The CCR format description explicitly notes that <c>endringstype="U"</c> records "have
/// all value columns blank ... so &lt;U&gt; records are commonly emitted as self-closing
/// tags", which is why this failed intermittently in production (~30 of 7000 online updates).
/// </summary>
public class Scenario29A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "KBO",
            name: "TESTSELSKAP AS KONKURSBO",
            cancellationToken: cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
          <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
            <head avsender="ER" dato="20260601" kjoerenr="05920" mottaker="ALT" type="A" />
              <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="KBO" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20260504" datoSistEndret="20260601">
                <samendringer data="T" felttype="BOBE" endringstype="U" type="R" />
              </enhet><trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();

        var updated = await parties
            .GetOrganizationByIdentifier(_org.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        // Success == the processor got all the way past the samendringer and matched the
        // trailer. With the bug live, ReadSamendring's trailing ReadEndElement threw
        // XmlException("'None' is an invalid XmlNodeType.") on the self-closing samendringer,
        // and the update transaction never committed.
        updated.ShouldNotBeNull();
        updated.UnitType.Value.ShouldBe("KBO");
    }
}
