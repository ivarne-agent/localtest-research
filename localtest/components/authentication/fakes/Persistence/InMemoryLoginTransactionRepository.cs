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
/// In-memory stand-in for the <c>oidcserver.login_transaction</c> table: a registered client's <c>authorize</c> request,
/// read back by the upstream callback. As in production a row is returned whatever its status or expiry (the service
/// decides); rows are dropped a day after they expire, which production leaves to a sweep.
/// </summary>
public sealed class InMemoryLoginTransactionRepository(TimeProvider timeProvider) : ILoginTransactionRepository
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    private readonly Dictionary<Guid, LoginTransaction> _rows = [];
    private readonly Lock _lock = new();

    public Task<LoginTransaction> InsertAsync(LoginTransactionCreate create, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        Require(!string.IsNullOrWhiteSpace(create.ClientId), "ClientId required.");
        Require(create.RedirectUri is { IsAbsoluteUri: true }, "RedirectUri must be absolute.");
        Require(create.Scopes is { Count: > 0 }, "At least one scope required.");
        Require(!string.IsNullOrWhiteSpace(create.State), "State required.");
        Require(!string.IsNullOrWhiteSpace(create.Nonce), "Nonce required.");
        Require(!string.IsNullOrWhiteSpace(create.CodeChallenge), "CodeChallenge required.");
        Require(!string.IsNullOrWhiteSpace(create.CodeChallengeMethod), "CodeChallengeMethod required.");

        var now = timeProvider.GetUtcNow();
        var row = new LoginTransaction
        {
            RequestId = Guid.CreateVersion7(),
            Status = "pending",
            CreatedAt = now,
            ExpiresAt = create.ExpiresAt,
            ClientId = create.ClientId,
            RedirectUri = create.RedirectUri,
            Scopes = create.Scopes.ToArray(),
            State = create.State,
            Nonce = create.Nonce,
            AcrValues = create.AcrValues?.ToArray(),
            Prompts = create.Prompts?.ToArray(),
            UiLocales = create.UiLocales?.ToArray(),
            MaxAge = create.MaxAge,
            CodeChallenge = create.CodeChallenge,
            CodeChallengeMethod = create.CodeChallengeMethod,
            RequestUri = create.RequestUri,
            RequestObjectJwt = create.RequestObjectJwt,
            AuthorizationDetailsJson = create.AuthorizationDetailsJson,
            CreatedByIp = create.CreatedByIp,
            UserAgentHash = create.UserAgentHash,
            CorrelationId = create.CorrelationId,
        };

        lock (_lock)
        {
            foreach (var expired in _rows.Values.Where(r => r.ExpiresAt + Retention < now).Select(r => r.RequestId).ToList())
            {
                _rows.Remove(expired);
            }

            _rows[row.RequestId] = row;
        }

        return Task.FromResult(row);
    }

    public Task<LoginTransaction?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_rows.TryGetValue(requestId, out var row) ? row : null);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message, "create");
        }
    }
}
