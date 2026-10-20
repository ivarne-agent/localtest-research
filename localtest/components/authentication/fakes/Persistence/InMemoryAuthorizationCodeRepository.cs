#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>
/// In-memory stand-in for the <c>oidcserver.authorization_code</c> table. Same semantics as production's SQL: <c>GetAsync</c>
/// finds only unused, unexpired codes; <c>TryConsumeAsync</c> is one conditional update (here under the lock);
/// <c>GetClientsPartOfSession</c> reads every code of the session, used or not, which is how logout finds the clients to
/// notify. Rows are dropped a day after they expire (production keeps them), so a session older than that no longer
/// notifies its clients on logout.
/// </summary>
public sealed class InMemoryAuthorizationCodeRepository(TimeProvider timeProvider) : IAuthorizationCodeRepository
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    private readonly Dictionary<string, AuthCodeRow> _rows = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public Task InsertAsync(AuthorizationCodeCreate codeCreate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeCreate);
        var row = new AuthCodeRow
        {
            Code = codeCreate.Code,
            ClientId = codeCreate.ClientId,
            SubjectId = codeCreate.SubjectId,
            ExternalId = codeCreate.ExternalId,
            SubjectPartyUuid = codeCreate.SubjectPartyUuid,
            SubjectPartyId = codeCreate.SubjectPartyId,
            SubjectUserId = codeCreate.SubjectUserId,
            SubjectUserName = codeCreate.SubjectUserName,
            SessionId = codeCreate.SessionId,
            RedirectUri = codeCreate.RedirectUri,
            Scopes = codeCreate.Scopes.ToArray(),
            Nonce = codeCreate.Nonce,
            Acr = codeCreate.Acr,
            Amr = codeCreate.Amr?.ToArray(),
            AuthTime = codeCreate.AuthTime,
            ProviderClaims = Copy(codeCreate.ProviderClaims),
            CodeChallenge = codeCreate.CodeChallenge,
            CodeChallengeMethod = codeCreate.CodeChallengeMethod,
            Used = false,
            ExpiresAt = codeCreate.ExpiresAt,
        };

        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            foreach (var expired in _rows.Values.Where(r => r.ExpiresAt + Retention < now).Select(r => r.Code).ToList())
            {
                _rows.Remove(expired);
            }

            // The table's primary key.
            if (!_rows.TryAdd(row.Code, row))
            {
                throw new InvalidOperationException("Authorization code already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<AuthCodeRow?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("code is required", nameof(code));
        }

        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            // Not found, already used, or expired: null, which the token service answers with invalid_grant.
            return Task.FromResult(_rows.TryGetValue(code, out var row) && !row.Used && row.ExpiresAt > now
                ? row with { ProviderClaims = Copy(row.ProviderClaims) }
                : null);
        }
    }

    public Task<bool> TryConsumeAsync(string code, string clientId, Uri redirectUri, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_rows.TryGetValue(code, out var row)
                || !string.Equals(row.ClientId, clientId, StringComparison.Ordinal)
                || !string.Equals(row.RedirectUri.ToString(), redirectUri.ToString(), StringComparison.Ordinal)
                || row.Used
                || row.ExpiresAt <= now)
            {
                return Task.FromResult(false);
            }

            _rows[code] = row with { Used = true };
            return Task.FromResult(true);
        }
    }

    public Task<List<string>> GetClientsPartOfSession(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.Values.Where(r => r.SessionId == sessionId).Select(r => r.ClientId).Distinct(StringComparer.Ordinal).ToList());
        }
    }

    private static Dictionary<string, List<string>>? Copy(Dictionary<string, List<string>>? claims)
        => claims?.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
}
