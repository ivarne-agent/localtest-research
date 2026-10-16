#nullable enable
using System;
using System.Collections.Generic;
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

    public sealed record TestUser(int UserId, int PartyId, Guid PartyUuid, string Ssn, string DisplayName, string? UserName, string? Email, string? Language, IReadOnlyList<TestUserParty> Parties);

    public sealed record TestUserParty(int PartyId, Guid PartyUuid, string? OrgNumber, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string> AccessPackages);
}
