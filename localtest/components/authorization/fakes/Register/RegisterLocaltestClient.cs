#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authorization.Localtest.Register;

/// <summary>
/// Reads the seeded test users and organisations from the register component's localtest endpoints. This is the
/// one source of roles and access packages for the Access Management, Profile and roles fakes; cached for a few
/// seconds so a decision burst does not hit the register for every request.
/// </summary>
public sealed class RegisterLocaltestClient(HttpClient httpClient, IOptions<LocaltestAuthorizationOptions> options, TimeProvider timeProvider)
{
    private static readonly TimeSpan _cacheFor = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<Snapshot> Get(CancellationToken cancellationToken = default)
    {
        if (_cached is not null && timeProvider.GetUtcNow() - _cachedAt < _cacheFor)
        {
            return _cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && timeProvider.GetUtcNow() - _cachedAt < _cacheFor)
            {
                return _cached;
            }

            var baseUri = new Uri(options.Value.RegisterLocaltestEndpoint.TrimEnd('/') + "/");
            var users = await httpClient.GetFromJsonAsync<List<TestUser>>(new Uri(baseUri, "test-users"), cancellationToken) ?? [];
            var orgs = await httpClient.GetFromJsonAsync<List<TestOrg>>(new Uri(baseUri, "organizations"), cancellationToken) ?? [];
            _cached = new Snapshot(users, orgs);
            _cachedAt = timeProvider.GetUtcNow();
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public sealed class Snapshot(IReadOnlyList<TestUser> users, IReadOnlyList<TestOrg> orgs)
    {
        public IReadOnlyList<TestUser> Users { get; } = users;

        public IReadOnlyList<TestOrg> Orgs { get; } = orgs;

        public TestUser? UserById(int userId) => Users.FirstOrDefault(u => u.UserId == userId);

        public TestUser? UserBySsn(string ssn) => Users.FirstOrDefault(u => string.Equals(u.Ssn, ssn, StringComparison.Ordinal));

        public TestUser? UserByPartyUuid(Guid partyUuid) => Users.FirstOrDefault(u => u.PartyUuid == partyUuid);

        public IEnumerable<TestOrg> SubUnitsOf(int partyId) => Orgs.Where(o => o.ParentPartyId == partyId);
    }

    public sealed record TestUser(int UserId, int PartyId, Guid PartyUuid, string Ssn, string DisplayName, string? UserName, string? Email, string? Language, IReadOnlyList<TestUserParty> Parties);

    public sealed record TestUserParty(int PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string>? RoleCodes, IReadOnlyList<string> AccessPackages);

    public sealed record TestOrg(int PartyId, Guid PartyUuid, string OrgNumber, string DisplayName, int? ParentPartyId);
}
