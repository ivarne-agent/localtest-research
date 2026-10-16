using System.Collections.Immutable;
using System.Data;
using Altinn.Register.Core.UnitOfWork;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Altinn.Register.Persistence.UnitOfWork;

/// <summary>
/// A <see cref="IUnitOfWorkParticipant"/> implementation for a <see cref="NpgsqlConnection"/>.
/// </summary>
internal class NpgsqlUnitOfWorkParticipant
    : IUnitOfWorkParticipant<NpgsqlConnection>
    , IUnitOfWorkParticipant<SavePointManager>
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly SavePointManager _savePointManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlUnitOfWorkParticipant"/> class.
    /// </summary>
    public NpgsqlUnitOfWorkParticipant(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IUnitOfWorkHandle handle)
    {
        Guard.IsNotNull(connection);
        Guard.IsNotNull(transaction);

        _connection = connection;
        _transaction = transaction;
        _savePointManager = new SavePointManager(transaction, handle);
    }

    /// <inheritdoc/>
    NpgsqlConnection IUnitOfWorkParticipant<NpgsqlConnection>.Service => _connection;

    /// <inheritdoc/>
    SavePointManager IUnitOfWorkParticipant<SavePointManager>.Service => _savePointManager;

    /// <inheritdoc/>
    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
        => new(_transaction.CommitAsync(cancellationToken));

    /// <inheritdoc/>
    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        => new(_transaction.RollbackAsync(cancellationToken));

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>
    /// A <see cref="IUnitOfWorkParticipantFactory"/> for <see cref="NpgsqlUnitOfWorkParticipant"/>.
    /// </summary>
    internal class Factory
        : IUnitOfWorkParticipantFactory
    {
        private static readonly ImmutableArray<Type> _serviceTypes = [typeof(NpgsqlConnection), typeof(SavePointManager)];

        /// <inheritdoc/>
        public ImmutableArray<Type> ServiceTypes => _serviceTypes;

        /// <inheritdoc/>
        public async ValueTask<IUnitOfWorkParticipant> Create(IUnitOfWorkHandle handle, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
            NpgsqlConnection? connection = null;
            NpgsqlTransaction? transaction = null;

            try
            {
                connection = await dataSource.OpenConnectionAsync(cancellationToken);
                transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                var participant = new NpgsqlUnitOfWorkParticipant(connection, transaction, handle);

                connection = null;
                transaction = null;
                return participant;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
        }
    }
}
