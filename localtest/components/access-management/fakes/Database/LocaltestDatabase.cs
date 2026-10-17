#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Altinn.AccessManagement.Localtest.Database;

/// <summary>
/// Creates the production roles and the database on the shared Postgres container when they do not exist, so the
/// vendored migrations (which grant to <c>platform_authorization_admin</c> and <c>platform_authorization</c> by name)
/// can run on an ordinary container without a volume reset. Idempotent; runs before Init on every start.
/// </summary>
public static class LocaltestDatabase
{
    private const string AdminRole = "platform_authorization_admin";
    private const string AppRole = "platform_authorization";

    public static async Task EnsureAsync(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<LocaltestAccessManagementOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LocaltestDatabase));
        var bootstrap = new NpgsqlConnectionStringBuilder(options.BootstrapConnectionString);

        await using (var connection = new NpgsqlConnection(bootstrap.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var role in new[] { AdminRole, AppRole })
            {
                // Superuser locally: in localtest mode the EF migrations run on the app connection and create
                // schemas, extensions and grants, which the production split of privileges does not allow.
                await Execute(connection, $"""
                    DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                            CREATE ROLE {role} LOGIN SUPERUSER PASSWORD '{Escape(options.DatabasePassword)}';
                        ELSE
                            ALTER ROLE {role} LOGIN SUPERUSER PASSWORD '{Escape(options.DatabasePassword)}';
                        END IF;
                    END $$;
                    """);
            }

            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", options.DatabaseName);
            if (await exists.ExecuteScalarAsync() is null)
            {
                await Execute(connection, $"CREATE DATABASE \"{options.DatabaseName}\" OWNER {AdminRole}");
                logger.LogInformation("Localtest: created database {Database} owned by {Role}", options.DatabaseName, AdminRole);
            }
        }

        var target = new NpgsqlConnectionStringBuilder(options.BootstrapConnectionString) { Database = options.DatabaseName };
        await using (var connection = new NpgsqlConnection(target.ConnectionString))
        {
            await connection.OpenAsync();
            await Execute(connection, "CREATE EXTENSION IF NOT EXISTS hstore");
        }

        logger.LogInformation("Localtest: database {Database} and roles {Admin}/{App} are in place on {Host}", options.DatabaseName, AdminRole, AppRole, bootstrap.Host);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
