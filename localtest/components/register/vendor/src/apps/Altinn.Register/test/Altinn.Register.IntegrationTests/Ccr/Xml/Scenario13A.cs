using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Infotype is VEDT. We ignore this field, but the parser should not fail.
/// </summary>
public class Scenario13A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            mailingAddress: new MailingAddressRecord
            {
                Address = "Gamleveien 12 Oslo NO",
                PostalCode = "1234",
                City = "Oslo"
            },
            cancellationToken: cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20140724" datoSistEndret="20260504">
            <infotype felttype="FADR" endringstype="N">
              <postnr>1234</postnr>
              <landkode>NO</landkode>
              <kommunenr>4601</kommunenr>
              <adresse1>Testgata 12B</adresse1>
            </infotype>
            <infotype felttype="PADR" endringstype="U" />
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
        updatedOrg.MailingAddress.Value.ShouldBeNull();
        updatedOrg.BusinessAddress.Value.ShouldNotBeNull().Address?.Contains("Testgata 12B").ShouldBeTrue();
    }
}
