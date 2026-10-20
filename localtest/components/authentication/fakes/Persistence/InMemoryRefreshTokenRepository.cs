#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>
/// In-memory stand-in for the <c>oidcserver.refresh_token_family</c> and <c>oidcserver.refresh_token</c> tables, with
/// production's semantics: one family per (client, subject, OP session); a token is found by its HMAC lookup key;
/// rotation marks the old token <c>used</c> (only from <c>active</c>); revoking a token or a family touches only
/// <c>active</c> and <c>rotated</c> tokens. As in production, a revoked family is never replaced for the same
/// (client, subject, session): the unique constraint keeps it, so <see cref="GetOrCreateFamilyAsync"/> throws.
/// Tokens are dropped a day after their absolute expiry.
/// </summary>
public sealed class InMemoryRefreshTokenRepository(TimeProvider timeProvider) : IRefreshTokenRepository
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    private readonly Dictionary<(string ClientId, string SubjectId, string OpSid), Family> _families = [];
    private readonly Dictionary<Guid, RefreshTokenRow> _tokens = [];
    private readonly Dictionary<string, Guid> _byLookupKey = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public Task<Guid> GetOrCreateFamilyAsync(string clientId, string subjectId, string opSid, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_families.TryGetValue((clientId, subjectId, opSid), out var family))
            {
                family = new Family(Guid.NewGuid(), opSid);
                _families[(clientId, subjectId, opSid)] = family;
            }

            if (family.RevokedAt is not null)
            {
                throw new DataException("Failed to get or create refresh token family.");
            }

            return Task.FromResult(family.FamilyId);
        }
    }

    public Task<IReadOnlyList<Guid>> GetFamiliesByOpSidAsync(string opSid, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<Guid> ids = _families.Values.Where(f => f.OpSid == opSid && f.RevokedAt is null).Select(f => f.FamilyId).ToList();
            return Task.FromResult(ids);
        }
    }

    public Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            foreach (var family in _families.Values.Where(f => f.FamilyId == familyId && f.RevokedAt is null))
            {
                family.RevokedAt = now;
                family.RevokedReason = reason;
            }

            foreach (var token in _tokens.Values.Where(t => t.FamilyId == familyId && t.Status is "active" or "rotated").ToList())
            {
                _tokens[token.TokenId] = token with { Status = "revoked", RevokedAt = now, RevokedReason = reason };
            }
        }

        return Task.CompletedTask;
    }

    public Task InsertAsync(RefreshTokenRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_families.Values.Any(f => f.FamilyId == row.FamilyId))
            {
                throw new DataException("refresh_token.family_id references no refresh_token_family.");
            }

            foreach (var old in _tokens.Values.Where(t => t.AbsoluteExpiresAt + Retention < now).ToList())
            {
                _tokens.Remove(old.TokenId);
                _byLookupKey.Remove(Convert.ToHexString(old.LookupKey));
            }

            var key = Convert.ToHexString(row.LookupKey);
            if (_tokens.ContainsKey(row.TokenId) || _byLookupKey.ContainsKey(key))
            {
                throw new DataException("Refresh token already exists.");
            }

            _tokens[row.TokenId] = Copy(row);
            _byLookupKey[key] = row.TokenId;
        }

        return Task.CompletedTask;
    }

    public Task<RefreshTokenRow?> GetByLookupKeyAsync(byte[] lookupKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_byLookupKey.TryGetValue(Convert.ToHexString(lookupKey), out var id) && _tokens.TryGetValue(id, out var row)
                ? Copy(row)
                : null);
        }
    }

    public Task MarkUsedAsync(Guid tokenId, Guid rotatedToTokenId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_tokens.TryGetValue(tokenId, out var row) && row.Status == "active")
            {
                _tokens[tokenId] = row with { Status = "used", RotatedToTokenId = rotatedToTokenId, RevokedAt = now, RevokedReason = "rotated" };
            }
        }

        return Task.CompletedTask;
    }

    public Task RevokeAsync(Guid tokenId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (_tokens.TryGetValue(tokenId, out var row) && row.Status is "active" or "rotated")
            {
                _tokens[tokenId] = row with { Status = "revoked", RevokedAt = now, RevokedReason = reason };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>What a database round trip does: the caller's arrays and dictionaries are not shared with the store.</summary>
    private static RefreshTokenRow Copy(RefreshTokenRow row) => row with
    {
        LookupKey = row.LookupKey.ToArray(),
        Hash = row.Hash.ToArray(),
        Salt = row.Salt.ToArray(),
        Scopes = row.Scopes.ToArray(),
        Amr = row.Amr?.ToArray(),
        ProviderClaims = row.ProviderClaims?.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
    };

    private sealed class Family(Guid familyId, string opSid)
    {
        public Guid FamilyId { get; } = familyId;

        public string OpSid { get; } = opSid;

        public DateTimeOffset? RevokedAt { get; set; }

        public string? RevokedReason { get; set; }
    }
}
