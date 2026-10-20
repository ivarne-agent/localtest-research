#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Core.Helpers;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Persistence;

/// <summary>
/// In-memory stand-in for the <c>oidcserver.client</c> table, seeded from <c>Localtest:Authentication:OidcClients</c>.
/// Production inserts client rows by operations; here each configured client's plain secret is hashed with production's
/// <see cref="ClientSecretHasher.HashHmac"/> and the decoded <c>GeneralSettings:OidcRefreshTokenPepper</c>, the format
/// production's verifier expects, so the legacy-hash upgrade never fires for a seeded client.
/// </summary>
public sealed class InMemoryOidcServerClientRepository : IOidcServerClientRepository
{
    private readonly Dictionary<string, OidcClient> _clients = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryOidcServerClientRepository(IOptions<LocaltestAuthenticationOptions> options, IOptions<GeneralSettings> generalSettings, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        byte[] pepper;
        try
        {
            pepper = Convert.FromBase64String(generalSettings.Value.OidcRefreshTokenPepper);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("GeneralSettings:OidcRefreshTokenPepper must be base64, as production decodes it.", ex);
        }

        foreach (var seed in options.Value.OidcClients)
        {
            if (string.IsNullOrWhiteSpace(seed.ClientId))
            {
                throw new InvalidOperationException("Every Localtest:Authentication:OidcClients entry needs a ClientId.");
            }

            var client = new OidcClient(
                clientId: seed.ClientId,
                clientName: string.IsNullOrWhiteSpace(seed.ClientName) ? seed.ClientId : seed.ClientName,
                enabled: true,
                clientType: seed.ClientType,
                tokenEndpointAuthMethod: seed.TokenEndpointAuthMethod,
                redirectUris: seed.RedirectUris.Select(u => new Uri(u, UriKind.Absolute)),
                allowedScopes: seed.AllowedScopes,
                requirePkce: seed.RequirePkce,
                allowedCodeChallengeMethods: ["S256"],
                requireNonce: seed.RequireNonce,
                subjectType: seed.SubjectType,
                clientSecretHash: string.IsNullOrEmpty(seed.ClientSecret) ? null : ClientSecretHasher.HashHmac(seed.ClientSecret, pepper),
                postLogoutRedirectUris: seed.PostLogoutRedirectUris.Select(u => new Uri(u, UriKind.Absolute)),
                backchannelLogoutUri: ToUri(seed.BackchannelLogoutUri),
                frontchannelLogoutUri: ToUri(seed.FrontchannelLogoutUri),
                createdAt: timeProvider.GetUtcNow());
            _clients[client.ClientId] = client;
        }
    }

    public Task<OidcClient?> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ClientId is required.");
        }

        lock (_lock)
        {
            return Task.FromResult(_clients.TryGetValue(clientId, out var client) && client.Enabled ? client : null);
        }
    }

    public Task<OidcClient> InsertClientAsync(OidcClientCreate create, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (string.IsNullOrWhiteSpace(create.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(create));
        }

        if (string.IsNullOrWhiteSpace(create.ClientName))
        {
            throw new ArgumentException("ClientName is required.", nameof(create));
        }

        if (create.RedirectUris is not { Count: > 0 })
        {
            throw new ArgumentException("At least one redirect URI is required.", nameof(create));
        }

        if (create.AllowedScopes is not { Count: > 0 })
        {
            throw new ArgumentException("At least one allowed scope is required.", nameof(create));
        }

        // The columns production stores and reads back; the rest are its mapping's constants.
        var client = new OidcClient(
            clientId: create.ClientId,
            clientName: create.ClientName,
            enabled: true,
            clientType: create.ClientType,
            tokenEndpointAuthMethod: create.TokenEndpointAuthMethod,
            redirectUris: create.RedirectUris,
            allowedScopes: create.AllowedScopes,
            requirePkce: true,
            allowedCodeChallengeMethods: ["S256"],
            requireNonce: true,
            clientSecretHash: create.ClientSecretHash,
            clientSecretExpiresAt: create.ClientSecretExpiresAt,
            secretRotationAt: create.SecretRotationAt,
            jwksUri: create.JwksUri,
            jwksJson: create.JwksJson,
            backchannelLogoutUri: ToUri(create.BackchannelLogoutUri),
            frontchannelLogoutUri: ToUri(create.FrontchannelLogoutUri),
            createdAt: _timeProvider.GetUtcNow());
        lock (_lock)
        {
            // The table's primary key.
            if (!_clients.TryAdd(client.ClientId, client))
            {
                throw new InvalidOperationException($"Client '{client.ClientId}' already exists.");
            }
        }

        return Task.FromResult(client);
    }

    public Task<bool> TryUpgradeClientSecretHashAsync(string clientId, string expectedCurrentHash, string newClientSecretHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(expectedCurrentHash))
        {
            throw new ArgumentException("Expected current hash is required.", nameof(expectedCurrentHash));
        }

        if (string.IsNullOrWhiteSpace(newClientSecretHash))
        {
            throw new ArgumentException("New client secret hash is required.", nameof(newClientSecretHash));
        }

        lock (_lock)
        {
            if (!_clients.TryGetValue(clientId, out var current) || !string.Equals(current.ClientSecretHash, expectedCurrentHash, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _clients[clientId] = WithSecretHash(current, newClientSecretHash, _timeProvider.GetUtcNow());
            return Task.FromResult(true);
        }
    }

    private static OidcClient WithSecretHash(OidcClient c, string hash, DateTimeOffset updatedAt) => new(
        c.ClientId,
        c.ClientName,
        c.Enabled,
        c.ClientType,
        c.TokenEndpointAuthMethod,
        c.RedirectUris,
        c.AllowedScopes,
        c.RequirePkce,
        c.AllowedCodeChallengeMethods,
        c.RequireNonce,
        c.RequireConsent,
        c.RequireActorSelection,
        c.SubjectType,
        c.SectorIdentifierUri,
        c.PairwiseSalt,
        hash,
        c.ClientSecretExpiresAt,
        c.SecretRotationAt,
        c.JwksUri,
        c.JwksJson,
        c.PostLogoutRedirectUris,
        c.BackchannelLogoutUri,
        c.FrontchannelLogoutUri,
        c.AllowTestIdp,
        c.RequireParForTestIdp,
        c.CreatedAt,
        updatedAt)
    {
        AllowsRefresh = c.AllowsRefresh,
    };

    private static Uri? ToUri(string? value) => string.IsNullOrEmpty(value) ? null : new Uri(value, UriKind.RelativeOrAbsolute);
}
