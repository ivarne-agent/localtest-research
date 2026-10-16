using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
///  infotype FMVA and value BFLA, which are not mapped to any field in the database, should not cause any issues and should be ignored,
/// </summary>
public class Scenario19A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "BEDR",
            name: "Gammelt Bedriftsnavn AS",
            cancellationToken: cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ENK" hovedsakstype="E" undersakstype="EBTC" foersteOverfoering="N" datoFoedt="20130815" datoSistEndret="20260502">
            <infotype felttype="FMVA" endringstype="N">
              <opplysning>BFLA</opplysning>
            </infotype>
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
    }
}
