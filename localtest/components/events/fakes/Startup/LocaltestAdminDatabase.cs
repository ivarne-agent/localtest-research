#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Altinn.Platform.Events.Localtest.Startup;

/// <summary>
/// eventsdb as <c>platform_events_admin</c> (the role that owns the tables and runs the migrations), for the localtest
/// startup scripts and the inspection API's reset. The production code keeps its own <c>platform_events</c> data source.
/// </summary>
public sealed class LocaltestAdminDatabase : IAsyncDisposable
{
    public LocaltestAdminDatabase(IConfiguration configuration)
    {
        // The same composition Program.cs uses for the Yuniql connection.
        string connectionString = string.Format(
            configuration["PostgreSQLSettings:AdminConnectionString"]!,
            configuration["PostgreSQLSettings:EventsDbAdminPwd"]);
        DataSource = NpgsqlDataSource.Create(connectionString);
    }

    public NpgsqlDataSource DataSource { get; }

    /// <summary>Runs one of the embedded <c>fakes/Startup/*.sql</c> scripts.</summary>
    public async Task RunScript(string script, CancellationToken cancellationToken)
    {
        await using var stream = typeof(LocaltestAdminDatabase).Assembly.GetManifestResourceStream($"Localtest.{script}")
            ?? throw new InvalidOperationException($"Embedded startup script Localtest.{script} is missing.");
        using var reader = new StreamReader(stream);
        await Execute(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
    }

    public async Task Execute(string sql, CancellationToken cancellationToken)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}
