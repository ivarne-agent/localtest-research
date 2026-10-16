#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>In-memory stand-in for the <c>oidcserver.unregistered_client_request</c> table.</summary>
public sealed class InMemoryUnregisteredClientRequestRepository(TimeProvider timeProvider) : IUnregisteredClientRepository
{
    private readonly ConcurrentDictionary<Guid, UnregisteredClientRequest> _rows = new();

    public Task InsertAsync(UnregisteredClientRequestCreate create, CancellationToken cancellationToken)
    {
        var row = new UnregisteredClientRequest(
            create.RequestId,
            UnregisteredClientRequestStatus.Pending,
            timeProvider.GetUtcNow(),
            create.ExpiresAt,
            null,
            create.Issuer ?? string.Empty,
            create.GotoUrl,
            null,
            create.CreatedByIp,
            create.UserAgentHash,
            create.CorrelationId,
            null);
        if (!_rows.TryAdd(create.RequestId, row))
        {
            throw new InvalidOperationException($"Unregistered client request {create.RequestId} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<UnregisteredClientRequest?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
        => Task.FromResult(_rows.TryGetValue(requestId, out var row) ? row : null);

    public Task<bool> MarkStatusAsync(Guid requestId, UnregisteredClientRequestStatus newStatus, DateTimeOffset whenUtc, string? handledByCallback, CancellationToken cancellationToken)
    {
        if (!_rows.TryGetValue(requestId, out var row))
        {
            return Task.FromResult(false);
        }

        _rows[requestId] = row with { Status = newStatus, CompletedAt = whenUtc, HandledByCallback = handledByCallback };
        return Task.FromResult(true);
    }

    public Task<int> SweepExpiredAsync(DateTimeOffset nowUtc, int limit, CancellationToken cancellationToken)
    {
        var expired = _rows.Values.Where(r => r.Status == UnregisteredClientRequestStatus.Pending && r.ExpiresAt <= nowUtc).Take(limit).ToList();
        foreach (var row in expired)
        {
            _rows.TryRemove(row.RequestId, out _);
        }

        return Task.FromResult(expired.Count);
    }
}
