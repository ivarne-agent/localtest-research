using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.TestUtils.TestData;

/// <summary>
/// Test helpers for <see cref="IPartyPersistence"/>, <see cref="IPartyExternalRolePersistence"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public static class PartyPersistenceExtensions
{
    public static ValueTask<OrganizationIdentifier> GetNewOrgNumber(
        this IUnitOfWork uow,
        CancellationToken cancellationToken = default)
        => uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetNewOrgNumber(cancellationToken);

    public static ValueTask<PersonIdentifier> GetNewPersonIdentifier(
        this IUnitOfWork uow,
        DateOnly birthDate,
        bool isDNumber,
        CancellationToken cancellationToken = default)
        => uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetNewPersonIdentifier(birthDate, isDNumber, cancellationToken);

    public static DateOnly GetRandomBirthDate(this IUnitOfWork uow)
    {
        var min = new DateOnly(1940, 01, 01);
        var maxExl = new DateOnly(2024, 01, 01);
        var value = Random.Shared.Next(min.DayNumber, maxExl.DayNumber);

        return DateOnly.FromDayNumber(value);
    }

    public static ValueTask<uint> GetNextPartyId(
        this IUnitOfWork uow,
        CancellationToken cancellationToken = default)
        => uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetNextPartyId(cancellationToken);

    public static ValueTask<IReadOnlyList<uint>> GetNewUserIds(
        this IUnitOfWork uow,
        int count = 1,
        CancellationToken cancellationToken = default)
        => uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetNextUserIds(count, cancellationToken);

    public static async Task<OrganizationRecord> CreateOrg(
        this IUnitOfWork uow,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<OrganizationIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<string> unitStatus = default,
        FieldValue<string> unitType = default,
        FieldValue<string> telephoneNumber = default,
        FieldValue<string> mobileNumber = default,
        FieldValue<string> faxNumber = default,
        FieldValue<string> emailAddress = default,
        FieldValue<string> internetAddress = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<MailingAddressRecord> businessAddress = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetOrgData(
                uuid,
                id,
                name,
                identifier,
                createdAt,
                modifiedAt,
                isDeleted,
                unitStatus,
                unitType,
                telephoneNumber,
                mobileNumber,
                faxNumber,
                emailAddress,
                internetAddress,
                mailingAddress,
                businessAddress,
                cancellationToken);

        var result = await uow.GetRequiredService<IPartyPersistence>()
            .UpsertParty(toInsert, cancellationToken);

        result.EnsureSuccess();
        return (OrganizationRecord)result.Value;
    }

    public static async Task<ImmutableArray<OrganizationRecord>> CreateOrgs(
        this IUnitOfWork uow,
        int count,
        FieldValue<string> unitType = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetOrgsData(count, unitType, cancellationToken);

        var persistence = uow.GetRequiredService<IPartyPersistence>();
        var builder = ImmutableArray.CreateBuilder<OrganizationRecord>(count);

        await foreach (var result in persistence.UpsertParties(toInsert, cancellationToken))
        {
            result.EnsureSuccess();
            builder.Add((OrganizationRecord)result.Value);
        }

        return builder.DrainToImmutable();
    }

    public static async Task ExecuteNonQueries(
        this IUnitOfWork uow,
        IEnumerable<string> queries,
        CancellationToken cancellationToken = default)
    {
        var connection = uow.GetRequiredService<NpgsqlConnection>();
        await using var batch = connection.CreateBatch();

        foreach (var query in queries)
        {
            batch.CreateBatchCommand(query);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<PersonRecord> CreatePerson(
        this IUnitOfWork uow,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<PersonIdentifier> identifier = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<PersonName> name = default,
        FieldValue<StreetAddressRecord> address = default,
        FieldValue<MailingAddressRecord> mailingAddress = default,
        FieldValue<DateOnly> dateOfBirth = default,
        FieldValue<DateOnly> dateOfDeath = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetPersonData(
                uuid,
                id,
                identifier,
                createdAt,
                modifiedAt,
                name,
                address,
                mailingAddress,
                dateOfBirth,
                dateOfDeath,
                user,
                cancellationToken);

        var result = await uow.GetRequiredService<IPartyPersistence>()
            .UpsertParty(toInsert, cancellationToken);

        result.EnsureSuccess();
        return (PersonRecord)result.Value;
    }

    public static async Task<ImmutableArray<PersonRecord>> CreatePeople(
        this IUnitOfWork uow,
        int count,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetPeopleData(count, cancellationToken);

        var persistence = uow.GetRequiredService<IPartyPersistence>();
        var builder = ImmutableArray.CreateBuilder<PersonRecord>(count);

        await foreach (var result in persistence.UpsertParties(toInsert, cancellationToken))
        {
            result.EnsureSuccess();
            builder.Add((PersonRecord)result.Value);
        }

        return builder.DrainToImmutable();
    }

    public static async Task<SelfIdentifiedUserRecord> CreateSelfIdentifiedUser(
        this IUnitOfWork uow,
        FieldValue<Guid> uuid = default,
        FieldValue<uint> id = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<SelfIdentifiedUserType> type = default,
        FieldValue<string> email = default,
        FieldValue<string> extRef = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetSelfIdentifiedUserData(
                uuid,
                id,
                name,
                createdAt,
                modifiedAt,
                isDeleted,
                type,
                email,
                extRef,
                user,
                cancellationToken);

        var result = await uow.GetRequiredService<IPartyPersistence>()
            .UpsertParty(toInsert, cancellationToken);

        result.EnsureSuccess();
        return (SelfIdentifiedUserRecord)result.Value;
    }

    public static async Task<ImmutableArray<SelfIdentifiedUserRecord>> CreateSelfIdentifiedUsers(
        this IUnitOfWork uow,
        int count,
        FieldValue<uint> idOffset = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetSelfIdentifiedUsersData(count, cancellationToken);

        var persistence = uow.GetRequiredService<IPartyPersistence>();
        var builder = ImmutableArray.CreateBuilder<SelfIdentifiedUserRecord>(count);

        await foreach (var result in persistence.UpsertParties(toInsert, cancellationToken))
        {
            result.EnsureSuccess();
            builder.Add((SelfIdentifiedUserRecord)result.Value);
        }

        return builder.DrainToImmutable();
    }

    public static async Task<EnterpriseUserRecord> CreateEnterpriseUser(
        this IUnitOfWork uow,
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        FieldValue<PartyUserRecord> user = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetEnterpriseUserData(
                owner,
                uuid,
                name,
                createdAt,
                modifiedAt,
                isDeleted,
                user,
                cancellationToken);

        var result = await uow.GetRequiredService<IPartyPersistence>()
            .UpsertParty(toInsert, cancellationToken);

        result.EnsureSuccess();
        return (EnterpriseUserRecord)result.Value;
    }

    public static async Task<ImmutableArray<EnterpriseUserRecord>> CreateSelfIdentifiedUsers(
        this IUnitOfWork uow,
        Guid owner,
        int count,
        FieldValue<uint> idOffset = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetEnterpriseUsersData(owner, count, cancellationToken);

        var persistence = uow.GetRequiredService<IPartyPersistence>();
        var builder = ImmutableArray.CreateBuilder<EnterpriseUserRecord>(count);

        await foreach (var result in persistence.UpsertParties(toInsert, cancellationToken))
        {
            result.EnsureSuccess();
            builder.Add((EnterpriseUserRecord)result.Value);
        }

        return builder.DrainToImmutable();
    }

    public static async Task<SystemUserRecord> CreateSystemUser(
        this IUnitOfWork uow,
        Guid owner,
        FieldValue<Guid> uuid = default,
        FieldValue<string> name = default,
        FieldValue<DateTimeOffset> createdAt = default,
        FieldValue<DateTimeOffset> modifiedAt = default,
        FieldValue<bool> isDeleted = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetSystemUserData(
                owner,
                uuid,
                name,
                createdAt,
                modifiedAt,
                isDeleted,
                cancellationToken);

        var result = await uow.GetRequiredService<IPartyPersistence>()
            .UpsertParty(toInsert, cancellationToken);

        result.EnsureSuccess();
        return (SystemUserRecord)result.Value;
    }

    public static async Task<ImmutableArray<SystemUserRecord>> CreateSystemUsers(
        this IUnitOfWork uow,
        Guid owner,
        int count,
        FieldValue<uint> idOffset = default,
        CancellationToken cancellationToken = default)
    {
        var toInsert = await uow.GetRequiredService<RegisterTestDataGenerator>()
            .GetSystemUsersData(owner, count, cancellationToken);

        var persistence = uow.GetRequiredService<IPartyPersistence>();
        var builder = ImmutableArray.CreateBuilder<SystemUserRecord>(count);

        await foreach (var result in persistence.UpsertParties(toInsert, cancellationToken))
        {
            result.EnsureSuccess();
            builder.Add((SystemUserRecord)result.Value);
        }

        return builder.DrainToImmutable();
    }

    public static async Task AddRole(
        this IUnitOfWork uow,
        ExternalRoleSource roleSource,
        string roleIdentifier,
        Guid from,
        Guid to,
        CancellationToken cancellationToken = default)
    {
        var connection = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            /*strpsql*/"""
            INSERT INTO register.external_role_assignment (source, identifier, from_party, to_party)
            VALUES (@source, @identifier, @from, @to)
            """;

        cmd.Parameters.Add<ExternalRoleSource>("source").TypedValue = roleSource;
        cmd.Parameters.Add<string>("identifier", NpgsqlDbType.Text).TypedValue = roleIdentifier;
        cmd.Parameters.Add<Guid>("from", NpgsqlDbType.Uuid).TypedValue = from;
        cmd.Parameters.Add<Guid>("to", NpgsqlDbType.Uuid).TypedValue = to;

        await cmd.PrepareAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<ImmutableDictionary<ExternalRoleSource, ImmutableArray<ExternalRoleDefinition>>> CreateFakeRoleDefinitions(
        this IUnitOfWork uow,
        CancellationToken cancellationToken = default)
    {
        var builder = ImmutableDictionary.CreateBuilder<ExternalRoleSource, ImmutableArray<ExternalRoleDefinition>>();
        builder.Add(ExternalRoleSource.CentralCoordinatingRegister, await uow.CreateFakeRoleDefinitions(ExternalRoleSource.CentralCoordinatingRegister, cancellationToken));
        builder.Add(ExternalRoleSource.NationalPopulationRegister, await uow.CreateFakeRoleDefinitions(ExternalRoleSource.NationalPopulationRegister, cancellationToken));
        builder.Add(ExternalRoleSource.CivilRightsAuthority, await uow.CreateFakeRoleDefinitions(ExternalRoleSource.CivilRightsAuthority, cancellationToken));

        return builder.ToImmutable();
    }

    public static async Task<ImmutableArray<ExternalRoleDefinition>> CreateFakeRoleDefinitions(
        this IUnitOfWork uow,
        ExternalRoleSource source,
        CancellationToken cancellationToken)
    {
        const int COUNT = 40;

        var builder = ImmutableArray.CreateBuilder<ExternalRoleDefinition>(COUNT);
        for (var i = 0; i < 40; i++)
        {
            builder.Add(await uow.CreateFakeRoleDefinition(source, $"fake-{i:D2}", cancellationToken));
        }

        return builder.MoveToImmutable();
    }

    public static async Task<ExternalRoleDefinition> CreateFakeRoleDefinition(
        this IUnitOfWork uow,
        ExternalRoleSource source,
        string identifier,
        CancellationToken cancellationToken = default)
    {
        const string QUERY =
            /*strpsql*/"""
            INSERT INTO register.external_role_definition (source, identifier, name, description)
            VALUES (@source, @identifier, @name, @name)
            RETURNING *, false as is_main_unit
            """;

        var conn = uow.GetRequiredService<NpgsqlConnection>();
        var name = new Dictionary<string, string>
        {
            ["en"] = $"Fake role {identifier}",
            ["nb"] = $"Falsk rolle {identifier}",
            ["nn"] = $"Falsk rolle {identifier}",
        };

        var cmd = conn.CreateCommand();
        cmd.CommandText = QUERY;

        cmd.Parameters.Add<ExternalRoleSource>("source").TypedValue = source;
        cmd.Parameters.Add<string>("identifier", NpgsqlDbType.Text).TypedValue = identifier;
        cmd.Parameters.Add<Dictionary<string, string>>("name", NpgsqlDbType.Hstore).TypedValue = name;

        await cmd.PrepareAsync(cancellationToken);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var def = await PostgreSqlExternalRoleDefinitionPersistence.Cache.ReadExternalRoleDefinitions(reader, cancellationToken).FirstOrDefaultAsync(cancellationToken);

        if (def is null)
        {
            throw new InvalidOperationException("Failed to create fake role definition");
        }

        return def;
    }

    private static void IncrementUserIds(ImmutableArray<uint>.Builder builder, uint offset)
    {
        for (int i = 0, l = builder.Count; i < l; i++)
        {
            builder[i] += offset;
        }
    }
}
