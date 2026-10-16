using System.Data;
using System.Diagnostics;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Persistence.Tests.Utils;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.TestData;
using Altinn.Urn;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Register.Persistence.Tests;

public class PostgreSqlPartyPersistenceTests(ITestOutputHelper output)
    : DatabaseTestBase
{
    protected override ITestOutputHelper? TestOutputHelper => output;

    private readonly static Guid OrganizationWithChildrenUuid = Guid.Parse("b6368d0a-bce4-4798-8460-f4f86fc354c2");
    private readonly static uint OrganizationWithChildrenId = 50056131;
    private readonly static OrganizationIdentifier OrganizationWithChildrenIdentifier = OrganizationIdentifier.Parse("910114166");
    private readonly static Guid ChildOrganizationUuid = Guid.Parse("08cb91ff-75a4-45a4-b141-3c6be1bf8728");
    private readonly static Guid PersonUuid = Guid.Parse("1dee49bc-7758-43d9-b5de-63c79dbe13a3");
    private readonly static PersonIdentifier PersonIdentifier = PersonIdentifier.Parse("01056261032");

    private IUnitOfWork? _unitOfWork;
    private NpgsqlConnection? _connection;
    private PostgreSqlPartyPersistence? _persistence;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await NewTransaction();
    }

    protected override async ValueTask DisposeAsync()
    {
        if (_unitOfWork is not null)
        {
            await _unitOfWork.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private ValueTask CommitAsync()
        => _unitOfWork!.CommitAsync(CancellationToken);

    private NpgsqlConnection Connection
        => _connection!;

    private PostgreSqlPartyPersistence Persistence
        => _persistence!;

    private IUnitOfWork UoW
        => _unitOfWork!;

    private async Task NewTransaction(bool commit = true)
    {
        if (_unitOfWork is { } uow)
        {
            if (commit)
            {
                await uow.CommitAsync(CancellationToken);
            }

            await uow.DisposeAsync();
        }

        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        _unitOfWork = await uowManager.CreateAsync(activityName: "test", cancellationToken: CancellationToken);
        _connection = _unitOfWork.GetRequiredService<NpgsqlConnection>();
        _persistence = _unitOfWork.GetRequiredService<PostgreSqlPartyPersistence>();
    }

    [Fact]
    public void CanGet_IPartyPersistence()
    {
        var persistence = _unitOfWork!.GetPartyPersistence();
        persistence.ShouldBeSameAs(Persistence);
    }

    [Fact]
    public void CanGet_IPartyRolePersistence()
    {
        var persistence = _unitOfWork!.GetPartyExternalRolePersistence();
        persistence.ShouldBeSameAs(Persistence);
    }

    [Fact]
    public async Task GetPartyById_NoneExistingUuid_ReturnsEmpty()
    {
        var partyUuid = Guid.Parse("F0000000-0000-0000-0000-000000000000");
        var result = await Persistence
            .GetPartyById(partyUuid, cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPartyById_NoneExistingId_ReturnsEmpty()
    {
        var partyId = 0U;
        var result = await Persistence
            .GetPartyById(partyId, cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPartyById_Returns_SingleParty()
    {
        var result = await Persistence
            .GetPartyById(OrganizationWithChildrenUuid, cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<OrganizationRecord>();
        party.ParentOrganizationUuid.ShouldBeUnset();

        party.PartyUuid.ShouldBe(OrganizationWithChildrenUuid);
        party.PartyId.ShouldBe(OrganizationWithChildrenId);
        party.PartyType.ShouldBe(PartyRecordType.Organization);
        party.DisplayName.ShouldBe("MOEN OG BJØRNEVATN");
        party.PersonIdentifier.ShouldBeNull();
        party.OrganizationIdentifier.ShouldBe(OrganizationWithChildrenIdentifier);

        party.UnitStatus.ShouldBeUnset();
        party.UnitType.ShouldBeUnset();
        party.TelephoneNumber.ShouldBeUnset();
        party.MobileNumber.ShouldBeUnset();
        party.FaxNumber.ShouldBeUnset();
        party.EmailAddress.ShouldBeUnset();
        party.InternetAddress.ShouldBeUnset();
        party.MailingAddress.ShouldBeUnset();
        party.BusinessAddress.ShouldBeUnset();
    }

    [Fact]
    public async Task GetPartyById_CanGet_OrganizationData()
    {
        var result = await Persistence
            .GetPartyById(OrganizationWithChildrenUuid, include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<OrganizationRecord>();
        party.ParentOrganizationUuid.ShouldBeUnset();

        party.PartyUuid.ShouldBe(OrganizationWithChildrenUuid);
        party.PartyId.ShouldBe(OrganizationWithChildrenId);
        party.PartyType.ShouldBe(PartyRecordType.Organization);
        party.DisplayName.ShouldBe("MOEN OG BJØRNEVATN");
        party.PersonIdentifier.ShouldBeNull();
        party.OrganizationIdentifier.ShouldBe(OrganizationWithChildrenIdentifier);

        party.UnitStatus.ShouldBe("N");
        party.UnitType.ShouldBe("FLI");
        party.TelephoneNumber.ShouldBeNull();
        party.MobileNumber.ShouldBeNull();
        party.FaxNumber.ShouldBeNull();
        party.EmailAddress.ShouldBe("test@test.test");
        party.InternetAddress.ShouldBeNull();
        party.MailingAddress.ShouldBeNull();
        party.BusinessAddress.ShouldBeNull();
    }

    [Fact]
    public async Task GetPartyByUserId_Returns_SingleParty()
    {
        var person = await UoW.CreatePerson(cancellationToken: CancellationToken);
        var personUserId = person.UserIds.CurrentValue.ShouldHaveValue();

        var result = await Persistence
            .GetPartyByUserId(personUserId, include: PartyFieldIncludes.Party, cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();

        party.PartyUuid.ShouldBe(person.PartyUuid);
        party.PartyId.ShouldBe(person.PartyId);
        party.PartyType.ShouldBe(person.PartyType);
        party.DisplayName.ShouldBe(person.DisplayName);
        party.PersonIdentifier.ShouldBe(person.PersonIdentifier);
        party.OrganizationIdentifier.ShouldBe(person.OrganizationIdentifier);
        party.UserIds.CurrentValue.ShouldBe(personUserId);
    }

    [Fact]
    public async Task GetPartyByUserId_HistoricalId_Returns_SingleParty_WithMultipleUserIds()
    {
        var person = await UoW.CreatePerson(cancellationToken: CancellationToken);
        var personUserIds = person.UserIds.Values.ShouldHaveValue();
        personUserIds.Count().ShouldBeGreaterThanOrEqualTo(3);

        var personUserId = personUserIds[0];
        var historicalUserId1 = personUserIds[1];
        var historicalUserId2 = personUserIds[2];

        {
            var result = await Persistence
                .GetPartyByUserId(historicalUserId1, include: PartyFieldIncludes.Party, cancellationToken: CancellationToken)
                .ToListAsync(CancellationToken);

            var party = result.ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();

            party.PartyUuid.ShouldBe(person.PartyUuid);
            party.PartyId.ShouldBe(person.PartyId);
            party.PartyType.ShouldBe(person.PartyType);
            party.DisplayName.ShouldBe(person.DisplayName);
            party.PersonIdentifier.ShouldBe(person.PersonIdentifier);
            party.OrganizationIdentifier.ShouldBe(person.OrganizationIdentifier);
            party.UserIds.Values.ShouldBe([personUserId, historicalUserId1]);
        }

        {
            var result = await Persistence.GetPartyByUserId(historicalUserId2, include: PartyFieldIncludes.Party, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

            var party = result.ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();

            party.PartyUuid.ShouldBe(person.PartyUuid);
            party.PartyId.ShouldBe(person.PartyId);
            party.PartyType.ShouldBe(person.PartyType);
            party.DisplayName.ShouldBe(person.DisplayName);
            party.PersonIdentifier.ShouldBe(person.PersonIdentifier);
            party.OrganizationIdentifier.ShouldBe(person.OrganizationIdentifier);
            party.UserIds.Values.ShouldBe([personUserId, historicalUserId2]);
        }
    }

    [Fact]
    public async Task GetPartyByOrganizationIdentifier_CanGet_OrganizationData()
    {
        var result = await Persistence.GetOrganizationByIdentifier(OrganizationWithChildrenIdentifier, include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<OrganizationRecord>();
        party.ParentOrganizationUuid.ShouldBeUnset();

        party.PartyUuid.ShouldBe(OrganizationWithChildrenUuid);
        party.PartyId.ShouldBe(OrganizationWithChildrenId);
        party.PartyType.ShouldBe(PartyRecordType.Organization);
        party.DisplayName.ShouldBe("MOEN OG BJØRNEVATN");
        party.PersonIdentifier.ShouldBeNull();
        party.OrganizationIdentifier.ShouldBe(OrganizationWithChildrenIdentifier);

        party.UnitStatus.ShouldBe("N");
        party.UnitType.ShouldBe("FLI");
        party.TelephoneNumber.ShouldBeNull();
        party.MobileNumber.ShouldBeNull();
        party.FaxNumber.ShouldBeNull();
        party.EmailAddress.ShouldBe("test@test.test");
        party.InternetAddress.ShouldBeNull();
        party.MailingAddress.ShouldBeNull();
        party.BusinessAddress.ShouldBeNull();
    }

    [Fact]
    public async Task GetPartyById_CanGet_PersonData()
    {
        var result = await Persistence.GetPartyById(PersonUuid, include: PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();
        party.PartyUuid.ShouldBe(PersonUuid);
        party.PartyId.ShouldBe(50002129);
        party.PartyType.ShouldBe(PartyRecordType.Person);
        party.DisplayName.ShouldBe("SANNE BJØRKUM");
        party.PersonIdentifier.ShouldBe(PersonIdentifier);
        party.OrganizationIdentifier.ShouldBeNull();

        party.FirstName.ShouldBe("SANNE");
        party.MiddleName.ShouldBeNull();
        party.LastName.ShouldBe("BJØRKUM");
        party.Address.ShouldBe(new StreetAddressRecord { PostalCode = "3230", City = "SANDEFJORD" });
        party.MailingAddress.ShouldBe(new MailingAddressRecord { Address = "Granholmveien 19", PostalCode = "3230", City = "SANDEFJORD" });
        party.DateOfBirth.ShouldBe(new DateOnly(1862, 05, 01));
        party.DateOfDeath.ShouldBeNull();
    }

    [Fact]
    public async Task GetPartyByPersonIdentifier_CanGet_PersonData()
    {
        var result = await Persistence.GetPersonByIdentifier(PersonIdentifier, include: PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();
        party.PartyUuid.ShouldBe(PersonUuid);
        party.PartyId.ShouldBe(50002129);
        party.PartyType.ShouldBe(PartyRecordType.Person);
        party.DisplayName.ShouldBe("SANNE BJØRKUM");
        party.PersonIdentifier.ShouldBe(PersonIdentifier);
        party.OrganizationIdentifier.ShouldBeNull();

        party.FirstName.ShouldBe("SANNE");
        party.MiddleName.ShouldBeNull();
        party.LastName.ShouldBe("BJØRKUM");
        party.Address.ShouldBe(new StreetAddressRecord { PostalCode = "3230", City = "SANDEFJORD" });
        party.MailingAddress.ShouldBe(new MailingAddressRecord { Address = "Granholmveien 19", PostalCode = "3230", City = "SANDEFJORD" });
        party.DateOfBirth.ShouldBe(new DateOnly(1862, 05, 01));
        party.DateOfDeath.ShouldBeNull();
    }

    [Fact]
    public async Task LookupParties_MultipleIdentifiers_ToSameParty_ReturnsSingleParty()
    {
        var result = await Persistence.LookupParties(
            partyUuids: [OrganizationWithChildrenUuid],
            partyIds: [OrganizationWithChildrenId],
            organizationIdentifiers: [OrganizationWithChildrenIdentifier],
            cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        var party = result.ShouldHaveSingleItem().ShouldBeOfType<OrganizationRecord>();
        party.ParentOrganizationUuid.ShouldBeUnset();

        party.PartyUuid.ShouldBe(OrganizationWithChildrenUuid);
        party.PartyId.ShouldBe(OrganizationWithChildrenId);
        party.PartyType.ShouldBe(PartyRecordType.Organization);
        party.DisplayName.ShouldBe("MOEN OG BJØRNEVATN");
        party.PersonIdentifier.ShouldBeNull();
        party.OrganizationIdentifier.ShouldBe(OrganizationWithChildrenIdentifier);

        party.UnitStatus.ShouldBeUnset();
        party.UnitType.ShouldBeUnset();
        party.TelephoneNumber.ShouldBeUnset();
        party.MobileNumber.ShouldBeUnset();
        party.FaxNumber.ShouldBeUnset();
        party.EmailAddress.ShouldBeUnset();
        party.InternetAddress.ShouldBeUnset();
        party.MailingAddress.ShouldBeUnset();
        party.BusinessAddress.ShouldBeUnset();
    }

    [Fact]
    public async Task LookupParties_CanReturn_MultipleParties()
    {
        var person1 = await UoW.CreatePerson(cancellationToken: CancellationToken);
        var person2 = await UoW.CreatePerson(cancellationToken: CancellationToken);
        var si = await UoW.CreateSelfIdentifiedUser(type: SelfIdentifiedUserType.IdPortenEmail, cancellationToken: CancellationToken);
        var sysUser = await UoW.CreateSystemUser(OrganizationWithChildrenUuid, cancellationToken: CancellationToken);

        var person1UserId = person1.UserIds.CurrentValue.ShouldHaveValue();
        var person2UserId = person2.UserIds.CurrentValue.ShouldHaveValue();
        var person2HistoricalUserId = person2.UserIds.Values.ShouldHaveValue()[1];

        var result = await Persistence.LookupParties(
            organizationIdentifiers: [OrganizationWithChildrenIdentifier],
            personIdentifiers: [PersonIdentifier],
            userIds: [person1UserId, person2HistoricalUserId],
            selfIdentifiedEmails: [si.Email.Value!],
            externalUrns: [sysUser.ExternalUrn.Value!],
            cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(6);

        var testDataPers = result.Where(static p => p.PartyUuid == PersonUuid).ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();
        var testDataOrg = result.Where(static p => p.PartyUuid == OrganizationWithChildrenUuid).ShouldHaveSingleItem().ShouldBeOfType<OrganizationRecord>();
        var person1Result = result.Where(p => p.PartyUuid == person1.PartyUuid).ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();
        var person2Result = result.Where(p => p.PartyUuid == person2.PartyUuid).ShouldHaveSingleItem().ShouldBeOfType<PersonRecord>();
        var siResult = result.Where(p => p.PartyUuid == si.PartyUuid).ShouldHaveSingleItem().ShouldBeOfType<SelfIdentifiedUserRecord>();
        var sysUserResult = result.Where(p => p.PartyUuid == sysUser.PartyUuid).ShouldHaveSingleItem().ShouldBeOfType<SystemUserRecord>();

        person1Result.UserIds.Values.ShouldBe([person1UserId]);
        person2Result.UserIds.Values.ShouldBe([person2UserId, person2HistoricalUserId]);
    }

    [Fact]
    public async Task LookupParties_With_SubUnits_OrdersCorrectly()
    {
        var administrativeSubUnitUuid = Guid.Parse("d0000000-0000-4000-8000-000000000001");
        await UoW.CreateOrg(uuid: administrativeSubUnitUuid, unitType: "BEDR", cancellationToken: CancellationToken);
        await UoW.AddRole(
            ExternalRoleSource.CentralCoordinatingRegister,
            "administrativ-enhet-offentlig-sektor",
            from: administrativeSubUnitUuid,
            to: Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"),
            cancellationToken: CancellationToken);

        var result = await Persistence.LookupParties(
            partyUuids: [
                Guid.Parse("b6368d0a-bce4-4798-8460-f4f86fc354c2"),
                Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"),
            ],
            include: PartyFieldIncludes.Party,
            transforms: PartyListTransforms.IncludeSubUnits,
            cancellationToken: CancellationToken)
            .Cast<OrganizationRecord>()
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(7);

        result[0].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"));
        result[0].ParentOrganizationUuid.ShouldBeUnset();

        result[1].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("4b28742a-5cd0-400e-a096-bd9817d12dca"));
        result[1].ParentOrganizationUuid.ShouldHaveValue().ShouldBe(Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"));

        result[2].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("ad18578d-94cb-4774-8f37-5b24801c219b"));
        result[2].ParentOrganizationUuid.ShouldHaveValue().ShouldBe(Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"));

        result[3].PartyUuid.ShouldHaveValue().ShouldBe(administrativeSubUnitUuid);
        result[3].ParentOrganizationUuid.ShouldHaveValue().ShouldBe(Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"));

        result[4].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("ec09feda-5dba-4b84-ad0b-f7886e6082cd"));
        result[4].ParentOrganizationUuid.ShouldHaveValue().ShouldBe(Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8"));

        result[5].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("b6368d0a-bce4-4798-8460-f4f86fc354c2"));
        result[5].ParentOrganizationUuid.ShouldBeUnset();

        result[6].PartyUuid.ShouldHaveValue().ShouldBe(Guid.Parse("08cb91ff-75a4-45a4-b141-3c6be1bf8728"));
        result[6].ParentOrganizationUuid.ShouldHaveValue().ShouldBe(Guid.Parse("b6368d0a-bce4-4798-8460-f4f86fc354c2"));
    }

    [Fact]
    public async Task LookupParties_Shared_SubUnit()
    {
        var child = await UoW.CreateOrg(uuid: Guid.Parse("10000000-0000-4000-8000-000000000001"), unitType: "BEDR", cancellationToken: CancellationToken);
        var administrativeChild = await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-4000-8000-000000000001"), unitType: "BEDR", cancellationToken: CancellationToken);
        var parent1 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);
        var parent2 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);

        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: child.PartyUuid.Value, to: parent2.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "administrativ-enhet-offentlig-sektor", from: administrativeChild.PartyUuid.Value, to: parent1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "administrativ-enhet-offentlig-sektor", from: administrativeChild.PartyUuid.Value, to: parent2.PartyUuid.Value, cancellationToken: CancellationToken);

        var result = await Persistence.LookupParties(
            partyUuids: [parent1.PartyUuid.Value, parent2.PartyUuid.Value],
            include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization,
            transforms: PartyListTransforms.IncludeSubUnits,
            cancellationToken: CancellationToken)
            .Cast<OrganizationRecord>()
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(6);

        List<OrganizationRecord> parents = [result[0], result[3]];
        parents.Sort(static (a, b) => a.VersionId.Value.CompareTo(b.VersionId.Value));

        result[0].PartyUuid.ShouldBe(parents[0].PartyUuid);
        result[0].ParentOrganizationUuid.ShouldBeUnset();

        result[1].PartyUuid.ShouldBe(child.PartyUuid);
        result[1].ParentOrganizationUuid.ShouldBe(parents[0].PartyUuid);

        result[2].PartyUuid.ShouldBe(administrativeChild.PartyUuid);
        result[2].ParentOrganizationUuid.ShouldBe(parents[0].PartyUuid);

        result[3].PartyUuid.ShouldBe(parents[1].PartyUuid);
        result[3].ParentOrganizationUuid.ShouldBeUnset();

        result[4].PartyUuid.ShouldBe(child.PartyUuid);
        result[4].ParentOrganizationUuid.ShouldBe(parents[1].PartyUuid);

        result[5].PartyUuid.ShouldBe(administrativeChild.PartyUuid);
        result[5].ParentOrganizationUuid.ShouldBe(parents[1].PartyUuid);
    }

    [Fact]
    public async Task LookupParties_ReplaceWithMainUnits()
    {
        var subUnit1 = await UoW.CreateOrg(uuid: Guid.Parse("10000000-0000-4000-8000-000000000002"), unitType: "BEDR", cancellationToken: CancellationToken);
        var subUnit2 = await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-4000-8000-000000000002"), unitType: "BEDR", cancellationToken: CancellationToken);
        var sharedSubUnit = await UoW.CreateOrg(uuid: Guid.Parse("30000000-0000-4000-8000-000000000002"), unitType: "BEDR", cancellationToken: CancellationToken);
        var administrativeSubUnit = await UoW.CreateOrg(uuid: Guid.Parse("40000000-0000-4000-8000-000000000002"), unitType: "BEDR", cancellationToken: CancellationToken);
        var withoutMainUnit = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);
        var mainUnit1 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);
        var mainUnit2 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);

        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit1.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit2.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: sharedSubUnit.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "ikke-naeringsdrivende-hovedenhet", from: sharedSubUnit.PartyUuid.Value, to: mainUnit2.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "administrativ-enhet-offentlig-sektor", from: administrativeSubUnit.PartyUuid.Value, to: mainUnit2.PartyUuid.Value, cancellationToken: CancellationToken);

        var result = await Persistence.LookupParties(
            partyUuids: [
                subUnit1.PartyUuid.Value,
                subUnit2.PartyUuid.Value,
                sharedSubUnit.PartyUuid.Value,
                administrativeSubUnit.PartyUuid.Value,
                withoutMainUnit.PartyUuid.Value,
                mainUnit1.PartyUuid.Value,
            ],
            include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization,
            transforms: PartyListTransforms.ReplaceWithMainUnits,
            cancellationToken: CancellationToken)
            .Cast<OrganizationRecord>()
            .ToListAsync(CancellationToken);

        // sub-units sharing a main unit collapse into a single entry, a sub-unit with several
        // main units yields all of them, and parties without a main unit are dropped
        result.Count.ShouldBe(2);
        result.Select(static r => r.PartyUuid.Value).ShouldBe([mainUnit1.PartyUuid.Value, mainUnit2.PartyUuid.Value], ignoreOrder: true);
        result[0].VersionId.Value.ShouldBeLessThan(result[1].VersionId.Value);

        result[0].ParentOrganizationUuid.ShouldBeUnset();
        result[1].ParentOrganizationUuid.ShouldBeUnset();
    }

    [Fact]
    public async Task LookupParties_ReplaceWithMainUnits_IncludeSubUnits()
    {
        var subUnit1 = await UoW.CreateOrg(uuid: Guid.Parse("10000000-0000-4000-8000-000000000003"), unitType: "BEDR", cancellationToken: CancellationToken);
        var subUnit2 = await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-4000-8000-000000000003"), unitType: "BEDR", cancellationToken: CancellationToken);
        var sharedSubUnit = await UoW.CreateOrg(uuid: Guid.Parse("30000000-0000-4000-8000-000000000003"), unitType: "BEDR", cancellationToken: CancellationToken);
        var administrativeSubUnit = await UoW.CreateOrg(uuid: Guid.Parse("40000000-0000-4000-8000-000000000003"), unitType: "BEDR", cancellationToken: CancellationToken);
        var withoutMainUnit = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);
        var mainUnit1 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);
        var mainUnit2 = await UoW.CreateOrg(unitType: "AS", cancellationToken: CancellationToken);

        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit1.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: subUnit2.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", from: sharedSubUnit.PartyUuid.Value, to: mainUnit1.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "ikke-naeringsdrivende-hovedenhet", from: sharedSubUnit.PartyUuid.Value, to: mainUnit2.PartyUuid.Value, cancellationToken: CancellationToken);
        await UoW.AddRole(ExternalRoleSource.CentralCoordinatingRegister, "administrativ-enhet-offentlig-sektor", from: administrativeSubUnit.PartyUuid.Value, to: mainUnit2.PartyUuid.Value, cancellationToken: CancellationToken);

        // only one sub-unit per main unit is looked up; the siblings are expected
        // to come back through the sub-unit expansion of the replaced main units
        var result = await Persistence.LookupParties(
            partyUuids: [
                subUnit1.PartyUuid.Value,
                administrativeSubUnit.PartyUuid.Value,
                withoutMainUnit.PartyUuid.Value,
            ],
            include: PartyFieldIncludes.Party | PartyFieldIncludes.Organization,
            transforms: PartyListTransforms.ReplaceWithMainUnits | PartyListTransforms.IncludeSubUnits,
            cancellationToken: CancellationToken)
            .Cast<OrganizationRecord>()
            .ToListAsync(CancellationToken);

        Dictionary<Guid, Guid[]> expectedSubUnits = new()
        {
            [mainUnit1.PartyUuid.Value] = [subUnit1.PartyUuid.Value, subUnit2.PartyUuid.Value, sharedSubUnit.PartyUuid.Value],
            [mainUnit2.PartyUuid.Value] = [sharedSubUnit.PartyUuid.Value, administrativeSubUnit.PartyUuid.Value],
        };

        result.Count.ShouldBe(7);

        var mainUnits = result.Where(static r => r.ParentOrganizationUuid.IsUnset).ToList();
        mainUnits.Count.ShouldBe(2);
        mainUnits.Select(static r => r.PartyUuid.Value).ShouldBe([mainUnit1.PartyUuid.Value, mainUnit2.PartyUuid.Value], ignoreOrder: true);
        mainUnits[0].VersionId.Value.ShouldBeLessThan(mainUnits[1].VersionId.Value);

        // each main unit is directly followed by its sub-units, ordered by sub-unit uuid
        var index = 0;
        foreach (var mainUnit in mainUnits)
        {
            result[index].PartyUuid.ShouldBe(mainUnit.PartyUuid);
            result[index].ParentOrganizationUuid.ShouldBeUnset();
            index++;

            foreach (var subUnitUuid in expectedSubUnits[mainUnit.PartyUuid.Value])
            {
                result[index].PartyUuid.ShouldHaveValue().ShouldBe(subUnitUuid);
                result[index].ParentOrganizationUuid.ShouldBe(mainUnit.PartyUuid);
                index++;
            }
        }

        index.ShouldBe(result.Count);
    }

    [Fact]
    public async Task LookupParties_ByUsername_MinimalReturn()
    {
        var userId = (await UoW.GetNewUserIds(count: 1, cancellationToken: CancellationToken))[0];
        var person = await UoW.CreatePerson(user: new PartyUserRecord(userId, "some-user-name"), cancellationToken: CancellationToken);

        var result = await Persistence.LookupParties(
            usernames: ["some-user-name"],
            include: PartyFieldIncludes.Username,
            cancellationToken: CancellationToken)
            .Cast<PersonRecord>()
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LookupParties_ByHistoricalUsername_MinimalReturn()
    {
        var userId = (await UoW.GetNewUserIds(count: 1, cancellationToken: CancellationToken))[0];
        var person = await UoW.CreatePerson(user: new PartyUserRecord(userId, "some-user-name"), cancellationToken: CancellationToken);
        await UoW.GetPartyPersistence().UpsertParty(
            person with
            {
                Usernames = PartyHistoricalAggregate<string>.CreateCurrent("updated-user-name"),
            },
            CancellationToken);

        var result = await Persistence.LookupParties(
            usernames: ["some-user-name"],
            include: PartyFieldIncludes.Username,
            cancellationToken: CancellationToken)
            .Cast<PersonRecord>()
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(1);
        result[0].Usernames.Values.ShouldBe(["updated-user-name", "some-user-name"]);
    }

    [Fact]
    public async Task GetRolesFromNonExistingParty_ReturnsEmpty()
    {
        var partyUuid = Guid.Empty;

        var roles = await Persistence.GetExternalRoleAssignmentsFromParty(partyUuid, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRolesToNonExistingParty_ReturnsEmpty()
    {
        var partyUuid = Guid.Empty;

        var roles = await Persistence.GetExternalRoleAssignmentsToParty(partyUuid, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        roles.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRolesFromParty_ReturnsRoles()
    {
        var roles = await Persistence.GetExternalRoleAssignmentsFromParty(ChildOrganizationUuid, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var role = roles.ShouldHaveSingleItem();
        role.Source.ShouldBe(ExternalRoleSource.CentralCoordinatingRegister);
        role.Identifier.ShouldBe("hovedenhet");
        role.FromParty.ShouldBe(ChildOrganizationUuid);
        role.ToParty.ShouldBe(OrganizationWithChildrenUuid);

        role.Name.ShouldBeUnset();
        role.Description.ShouldBeUnset();
    }

    [Fact]
    public async Task GetRoles_CanInclude_RoleDefinitions()
    {
        var roles = await Persistence.GetExternalRoleAssignmentsFromParty(ChildOrganizationUuid, include: PartyExternalRoleAssignmentFieldIncludes.RoleAssignment | PartyExternalRoleAssignmentFieldIncludes.RoleDefinition, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        var role = roles.ShouldHaveSingleItem();
        role.Source.ShouldBe(ExternalRoleSource.CentralCoordinatingRegister);
        role.Identifier.ShouldBe("hovedenhet");
        role.FromParty.ShouldBe(ChildOrganizationUuid);
        role.ToParty.ShouldBe(OrganizationWithChildrenUuid);

        IReadOnlyDictionary<LangCode, string> name = role.Name.ShouldHaveValue();
        IReadOnlyDictionary<LangCode, string> description = role.Description.ShouldHaveValue();

        name[LangCode.En].ShouldBe("Has as the registration entity");
        name[LangCode.Nb].ShouldBe("Har som registreringsenhet");
        name[LangCode.Nn].ShouldBe("Har som registreringseininga");

        description[LangCode.En].ShouldBe("Has as the registration entity");
        description[LangCode.Nb].ShouldBe("Har som registreringsenhet");
        description[LangCode.Nn].ShouldBe("Har som registreringseininga");
    }

    [Fact]
    public async Task GetRolesToParty_ReturnsRoles()
    {
        var party = Guid.Parse("e2081abd-a16f-4302-93b0-05aaa42023e8");
        var roles = await Persistence.GetExternalRoleAssignmentsToParty(party, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        roles.Count.ShouldBe(3);

        foreach (var role in roles)
        {
            role.Source.ShouldBe(ExternalRoleSource.CentralCoordinatingRegister);
            role.Identifier.ShouldBe("hovedenhet");
            role.FromParty.ShouldHaveValue();
            role.ToParty.ShouldBe(party);

            role.Name.ShouldBeUnset();
            role.Description.ShouldBeUnset();
        }
    }

    [Fact]
    public async Task GetRolesToParty_FilterByRole()
    {
        var role1 = await UoW.CreateFakeRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, "fake", CancellationToken);
        var role2 = await UoW.CreateFakeRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, "fake-other", CancellationToken);

        var org1 = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var org2 = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var org3 = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var org4 = await UoW.CreateOrg(cancellationToken: CancellationToken);

        await Persistence.UpsertExternalRolesFromPartyBySource(
            commandId: Guid.CreateVersion7(),
            partyUuid: org2.PartyUuid.Value,
            roleSource: ExternalRoleSource.CentralCoordinatingRegister,
            update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                new(role1.Identifier, org1.PartyUuid.Value),
                new(role2.Identifier, org1.PartyUuid.Value),
            ]),
            cancellationToken: CancellationToken);

        await Persistence.UpsertExternalRolesFromPartyBySource(
            commandId: Guid.CreateVersion7(),
            partyUuid: org3.PartyUuid.Value,
            roleSource: ExternalRoleSource.CentralCoordinatingRegister,
            update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                new(role1.Identifier, org1.PartyUuid.Value),
            ]),
            cancellationToken: CancellationToken);

        await Persistence.UpsertExternalRolesFromPartyBySource(
            commandId: Guid.CreateVersion7(),
            partyUuid: org4.PartyUuid.Value,
            roleSource: ExternalRoleSource.CentralCoordinatingRegister,
            update: PartyExternalRoleAssignmentsUpdate.CreateFull([
                new(role2.Identifier, org1.PartyUuid.Value),
            ]),
            cancellationToken: CancellationToken);

        var roles = await Persistence.GetExternalRoleAssignmentsToParty(
            partyUuid: org1.PartyUuid.Value,
            role: new(role1.Source, role1.Identifier),
            cancellationToken: CancellationToken)
            .ToListAsync(CancellationToken);

        roles.Count.ShouldBe(2);
        roles.Where(r => r.FromParty.Value == org2.PartyUuid.Value).ShouldHaveSingleItem();
        roles.Where(r => r.FromParty.Value == org3.PartyUuid.Value).ShouldHaveSingleItem();
        foreach (var roleAssignment in roles)
        {
            roleAssignment.ToParty.ShouldBe(org1.PartyUuid.Value);
        }
    }

    [Theory]
    [InlineData(PartyFieldIncludes.Party)]
    [InlineData(PartyFieldIncludes.Identifiers)]
    [InlineData(PartyFieldIncludes.Party | PartyFieldIncludes.Organization | PartyFieldIncludes.Person)]
    public async Task GetPartyStream(PartyFieldIncludes includes)
    {
        var items = await Persistence.GetPartyStream(0, 100, includes, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        items.Count.ShouldBe(100);
    }

    [Fact]
    public async Task GetPartyStream_IgnoresSequenceLocksFromOtherDatabases()
    {
        await using var otherDatabase = await CreateDatabaseFromTemplate(CancellationToken);
        await using var otherConnection = new NpgsqlConnection(otherDatabase.ConnectionString);
        await otherConnection.OpenAsync(CancellationToken);
        await using var otherTransaction = await otherConnection.BeginTransactionAsync(CancellationToken);
        await using var otherCommand = otherConnection.CreateCommand();
        otherCommand.CommandText = /*strpsql*/"SELECT register.tx_nextval('register.party_version_id_seq')";
        await otherCommand.ExecuteNonQueryAsync(CancellationToken);

        await using var maxSafeValueCommand = Connection.CreateCommand();
        maxSafeValueCommand.CommandText = /*strpsql*/"SELECT register.tx_max_safeval('register.party_version_id_seq')";
        var maxSafeValue = (long)(await maxSafeValueCommand.ExecuteScalarAsync(CancellationToken))!;

        maxSafeValue.ShouldBe(long.MaxValue);

        var items = await Persistence.GetPartyStream(0, 100, PartyFieldIncludes.Party, cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        items.Count.ShouldBe(100);
    }

    #region Upsert Org

    [Fact]
    public async Task UpsertParty_Org_Inserts_New_Org()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new OrganizationRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<OrganizationRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Org_NoPartyIds_Inserts_New_Org()
    {
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);

        var toInsert = new OrganizationRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<OrganizationRecord>();
        inserted.PartyUuid.ShouldHaveValue();
        inserted.PartyId.ShouldHaveValue();
        inserted.PartyId.Value.ShouldBeGreaterThan(1_000_000_000U);
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(inserted.PartyUuid.Value, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { PartyId = inserted.PartyId, PartyUuid = inserted.PartyUuid, VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Org_Updates_Name_And_Updated_And_OrgProps()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new OrganizationRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            UnitStatus = "U",
            UnitType = "hovedenhet",
            TelephoneNumber = "tel",
            MobileNumber = "mob",
            FaxNumber = "fax",
            EmailAddress = "email",
            InternetAddress = "internet",
            MailingAddress = new MailingAddressRecord { Address = "mailing", City = "mailing city", PostalCode = "0123" },
            BusinessAddress = new MailingAddressRecord { Address = "business", City = "business city", PostalCode = "0123" },
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<OrganizationRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_Org_NoPartyIds_Updates_Name_And_Updated_And_OrgProps()
    {
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);

        var toInsert = new OrganizationRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();
        var inserted = result.Value!;
        inserted.PartyUuid.ShouldHaveValue();
        inserted.PartyId.ShouldHaveValue();
        inserted.PartyId.Value.ShouldBeGreaterThan(1_000_000_000U);

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            UnitStatus = "U",
            UnitType = "hovedenhet",
            TelephoneNumber = "tel",
            MobileNumber = "mob",
            FaxNumber = "fax",
            EmailAddress = "email",
            InternetAddress = "internet",
            MailingAddress = new MailingAddressRecord { Address = "mailing", City = "mailing city", PostalCode = "0123" },
            BusinessAddress = new MailingAddressRecord { Address = "business", City = "business city", PostalCode = "0123" },
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<OrganizationRecord>();
        updated.ShouldBeEquivalentTo(expected with { PartyId = inserted.PartyId, PartyUuid = inserted.PartyUuid, VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(inserted.PartyUuid.Value, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_Org_Cannot_Update_Id()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new OrganizationRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            PartyId = id + 1,
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Org_Cannot_Update_OrgNr()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new OrganizationRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            OrganizationIdentifier = await UoW.GetNewOrgNumber(CancellationToken),
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Org_CannotInsert_WithSamePartyId()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var orgNo = await UoW.GetNewOrgNumber(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new OrganizationRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var uuid2 = Guid.NewGuid();
        var orgNo2 = await UoW.GetNewOrgNumber(CancellationToken);
        var toInsert2 = new OrganizationRecord
        {
            PartyUuid = uuid2,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(orgNo2),
            DisplayName = "Test",
            PersonIdentifier = null,
            OrganizationIdentifier = orgNo2,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = orgNo2.ToString().StartsWith('0') ? OrganizationSource.RegisteredWithSkatteetaten : OrganizationSource.CentralCoordinatingRegister,
            UnitStatus = "N",
            UnitType = "AS",
            TelephoneNumber = null,
            MobileNumber = null,
            FaxNumber = null,
            EmailAddress = null,
            InternetAddress = null,
            MailingAddress = null,
            BusinessAddress = null,
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toInsert2, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.PartyConflict.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid2, PartyFieldIncludes.Party | PartyFieldIncludes.Organization, cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        fromDb.ShouldBeEmpty();
    }

    #endregion

    #region Upsert Person

    [Fact]
    public async Task UpsertParty_Person_Inserts_New_Person()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<PersonRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Person_NoPartyIds_Inserts_New_Person()
    {
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);

        var toInsert = new PersonRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<PersonRecord>();
        inserted.PartyUuid.ShouldHaveValue();
        inserted.PartyId.ShouldHaveValue();
        inserted.PartyId.Value.ShouldBeGreaterThan(1_000_000_000U);
        inserted.UserIds.CurrentValue.ShouldBe(inserted.PartyId.Value);
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(inserted.PartyId.Value),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(inserted.PartyUuid.Value, PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.User, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(inserted.PartyId.Value),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_Person_Updates_Name_And_Updated_And_PersonProps()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            FirstName = "Test Updated",
            MiddleName = "Mid Updated",
            LastName = "Testson Updated",
            ShortName = "TESTSON Updated Test Mid",
            Address = new StreetAddressRecord
            {
                MunicipalName = "mn",
                MunicipalNumber = "00",
                HouseNumber = "50",
                HouseLetter = "L",
                City = "s",
                PostalCode = "pc",
                StreetName = "sn",
            },
            MailingAddress = new MailingAddressRecord { Address = "mailing", City = "mailing city", PostalCode = "mailing postal code" },
            DateOfBirth = birthDate.AddDays(10),
            DateOfDeath = birthDate.AddDays(30),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<PersonRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_Person_NoPartyIds_Updates_Name_And_Updated_And_PersonProps()
    {
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);

        var toInsert = new PersonRecord
        {
            PartyUuid = FieldValue.Unset,
            PartyId = FieldValue.Unset,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();
        var inserted = result.Value!;
        inserted.PartyUuid.ShouldHaveValue();
        inserted.PartyId.ShouldHaveValue();
        inserted.PartyId.Value.ShouldBeGreaterThan(1_000_000_000U);

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            FirstName = "Test Updated",
            MiddleName = "Mid Updated",
            LastName = "Testson Updated",
            ShortName = "TESTSON Updated Test Mid",
            Address = new StreetAddressRecord
            {
                MunicipalName = "mn",
                MunicipalNumber = "00",
                HouseNumber = "50",
                HouseLetter = "L",
                City = "s",
                PostalCode = "pc",
                StreetName = "sn",
            },
            MailingAddress = new MailingAddressRecord { Address = "mailing", City = "mailing city", PostalCode = "mailing postal code" },
            DateOfBirth = birthDate.AddDays(10),
            DateOfDeath = birthDate.AddDays(30),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
        };

        var updated = result.Value.ShouldBeOfType<PersonRecord>();
        updated.ShouldBeEquivalentTo(expected with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            UserIds = FieldValue.Unset, // user-id is not returned unless updated explicitly
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = updated.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(inserted.PartyUuid.Value, PartyFieldIncludes.Party | PartyFieldIncludes.Person | PartyFieldIncludes.User, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            PartyId = inserted.PartyId,
            PartyUuid = inserted.PartyUuid,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(inserted.PartyId.Value),
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = updated.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_Person_Cannot_Update_Id()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            PartyId = id + 1,
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Person_Cannot_Update_PersonIdentifier()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            PersonIdentifier = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken),
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_Person_CannotInsert_WithSamePartyId()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var uuid2 = Guid.NewGuid();
        var toInsert2 = new PersonRecord
        {
            PartyUuid = uuid2,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toInsert2, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.PartyConflict.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid2, PartyFieldIncludes.Party | PartyFieldIncludes.Person, cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        fromDb.ShouldBeEmpty();
    }

    #endregion

    #region Upsert Self-Identified User

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_Inserts_New_SelfIdentifiedUser_NoType()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = FieldValue.Null,
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = FieldValue.Null,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<SelfIdentifiedUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_Inserts_New_SelfIdentifiedUser_Legacy()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<SelfIdentifiedUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_Inserts_New_SelfIdentifiedUser_Edu()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = FieldValue.Null,
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            Email = FieldValue.Null,
            ExtRef = uuid.ToString(),
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<SelfIdentifiedUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_Inserts_New_SelfIdentifiedUser_IdPortenEmail()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create("test@example.com")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
            Email = "test-si-user@example.com",
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<SelfIdentifiedUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Theory]
    [EnumMembersData<SelfIdentifiedUserType>]
    public async Task UpsertParty_SelfIdentified_Can_Update_SelfIdentifiedType_FromNull(SelfIdentifiedUserType type)
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var userIds = await UoW.GetNewUserIds(1, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = FieldValue.Null,
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(userIds.Single()),
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = FieldValue.Null,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = type switch
        {
            SelfIdentifiedUserType.Legacy => toInsert with
            {
                SelfIdentifiedUserType = type,
                Email = FieldValue.Null,
                ExtRef = FieldValue.Null,
                ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            },
            SelfIdentifiedUserType.Educational => toInsert with
            {
                SelfIdentifiedUserType = type,
                Email = FieldValue.Null,
                ExtRef = uuid.ToString(),
                ExternalUrn = FieldValue.Null,
            },
            SelfIdentifiedUserType.IdPortenEmail => toInsert with
            {
                SelfIdentifiedUserType = type,
                Email = "test-si-user@example.com",
                ExtRef = FieldValue.Null,
                ExternalUrn = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create("test-si-user@example.com")),
            },
            _ => throw new UnreachableException($"Invalid {nameof(SelfIdentifiedUserType)}: {type}"),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty, // populated by the database on upsert
        };

        var updated = result.Value.ShouldBeOfType<SelfIdentifiedUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser | PartyFieldIncludes.User, cancellationToken: CancellationToken)
            .SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentified_Cannot_Update_SelfIdentifiedType_FromNonNull()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            SelfIdentifiedUserType = SelfIdentifiedUserType.Educational,
            ExternalUrn = FieldValue.Null,
            ExtRef = uuid.ToString(),
        };

        await NewTransaction();
        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);

        await NewTransaction(commit: false);
        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_Updates_Name_And_Updated()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<SelfIdentifiedUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpsertParty_SelfIdentifiedUser_Can_Retain_IsDeleted(bool isDeleted)
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? TimeProvider.GetUtcNow() : FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            IsDeleted = toInsert.IsDeleted, // IsDeleted should not change
            DeletedAt = toInsert.DeletedAt, // DeletedAt should not change
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<SelfIdentifiedUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_SelfIdentifiedUser_IsDeleted_DefaultsFalse()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var uuid = Guid.NewGuid();

        var toInsert = new SelfIdentifiedUserRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("test_si_user")),
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = FieldValue.Unset,
            DeletedAt = FieldValue.Unset,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            SelfIdentifiedUserType = SelfIdentifiedUserType.Legacy,
            Email = FieldValue.Null,
            ExtRef = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toInsert with
        {
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<SelfIdentifiedUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    #endregion

    #region Upsert System User

    [Theory]
    [InlineData(SystemUserRecordType.Standard)]
    [InlineData(SystemUserRecordType.Agent)]
    public async Task UpsertParty_SystemUser_Inserts_New_SystemUser(SystemUserRecordType type)
    {
        var org = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SystemUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(uuid),
            DisplayName = "Test System User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org.PartyUuid,
            SystemUserType = type,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<SystemUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SystemUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_SystemUser_Updates_Name_And_Updated()
    {
        var org = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SystemUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(uuid),
            DisplayName = "Test System User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org.PartyUuid,
            SystemUserType = SystemUserRecordType.Standard,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<SystemUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SystemUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_SystemUser_CanKeepOwner()
    {
        var org = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SystemUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(uuid),
            DisplayName = "Test System User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org.PartyUuid,
            SystemUserType = SystemUserRecordType.Standard,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            OwnerUuid = FieldValue.Unset,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            OwnerUuid = toInsert.OwnerUuid, // owner should not change
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<SystemUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party | PartyFieldIncludes.SystemUser, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    [Fact]
    public async Task UpsertParty_SystemUser_Cannot_Update_Owner()
    {
        var org1 = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var org2 = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new SystemUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(uuid),
            DisplayName = "Test System User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org1.PartyUuid,
            SystemUserType = SystemUserRecordType.Agent,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            OwnerUuid = org2.PartyUuid,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        var error = result.ShouldBeProblem(Problems.InvalidPartyUpdate.ErrorCode);
        error.Extensions["column"].Single().ShouldBe("owner");
    }

    #endregion

    #region Upsert Enterprise User

    [Fact]
    public async Task UpsertParty_EnterpriseUser_Inserts_New_EnterpriseUser()
    {
        var org = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new EnterpriseUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = FieldValue.Null,
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org.PartyUuid,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        var inserted = result.ShouldHaveValue().ShouldBeOfType<EnterpriseUserRecord>();
        inserted.ShouldBeEquivalentTo(toInsert with
        {
            Usernames = PartyHistoricalAggregate<string>.Empty,
            VersionId = inserted.VersionId,
        });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(toInsert with { VersionId = fromDb.VersionId });
    }

    [Fact]
    public async Task UpsertParty_EnterpriseUser_Updates_Name_And_Updated()
    {
        var org = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new EnterpriseUserRecord
        {
            PartyUuid = uuid,
            PartyId = FieldValue.Null,
            ExternalUrn = FieldValue.Null,
            DisplayName = "Test SI User",
            PersonIdentifier = null,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = org.PartyUuid,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        TimeProvider.Advance(TimeSpan.FromDays(30));

        var toUpdate = toInsert with
        {
            DisplayName = "Test Updated",
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        var expected = toUpdate with
        {
            CreatedAt = toInsert.CreatedAt, // created at should not change
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        var updated = result.Value.ShouldBeOfType<EnterpriseUserRecord>();
        updated.ShouldBeEquivalentTo(expected with { VersionId = updated.VersionId });

        var fromDb = await Persistence.GetPartyById(uuid, PartyFieldIncludes.Party, cancellationToken: CancellationToken).SingleAsync(CancellationToken);
        fromDb.ShouldBeEquivalentTo(expected with
        {
            Usernames = FieldValue.Unset,
            VersionId = fromDb.VersionId,
        });
    }

    #endregion

    #region Upsert Role-Assigments

    [Fact]
    public async Task UpsertExternalRolesFromPartyBySource()
    {
        var added = ExternalRoleAssignmentEvent.EventType.Added;
        var removed = ExternalRoleAssignmentEvent.EventType.Removed;
        var source = ExternalRoleSource.CentralCoordinatingRegister;

        await UoW.CreateFakeRoleDefinitions(CancellationToken);

        var party1 = (await UoW.CreateOrg(uuid: Guid.Parse("00000000-0000-0000-0000-000000000001"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party2 = (await UoW.CreateOrg(uuid: Guid.Parse("00000000-0000-0000-0000-000000000002"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party3 = (await UoW.CreateOrg(uuid: Guid.Parse("00000000-0000-0000-0000-000000000003"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party4 = (await UoW.CreateOrg(uuid: Guid.Parse("00000000-0000-0000-0000-000000000004"), cancellationToken: CancellationToken)).PartyUuid.Value;

        // assign empty to already empty
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [],
            []);

        // assign new roles
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("hovedenhet", party2),
                new("ikke-naeringsdrivende-hovedenhet", party2),
                new("styremedlem", party2),
            ],
            [
                new(added, "hovedenhet", party2),
                new(added, "ikke-naeringsdrivende-hovedenhet", party2),
                new(added, "styremedlem", party2),
            ]);

        // remove 1 role
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("hovedenhet", party2),
                new("styremedlem", party2),
            ],
            [
                new(removed, "ikke-naeringsdrivende-hovedenhet", party2),
            ]);

        // replace 1 role
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("hovedenhet", party2),
                new("ikke-naeringsdrivende-hovedenhet", party2),
            ],
            [
                new(added, "ikke-naeringsdrivende-hovedenhet", party2),
                new(removed, "styremedlem", party2),
            ]);

        // replace all roles
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("styremedlem", party2),
            ],
            [
                new(added, "styremedlem", party2),
                new(removed, "hovedenhet", party2),
                new(removed, "ikke-naeringsdrivende-hovedenhet", party2),
            ]);

        // remove all roles
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [],
            [
                new(removed, "styremedlem", party2),
            ]);

        // add roles from ccr
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ExternalRoleSource.CentralCoordinatingRegister,
            [
                new("fake-01", party2),
                new("fake-02", party2),
                new("fake-03", party2),
                new("fake-04", party2),
            ],
            [
                new(added, "fake-01", party2),
                new(added, "fake-02", party2),
                new(added, "fake-03", party2),
                new(added, "fake-04", party2),
            ]);

        // add roles from npr
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ExternalRoleSource.NationalPopulationRegister,
            [
                new("fake-01", party2),
                new("fake-02", party2),
                new("fake-03", party2),
                new("fake-04", party2),
            ],
            [
                new(added, "fake-01", party2),
                new(added, "fake-02", party2),
                new(added, "fake-03", party2),
                new(added, "fake-04", party2),
            ]);

        // replace roles from ccr
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ExternalRoleSource.CentralCoordinatingRegister,
            [
                new("fake-03", party2),
                new("fake-04", party2),
                new("fake-05", party2),
                new("fake-06", party2),
            ],
            [
                new(removed, "fake-01", party2),
                new(removed, "fake-02", party2),
                new(added, "fake-05", party2),
                new(added, "fake-06", party2),
            ]);

        // replace roles from npr
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ExternalRoleSource.NationalPopulationRegister,
            [
                new("fake-03", party2),
                new("fake-04", party2),
                new("fake-05", party2),
                new("fake-06", party2),
            ],
            [
                new(removed, "fake-01", party2),
                new(removed, "fake-02", party2),
                new(added, "fake-05", party2),
                new(added, "fake-06", party2),
            ]);

        // add same role to multiple parties
        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ExternalRoleSource.CentralCoordinatingRegister,
            [
                new("fake-01", party3),
                new("fake-01", party4),
                new("fake-02", party3),
                new("fake-02", party4),
            ],
            [
                new(added, "fake-01", party3),
                new(added, "fake-01", party4),
                new(added, "fake-02", party3),
                new(added, "fake-02", party4),
                new(removed, "fake-03", party2),
                new(removed, "fake-04", party2),
                new(removed, "fake-05", party2),
                new(removed, "fake-06", party2),
            ]);

        // check final roles
        var roles = await Persistence.GetExternalRoleAssignmentsFromParty(party1, cancellationToken: CancellationToken).ToListAsync(CancellationToken);
        roles.Count.ShouldBe(8);

        roles.Where(Matches(ExternalRoleSource.NationalPopulationRegister, "fake-03", party2)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.NationalPopulationRegister, "fake-04", party2)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.NationalPopulationRegister, "fake-05", party2)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.NationalPopulationRegister, "fake-06", party2)).ShouldHaveSingleItem();

        roles.Where(Matches(ExternalRoleSource.CentralCoordinatingRegister, "fake-01", party3)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.CentralCoordinatingRegister, "fake-01", party4)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.CentralCoordinatingRegister, "fake-02", party3)).ShouldHaveSingleItem();
        roles.Where(Matches(ExternalRoleSource.CentralCoordinatingRegister, "fake-02", party4)).ShouldHaveSingleItem();

        static Func<PartyExternalRoleAssignmentRecord, bool> Matches(ExternalRoleSource source, string identifier, Guid toParty)
            => r => r.Source == source && r.Identifier == identifier && r.ToParty == toParty;
    }

    [Fact]
    public async Task UpsertExternalRolesFromPartyBySource_Patch_CanRemoveAndAddSameRoleInSameUpdate()
    {
        var added = ExternalRoleAssignmentEvent.EventType.Added;
        var removed = ExternalRoleAssignmentEvent.EventType.Removed;
        var source = ExternalRoleSource.CentralCoordinatingRegister;

        await UoW.CreateFakeRoleDefinitions(CancellationToken);

        var party1 = (await UoW.CreateOrg(uuid: Guid.Parse("10000000-0000-0000-0000-000000000001"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party2 = (await UoW.CreateOrg(uuid: Guid.Parse("10000000-0000-0000-0000-000000000002"), cancellationToken: CancellationToken)).PartyUuid.Value;

        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [new("fake-01", party2)],
            [new(added, "fake-01", party2)]);

        await CheckUpsertExternalRolesFromPartyBySourcePatch(
            party1,
            source,
            new PartyExternalRoleAssignmentsUpdate.Patch
            {
                AbsentByIdentifier = [],
                Absent = [Assignment("fake-01", party2)],
                Present = [Assignment("fake-01", party2)],
            },
            expectedEvents:
            [
                new(removed, "fake-01", party2),
                new(added, "fake-01", party2),
            ],
            expectedAssignments:
            [
                new("fake-01", party2),
            ]);
    }

    [Fact]
    public async Task UpsertExternalRolesFromPartyBySource_Patch_AbsentByIdentifier_RemovesMatchingSourceAndIdentifier()
    {
        var added = ExternalRoleAssignmentEvent.EventType.Added;
        var removed = ExternalRoleAssignmentEvent.EventType.Removed;
        var ccr = ExternalRoleSource.CentralCoordinatingRegister;
        var npr = ExternalRoleSource.NationalPopulationRegister;

        await UoW.CreateFakeRoleDefinitions(CancellationToken);

        var party1 = (await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-0000-0000-000000000001"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party2 = (await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-0000-0000-000000000002"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party3 = (await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-0000-0000-000000000003"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party4 = (await UoW.CreateOrg(uuid: Guid.Parse("20000000-0000-0000-0000-000000000004"), cancellationToken: CancellationToken)).PartyUuid.Value;

        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            ccr,
            [
                new("fake-01", party2),
                new("fake-01", party3),
                new("fake-02", party2),
            ],
            [
                new(added, "fake-01", party2),
                new(added, "fake-01", party3),
                new(added, "fake-02", party2),
            ]);

        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            npr,
            [
                new("fake-01", party4),
            ],
            [
                new(added, "fake-01", party4),
            ]);

        await CheckUpsertExternalRolesFromPartyBySourcePatch(
            party1,
            ccr,
            new PartyExternalRoleAssignmentsUpdate.Patch
            {
                AbsentByIdentifier = ["fake-01"],
                Absent = [],
                Present = [],
            },
            expectedEvents:
            [
                new(removed, "fake-01", party2),
                new(removed, "fake-01", party3),
            ],
            expectedAssignments:
            [
                new("fake-02", party2),
            ]);

        var nprRoles = await Persistence
            .GetExternalRoleAssignmentsFromParty(party1, cancellationToken: CancellationToken)
            .Where(r => r.Source == npr)
            .ToListAsync(CancellationToken);

        nprRoles.Count.ShouldBe(1);
        nprRoles.ShouldContain(r => r.Identifier == "fake-01" && r.ToParty == party4);
    }

    [Fact]
    public async Task UpsertExternalRolesFromPartyBySource_Patch_RemovingMissingRolesIsSafe()
    {
        var added = ExternalRoleAssignmentEvent.EventType.Added;
        var source = ExternalRoleSource.CentralCoordinatingRegister;

        await UoW.CreateFakeRoleDefinitions(CancellationToken);

        var party1 = (await UoW.CreateOrg(uuid: Guid.Parse("30000000-0000-0000-0000-000000000001"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party2 = (await UoW.CreateOrg(uuid: Guid.Parse("30000000-0000-0000-0000-000000000002"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party3 = (await UoW.CreateOrg(uuid: Guid.Parse("30000000-0000-0000-0000-000000000003"), cancellationToken: CancellationToken)).PartyUuid.Value;

        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("fake-01", party2),
            ],
            [
                new(added, "fake-01", party2),
            ]);

        await CheckUpsertExternalRolesFromPartyBySourcePatch(
            party1,
            source,
            new PartyExternalRoleAssignmentsUpdate.Patch
            {
                AbsentByIdentifier = ["missing-role"],
                Absent =
                [
                    Assignment("fake-01", party3),
                    Assignment("also-missing", party2),
                ],
                Present = [],
            },
            expectedEvents: [],
            expectedAssignments:
            [
                new("fake-01", party2),
            ]);
    }

    [Fact]
    public async Task UpsertExternalRolesFromPartyBySource_Patch_AddingExistingRoleDoesNothing()
    {
        var added = ExternalRoleAssignmentEvent.EventType.Added;
        var source = ExternalRoleSource.CentralCoordinatingRegister;

        await UoW.CreateFakeRoleDefinitions(CancellationToken);

        var party1 = (await UoW.CreateOrg(uuid: Guid.Parse("40000000-0000-0000-0000-000000000001"), cancellationToken: CancellationToken)).PartyUuid.Value;
        var party2 = (await UoW.CreateOrg(uuid: Guid.Parse("40000000-0000-0000-0000-000000000002"), cancellationToken: CancellationToken)).PartyUuid.Value;

        await CheckUpsertExternalRolesFromPartyBySource(
            party1,
            source,
            [
                new("fake-01", party2),
            ],
            [
                new(added, "fake-01", party2),
            ]);

        await CheckUpsertExternalRolesFromPartyBySourcePatch(
            party1,
            source,
            new PartyExternalRoleAssignmentsUpdate.Patch
            {
                AbsentByIdentifier = [],
                Absent = [],
                Present = [Assignment("fake-01", party2)],
            },
            expectedEvents: [],
            expectedAssignments:
            [
                new("fake-01", party2),
            ]);
    }

    private async Task CheckUpsertExternalRolesFromPartyBySource(
        Guid fromParty,
        ExternalRoleSource source,
        IReadOnlyList<KeyValuePair<string, Guid>> assignments,
        IReadOnlyList<CheckUpsertExternalRolesFromPartyBySourceExpectedEvent> expectedEvents)
    {
        var cmdId = Guid.CreateVersion7();
        var firstTryEvents = await Persistence
            .UpsertExternalRolesFromPartyBySource(
                cmdId,
                fromParty,
                source,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull(assignments),
                cancellationToken: CancellationToken)
            .Select(static e => new CheckUpsertExternalRolesFromPartyBySourceExpectedEvent(e.Type, e.RoleIdentifier, e.ToParty))
            .ToListAsync(CancellationToken);

        firstTryEvents.Count.ShouldBe(expectedEvents.Count);
        foreach (var expected in expectedEvents)
        {
            firstTryEvents.ShouldContain(expected);
        }

        // idempotency check
        var secondTryEvents = await Persistence
            .UpsertExternalRolesFromPartyBySource(
                cmdId,
                fromParty,
                source,
                update: PartyExternalRoleAssignmentsUpdate.CreateFull(assignments),
                cancellationToken: CancellationToken)
            .Select(static e => new CheckUpsertExternalRolesFromPartyBySourceExpectedEvent(e.Type, e.RoleIdentifier, e.ToParty))
            .ToListAsync(CancellationToken);

        secondTryEvents.Count.ShouldBe(expectedEvents.Count);
        foreach (var expected in expectedEvents)
        {
            secondTryEvents.ShouldContain(expected);
        }

        // check that the roles are actually assigned
        var roles = await Persistence.GetExternalRoleAssignmentsFromParty(fromParty, cancellationToken: CancellationToken).Where(r => r.Source == source).ToListAsync(CancellationToken);
        roles.Count.ShouldBe(assignments.Count);
        foreach (var assignment in assignments)
        {
            roles.Where(r => r.Identifier == assignment.Key && r.ToParty == assignment.Value).ShouldHaveSingleItem();
        }
    }

    private async Task CheckUpsertExternalRolesFromPartyBySourcePatch(
        Guid fromParty,
        ExternalRoleSource source,
        PartyExternalRoleAssignmentsUpdate.Patch patch,
        IReadOnlyList<CheckUpsertExternalRolesFromPartyBySourceExpectedEvent> expectedEvents,
        IReadOnlyList<KeyValuePair<string, Guid>> expectedAssignments)
    {
        var cmdId = Guid.CreateVersion7();
        var firstTryEvents = await Persistence
            .UpsertExternalRolesFromPartyBySource(
                cmdId,
                fromParty,
                source,
                update: patch,
                cancellationToken: CancellationToken)
            .Select(static e => new CheckUpsertExternalRolesFromPartyBySourceExpectedEvent(e.Type, e.RoleIdentifier, e.ToParty))
            .ToListAsync(CancellationToken);

        firstTryEvents.Count.ShouldBe(expectedEvents.Count);
        foreach (var expected in expectedEvents)
        {
            firstTryEvents.ShouldContain(expected);
        }

        var secondTryEvents = await Persistence
            .UpsertExternalRolesFromPartyBySource(
                cmdId,
                fromParty,
                source,
                update: patch,
                cancellationToken: CancellationToken)
            .Select(static e => new CheckUpsertExternalRolesFromPartyBySourceExpectedEvent(e.Type, e.RoleIdentifier, e.ToParty))
            .ToListAsync(CancellationToken);

        secondTryEvents.Count.ShouldBe(expectedEvents.Count);
        foreach (var expected in expectedEvents)
        {
            secondTryEvents.ShouldContain(expected);
        }

        var roles = await Persistence
            .GetExternalRoleAssignmentsFromParty(fromParty, cancellationToken: CancellationToken)
            .Where(r => r.Source == source)
            .ToListAsync(CancellationToken);

        roles.Count.ShouldBe(expectedAssignments.Count);
        foreach (var assignment in expectedAssignments)
        {
            roles.Where(r => r.Identifier == assignment.Key && r.ToParty == assignment.Value).ShouldHaveSingleItem();
        }
    }

    private static PartyExternalRoleAssignment Assignment(string identifier, Guid toParty)
        => new()
        {
            ExternalRoleIdentifier = identifier,
            ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = toParty },
        };

    private record CheckUpsertExternalRolesFromPartyBySourceExpectedEvent(ExternalRoleAssignmentEvent.EventType Type, string Identifier, Guid ToParty);

    #endregion

    #region Sequence Transaction handling

    [Fact]
    public async Task Sequence_Transaction_Handling()
    {
        var dataSource = GetRequiredService<NpgsqlDataSource>();
        await using var noTxConn = await dataSource.OpenConnectionAsync(CancellationToken);
        await using var maxSafeCmd = noTxConn.CreateCommand();
        maxSafeCmd.CommandText = /*strpsql*/"SELECT register.tx_max_safeval('register.party_version_id_seq')";
        await maxSafeCmd.PrepareAsync(CancellationToken);

        await using var uow1 = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(activityName: "uow1", cancellationToken: CancellationToken);
        await using var uow2 = await GetRequiredService<IUnitOfWorkManager>().CreateAsync(activityName: "uow2", cancellationToken: CancellationToken);

        var tx1Conn = uow1.GetRequiredService<NpgsqlConnection>();
        var tx2Conn = uow2.GetRequiredService<NpgsqlConnection>();

        Assert.Equal(9223372036854775807UL, await GetVisible());

        var val1 = await NextVal(tx1Conn);
        var val2 = await NextVal(tx2Conn);
        val1.ShouldBeLessThan(val2);

        (await GetVisible()).ShouldBe(val1 - 1);

        await uow2.CommitAsync(CancellationToken);
        (await GetVisible()).ShouldBe(val1 - 1);

        await uow1.CommitAsync(CancellationToken);
        Assert.Equal(9223372036854775807UL, await GetVisible());

        async Task<ulong> GetVisible()
        {
            await using var reader = await maxSafeCmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));

            var result = await reader.GetFieldValueAsync<long>(0, CancellationToken);
            return (ulong)result;
        }

        async Task<ulong> NextVal(NpgsqlConnection conn)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = /*strpsql*/"SELECT register.tx_nextval('register.party_version_id_seq')";
            await cmd.PrepareAsync(CancellationToken);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow, CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));

            var result = await reader.GetFieldValueAsync<long>(0, CancellationToken);
            return (ulong)result;
        }
    }

    #endregion

    #region UserId handling

    [Fact]
    public async Task Person_CanBeUpdated_WithActiveUserId()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.ShouldBeUnset();
        result.ShouldHaveValue().Usernames.Values.ShouldBe([]);

        await NewTransaction();
        var userIds = await TestDataGenerator.GetNextUserIds(cancellationToken: CancellationToken);
        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create(userIds, hasActiveValue: true),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.Values.ShouldBe([userIds[0]]);
    }

    [Fact]
    public async Task Person_CanBeUpdated_WithActiveUserId_And_Username()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.ShouldBeUnset();
        result.ShouldHaveValue().Usernames.Values.ShouldBe([]);

        await NewTransaction();
        var userIds = await TestDataGenerator.GetNextUserIds(cancellationToken: CancellationToken);
        var username = $"user_{userIds[0]}";
        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create(userIds, hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent(username),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.Values.ShouldBe([userIds[0]]);
        result.ShouldHaveValue().Usernames.Values.ShouldBe([username]);
    }

    [Fact]
    public async Task Person_CanBeUpdated_WithHistoricalUserIds()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.ShouldBeUnset();
        result.ShouldHaveValue().Usernames.Values.ShouldBe([]);

        await NewTransaction();
        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.Values.ShouldBe([10U, 5U, 2U]);
    }

    [Fact]
    public async Task Person_CanBeInserted_WithHistoricalUserIds()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.Values.ShouldBe([10U, 5U, 2U]);
    }

    [Fact]
    public async Task Person_CanBeUpdated_WithoutTouchingUserIds()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await NewTransaction();
        var toUpdate = toInsert with
        {
            UserIds = FieldValue.Unset,
            Usernames = FieldValue.Unset,
            DisplayName = "updated"
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        var person = result.ShouldHaveValue().ShouldBeOfType<PersonRecord>();

        person.UserIds.ShouldBeUnset();

        await NewTransaction();
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = /*strpsql*/"SELECT user_id, is_active FROM register.user WHERE uuid = @partyId ORDER BY user_id DESC";

        cmd.Parameters.Add<Guid>("partyId", NpgsqlTypes.NpgsqlDbType.Uuid).TypedValue = uuid;

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken);

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetInt64("user_id").ShouldBe(10);
        reader.GetBoolean("is_active").ShouldBeTrue();

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetInt64("user_id").ShouldBe(5);
        reader.GetBoolean("is_active").ShouldBeFalse();

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetInt64("user_id").ShouldBe(2);
        reader.GetBoolean("is_active").ShouldBeFalse();

        (await reader.ReadAsync(CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Person_Cannot_UpdateActiveUserId()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = FieldValue.Unset,
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await NewTransaction();

        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create([12U, 10U, 2U, 5U], hasActiveValue: true),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        var problem = result.ShouldBeProblem();
        problem.ErrorCode.ShouldBe(Problems.InvalidPartyUpdate.ErrorCode);
        problem.Extensions["column"].Single().ShouldBe("user_id");
    }

    [Fact]
    public async Task Person_Can_UpdateActiveUsername()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent("user1"),
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await NewTransaction();

        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent("user2"),
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.CurrentValue.ShouldBe(10U);
        result.ShouldHaveValue().Usernames.CurrentValue.ShouldBe("user2");

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = /*strpsql*/"SELECT username, is_active FROM register.username WHERE uuid = @partyId ORDER BY username DESC";

        cmd.Parameters.Add<Guid>("partyId", NpgsqlTypes.NpgsqlDbType.Uuid).TypedValue = uuid;

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken);

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetString("username").ShouldBe("user2");
        reader.GetBoolean("is_active").ShouldBeTrue();

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetString("username").ShouldBe("user1");
        reader.GetBoolean("is_active").ShouldBeFalse();

        (await reader.ReadAsync(CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Person_Can_RemoveActiveUsername()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent("user1"),
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await NewTransaction();

        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.Empty,
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.CurrentValue.ShouldBe(10U);
        result.ShouldHaveValue().Usernames.CurrentValue.ShouldBeNull();

        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = /*strpsql*/"SELECT username, is_active FROM register.username WHERE uuid = @partyId ORDER BY username DESC";

        cmd.Parameters.Add<Guid>("partyId", NpgsqlTypes.NpgsqlDbType.Uuid).TypedValue = uuid;

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken);

        (await reader.ReadAsync(CancellationToken)).ShouldBeTrue();
        reader.GetString("username").ShouldBe("user1");
        reader.GetBoolean("is_active").ShouldBeFalse();

        (await reader.ReadAsync(CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Person_Can_RetainActiveUsername()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var toInsert = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent("user1"),
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var result = await Persistence.UpsertParty(toInsert, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await NewTransaction();

        var toUpdate = toInsert with
        {
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = FieldValue.Unset,
        };

        result = await Persistence.UpsertParty(toUpdate, cancellationToken: CancellationToken);
        result.ShouldHaveValue().UserIds.CurrentValue.ShouldBe(10U);
        result.ShouldHaveValue().Usernames.CurrentValue.ShouldBe("user1");
    }

    [Fact]
    public async Task GetOrCreateSelfIdentifiedEmailUser_Returns_Existing_User()
    {
        const string Email = "existing-email@example.com";
        var existing = await UoW.CreateSelfIdentifiedUser(
            type: SelfIdentifiedUserType.IdPortenEmail,
            email: Email,
            cancellationToken: CancellationToken);

        var result = await Persistence.GetOrCreateSelfIdentifiedEmailUser(Email, cancellationToken: CancellationToken);

        var user = result.ShouldHaveValue();
        user.IsNew.ShouldBeFalse();
        user.Value.PartyUuid.ShouldBe(existing.PartyUuid);
        user.Value.PartyId.ShouldBe(existing.PartyId);
        user.Value.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        user.Value.Email.ShouldBe(Email);
        user.Value.ExtRef.ShouldBeNull();
        user.Value.DisplayName.ShouldBe(existing.DisplayName);
        user.Value.UserIds.CurrentValue.ShouldBe(existing.UserIds.CurrentValue);
        user.Value.Usernames.CurrentValue.ShouldBe(existing.Usernames.CurrentValue);
    }

    [Fact]
    public async Task GetOrCreateSelfIdentifiedEmailUser_Creates_New_User()
    {
        const string Email = "new-email@example.com";

        var result = await Persistence.GetOrCreateSelfIdentifiedEmailUser(Email, cancellationToken: CancellationToken);

        var user = result.ShouldHaveValue();
        user.IsNew.ShouldBeTrue();
        user.Value.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        user.Value.Email.ShouldBe(Email);
        user.Value.ExtRef.ShouldBeNull();
        user.Value.DisplayName.ShouldBe(Email);
        user.Value.UserIds.CurrentValue.ShouldHaveValue();
        user.Value.Usernames.CurrentValue.ShouldBeNull();

        var persisted = await Persistence.LookupParties(
            selfIdentifiedEmails: [Email],
            include: PartyFieldIncludes.Party | PartyFieldIncludes.User | PartyFieldIncludes.SelfIdentifiedUser,
            cancellationToken: CancellationToken)
            .FirstOrDefaultAsync(CancellationToken)
            .AsTask();

        var persistedUser = persisted.ShouldNotBeNull().ShouldBeOfType<SelfIdentifiedUserRecord>();
        persistedUser.PartyUuid.ShouldBe(user.Value.PartyUuid);
        persistedUser.PartyId.ShouldBe(user.Value.PartyId);
        persistedUser.DisplayName.ShouldBe(user.Value.DisplayName);
        persistedUser.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.IdPortenEmail);
        persistedUser.Email.ShouldBe(Email);
        persistedUser.ExtRef.ShouldBeNull();
        persistedUser.UserIds.CurrentValue.ShouldBe(user.Value.UserIds.CurrentValue);
        persistedUser.Usernames.CurrentValue.ShouldBe(user.Value.Usernames.CurrentValue);
    }

    [Fact]
    public async Task GetOrCreateSelfIdentifiedEduUser_Returns_Existing_User()
    {
        const string ExtRef = "existing-edu-ref";
        const string ExistingUsername = "existing-edu-user";
        var existing = await UoW.CreateSelfIdentifiedUser(
            type: SelfIdentifiedUserType.Educational,
            extRef: ExtRef,
            name: ExistingUsername,
            cancellationToken: CancellationToken);

        var result = await Persistence.GetOrCreateSelfIdentifiedEduUser(ExtRef, "ignored-new-username", cancellationToken: CancellationToken);

        var user = result.ShouldHaveValue();
        user.IsNew.ShouldBeFalse();
        user.Value.PartyUuid.ShouldBe(existing.PartyUuid);
        user.Value.PartyId.ShouldBe(existing.PartyId);
        user.Value.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.Educational);
        user.Value.ExtRef.ShouldBe(ExtRef);
        user.Value.Email.ShouldBeNull();
        user.Value.DisplayName.ShouldBe(ExistingUsername);
        user.Value.UserIds.CurrentValue.ShouldBe(existing.UserIds.CurrentValue);
        user.Value.Usernames.CurrentValue.ShouldBe(existing.Usernames.CurrentValue);
    }

    [Fact]
    public async Task GetOrCreateSelfIdentifiedEduUser_Creates_New_User()
    {
        const string ExtRef = "new-edu-ref";
        const string Username = "new-edu-user";

        var result = await Persistence.GetOrCreateSelfIdentifiedEduUser(ExtRef, Username, cancellationToken: CancellationToken);

        var user = result.ShouldHaveValue();
        user.IsNew.ShouldBeTrue();
        user.Value.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.Educational);
        user.Value.ExtRef.ShouldBe(ExtRef);
        user.Value.Email.ShouldBeNull();
        user.Value.DisplayName.ShouldBe(Username);
        user.Value.UserIds.CurrentValue.ShouldHaveValue();
        user.Value.Usernames.CurrentValue.ShouldBeNull();

        var persisted = await Persistence
            .GetPartyById(user.Value.PartyUuid.Value, include: PartyFieldIncludes.Party | PartyFieldIncludes.User | PartyFieldIncludes.SelfIdentifiedUser, cancellationToken: CancellationToken)
            .FirstOrDefaultAsync(CancellationToken)
            .AsTask();

        var persistedUser = persisted.ShouldNotBeNull().ShouldBeOfType<SelfIdentifiedUserRecord>();
        persistedUser.PartyUuid.ShouldBe(user.Value.PartyUuid);
        persistedUser.PartyId.ShouldBe(user.Value.PartyId);
        persistedUser.DisplayName.ShouldBe(user.Value.DisplayName);
        persistedUser.SelfIdentifiedUserType.ShouldBe(SelfIdentifiedUserType.Educational);
        persistedUser.ExtRef.ShouldBe(ExtRef);
        persistedUser.Email.ShouldBeNull();
        persistedUser.UserIds.CurrentValue.ShouldBe(user.Value.UserIds.CurrentValue);
        persistedUser.Usernames.CurrentValue.ShouldBe(user.Value.Usernames.CurrentValue);
    }

    #endregion

    #region AsyncSingleton

    [Fact]
    public async Task AsyncSingleton_YieldSingleParty()
    {
        var id = await UoW.GetNextPartyId(CancellationToken);
        var birthDate = UoW.GetRandomBirthDate();
        var isDNumber = TestDataGenerator.GetRandomBool(0.1); // 10% chance of D-number
        var personId = await UoW.GetNewPersonIdentifier(birthDate, isDNumber, CancellationToken);
        var uuid = Guid.NewGuid();

        var party = new PersonRecord
        {
            PartyUuid = uuid,
            PartyId = id,
            ExternalUrn = PartyExternalRefUrn.PersonId.Create(personId),
            DisplayName = "Test Mid Testson",
            PersonIdentifier = personId,
            OrganizationIdentifier = null,
            CreatedAt = TimeProvider.GetUtcNow(),
            ModifiedAt = TimeProvider.GetUtcNow(),
            IsDeleted = false,
            DeletedAt = FieldValue.Null,
            UserIds = PartyHistoricalAggregate<uint>.Create([10U, 2U, 5U], hasActiveValue: true),
            Usernames = PartyHistoricalAggregate<string>.CreateCurrent("user1"),
            VersionId = FieldValue.Unset,
            OwnerUuid = FieldValue.Null,
            Source = PersonSource.NationalPopulationRegister,
            FirstName = "Test",
            MiddleName = "Mid",
            LastName = "Testson",
            ShortName = "TESTSON Test Mid",
            Address = null,
            MailingAddress = null,
            DateOfBirth = birthDate,
            DateOfDeath = FieldValue.Null,
        };

        var singleton = new PostgreSqlPartyPersistence.UpsertPartyQuery.AsyncSingleton(party);
        var list = await singleton.ToListAsync(CancellationToken);

        list.ShouldHaveSingleItem().ShouldBeSameAs(party);
    }

    #endregion

}
