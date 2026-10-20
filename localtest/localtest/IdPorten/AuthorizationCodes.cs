using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Localtest.IdPorten;

/// <summary>One-time authorization codes: issued when a user is picked, redeemed once at the token endpoint.</summary>
public sealed class AuthorizationCodes
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (AuthorizationGrant Grant, DateTimeOffset IssuedAt)> _codes = new(StringComparer.Ordinal);

    public string Issue(AuthorizationGrant grant)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _codes.Where(c => now - c.Value.IssuedAt > Lifetime).Select(c => c.Key).ToList())
        {
            _codes.TryRemove(expired, out _);
        }

        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _codes[code] = (grant, now);
        return code;
    }

    public AuthorizationGrant? Redeem(string code)
        => _codes.TryRemove(code, out var entry) && DateTimeOffset.UtcNow - entry.IssuedAt <= Lifetime ? entry.Grant : null;
}

/// <summary>What a code stands for: the picked identity and the request it answers.</summary>
public sealed record AuthorizationGrant(
    string? Ssn,
    int UserId,
    string Acr,
    string ClientId,
    string RedirectUri,
    string? Nonce,
    string? CodeChallenge);
