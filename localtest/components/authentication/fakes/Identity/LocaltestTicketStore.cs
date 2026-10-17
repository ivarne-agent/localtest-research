#nullable enable
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Altinn.Platform.Authentication.Localtest.Identity;

/// <summary>One-time codes for picked identities, standing in for the upstream provider's authorization codes.</summary>
public sealed class LocaltestTicketStore(TimeProvider timeProvider)
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, PickedIdentity> _tickets = new(StringComparer.Ordinal);

    public string Issue(string? ssn, int? userId, string acr, string? nonce)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _tickets[code] = new PickedIdentity(ssn, userId, acr, nonce, timeProvider.GetUtcNow());
        return code;
    }

    public PickedIdentity? Redeem(string code)
    {
        if (!_tickets.TryRemove(code, out var ticket))
        {
            return null;
        }

        return timeProvider.GetUtcNow() - ticket.IssuedAt > _lifetime ? null : ticket;
    }
}

/// <summary>The identity a developer picked on the login page, handed from the picker to the fake upstream provider by a one-time code.</summary>
public sealed record PickedIdentity(string? Ssn, int? UserId, string Acr, string? Nonce, DateTimeOffset IssuedAt);
