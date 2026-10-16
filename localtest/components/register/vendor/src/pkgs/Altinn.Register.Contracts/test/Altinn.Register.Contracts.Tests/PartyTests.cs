using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts.Tests;

public abstract class PartyTests
    : JsonModelTests
{
    protected static Guid Uuid { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

    protected static uint PartyId { get; } = 12345678U;

    protected static DateTimeOffset CreatedAt { get; } = new(2020, 01, 02, 03, 04, 05, TimeSpan.Zero);

    protected static DateTimeOffset ModifiedAt { get; } = new(2022, 05, 06, 07, 08, 09, TimeSpan.Zero);

    protected static DateTimeOffset DeletedAt { get; } = new(2021, 03, 04, 05, 06, 07, TimeSpan.Zero);

    public static string Username { get; } = "username";

    protected static PartyUser FullUser { get; } = new PartyUser(50, Username, ImmutableValueArray.Create<uint>(50, 30, 1), ImmutableValueArray.Create(Username));

    protected static PartyRef OwnerRef { get; } = new PartyRef { Uuid = Guid.Parse("00000000-0000-0000-0000-000000000002") };

    protected static ulong VersionId { get; } = 1UL;

    protected async ValueTask ValidateParty<T>(
        T party,
        [StringSyntax(StringSyntaxAttribute.Json)] string json,
        bool snapshot = true)
        where T : Party
    {
        await ValidateJson<T>(party, json);
        await ValidateJson<Party>(party, json, snapshot: false);
    }
}
