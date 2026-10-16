using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Provides a test scenario for applying and verifying XML-based updates to an organization record using the
/// "batchAjourholdXML" format.
/// </summary>
/// <remarks>This scenario sets up an organization with initial values, applies an XML update that modifies the
/// email address and internet address, and verifies that the changes are correctly persisted. It is intended for use in
/// integration or system tests that validate XML update processing logic.</remarks>
public class Scenario14A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        // we can specify things we want here
        _org = await uow.CreateOrg(
            unitType: "BEDR",
            emailAddress: "old@example.com",
            internetAddress: FieldValue.Null,
            mobileNumber: "12345678",
            cancellationToken: cancellationToken);
    }

    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
            <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
            <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="BEDR" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130916" datoSistEndret="20260504">
                <infotype felttype="EPOS" endringstype="N">
                    <opplysning>test.testperson@example.com</opplysning>
                </infotype>
                <infotype felttype="IADR" endringstype="N">
                    <opplysning>example.com/consultant/test-testperson</opplysning>
                </infotype>
                <infotype felttype="MTLF" endringstype="U" />
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
        updatedOrg.UnitType.ShouldBe("BEDR");
        updatedOrg.EmailAddress.ShouldBe("test.testperson@example.com");
        updatedOrg.InternetAddress.ShouldBe("example.com/consultant/test-testperson");
        updatedOrg.MobileNumber.ShouldBeNull();
    }
}
