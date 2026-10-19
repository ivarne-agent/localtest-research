#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Events.Models;
using Altinn.Platform.Events.Repository;

namespace Altinn.Platform.Events.Localtest.Persistence;

/// <summary>
/// The transaction behind the production claim loop (<c>RegisteredEventsProcessingService</c>), in memory. A unit of
/// work holds the row it claimed (production: <c>FOR UPDATE SKIP LOCKED</c>) and buffers its writes; commit applies
/// them and releases the row, rollback drops them and releases the row, and a savepoint marks how many buffered
/// writes survive a rollback to it. <see cref="UnitOfWork.Transaction"/> stays null: nothing here has a connection.
/// </summary>
public sealed class InMemoryUnitOfWorkRepository(LocaltestEventsStore store) : IUnitOfWorkRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<UnitOfWork, Pending> _open = new(ReferenceEqualityComparer.Instance);

    public Task<UnitOfWork> StartUnitOfWork()
    {
        var unitOfWork = new UnitOfWork { Transaction = null! };
        lock (_gate)
        {
            _open[unitOfWork] = new Pending();
        }

        return Task.FromResult(unitOfWork);
    }

    public Task CommitUnitOfWork(UnitOfWork unitOfWork)
    {
        var pending = Close(unitOfWork);
        try
        {
            foreach (var write in pending.Writes)
            {
                write();
            }
        }
        finally
        {
            Release(pending);
        }

        return Task.CompletedTask;
    }

    public Task RollbackUnitOfWork(UnitOfWork unitOfWork)
    {
        Release(Close(unitOfWork));
        return Task.CompletedTask;
    }

    public Task SaveUnitOfWork(UnitOfWork unitOfWork, string savepoint)
    {
        var pending = Get(unitOfWork);
        pending.Savepoints[savepoint] = pending.Writes.Count;
        return Task.CompletedTask;
    }

    public Task RollbackUnitOfWorkToSavepoint(UnitOfWork unitOfWork, string savepoint)
    {
        var pending = Get(unitOfWork);
        if (!pending.Savepoints.TryGetValue(savepoint, out int count))
        {
            throw new InvalidOperationException($"savepoint \"{savepoint}\" does not exist");
        }

        pending.Writes.RemoveRange(count, pending.Writes.Count - count);
        return Task.CompletedTask;
    }

    internal void RecordClaim(UnitOfWork unitOfWork, long sequenceNo) => Get(unitOfWork).Claims.Add(sequenceNo);

    internal void Enqueue(UnitOfWork unitOfWork, Action write) => Get(unitOfWork).Writes.Add(write);

    private Pending Get(UnitOfWork unitOfWork)
    {
        lock (_gate)
        {
            return _open.TryGetValue(unitOfWork, out var pending)
                ? pending
                : throw new InvalidOperationException("The unit of work is not open.");
        }
    }

    private Pending Close(UnitOfWork unitOfWork)
    {
        lock (_gate)
        {
            return _open.Remove(unitOfWork, out var pending)
                ? pending
                : throw new InvalidOperationException("The unit of work is not open.");
        }
    }

    private void Release(Pending pending)
    {
        foreach (long sequenceNo in pending.Claims)
        {
            store.ReleaseClaim(sequenceNo);
        }
    }

    private sealed class Pending
    {
        public List<Action> Writes { get; } = [];

        public List<long> Claims { get; } = [];

        public Dictionary<string, int> Savepoints { get; } = [];
    }
}
