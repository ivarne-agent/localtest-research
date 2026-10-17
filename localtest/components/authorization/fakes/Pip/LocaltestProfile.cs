#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Localtest.Register;
using Altinn.Platform.Authorization.Services.Interface;
using Altinn.Platform.Profile.Models;
using Altinn.Platform.Register.Enums;
using Altinn.Platform.Register.Models;

namespace Altinn.Platform.Authorization.Localtest.Pip;

/// <summary>Profile PIP (<c>internal/user</c> in production) over the register seed: user id or ssn to the user's party.</summary>
public sealed class LocaltestProfile(RegisterLocaltestClient register) : IProfile
{
    public async Task<UserProfile?> GetUserProfile(int userId, CancellationToken cancellationToken = default)
        => ToProfile((await register.Get(cancellationToken)).UserById(userId));

    public async Task<UserProfile?> GetUserProfileByPersonId(string personId, CancellationToken cancellationToken = default)
        => ToProfile((await register.Get(cancellationToken)).UserBySsn(personId));

    private static UserProfile? ToProfile(RegisterLocaltestClient.TestUser? user)
        => user is null ? null : new UserProfile
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Email = user.Email,
            PartyId = user.PartyId,
            Party = new Party
            {
                PartyId = user.PartyId,
                PartyUuid = user.PartyUuid,
                PartyTypeName = PartyType.Person,
                SSN = user.Ssn,
                Name = user.DisplayName,
            },
        };
}
