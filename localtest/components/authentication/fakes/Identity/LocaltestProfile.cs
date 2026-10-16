#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Core.Models.Profile;
using Altinn.Platform.Authentication.Core.Models.Profile.Enums;
using Altinn.Platform.Authentication.Model;
using Altinn.Platform.Authentication.Services.Interfaces;
using Altinn.Register.Contracts.V1;

namespace Altinn.Platform.Authentication.Localtest.Identity;

/// <summary>
/// Stands in for Profile's <c>internal/user</c> lookup, which production sign-in uses to resolve the Altinn ids of
/// the authenticated person. Answers from the register component's seeded test users.
/// </summary>
public sealed class LocaltestProfile(RegisterLocaltestClient register) : IProfile
{
    public async Task<UserProfile> GetUserProfile(UserProfileLookup profileLookup)
    {
        var users = await register.GetTestUsers();
        var user = profileLookup switch
        {
            { Ssn: { Length: > 0 } ssn } => users.FirstOrDefault(u => u.Ssn == ssn),
            { UserId: > 0 } => users.FirstOrDefault(u => u.UserId == profileLookup.UserId),
            { Username: { Length: > 0 } name } => users.FirstOrDefault(u => string.Equals(u.UserName, name, System.StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };

        if (user is null)
        {
            return null!;
        }

        return new UserProfile
        {
            UserId = user.UserId,
            UserUuid = user.PartyUuid,
            UserName = user.UserName,
            Email = user.Email,
            PartyId = user.PartyId,
            UserType = UserType.SSNIdentified,
            Party = new Party
            {
                PartyId = user.PartyId,
                PartyUuid = user.PartyUuid,
                PartyTypeName = PartyType.Person,
                SSN = user.Ssn,
                Name = user.DisplayName,
                Person = new Person { SSN = user.Ssn, Name = user.DisplayName },
            },
            ProfileSettingPreference = new ProfileSettingPreference { Language = user.Language ?? "nb" },
        };
    }
}
