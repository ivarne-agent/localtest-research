using System.Net.Http.Json;

namespace Localtest.IdPorten;

/// <summary>
/// The seeded test users, read from the register component's <c>register/api/localtest/test-users</c> by its service
/// name (<c>http://register/…</c>, resolved by service discovery), not through the front door. Cached for a few seconds, so a page render and the form post that follows see
/// the same list without hammering the register.
/// </summary>
public sealed class TestUsers(IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TestUser[]? _cached;
    private DateTimeOffset _cachedAt;

    public Uri Endpoint { get; } = new("http://register/register/api/localtest/test-users");

    public async Task<TestUser[]> GetAll(CancellationToken cancellationToken)
    {
        if (_cached is { } cached && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } again && DateTimeOffset.UtcNow - _cachedAt < CacheFor)
            {
                return again;
            }

            using var client = httpClientFactory.CreateClient();
            _cached = await client.GetFromJsonAsync(Endpoint, LocaltestJsonContext.Default.TestUserArray, cancellationToken) ?? [];
            _cachedAt = DateTimeOffset.UtcNow;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// A seeded test user, the part of the register's <c>test-users</c> entry the picker and <c>/Home</c> need. Persons sign
/// in by <c>Ssn</c>; self-identified users have none and sign in by <c>UserId</c>. <c>Parties</c> starts with the user's
/// own party, then the parties the user holds something for directly.
/// </summary>
public sealed record TestUser(int UserId, int PartyId, Guid PartyUuid, string? Ssn, string DisplayName, TestUserParty[]? Parties)
{
    public IEnumerable<TestUserParty> OtherParties => (Parties ?? []).Where(p => p.PartyId != PartyId);

    /// <summary>The user's own party, or one of the others; null when the user cannot act for it.</summary>
    public (int PartyId, Guid PartyUuid)? FindParty(int partyId)
        => partyId == PartyId
            ? (PartyId, PartyUuid)
            : OtherParties.FirstOrDefault(p => p.PartyId == partyId) is { } party ? (party.PartyId, party.PartyUuid) : null;
}

public sealed record TestUserParty(int PartyId, Guid PartyUuid, string DisplayName);
