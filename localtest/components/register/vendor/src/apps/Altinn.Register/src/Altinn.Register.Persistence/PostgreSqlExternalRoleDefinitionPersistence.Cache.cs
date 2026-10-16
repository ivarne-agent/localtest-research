using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Altinn.Register.Persistence;

/// <summary>
/// Persistence service for external role definitions.
/// </summary>
internal sealed partial class PostgreSqlExternalRoleDefinitionPersistence
{
    /// <summary>
    /// Cache for external role definitions.
    /// </summary>
    /// <remarks>
    /// Given that adding role definitions requires redeploying the application, we can cache them indefinitely.
    /// If ever the ability to add role definitions dynamically is added, this will need some sort of eviction policy.
    /// </remarks>
    internal sealed partial class Cache
    {
        private readonly Lock _lock = new();
        private readonly NpgsqlDataSource _dataSource;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger<Cache> _logger;

        private object? _state; // State | Task<State> | null

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
        /// Asynchronously retrieves all available external role definitions.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>An array of all available external-role definitions.</returns>
        public ValueTask<ImmutableArray<ExternalRoleDefinition>> GetAllRoleDefinitions(CancellationToken cancellationToken)
        {
            return WithState(null, static (State state, object? _) => state.AllRoleDefinitions, cancellationToken);
        }

        /// <summary>
        /// Tries to get the role definition for the specified source and identifier.
        /// </summary>
        /// <param name="source">The role definition source.</param>
        /// <param name="identifier">The role definition identifier.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="ExternalRoleDefinition"/>, if found.</returns>
        public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinition(ExternalRoleSource source, string identifier, CancellationToken cancellationToken)
        {
            var key = new RoleKey(source, identifier);

            return WithState(key, static (state, key) => state.TryGetRoleDefinition(key), cancellationToken);
        }

        /// <summary>
        /// Tries to get the role definition for the specified role-code.
        /// </summary>
        /// <param name="roleCode">The role definition role-code.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="ExternalRoleDefinition"/>, if found.</returns>
        public ValueTask<ExternalRoleDefinition?> TryGetRoleDefinitionByRoleCode(string roleCode, CancellationToken cancellationToken)
        {
            return WithState(roleCode, static (state, roleCode) => state.TryGetRoleDefinitionByRoleCode(roleCode), cancellationToken);
        }

