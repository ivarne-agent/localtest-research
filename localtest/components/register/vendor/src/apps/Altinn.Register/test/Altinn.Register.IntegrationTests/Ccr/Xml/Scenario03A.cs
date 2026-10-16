using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils.TestData;

namespace Altinn.Register.IntegrationTests.Ccr.Xml;

/// <summary>
/// Swaps out the Styreleder and 2 Medlemmer, additionally one Vara becomes Medlem.
/// </summary>
public class Scenario03A
    : CcrXmlUpdateTestBase
{
    private OrganizationRecord _org = null!;
    private PersonRecord _personLedeNew = null!;
    private PersonRecord _personLedeOld = null!;
    private PersonRecord _personMedlOld1 = null!;
    private PersonRecord _personMedlOld2 = null!;
    private PersonRecord _personMedlNew1 = null!;
    private PersonRecord _personMedlNew2 = null!;
    private PersonRecord _personVaraBlirMedl = null!;

    protected override async ValueTask Setup(IUnitOfWork uow, CancellationToken cancellationToken)
    {
        _org = await uow.CreateOrg(
            unitType: "AS",
            name: "REGN REVI TEST AS",
            cancellationToken: cancellationToken);

        _personLedeOld = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Styreleder"),
            cancellationToken: cancellationToken);

        _personLedeNew = await uow.CreatePerson(
            name: PersonName.Create("Ny", "Styreleder"),
            cancellationToken: cancellationToken);

        _personMedlOld1 = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Medlem 1"),
            cancellationToken: cancellationToken);

        _personMedlOld2 = await uow.CreatePerson(
            name: PersonName.Create("Forrige", "Medlem 2"),
            cancellationToken: cancellationToken);

        _personMedlNew1 = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "Medlem 1"),
            cancellationToken: cancellationToken);

        _personMedlNew2 = await uow.CreatePerson(
            name: PersonName.Create("Nytt", "Medlem 2"),
            cancellationToken: cancellationToken);

        _personVaraBlirMedl = await uow.CreatePerson(
            name: PersonName.Create("Vara", "BlirMedlem"),
            cancellationToken: cancellationToken);

        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styreleder", from: _org.PartyUuid.Value, to: _personLedeOld.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styremedlem", from: _org.PartyUuid.Value, to: _personMedlOld1.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "styremedlem", from: _org.PartyUuid.Value, to: _personMedlOld2.PartyUuid.Value, cancellationToken);
        await uow.AddRole(ExternalRoleSource.CentralCoordinatingRegister, roleIdentifier: "varamedlem", from: _org.PartyUuid.Value, to: _personVaraBlirMedl.PartyUuid.Value, cancellationToken);
    }

    [StringSyntax(StringSyntaxAttribute.Xml)]
    protected override string XmlToApply
        => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
          <head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
          <enhet organisasjonsnummer="{{_org.OrganizationIdentifier.Value}}" organisasjonsform="ESEK" hovedsakstype="E" undersakstype="EN" foersteOverfoering="N" datoFoedt="20130413" datoSistEndret="20260504">
            <samendringer data="D" felttype="LEDE" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>1</rolleRekkefoelge>
              <rolleFoedselsnr>{{_personLedeNew.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Anne</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 1</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="LEDE" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personLedeOld.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personMedlOld1.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>3</rolleRekkefoelge>
              <rolleFoedselsnr>{{_personMedlNew1.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Ola Test</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 1</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personMedlOld2.PersonIdentifier.Value}}</rolleFoedselsnr>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleFoedselsnr>{{_personMedlNew2.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Kari Marit</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>5678</postnr>
              <adresse1>Testgata 14</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="MEDL" endringstype="N" type="R">
              <rolleFratraadt>N</rolleFratraadt>
              <rolleRekkefoelge>2</rolleRekkefoelge>
              <rolleFoedselsnr>{{_personVaraBlirMedl.PersonIdentifier.Value}}</rolleFoedselsnr>
              <fornavn>Per</fornavn>
              <slektsnavn>Testperson</slektsnavn>
              <postnr>1234</postnr>
              <adresse1>Testveien 1</adresse1>
              <adresseLandkode>NO</adresseLandkode>
              <personstatus>L</personstatus>
            </samendringer>
            <samendringer data="D" felttype="VARA" endringstype="U" type="R">
              <rolleFoedselsnr>{{_personVaraBlirMedl.PersonIdentifier.Value}}</rolleFoedselsnr>
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

        roleAssignments.Count.ShouldBe(4);

        updatedOrg.ShouldNotBeNull();
        var styrelederFound = roleAssignments.Where(r => r.Identifier == "styreleder").ToList();
        var styremedlemmerFound = roleAssignments.Where(r => r.Identifier == "styremedlem").ToList();
        var varaFound = roleAssignments.Where(r => r.Identifier == "varamedlem").ToList();

        styrelederFound.ShouldHaveSingleItem().ToParty.ShouldBe(_personLedeNew.PartyUuid.Value);
        styremedlemmerFound.Count.ShouldBe(3);
        styremedlemmerFound.ShouldContain(r => r.ToParty == _personMedlNew1.PartyUuid.Value);
        styremedlemmerFound.ShouldContain(r => r.ToParty == _personMedlNew2.PartyUuid.Value);
        styremedlemmerFound.ShouldContain(r => r.ToParty == _personVaraBlirMedl.PartyUuid.Value);

        varaFound.ShouldBeEmpty();
    }
}
