using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Provides an integration test scenario for updating organization records using CcrXml batch XML, specifically testing
/// the removal of email and telephone information.
/// </summary>
/// <remarks>This test scenario sets up an organization, applies a batch XML update that removes email and
/// telephone fields, and verifies that these fields are cleared as expected. It is intended for use within the CcrXml
/// update test framework and demonstrates handling of 'U' (update) operations for specific infotypes.</remarks>
public class Scenario18A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "BEDR",
            emailAddress: "old.stuff@tobedeleted.nope",
            telephoneNumber: "12345678",
            name: "Gammelt Bedriftsnavn AS",
            cancellationToken: cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="AAFY" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20191105" datoSistEndret="20260503">
            <infotype felttype="EPOS" endringstype="U" />
            <infotype felttype="TFON" endringstype="U" />
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();

        var updatedOrg = await parties.GetOrganizationByIdentifier(_org.OrganizationIdentifier.Value!, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        updatedOrg.ShouldNotBeNull();
        updatedOrg.EmailAddress.Value.ShouldBeNull();
        updatedOrg.TelephoneNumber.Value.ShouldBeNull();
    }
}