        /// <summary>
        /// Asynchronously retrieves a lookup service for external role definitions, allowing retrieval of role definitions by source/identifier or role-code without asynchronous operations.
        /// </summary>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A lookup service for external role definitions.</returns>
        public ValueTask<IExternalRoleDefinitionLookup> GetRoleDefinitionLookup(CancellationToken cancellationToken = default)
        {
            return WithState(null, static (State state, object? _) => (IExternalRoleDefinitionLookup)state, cancellationToken);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask<TResult> WithState<TState, TResult>(
            TState arg,
            Func<State, TState, TResult> func,
            CancellationToken cancellationToken)
        {
            object? current = Volatile.Read(ref _state);
            if (current is State s)
            {
                return new(func(s, arg));
            }

            return WithStateAsync(arg, func, cancellationToken);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ValueTask<TResult> WithStateAsync<TState, TResult>(
            TState arg,
            Func<State, TState, TResult> func,
            CancellationToken cancellationToken)
        {
            var stateTask = GetState(cancellationToken);

            if (!stateTask.IsCompletedSuccessfully)
            {
                return AwaitStateAsync(stateTask, arg, func);
            }

            var state = stateTask.GetAwaiter().GetResult();
            return ValueTask.FromResult(func(state, arg));

            async static ValueTask<TResult> AwaitStateAsync(ValueTask<State> stateTask, TState arg, Func<State, TState, TResult> func)
            {
                var state = await stateTask;

                return func(state, arg);
            }
        }

        private ValueTask<State> GetState(CancellationToken cancellationToken)
        {
            object? current;
            Task<Task<State>>? coldTask = null;
            lock (_lock)
            {
                current = Volatile.Read(ref _state);

                if (current is State s)
                {
                    return ValueTask.FromResult(s);
                }

                if (current is null)
                {
                    coldTask = new Task<Task<State>>(() => LoadStateAsync(_applicationLifetime.ApplicationStopping));
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

            Debug.Assert(current is Task<State>);
            return new(Unsafe.As<Task<State>>(current));
        }

        private async Task<State> LoadStateAsync(CancellationToken cancellationToken)
        {
            const string QUERY
                = /*strpsql*/"""
                SELECT
                  source
                , identifier
                , name
                , description
                , code
                , mu.identifier IS NULL AS is_main_unit
                FROM register.external_role_definition
                LEFT JOIN register.external_main_unit_role mu using (source, identifier)
                """;

            Log.FetchingExternalRoleDefinitions(_logger);
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = QUERY;

            await cmd.PrepareAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var byRoleKey = new Dictionary<RoleKey, ExternalRoleDefinition>();
            var byRoleCode = new Dictionary<string, ExternalRoleDefinition>();

            await foreach (var roleDefinition in ReadExternalRoleDefinitions(reader, cancellationToken))
            {
                var key = new RoleKey(roleDefinition.Source, roleDefinition.Identifier);
                byRoleKey.Add(key, roleDefinition);

                if (roleDefinition.Code is not null)
                {
                    byRoleCode.Add(roleDefinition.Code, roleDefinition);
                }
            }

            return new State(byRoleKey, byRoleCode);
        }

        /// <summary>
        /// Reads external role definitions from the specified <see cref="NpgsqlDataReader"/>.
        /// </summary>
        /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>An async enumerable of <see cref="ExternalRoleDefinition"/>s.</returns>
        /// <remarks>Used by tests.</remarks>
        internal static async IAsyncEnumerable<ExternalRoleDefinition> ReadExternalRoleDefinitions(NpgsqlDataReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var sourceOrdinal = reader.GetOrdinal("source");
            var identifierOrdinal = reader.GetOrdinal("identifier");
            var nameOrdinal = reader.GetOrdinal("name");
            var descriptionOrdinal = reader.GetOrdinal("description");
            var codeOrdinal = reader.GetOrdinal("code");
            var isMainUnitOrdinal = reader.GetOrdinal("is_main_unit");

            while (await reader.ReadAsync(cancellationToken))
            {
                var source = await reader.GetFieldValueAsync<ExternalRoleSource>(sourceOrdinal, cancellationToken);
                var identifier = await reader.GetFieldValueAsync<string>(identifierOrdinal, cancellationToken);
                var name = await reader.GetConvertibleFieldValueAsync(nameOrdinal, TranslatedTextConverter.FromDb, cancellationToken);
                var description = await reader.GetConvertibleFieldValueAsync(descriptionOrdinal, TranslatedTextConverter.FromDb, cancellationToken);
                var code = await reader.GetFieldValueOrDefaultAsync<string>(codeOrdinal, cancellationToken);

                var flags = ExternalRoleDefinition.RoleFlags.None;
                flags |= await ReadFlag(reader, isMainUnitOrdinal, ExternalRoleDefinition.RoleFlags.MainUnit, cancellationToken);

                var roleDefinition = new ExternalRoleDefinition()
                {
                    Source = source,
                    Identifier = identifier,
                    Name = name,
                    Description = description,
                    Code = code,
                    Flags = flags,
                };

                yield return roleDefinition;
            }

            static async ValueTask<ExternalRoleDefinition.RoleFlags> ReadFlag(
                NpgsqlDataReader reader,
                int ordinal,
                ExternalRoleDefinition.RoleFlags flag,
                CancellationToken cancellationToken)
            {
                var isSet = await reader.GetFieldValueAsync<bool>(ordinal, cancellationToken);
                return isSet ? flag : default;
            }
        }

        private readonly record struct RoleKey(ExternalRoleSource Source, string Identifier);

        private sealed class State
            : IExternalRoleDefinitionLookup
        {
            private readonly ImmutableArray<ExternalRoleDefinition> _all;
            private readonly FrozenDictionary<RoleKey, ExternalRoleDefinition> _byRoleKey;
            private readonly FrozenDictionary<string, ExternalRoleDefinition> _byRoleCode;

            public State(
                Dictionary<RoleKey, ExternalRoleDefinition> byRoleKey,
                Dictionary<string, ExternalRoleDefinition> byRoleCode)
            {
                _all = byRoleKey.Values.OrderBy(static r => r.Source).ThenBy(static r => r.Identifier).ToImmutableArray();
                _byRoleKey = byRoleKey.ToFrozenDictionary(byRoleKey.Comparer);
                _byRoleCode = byRoleCode.ToFrozenDictionary(byRoleCode.Comparer);
            }

            /// <inheritdoc/>
            public ImmutableArray<ExternalRoleDefinition> AllRoleDefinitions
                => _all;

            public ExternalRoleDefinition? TryGetRoleDefinition(RoleKey key)
                => _byRoleKey.TryGetValue(key, out var value) ? value : null;

            public ExternalRoleDefinition? TryGetRoleDefinitionByRoleCode(string roleCode)
                => _byRoleCode.TryGetValue(roleCode, out var value) ? value : null;

            /// <inheritdoc/>
            bool IExternalRoleDefinitionLookup.TryGetRoleDefinition(ExternalRoleSource source, string identifier, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition)
                => _byRoleKey.TryGetValue(new(source, identifier), out roleDefinition);

            /// <inheritdoc/>
            bool IExternalRoleDefinitionLookup.TryGetRoleDefinitionByRoleCode(string roleCode, [NotNullWhen(true)] out ExternalRoleDefinition? roleDefinition)
                => _byRoleCode.TryGetValue(roleCode, out roleDefinition);
        }

        private static partial class Log
        {
            [LoggerMessage(0, LogLevel.Debug, "Fetching external role definitions.")]
            public static partial void FetchingExternalRoleDefinitions(ILogger logger);
        }
    }
}
