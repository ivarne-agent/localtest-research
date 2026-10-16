using System.Diagnostics;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.Location;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Altinn.Register.Persistence.Location;

/// <summary>
/// Implementation for <see cref="ILocationLookupProvider"/> using PostgreSQL as the data source.
/// </summary>
internal sealed partial class PostgreSqlLocationLookupProvider
{
    /// <summary>
    /// Provides a thread-safe cache for the <see cref="ILocationLookup"/> that is loaded from the database on demand.
    /// </summary>
    internal sealed partial class Cache
    {
        private readonly Lock _lock = new();
        private readonly NpgsqlDataSource _dataSource;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger<Cache> _logger;

        private object? _state; // ILocationLookup | Task<ILocationLookup> | null

        /// <summary>
        /// Initializes a new instance of the <see cref="Cache"/> class.
        /// </summary>
        public Cache(
            ILogger<Cache> logger,
            NpgsqlDataSource dataSource,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _dataSource = dataSource;
            _applicationLifetime = applicationLifetime;
        }

        /// <summary>
        /// Gets the <see cref="ILocationLookup"/> from the cache, or loads it if it's not already loaded.
        /// This method is thread-safe and ensures that the <see cref="ILocationLookup"/> is only loaded once.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="ILocationLookup"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<ILocationLookup> Get(CancellationToken cancellationToken = default)
        {
            object? current = Volatile.Read(ref _state);
            if (current is ILocationLookup lookup)
            {
                return ValueTask.FromResult(lookup);
            }

            return GetAsync(cancellationToken);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ValueTask<ILocationLookup> GetAsync(CancellationToken cancellationToken)
        {
            var stateTask = GetState(cancellationToken);

            if (!stateTask.IsCompletedSuccessfully)
            {
                return AwaitStateAsync(stateTask);
            }

            var state = stateTask.GetAwaiter().GetResult();
            return ValueTask.FromResult(state);

            async static ValueTask<ILocationLookup> AwaitStateAsync(ValueTask<ILocationLookup> stateTask)
                => await stateTask;
        }

        private ValueTask<ILocationLookup> GetState(CancellationToken cancellationToken)
        {
            object? current;
            Task<Task<ILocationLookup>>? coldTask = null;
            lock (_lock)
            {
                current = Volatile.Read(ref _state);

                if (current is ILocationLookup lookup)
                {
                    return ValueTask.FromResult(lookup);
                }

                if (current is null)
                {
                    coldTask = new(() => LoadStateAsync(_applicationLifetime.ApplicationStopping));
                    current = coldTask.Unwrap().ContinueWith(
                        task =>
                        {
                            lock (_lock)
                            {
                                if (ReferenceEquals(_state, current))
                                {
                                    if (!task.IsCompletedSuccessfully)
                                    {
                                        _state = null;
                                        return task;
                                    }

                                    var result = task.GetAwaiter().GetResult();
                                    _state = result;
                                    return task;
                                }
                            }

                            // state has been updated since this task started, use the new value
                            return GetState(cancellationToken).AsTask();
                        },
                        TaskContinuationOptions.ExecuteSynchronously)
                        .Unwrap();

                    Volatile.Write(ref _state, current);
                }
            }

            // we don't want to run any of the LoadStateAsync logic while holding the lock
            coldTask?.Start();

            Debug.Assert(current is Task<ILocationLookup>);
            return new(Unsafe.As<Task<ILocationLookup>>(current));
        }

        private async Task<ILocationLookup> LoadStateAsync(CancellationToken cancellationToken)
        {
            const string COUNTRIES_QUERY
                = /*strpsql*/"""
                SELECT code2, code3, name
                FROM register.country
                """;

            const string MUNICIPALITIES_QUERY
                = /*strpsql*/"""
                SELECT number, name, status
                FROM register.municipality
                """;

            Log.FetchingLocationLookupData(_logger);
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var batch = conn.CreateBatch();
            batch.CreateBatchCommand(COUNTRIES_QUERY);
            batch.CreateBatchCommand(MUNICIPALITIES_QUERY);

            await batch.PrepareAsync(cancellationToken);
            await using var reader = await batch.ExecuteReaderAsync(cancellationToken);

            var countries = await ReadCountries(reader, cancellationToken);

            var nextResult = await reader.NextResultAsync(cancellationToken);
            Debug.Assert(nextResult);

            var municipalities = await ReadMunicipalities(reader, cancellationToken);
            var lookup = LocationLookup.Create(countries, municipalities);

            return lookup;

            static async Task<List<Country>> ReadCountries(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                var countries = new List<Country>();

                var code2 = reader.GetOrdinal("code2");
                var code3 = reader.GetOrdinal("code3");
                var name = reader.GetOrdinal("name");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var country = new Country
                    {
                        Code2 = await reader.GetFieldValueAsync<string>(code2, cancellationToken),
                        Code3 = await reader.GetFieldValueAsync<string>(code3, cancellationToken),
                        Name = await reader.GetFieldValueAsync<string>(name, cancellationToken)
                    };

                    countries.Add(country);
                }

                return countries;
            }

            static async Task<List<Municipality>> ReadMunicipalities(NpgsqlDataReader reader, CancellationToken cancellationToken)
            {
                var municipalities = new List<Municipality>();

                var number = reader.GetOrdinal("number");
                var name = reader.GetOrdinal("name");
                var status = reader.GetOrdinal("status");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var numberInt = await reader.GetFieldValueAsync<int>(number, cancellationToken);

                    // The municipalities are effectively constant (from the database)
                    // so a Debug.Assert will catch errors in testsing, but we don't need
                    // to actually check this in production.
                    Debug.Assert(numberInt >= 0);

                    var municipality = new Municipality
                    {
                        Number = new(checked((uint)numberInt)),
                        Name = await reader.GetFieldValueAsync<string>(name, cancellationToken),
                        Status = await reader.GetFieldValueAsync<MunicipalityStatus>(status, cancellationToken)
                    };

                    municipalities.Add(municipality);
                }

                return municipalities;
            }
        }

        private static partial class Log
        {
            [LoggerMessage(0, LogLevel.Debug, "Fetching location lookup data.")]
            public static partial void FetchingLocationLookupData(ILogger logger);
        }
    }
}
