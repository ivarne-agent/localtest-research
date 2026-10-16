#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>In-memory stand-in for the <c>oidcserver.oidc_session</c> table. Sessions live until the process restarts.</summary>
public sealed class InMemoryOidcSessionRepository(TimeProvider timeProvider) : IOidcSessionRepository
{
    private readonly ConcurrentDictionary<string, Row> _rows = new(StringComparer.Ordinal);

    public Task<OidcSession> CreateSession(OidcSessionCreate create, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(create.Sid))
        {
            throw new ArgumentException("Sid is required", nameof(create));
        }

        if (create.SubjectPartyUuid is null || create.SubjectPartyUuid == Guid.Empty)
        {
            throw new ArgumentException("SubjectPartyUuid is required", nameof(create));
        }

        var now = create.Now ?? timeProvider.GetUtcNow();
        var row = new Row(create, now) { UpdatedAt = now, LastSeenAt = now, ExpiresAt = create.ExpiresAt };
        _rows[create.Sid] = row;
        return Task.FromResult(row.ToModel());
    }

    public Task<OidcSession?> GetBySidAsync(string sid, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.TryGetValue(sid, out var row) ? row.ToModel() : null);

    public Task<OidcSession?> GetBySessionHandleHashAsync(byte[] sessionHandleHash, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.Values.FirstOrDefault(r => r.Create.SessionHandleHash.AsSpan().SequenceEqual(sessionHandleHash))?.ToModel());

    public Task<bool> DeleteBySidAsync(string sid, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.TryRemove(sid, out _));

    public Task TouchLastSeenAsync(string sid, CancellationToken cancellationToken = default)
    {
        if (_rows.TryGetValue(sid, out var row))
        {
            row.LastSeenAt = timeProvider.GetUtcNow();
        }

        return Task.CompletedTask;
    }

    public Task<bool> SlideExpiryToAsync(string sid, DateTimeOffset newExpiresAt, CancellationToken cancellationToken = default)
    {
        if (!_rows.TryGetValue(sid, out var row))
        {
            return Task.FromResult(false);
        }

        var now = timeProvider.GetUtcNow();
        row.ExpiresAt = newExpiresAt;
        row.UpdatedAt = now;
        row.LastSeenAt = now;
        return Task.FromResult(true);
    }

    public Task<string[]> GetSidsByUpstreamSessionSidAsync(string issuer, string upstreamSid, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.Values.Where(r => r.Create.UpstreamIssuer == issuer && r.Create.UpstreamSessionSid == upstreamSid).Select(r => r.Create.Sid).ToArray());

    public Task<int> DeleteByUpstreamSessionSidAsync(string issuer, string upstreamSid, CancellationToken cancellationToken = default)
    {
        var sids = _rows.Values.Where(r => r.Create.UpstreamIssuer == issuer && r.Create.UpstreamSessionSid == upstreamSid).Select(r => r.Create.Sid).ToList();
        foreach (var sid in sids)
        {
            _rows.TryRemove(sid, out _);
        }

        return Task.FromResult(sids.Count);
    }

    private sealed class Row(OidcSessionCreate create, DateTimeOffset createdAt)
    {
        public OidcSessionCreate Create { get; } = create;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? LastSeenAt { get; set; }

        public DateTimeOffset? ExpiresAt { get; set; }

        public OidcSession ToModel() => new()
        {
            Sid = Create.Sid,
            SessionHandle = Create.SessionHandleHash,
            UpstreamIssuer = Create.UpstreamIssuer,
            UpstreamSub = Create.UpstreamSub,
            SubjectId = Create.SubjectId ?? string.Empty,
            ExternalId = Create.ExternalId,
            SubjectPartyUuid = Create.SubjectPartyUuid,
            SubjectPartyId = Create.SubjectPartyId,
            SubjectUserId = Create.SubjectUserId,
            SubjectUserName = Create.SubjectUserName,
            Provider = Create.Provider,
            Acr = Create.Acr,
            ProviderClaims = Create.ProviderClaims,
            AuthTime = Create.AuthTime,
            Amr = Create.Amr,
            Scopes = Create.Scopes.ToArray(),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastSeenAt = LastSeenAt,
            ExpiresAt = ExpiresAt,
            UpstreamSessionSid = Create.UpstreamSessionSid,
        };
    }
}
