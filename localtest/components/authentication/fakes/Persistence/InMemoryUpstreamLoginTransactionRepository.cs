#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>In-memory stand-in for the <c>oidcserver.login_transaction_upstream</c> table.</summary>
public sealed class InMemoryUpstreamLoginTransactionRepository(TimeProvider timeProvider) : IUpstreamLoginTransactionRepository
{
    private readonly ConcurrentDictionary<Guid, Row> _rows = new();

    public Task<UpstreamLoginTransaction> InsertAsync(UpstreamLoginTransactionCreate create, CancellationToken cancellationToken = default)
    {
        if ((create.RequestId is null) == (create.UnregisteredClientRequestId is null))
        {
            throw new ArgumentException("Exactly one of RequestId or UnregisteredClientRequestId must be set.", nameof(create));
        }

        var row = new Row(Guid.NewGuid(), create, timeProvider.GetUtcNow()) { Status = "pending" };
        _rows[row.UpstreamRequestId] = row;
        return Task.FromResult(row.ToModel());
    }

    public Task<UpstreamLoginTransaction?> GetForCallbackByStateAsync(string upstreamState, CancellationToken cancellationToken = default)
        => Task.FromResult(_rows.Values.FirstOrDefault(r => r.Create.State == upstreamState)?.ToModel());

    public Task<int> SetCallbackSuccessAsync(Guid upstreamRequestId, string authCode, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
        => Update(upstreamRequestId, r => r.Status == "pending" && r.AuthCode is null, r =>
        {
            r.AuthCode = authCode;
            r.AuthCodeReceivedAt = receivedAt;
        });

    public Task<int> SetCallbackErrorAsync(Guid upstreamRequestId, string error, string? errorDescription, DateTimeOffset receivedAt, CancellationToken cancellationToken = default)
        => Update(upstreamRequestId, r => r.Status == "pending", r =>
        {
            r.Error = error;
            r.ErrorDescription = errorDescription;
            r.Status = "error";
            r.CompletedAt = receivedAt;
        });

    public Task<int> SetTokenExchangedAsync(Guid upstreamRequestId, string issuer, string sub, string? acr, DateTimeOffset? authTime, string? idTokenJti, string? sessionSid, DateTimeOffset exchangedAt, CancellationToken cancellationToken = default)
        => Update(upstreamRequestId, _ => true, r =>
        {
            r.UpstreamIssuer = issuer;
            r.UpstreamSub = sub;
            r.UpstreamAcr = acr;
            r.UpstreamAuthTime = authTime;
            r.UpstreamIdTokenJti = idTokenJti;
            r.UpstreamSessionSid = sessionSid;
            r.TokenExchangedAt = exchangedAt;
        });

    public Task<int> MarkTokenExchangedAsync(Guid upstreamRequestId, string issuer, string sub, string? acr, DateTimeOffset? authTime, string? idTokenJti, string? upstreamSid, CancellationToken cancellationToken = default)
        => SetTokenExchangedAsync(upstreamRequestId, issuer, sub, acr, authTime, idTokenJti, upstreamSid, timeProvider.GetUtcNow(), cancellationToken);

    public Task<int> MarkCompletedAsync(Guid upstreamRequestId, bool success, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
        => Update(upstreamRequestId, _ => true, r =>
        {
            r.Status = success ? "completed" : "failed";
            r.CompletedAt = completedAt;
        });

    private Task<int> Update(Guid id, Func<Row, bool> when, Action<Row> apply)
    {
        if (!_rows.TryGetValue(id, out var row))
        {
            return Task.FromResult(0);
        }

        lock (row)
        {
            if (!when(row))
            {
                return Task.FromResult(0);
            }

            apply(row);
            return Task.FromResult(1);
        }
    }

    private sealed class Row(Guid upstreamRequestId, UpstreamLoginTransactionCreate create, DateTimeOffset createdAt)
    {
        public Guid UpstreamRequestId { get; } = upstreamRequestId;

        public UpstreamLoginTransactionCreate Create { get; } = create;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public string Status { get; set; } = "pending";

        public DateTimeOffset? CompletedAt { get; set; }

        public string? AuthCode { get; set; }

        public DateTimeOffset? AuthCodeReceivedAt { get; set; }

        public string? Error { get; set; }

        public string? ErrorDescription { get; set; }

        public DateTimeOffset? TokenExchangedAt { get; set; }

        public string? UpstreamIssuer { get; set; }

        public string? UpstreamSub { get; set; }

        public string? UpstreamAcr { get; set; }

        public DateTimeOffset? UpstreamAuthTime { get; set; }

        public string? UpstreamIdTokenJti { get; set; }

        public string? UpstreamSessionSid { get; set; }

        public UpstreamLoginTransaction ToModel() => new()
        {
            UpstreamRequestId = UpstreamRequestId,
            RequestId = Create.RequestId,
            UnregisteredClientRequestId = Create.UnregisteredClientRequestId,
            Status = Status,
            CreatedAt = CreatedAt,
            ExpiresAt = Create.ExpiresAt,
            CompletedAt = CompletedAt,
            Provider = Create.Provider,
            UpstreamClientId = Create.UpstreamClientId,
            UpstreamRedirectUri = Create.UpstreamRedirectUri,
            State = Create.State,
            Nonce = Create.Nonce,
            Scopes = Create.Scopes,
            AcrValues = Create.AcrValues,
            Prompts = Create.Prompts,
            UiLocales = Create.UiLocales,
            MaxAge = Create.MaxAge,
            CodeVerifier = Create.CodeVerifier,
            CodeChallenge = Create.CodeChallenge,
            CodeChallengeMethod = Create.CodeChallengeMethod,
            AuthCode = AuthCode,
            AuthCodeReceivedAt = AuthCodeReceivedAt,
            Error = Error,
            ErrorDescription = ErrorDescription,
            TokenExchangedAt = TokenExchangedAt,
            UpstreamIssuer = UpstreamIssuer,
            UpstreamSub = UpstreamSub,
            UpstreamAcr = UpstreamAcr,
            UpstreamAuthTime = UpstreamAuthTime,
            UpstreamIdTokenJti = UpstreamIdTokenJti,
            UpstreamSessionSid = UpstreamSessionSid,
            CorrelationId = Create.CorrelationId,
            CreatedByIp = Create.CreatedByIp,
            UserAgentHash = Create.UserAgentHash,
        };
    }
}
