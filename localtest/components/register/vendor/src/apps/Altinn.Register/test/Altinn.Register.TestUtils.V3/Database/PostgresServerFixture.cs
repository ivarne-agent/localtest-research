using System.Collections.Concurrent;
using System.Diagnostics;
using Altinn.Register.TestUtils.Async;
using Altinn.Register.TestUtils.Tracing;
using CommunityToolkit.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Altinn.Register.TestUtils.Database;

public class PostgresServerFixture
    : IAsyncLifetime
{
    private const int MAX_CONCURRENCY = AltinnTestCollectionFactory.MaxConcurrency;

    private readonly AsyncConcurrencyLimiter _throttler = new(MAX_CONCURRENCY);
    private readonly PostgreSqlContainer _container;
    private readonly ConcurrentDictionary<string, Task<TemplateDb>> _templateDatabases = new();

    private NpgsqlDataSource? _dataSource;
    private string? _serverConnectionString;
    private int _dbCounter = 0;
    private int _templateDbCounter = 0;

    public PostgresServerFixture()
    {
        var username = Debugger.IsAttached ? "username" : Guid.NewGuid().ToString("N");
        var password = Debugger.IsAttached ? "password" : Guid.NewGuid().ToString("N");

        var builder = new PostgreSqlBuilder("postgres:16.10-alpine")
            .WithUsername(username)
            .WithPassword(password)
            .WithCommand("-c", "max_locks_per_transaction=4096")
            .WithCleanUp(true);

        if (Debugger.IsAttached)
        {
            builder = builder.WithPortBinding(44181, PostgreSqlBuilder.PostgreSqlPort);
        }

        _container = builder.Build();
    }

    public Task<PostgresDatabase> CreateDatabase(CancellationToken cancellationToken)
        => CreateDatabase(template: null, cancellationToken);

    public async Task<PostgresDatabase> CreateDatabase(TemplateDb? template, CancellationToken cancellationToken)
    {
        var dbName = $"db_{Interlocked.Increment(ref _dbCounter):D4}";
        var handle = await CreateDatabaseCore(dbName, template, cancellationToken);

        return new PostgresDatabase(handle);
    }

    public async Task<TemplateDb> GetOrCreateTemplateDatabase(
        string key,
        Func<PostgresDatabase, CancellationToken, Task> initialize,
        CancellationToken cancellationToken)
    {
        Guard.IsNotNullOrWhiteSpace(key);
        Guard.IsNotNull(initialize);

        var task = _templateDatabases.GetOrAdd(key, key => CreateTemplateDatabaseCore(key, initialize, CancellationToken.None));

        return await task.WaitAsync(cancellationToken);

        async Task<TemplateDb> CreateTemplateDatabaseCore(string key, Func<PostgresDatabase, CancellationToken, Task> initialize, CancellationToken cancellationToken)
        {
            var dbName = $"db_template_{Interlocked.Increment(ref _templateDbCounter):D4}";
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"create template database {key} ({dbName})");

            DatabaseHandle? db = null;
            IDisposable? ticket = null;
            try
            {
                db = await CreateDatabaseCore(dbName, template: null, cancellationToken);
                (var templateDb, ticket) = db.IntoTemplate();
                db = null;

                {
                    await using var wrapper = new PostgresDatabase(templateDb);
                    await initialize(wrapper, cancellationToken);
                }

                return new TemplateDb(templateDb);
            }
            finally
            {
                if (db is not null)
                {
                    await db.DisposeAsync();
                }

                if (ticket is not null)
                {
                    ticket.Dispose();
                }
            }
        }
    }

    private async Task<DatabaseHandle> CreateDatabaseCore(
        string dbName,
        TemplateDb? template,
        CancellationToken cancellationToken)
    {
        var activityName = template is null
            ? $"create database {dbName}"
            : $"create database {dbName} from template {template.DatabaseName}";
        using var rootActivity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: activityName);

        DbUser owner;
        DbUser migrator;
        DbUser seeder;
        DbUser app;

        if (template is null)
        {
            owner = DefineUser(dbName, PostgresUserType.Owner);
            migrator = DefineUser(dbName, PostgresUserType.Migrator);
            seeder = DefineUser(dbName, PostgresUserType.Seeder);
            app = DefineUser(dbName, PostgresUserType.App);
        }
        else
        {
            // We use the users from the template, as they have already been granted persmissions
            owner = template.Owner;
            migrator = template.Migrator;
            seeder = template.Seeder;
            app = template.App;
        }

        IDisposable? ticket = null;
        DatabaseHandle? handle = null;
        try
        {
            {
                using var waitActivity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: "acquire database ticket")
                    .HideIfShorterThan(TimeSpan.FromMilliseconds(50));

                ticket = await _throttler.Acquire();
            }

            handle = new DatabaseHandle(this, ticket, dbName, owner: owner, migrator: migrator, seeder: seeder, app: app);
            ticket = null;

            if (template is not null)
            {
                await using var conn = await _dataSource!.OpenConnectionAsync(cancellationToken);
                await CreateDatabaseFromTemplate(conn, dbName, owner, template.DatabaseName, cancellationToken);
                return ClearRef(ref handle);
            }

            {
                await using var conn = await _dataSource!.OpenConnectionAsync(cancellationToken);
                await CreateRoles(conn, owner: owner, migrator: migrator, seeder: seeder, app: app, cancellationToken);
                await CreateDatabase(conn, dbName, owner, cancellationToken);
                await GrantDatabasePrivileges(conn, dbName, migrator: migrator, seeder: seeder, app: app, cancellationToken);
            }

            {
                var connectionString = handle.ConnectionString(PostgresUserType.Owner);
                await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
                await using var conn = await source.OpenConnectionAsync(cancellationToken);
                await GrantSchemaPrivileges(conn, migrator: migrator, app: app, cancellationToken);
            }

            return ClearRef(ref handle);
        }
        finally
        {
            if (handle is { } h)
            {
                await h.DisposeAsync();
            }

            if (ticket is { } t)
            {
                t.Dispose();
            }
        }

        static T ClearRef<T>(ref T? value)
            where T : class
        {
            var result = value!;
            Guard.IsNotNull(result);

            value = null;
            return result;
        }

        static DbUser DefineUser(string dbName, PostgresUserType userType)
        {
            var typeName = UserTypeString(userType);
            return new($"{dbName}_{typeName}", $"{dbName}_{typeName}_{Guid.NewGuid():N}", userType);
        }

        static async Task CreateRoles(NpgsqlConnection conn, DbUser owner, DbUser migrator, DbUser seeder, DbUser app, CancellationToken cancellationToken)
        {
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"create roles");
            await using var batch = conn.CreateBatch();

            AddCommand(
                batch,
                /*strpsql*/$"""
                CREATE ROLE "{owner.Name}" WITH LOGIN PASSWORD '{owner.Pass}'
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                CREATE ROLE "{migrator.Name}" WITH LOGIN PASSWORD '{migrator.Pass}'
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                CREATE ROLE "{seeder.Name}" WITH LOGIN PASSWORD '{seeder.Pass}'
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                CREATE ROLE "{app.Name}" WITH LOGIN PASSWORD '{app.Pass}'
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT "{migrator.Name}" TO "{owner.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT "{seeder.Name}" TO "{owner.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT "{app.Name}" TO "{owner.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT "{app.Name}" TO "{seeder.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT "{app.Name}" TO "{migrator.Name}"
                """);

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        static async Task CreateDatabase(NpgsqlConnection conn, string dbName, DbUser owner, CancellationToken cancellationToken)
        {
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"create database");
            await using var cmd = conn.CreateCommand();

            cmd.CommandText =
                /*strpsql*/$"""
                CREATE DATABASE "{dbName}" OWNER "{owner.Name}"
                """;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        static async Task CreateDatabaseFromTemplate(NpgsqlConnection conn, string dbName, DbUser owner, string templateDbName, CancellationToken cancellationToken)
        {
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"create database");
            await using var cmd = conn.CreateCommand();

            cmd.CommandText =
                /*strpsql*/$"""
                CREATE DATABASE "{dbName}" WITH OWNER "{owner.Name}" TEMPLATE "{templateDbName}"
                """;

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        static async Task GrantDatabasePrivileges(NpgsqlConnection conn, string dbName, DbUser migrator, DbUser seeder, DbUser app, CancellationToken cancellationToken)
        {
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"grant database privileges");
            await using var batch = conn.CreateBatch();

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT CREATE, CONNECT ON DATABASE "{dbName}" TO "{migrator.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT CONNECT ON DATABASE "{dbName}" TO "{seeder.Name}"
                """);

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT CONNECT ON DATABASE "{dbName}" TO "{app.Name}"
                """);

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        static async Task GrantSchemaPrivileges(NpgsqlConnection conn, DbUser migrator, DbUser app, CancellationToken cancellationToken)
        {
            using var activity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"grant schema privileges");
            await using var batch = conn.CreateBatch();

            AddCommand(
                batch,
                /*strpsql*/$"""
                GRANT CREATE ON SCHEMA "public" TO "{migrator.Name}"
                """);

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        static void AddCommand(NpgsqlBatch batch, string commandText)
        {
            var cmd = batch.CreateBatchCommand();
            cmd.CommandText = commandText;
            batch.BatchCommands.Add(cmd);
        }
    }

    private async Task DeleteDatabase(string databaseName, CancellationToken cancellationToken)
    {
        using var rootActivity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"delete database {databaseName}");

        await using var conn = await _dataSource!.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = /*strpsql*/$"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string UserTypeString(PostgresUserType type)
        => type switch
        {
            PostgresUserType.Owner => "owner",
            PostgresUserType.Migrator => "migrator",
            PostgresUserType.Seeder => "seeder",
            PostgresUserType.App => "app",
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<string>(nameof(type)),
        };

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_dataSource is { } dataSource)
        {
            await dataSource.DisposeAsync();
        }

        if (_container is { } container)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Disposing PostgreSQL container...");

            await container.DisposeAsync();
        }
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        TestContext.Current.TestOutputHelper?.WriteLine("Starting PostgreSQL container...");

        using var rootActivity = TestUtilsActivities.Source.StartActivity(ActivityKind.Internal, name: $"start database server");
        await _container.StartAsync(TestContext.Current.CancellationToken);

        var csBuilder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        csBuilder.IncludeErrorDetail = true;
        csBuilder.Pooling = false;

        _serverConnectionString = csBuilder.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(csBuilder);
    }

    internal abstract class BaseDatabaseHandle
        : IAsyncDisposable
    {
        private readonly DbUser _owner;
        private readonly DbUser _migrator;
        private readonly DbUser _seeder;
        private readonly DbUser _app;

        private int _disposed = 0;
        private string? _ownerConnectionString;
        private string? _migratorConnectionString;
        private string? _seederConnectionString;
        private string? _appConnectionString;

        protected PostgresServerFixture Server { get; }

        protected string DatabaseName { get; }

        protected DbUser Owner => _owner;

        protected DbUser Migrator => _migrator;

        protected DbUser Seeder => _seeder;

        protected DbUser App => _app;

        protected BaseDatabaseHandle(
            PostgresServerFixture server,
            string databaseName,
            DbUser owner,
            DbUser migrator,
            DbUser seeder,
            DbUser app)
        {
            Server = server;
            DatabaseName = databaseName;
            _owner = owner;
            _migrator = migrator;
            _seeder = seeder;
            _app = app;
        }

        protected BaseDatabaseHandle(BaseDatabaseHandle other)
            : this(other.Server, other.DatabaseName, other._owner, other._migrator, other._seeder, other._app)
        {
        }

        public string ConnectionString(PostgresUserType type)
        {
            ThrowIfDisposed();

            return type switch
            {
                PostgresUserType.Owner => ConnectionString(in _owner, ref _ownerConnectionString),
                PostgresUserType.Migrator => ConnectionString(in _migrator, ref _migratorConnectionString),
                PostgresUserType.Seeder => ConnectionString(in _seeder, ref _seederConnectionString),
                PostgresUserType.App => ConnectionString(in _app, ref _appConnectionString),
                _ => ThrowHelper.ThrowArgumentOutOfRangeException<string>(nameof(type)),
            };
        }

        private string ConnectionString(in DbUser user, ref string? connectionString)
        {
            if (connectionString is not null)
            {
                return connectionString;
            }

            var builder = new NpgsqlConnectionStringBuilder(Server._dataSource!.ConnectionString)
            {
                Database = DatabaseName,
                Username = user.Name,
                Password = user.Pass,
                Pooling = false,
                CommandTimeout = 300,
            };

            connectionString = builder.ConnectionString;
            return connectionString;
        }

        public virtual ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                return DisposeAsyncCore();
            }

            return ValueTask.CompletedTask;
        }

        protected abstract ValueTask DisposeAsyncCore();

        protected void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(DatabaseHandle));
            }
        }
    }

    public sealed class TemplateDb
    {
        private readonly TemplateDbHandle _templateDb;

        internal TemplateDb(TemplateDbHandle templateDb)
        {
            _templateDb = templateDb;
        }

        public string DatabaseName => _templateDb.DatabaseName;

        internal DbUser Owner => _templateDb.Owner;

        internal DbUser Migrator => _templateDb.Migrator;

        internal DbUser Seeder => _templateDb.Seeder;

        internal DbUser App => _templateDb.App;
    }

    internal sealed class TemplateDbHandle
        : BaseDatabaseHandle
    {
        internal TemplateDbHandle(BaseDatabaseHandle databaseInfo)
            : base(databaseInfo)
        {
        }

        public new string DatabaseName => base.DatabaseName;

        public new DbUser Owner => base.Owner;

        public new DbUser Migrator => base.Migrator;

        public new DbUser Seeder => base.Seeder;

        public new DbUser App => base.App;

        protected override ValueTask DisposeAsyncCore()
            => throw new NotSupportedException(); // should not happen

        // Template databases are not deleted after use, so disposal is a no-op.
        public override ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    internal record struct DbUser(string Name, string Pass, PostgresUserType Type);

    internal sealed class DatabaseHandle
        : BaseDatabaseHandle
    {
        private readonly IDisposable _ticket;

        public DatabaseHandle(
            PostgresServerFixture server,
            IDisposable ticket,
            string databaseName,
            DbUser owner,
            DbUser migrator,
            DbUser seeder,
            DbUser app)
            : base(server, databaseName, owner, migrator, seeder, app)
        {
            _ticket = ticket;
        }

        internal (TemplateDbHandle TemplateDb, IDisposable Ticket) IntoTemplate()
        {
            var templateDb = new TemplateDbHandle(this);
            return (templateDb, _ticket);
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            await Server.DeleteDatabase(DatabaseName, TestContext.Current.CancellationToken);

            _ticket.Dispose();
        }
    }
}
