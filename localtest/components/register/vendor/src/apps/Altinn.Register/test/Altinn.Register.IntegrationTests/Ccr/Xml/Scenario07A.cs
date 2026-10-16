using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Changes the organization form of an existing organization to "ENK".
/// Also changes the address info type with field type "FADR"
/// </summary>
public class Scenario07A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ENK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20230627" datoSistEndret="20260502">
            <infotype felttype="FADR" endringstype="N">
              <postnr>5678</postnr>
              <landkode>NO</landkode>
              <kommunenr>4601</kommunenr>
              <adresse1>Testgata 2</adresse1>
            </infotype>
          </enhet>
          <trai antallEnheter="1" avsender="ER" />
        </batchAjourholdXML>
        """;

    protected override async ValueTask Verify(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        var parties = uow.GetPartyPersistence();

        var updatedOrg = await parties.GetOrganizationByIdentifier(
            _org.OrganizationIdentifier.Value!,
            PartyFieldIncludes.Party | PartyFieldIncludes.Organization,
            cancellationToken)
            .FirstOrDefaultAsync(cancellationToken);

        updatedOrg.ShouldNotBeNull();
        updatedOrg?.UnitType.Value.ShouldBe("ENK");
        updatedOrg?.BusinessAddress.Value?.PostalCode.ShouldBe("5678");
        updatedOrg?.BusinessAddress.Value?.Address?.Contains("Testgata 2").ShouldBeTrue();
    }
}
