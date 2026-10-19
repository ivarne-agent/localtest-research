#nullable enable
using System.Threading.Tasks;
using Altinn.Platform.Events.Models;
using Altinn.Platform.Events.Repository;

namespace Altinn.Platform.Events.Localtest.Persistence;

/// <summary><see cref="TraceLogRepository"/> on <see cref="LocaltestEventsStore"/>. No year partitions to run out of.</summary>
public sealed class InMemoryTraceLogRepository(LocaltestEventsStore store) : ITraceLogRepository
{
    public Task CreateTraceLogEntry(TraceLog traceLog)
    {
        store.InsertTraceLog(traceLog);
        return Task.CompletedTask;
    }
}
