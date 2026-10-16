using Npgsql;

namespace Altinn.Register.TestUtils.Database;

public sealed class PostgresDatabase
    : IAsyncDisposable
{
    public static async Task<PostgresDatabase> Create()
    {
        var testContext = TestContext.Current;
        var serverFixture = await testContext.GetFixture<PostgresServerFixture>();
        if (serverFixture is null)
        {
            throw new InvalidOperationException("PostgresServerFixture not found in test context.");
        }

        return await serverFixture.CreateDatabase(testContext.CancellationToken);
    }

    private readonly PostgresServerFixture.BaseDatabaseHandle _handle;
    private NpgsqlDataSource? _ownerDataSource;
    private NpgsqlDataSource? _migratorDataSource;
    private NpgsqlDataSource? _seederDataSource;
    private NpgsqlDataSource? _dataSource;

    internal PostgresDatabase(PostgresServerFixture.BaseDatabaseHandle handle)
    {
        _handle = handle;
    }

    public string ConnectionStringFor(PostgresUserType type)
        => _handle.ConnectionString(type);

    public string OwnerConnectionString
        => ConnectionStringFor(PostgresUserType.Owner);

    public string MigratorConnectionString
        => ConnectionStringFor(PostgresUserType.Migrator);

    public string SeederConnectionString
        => ConnectionStringFor(PostgresUserType.Seeder);

    public string ConnectionString
        => AppConnectionString;

    public string AppConnectionString
        => ConnectionStringFor(PostgresUserType.App);

    public string UserName
        => new NpgsqlConnectionStringBuilder(AppConnectionString).Username!;

    public NpgsqlDataSource OwnerDataSource
        => GetDataSource(ref _ownerDataSource, OwnerConnectionString);

    public NpgsqlDataSource MigratorDataSource
        => GetDataSource(ref _migratorDataSource, MigratorConnectionString);

    public NpgsqlDataSource SeederDataSource
        => GetDataSource(ref _seederDataSource, SeederConnectionString);

    public NpgsqlDataSource DataSource
        => GetDataSource(ref _dataSource, AppConnectionString);

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeDataSource(_ownerDataSource);
        await DisposeDataSource(_migratorDataSource);
        await DisposeDataSource(_seederDataSource);
        await DisposeDataSource(_dataSource);
        await _handle.DisposeAsync();

        static ValueTask DisposeDataSource(NpgsqlDataSource? dataSource)
            => dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private static NpgsqlDataSource GetDataSource(ref NpgsqlDataSource? dataSource, string connectionString)
        => dataSource ??= NpgsqlDataSource.Create(connectionString);
}
