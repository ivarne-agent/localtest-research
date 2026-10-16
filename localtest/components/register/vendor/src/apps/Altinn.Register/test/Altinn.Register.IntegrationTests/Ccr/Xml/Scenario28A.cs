using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Regression test for an <c>&lt;enhet&gt;</c> whose only child is a self-closing element
/// (here <c>&lt;infotype felttype="PADR" endringstype="U" /&gt;</c>). Previously
/// <c>CcrXmlProcessor.ReadEnhet</c> tested <c>IsEmptyElement</c> after <c>ReadStartElement</c>,
/// so the property reported on the first child rather than on <c>&lt;enhet&gt;</c> itself —
/// the inner loop was skipped, <c>&lt;/enhet&gt;</c> was never consumed, and the trailer check
/// downstream failed with <c>"The number of &lt;enhet&gt; elements read (1) does not match
/// the 'antallEnheter' attribute in the trailer ()"</c>
/// </summary>
public class Scenario28A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AAFY",
            name: "TOM ENHET TEST",
            cancellationToken: cancellationToken);
    }

    // NOTE: Intentionally no pretty-printing of the XML here to reproduce a bug in the processor
    // that only manifested when there was no whitespace nodes in the document.
    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?><batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd"><head avsender="ER" dato="20260520" kjoerenr="04980" mottaker="ALT" type="A" /><enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="AAFY" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20210319" datoSistEndret="20260520"><infotype felttype="PADR" endringstype="U" /></enhet><trai antallEnheter="1" avsender="ER" /></batchAjourholdXML>
        """.Trim();

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();

        var updated = await parties
            .GetOrganizationByIdentifier(_org.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        updated.ShouldNotBeNull();

        // Two invariants in one assertion: (a) the <enhet> with only a self-closing child
        // parsed end-to-end (no trailer-mismatch throw), and (b) the ("PADR", "U") branch in
        // ReadInfoType actually fired and applied its `org.MailingAddress = FieldValue.Null`
        // side-effect to the persisted row.
        updated.MailingAddress.Value.ShouldBeNull();
    }
}
