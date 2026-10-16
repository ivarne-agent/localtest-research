using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Authorization.ServiceDefaults.Npgsql.TestSeed;
using Altinn.Authorization.ServiceDefaults.Npgsql.Yuniql;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Core.Utils;
using Altinn.Register.Persistence;
using Altinn.Register.Persistence.DbArgTypes;
using Altinn.Register.Persistence.ImportJobs;
using Altinn.Register.Persistence.Leases;
using Altinn.Register.Persistence.Location;
using Altinn.Register.Persistence.RateLimiting;
using Altinn.Register.Persistence.UnitOfWork;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Npgsql;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class RegisterPersistenceExtensions
{
    /// <summary>
    /// Adds persistence for the register application.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddRegisterPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddPartyPersistence();
        builder.AddPostgresLeaseProvider();
        builder.AddPostgresRateLimiter();
        builder.AddPostgresImportTracker();

        return builder;
    }

    /// <summary>
    /// Adds a job condition that checks if the PostgreSQL database is smaller than the specified size.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="maxSize">The max size of the database.</param>
    /// <param name="jobConditionName">The name of the job condition.</param>
    /// <param name="jobTags">Tags used to target specific jobs the condition applies to.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddDatabaseSizeJobCondition(
        this IHostApplicationBuilder builder,
        ByteSize maxSize,
        string? jobConditionName = null,
        IEnumerable<string>? jobTags = null)
    {
        AddDatabase(builder);
        builder.Services.TryAddSingleton<PostgreSqlSizeService>();

        jobConditionName ??= $"PostgreSQL database smaller than {maxSize}";
        jobTags ??= ImmutableArray<string>.Empty;

        builder.Services.AddJobCondition(
            jobConditionName,
            jobTags,
            (PostgreSqlSizeService service, CancellationToken ct) => service.IsDatabaseSmallerThan(maxSize, ct));

        return builder;
    }

    /// <summary>
    /// Adds persistence for the party component.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddPartyPersistence(this IHostApplicationBuilder builder)
    {
        AddDatabase(builder);

        builder.Services.AddUnitOfWorkParticipant<NpgsqlUnitOfWorkParticipant.Factory>();
        builder.Services.AddUnitOfWorkService<PostgreSqlPartyPersistence>();
        builder.Services.AddUnitOfWorkService<IPartyPersistence>(static s => s.GetRequiredService<PostgreSqlPartyPersistence>());
        builder.Services.AddUnitOfWorkService<IPartyExternalRolePersistence>(static s => s.GetRequiredService<PostgreSqlPartyPersistence>());

        builder.Services.AddUnitOfWorkService<IImportJobStatePersistence, PostgresImportJobStatePersistence>();

        builder.Services.AddUnitOfWorkService<PostgresSagaStatePersistence>();
        builder.Services.AddUnitOfWorkService<ISagaStatePersistence>(static s => s.GetRequiredService<PostgresSagaStatePersistence>());
        builder.Services.AddUnitOfWorkService<ISagaStateCleanup>(static s => s.GetRequiredService<PostgresSagaStatePersistence>());

        // Not part of unit of work
        builder.Services.AddSingleton<PostgreSqlExternalRoleDefinitionPersistence.Cache>();
        builder.Services.AddScoped<PostgreSqlExternalRoleDefinitionPersistence>();
        builder.Services.AddScoped<IExternalRoleDefinitionPersistence>(static s => s.GetRequiredService<PostgreSqlExternalRoleDefinitionPersistence>());
        builder.Services.AddSingleton<PostgreSqlLocationLookupProvider.Cache>();
        builder.Services.AddScoped<PostgreSqlLocationLookupProvider>();
        builder.Services.AddScoped<ILocationLookupProvider>(static s => s.GetRequiredService<PostgreSqlLocationLookupProvider>());
        builder.Services.AddSingleton<IPartyPersistenceCleanupService, PartyPostgreSqlPersistenceCleanupService>();

        return builder;
    }

    /// <summary>
    /// Adds a postgresql backed lease provider.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddPostgresLeaseProvider(this IHostApplicationBuilder builder)
    {
        AddDatabase(builder);

        builder.Services.TryAddSingleton<PostgresqlLeaseProvider>();
        builder.Services.TryAddSingleton<ILeaseProvider>(s => s.GetRequiredService<PostgresqlLeaseProvider>());

        return builder;
    }

    /// <summary>
    /// Adds a postgresql backed rate limiter.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddPostgresRateLimiter(this IHostApplicationBuilder builder)
    {
        AddDatabase(builder);

        builder.Services.TryAddSingleton<PostgresRateLimitProvider>();
        builder.Services.TryAddSingleton<IRateLimitProvider>(s => s.GetRequiredService<PostgresRateLimitProvider>());

        return builder;
    }

    /// <summary>
    /// Adds a postgresql backed import tracker.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <returns><paramref name="builder"/>.</returns>
    public static IHostApplicationBuilder AddPostgresImportTracker(this IHostApplicationBuilder builder)
    {
        AddDatabase(builder);

        builder.Services.TryAddSingleton<PostgresImportJobTracker>();
        builder.Services.TryAddSingleton<IImportJobTracker>(s => s.GetRequiredService<PostgresImportJobTracker>());

        return builder;
    }

    private static void AddDatabase(IHostApplicationBuilder builder)
    {
        if (builder.Services.Contains(Marker.ServiceDescriptor))
        {
            // already added
            return;
        }

        builder.Services.Add(Marker.ServiceDescriptor);

        var descriptor = builder.GetAltinnServiceDescriptor();
        var yuniqlSchema = builder.Configuration.GetValue($"Altinn:Npgsql:{descriptor.Name}:Yuniql:MigrationsTable:Schema", defaultValue: "yuniql");
        var yuniqlTable = builder.Configuration.GetValue($"Altinn:Npgsql:{descriptor.Name}:Yuniql:MigrationsTable:Table", defaultValue: "register_migrations");
        var migrationsFs = new ManifestEmbeddedFileProvider(typeof(RegisterPersistenceExtensions).Assembly, "Migration");
        var seedDataFs = new ManifestEmbeddedFileProvider(typeof(RegisterPersistenceExtensions).Assembly, "TestData");
        builder.AddAltinnPostgresDataSource()
            .MapRegisterTypes()
            .SeedFromFileProvider(seedDataFs)
            .AddYuniqlMigrations(typeof(RegisterPersistenceExtensions), y =>
            {
                y.WorkspaceFileProvider = migrationsFs;
                y.MigrationsTable.Schema = yuniqlSchema;
                y.MigrationsTable.Name = yuniqlTable;
            });

        builder.Services.AddSingleton<PostgreSqlVacuumService>();
    }

    private static INpgsqlDatabaseBuilder MapRegisterTypes(this INpgsqlDatabaseBuilder builder)
    {
        builder.MapEnum<PartyRecordType>("register.party_type", new EnumNameTranslator<PartyRecordType>(static value => value switch
        {
            PartyRecordType.Organization => "organization",
            PartyRecordType.Person => "person",
            PartyRecordType.SelfIdentifiedUser => "self-identified-user",
            PartyRecordType.SystemUser => "system-user",
            PartyRecordType.EnterpriseUser => "enterprise-user",
            _ => null,
        }));

        builder.MapEnum<PartySource>("register.party_source", new EnumNameTranslator<PartySource>(static value => value switch
        {
            PartySource.CentralCoordinatingRegister => "ccr",
            PartySource.NationalPopulationRegister => "npr",
            PartySource.RegisteredWithSkatteetaten => "sire",
            _ => null,
        }));

        builder.MapEnum<OrganizationSource>("register.organization_source", new EnumNameTranslator<OrganizationSource>(static value => value switch
        {
            OrganizationSource.CentralCoordinatingRegister => "ccr",
            OrganizationSource.RegisteredWithSkatteetaten => "sire",
            _ => null,
        }));

        builder.MapEnum<PersonSource>("register.person_source", new EnumNameTranslator<PersonSource>(static value => value switch
        {
            PersonSource.NationalPopulationRegister => "npr",
            _ => null,
        }));

        builder.MapEnum<ExternalRoleSource>("register.external_role_source", new EnumNameTranslator<ExternalRoleSource>(static value => value switch
        {
            ExternalRoleSource.CentralCoordinatingRegister => "ccr",
            ExternalRoleSource.NationalPopulationRegister => "npr",
            ExternalRoleSource.EmployersEmployeeRegister => "aar",
            ExternalRoleSource.CivilRightsAuthority => "cra",
            _ => null,
        }));

        builder.MapEnum<ExternalRoleAssignmentEvent.EventType>("register.external_role_assignment_event_type", new EnumNameTranslator<ExternalRoleAssignmentEvent.EventType>(static value => value switch
        {
            ExternalRoleAssignmentEvent.EventType.Added => "added",
            ExternalRoleAssignmentEvent.EventType.Removed => "removed",
            _ => null,
        }));

        builder.MapEnum<SystemUserRecordType>("register.system_user_type", new EnumNameTranslator<SystemUserRecordType>(static value => value switch
        {
            SystemUserRecordType.Standard => "standard",
            SystemUserRecordType.Agent => "agent",
            _ => null,
        }));

        builder.MapEnum<SagaStatus>("register.saga_status", new EnumNameTranslator<SagaStatus>(static value => value switch
        {
            SagaStatus.InProgress => "in_progress",
            SagaStatus.Completed => "completed",
            SagaStatus.Faulted => "faulted",
            _ => null,
        }));

        builder.MapEnum<SelfIdentifiedUserType>("register.self_identified_user_type", new EnumNameTranslator<SelfIdentifiedUserType>(static value => value switch
        {
            SelfIdentifiedUserType.Legacy => "legacy",
            SelfIdentifiedUserType.Educational => "edu",
            SelfIdentifiedUserType.IdPortenEmail => "idporten-email",
            _ => null,
        }));

        builder.MapEnum<MunicipalityStatus>("register.municipality_status", new EnumNameTranslator<MunicipalityStatus>(static value => value switch
        {
            MunicipalityStatus.Inactive => "inactive",
            MunicipalityStatus.Active => "active",
            _ => null,
        }));

        builder.MapEnum<BlockedRequestBehavior>("register.blocked_request_behavior", new EnumNameTranslator<BlockedRequestBehavior>(static value => value switch
        {
            BlockedRequestBehavior.Ignore => "ignore",
            BlockedRequestBehavior.Renew => "renew",
            _ => null,
        }));

        builder.MapEnum<PersistenceFeatureFlag>("register.db_feature_flag", new EnumNameTranslator<PersistenceFeatureFlag>(static value => value switch
        {
            PersistenceFeatureFlag.CreatePartyId => "create_party_id",
            _ => null,
        }));

        builder.MapComposite<MailingAddressRecord>("register.co_mailing_address", new CompositeNameTranslator<MailingAddressRecord>(static member => member.Name switch
        {
            nameof(MailingAddressRecord.Address) => "address",
            nameof(MailingAddressRecord.PostalCode) => "postal_code",
            nameof(MailingAddressRecord.City) => "city",
            _ => null,
        }));

        builder.MapComposite<StreetAddressRecord>("register.co_street_address", new CompositeNameTranslator<StreetAddressRecord>(static member => member.Name switch
        {
            nameof(StreetAddressRecord.MunicipalNumber) => "municipal_number",
            nameof(StreetAddressRecord.MunicipalName) => "municipal_name",
            nameof(StreetAddressRecord.StreetName) => "street_name",
            nameof(StreetAddressRecord.HouseNumber) => "house_number",
            nameof(StreetAddressRecord.HouseLetter) => "house_letter",
            nameof(StreetAddressRecord.PostalCode) => "postal_code",
            nameof(StreetAddressRecord.City) => "city",
            _ => null,
        }));

        builder.MapComposite<ArgRolePartyRefPersonName>("register.arg_role_party_ref_person_name", new CompositeNameTranslator<ArgRolePartyRefPersonName>(static member => member.Name switch
        {
            nameof(ArgRolePartyRefPersonName.FirstName) => "first_name",
            nameof(ArgRolePartyRefPersonName.MiddleName) => "middle_name",
            nameof(ArgRolePartyRefPersonName.LastName) => "last_name",
            nameof(ArgRolePartyRefPersonName.ShortName) => "short_name",
            nameof(ArgRolePartyRefPersonName.DisplayName) => "display_name",
            _ => null,
        }));

        builder.MapComposite<ArgRolePartyRef>("register.arg_role_party_ref", new CompositeNameTranslator<ArgRolePartyRef>(static member => member.Name switch
        {
            nameof(ArgRolePartyRef.PartyUuid) => "party_uuid",
            nameof(ArgRolePartyRef.PersonIdentifier) => "person_identifier",
            nameof(ArgRolePartyRef.OrganizationNumber) => "organization_identifier",
            nameof(ArgRolePartyRef.PersonName) => "person_name",
            nameof(ArgRolePartyRef.MailingAddress) => "mailing_address",
            _ => null,
        }));

        builder.MapComposite<ArgRoleAssignment>("register.arg_role_assignment", new CompositeNameTranslator<ArgRoleAssignment>(static member => member.Name switch
        {
            nameof(ArgRoleAssignment.Identifier) => "identifier",
            nameof(ArgRoleAssignment.ToParty) => "to_party",
            _ => null,
        }));

        return builder;
    }

    private sealed class Marker
    {
        public static readonly ServiceDescriptor ServiceDescriptor = ServiceDescriptor.Singleton<Marker, Marker>();
    }

    private sealed class EnumNameTranslator<TEnum>
        : INpgsqlNameTranslator
        where TEnum : struct, Enum
    {
        private readonly ImmutableArray<(string MemberName, string PgName)> _values;

        public EnumNameTranslator(Func<TEnum, string?> resolver)
        {
            var enumValues = Enum.GetValues<TEnum>();
            var builder = ImmutableArray.CreateBuilder<(string MemberName, string PgName)>(enumValues.Length);

            foreach (var enumValue in enumValues)
            {
                var memberName = enumValue.ToString();
                var pgName = resolver(enumValue);
                if (pgName is null)
                {
                    ThrowHelper.ThrowInvalidOperationException($"Missing mapping for enum member '{memberName}' in type '{typeof(TEnum).FullName}'");
                }

                builder.Add((memberName, pgName));
            }

            builder.Sort(static (l, r) => string.CompareOrdinal(l.MemberName, r.MemberName));
            _values = builder.ToImmutable();
        }

        public string TranslateMemberName(string clrName)
        {
            var index = _values.AsSpan().BinarySearch(new MemberNameMatcher(clrName));

            if (index < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(clrName));
            }

            return _values[index].PgName;
        }

        public string TranslateTypeName(string clrName)
            => ThrowHelper.ThrowNotSupportedException<string>();
    }

    private sealed class CompositeNameTranslator<T>
        : INpgsqlNameTranslator
    {
        private readonly ImmutableArray<(string MemberName, string PgName)> _values;

        public CompositeNameTranslator(Func<PropertyInfo, string?> resolver)
        {
            var members = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var builder = ImmutableArray.CreateBuilder<(string MemberName, string PgName)>(members.Length);

            foreach (var member in members)
            {
                var memberName = member.Name;
                var pgName = resolver(member);
                if (pgName is null)
                {
                    ThrowHelper.ThrowInvalidOperationException($"Missing mapping for composite member '{memberName}' in type '{typeof(T).FullName}'");
                }

                builder.Add((memberName, pgName));
            }

            builder.Sort(static (l, r) => string.CompareOrdinal(l.MemberName, r.MemberName));
            _values = builder.ToImmutable();
        }

        public string TranslateMemberName(string clrName)
        {
            var index = _values.AsSpan().BinarySearch(new MemberNameMatcher(clrName));

            if (index < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(clrName));
            }

            return _values[index].PgName;
        }

        public string TranslateTypeName(string clrName)
            => ThrowHelper.ThrowNotSupportedException<string>();
    }

    private readonly struct MemberNameMatcher(string memberName)
        : IComparable<(string MemberName, string PgName)>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo((string MemberName, string PgName) other)
            => string.CompareOrdinal(memberName, other.MemberName);
    }
}
