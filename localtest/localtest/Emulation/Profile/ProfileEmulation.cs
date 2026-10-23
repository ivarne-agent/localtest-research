namespace Localtest.Emulated;

/// <summary>
/// Profile in minimal mode, under <c>profile/api/v1/</c>: <c>UserProfile</c> per test user by user id, by national
/// identity number (<c>POST users</c> with the number as a JSON string) and by UUID, with the embedded party (app-lib
/// throws without it) and the preferences from the test data. No settings writes, favourites or contact points.
/// </summary>
public static class ProfileEmulation
{
    public const string Service = "profile";

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapGet("/users/{userId:int}", (int userId, TestDataStore store) =>
            Answer(store.Current, store.Current.UsersByUserId.GetValueOrDefault(userId)));

        v1.MapGet("/users/byuuid/{userUuid:guid}", (Guid userUuid, TestDataStore store) =>
            Answer(store.Current, store.Current.ByUuid.GetValueOrDefault(userUuid) is { } party ? store.Current.UsersByPartyId.GetValueOrDefault(party.PartyId) : null));

        v1.MapPost("/users", async (HttpRequest request, TestDataStore store) =>
        {
            var ssn = await EmulationJson.Read(request, EmulationJsonContext.Default.String);
            return Answer(store.Current, string.IsNullOrEmpty(ssn) ? null : store.Current.UserBySsn(ssn));
        });
    }

    private static IResult Answer(TestDataSnapshot data, TestDataUser? user)
        => user is null ? Results.NotFound() : Results.Json(ToProfile(data, user), EmulationJsonContext.Default.UserProfile);

    public static UserProfile ToProfile(TestDataSnapshot data, TestDataUser user)
    {
        var p = user.Person;
        return new UserProfile(
            user.UserId,
            user.Party.PartyUuid,
            p.UserName ?? string.Empty,
            string.Empty,
            p.Reserved ?? false,
            p.MobileNumber ?? p.TelephoneNumber,
            user.Party.Kind == PartyKind.SelfIdentified ? user.Party.Name : p.Email,
            user.Party.PartyId,
            RegisterModels.ToParty(data, user.Party),
            user.Party.Kind == PartyKind.SelfIdentified ? 2 : 1,
            new ProfileSettingPreference(p.Language ?? "nb", p.PreSelectedPartyId ?? 0, p.DoNotPromptForParty ?? false));
    }
}

/// <summary>Profile's <c>UserProfile</c> (camelCase; <c>userType</c> 1 SSN-identified, 2 self-identified).</summary>
public sealed record UserProfile(
    int UserId,
    Guid UserUuid,
    string UserName,
    string ExternalIdentity,
    bool IsReserved,
    string? PhoneNumber,
    string? Email,
    int PartyId,
    Party Party,
    int UserType,
    ProfileSettingPreference ProfileSettingPreference);

public sealed record ProfileSettingPreference(string Language, int PreSelectedPartyId, bool DoNotPromptForParty);
