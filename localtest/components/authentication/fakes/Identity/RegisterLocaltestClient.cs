#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Identity;

/// <summary>Reads the seeded test users from the register component's localtest endpoint.</summary>
public sealed class RegisterLocaltestClient(HttpClient httpClient, IOptions<LocaltestAuthenticationOptions> options, TimeProvider timeProvider)
{
    private static readonly TimeSpan _cacheFor = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<TestUser>? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<IReadOnlyList<TestUser>> GetTestUsers(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (_cached is not null && now - _cachedAt < _cacheFor)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && now - _cachedAt < _cacheFor)
            {
                return _cached;
            }

            var baseUrl = options.Value.RegisterLocaltestEndpoint.TrimEnd('/') + "/";
            var users = await httpClient.GetFromJsonAsync<List<TestUser>>(new Uri(new Uri(baseUrl), "test-users"), cancellationToken) ?? [];
            _cached = users;
            _cachedAt = now;
            return users;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// A seeded test user. Read defensively: unknown fields are ignored and missing lists are empty, because the
    /// register component's DTO grows (profile preferences, a full party list).
    /// Persons sign in by <c>Ssn</c>; self-identified users have none and sign in by <c>UserId</c>. <c>Parties</c> are the
    /// other parties the user may act for (organisations, and persons through delegations).
    /// </summary>
    public sealed record TestUser(int UserId, int PartyId, Guid PartyUuid, string? Ssn, string DisplayName, string? UserName, string? Email, string? Language, IReadOnlyList<TestUserParty>? Parties)
    {
        public IReadOnlyList<TestUserParty> OtherParties => Parties?.Where(p => p.PartyId != PartyId).ToList() ?? [];

        /// <summary>The selected party's id and uuid: the user's own party or one of <see cref="Parties"/>.</summary>
        public (int PartyId, Guid PartyUuid)? FindParty(int partyId)
            => partyId == PartyId
                ? (PartyId, PartyUuid)
                : OtherParties.FirstOrDefault(p => p.PartyId == partyId) is { } party ? (party.PartyId, party.PartyUuid) : null;
    }

    public sealed record TestUserParty(int PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, IReadOnlyList<string>? Roles, IReadOnlyList<string>? AccessPackages);
}
