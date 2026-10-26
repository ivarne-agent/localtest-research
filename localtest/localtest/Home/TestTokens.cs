using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Proxy;
using Localtest.Tokens;

namespace Localtest.Home;

/// <summary>
/// The tokens behind <c>/Home/GetTestUserToken</c> and <c>/Home/GetTestOrgToken</c>, for the endpoints themselves and
/// for the instances page (<c>Home/Instances/</c>), which calls Storage with them. A user token comes from production
/// Authentication's sign-in against the fake ID-porten, an org token from its production <c>exchange/maskinporten</c>;
/// nothing is minted as an Altinn token here.
/// </summary>
public sealed class TestTokens(
    TestUsers testUsers,
    AuthenticationClient authentication,
    [FromKeyedServices(MaskinportenApi.KeyName)] SigningKey maskinportenKey,
    MaskinportenSettings maskinporten)
{
    /// <summary>Digdir, which production <c>exchange/maskinporten?test=true</c> turns into <c>ttd</c>.</summary>
    public const string DefaultOrgNumber = "991825827";

    public const string DefaultServiceOwnerScopes = "altinn:serviceowner/instances.read altinn:serviceowner/instances.write";

    /// <summary>Why a token could not be had: the status to answer with and the message.</summary>
    public sealed record Failure(int Status, string Message);

    /// <summary>A signed-in user: the JWT (the user's own party, as production's) and the browser cookies on the selected party.</summary>
    public sealed record UserToken(string Jwt, string[] BrowserCookies);

    /// <summary>
    /// Signs <paramref name="userId"/> in at <paramref name="level"/> (default 2). <paramref name="partyId"/>, default the
    /// user's own, must be a party the user can act for; it is the party of the browser cookies.
    /// </summary>
    public async Task<(UserToken? Token, Failure? Failure)> ForUser(int userId, int? partyId, int? level, CancellationToken cancellationToken)
    {
        if (AuthLevels.ForNumber(level ?? AuthLevels.Default) is not { } authLevel)
        {
            return (null, new Failure(StatusCodes.Status400BadRequest, "authenticationLevel must be 0, 1, 2, 3 or 4."));
        }

        TestUser[] users;
        try
        {
            users = await testUsers.GetAll(cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return (null, new Failure(StatusCodes.Status502BadGateway, $"Could not read the test users from {testUsers.Endpoint}: {ex.Message}. Is the register component running?"));
        }

        var user = users.FirstOrDefault(u => u.UserId == userId);
        if (user is null)
        {
            return (null, new Failure(StatusCodes.Status404NotFound, $"No test user with userId {userId}."));
        }

        if (user.FindParty(partyId ?? user.PartyId) is not { } party)
        {
            return (null, new Failure(StatusCodes.Status400BadRequest, $"Test user {userId} cannot act for party {partyId}."));
        }

        AuthenticationClient.SignInResult? result;
        string? error;
        try
        {
            (result, error) = await authentication.SignIn(user, authLevel.Acr, cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            (result, error) = (null, $"Could not reach Authentication: {ex.Message}. Is the authentication component running?");
        }

        return result is null
            ? (null, new Failure(StatusCodes.Status502BadGateway, error ?? "The sign-in failed."))
            : (new UserToken(result.Jwt, [.. AuthenticationClient.ForBrowser(result.SetCookies, party.PartyId, party.PartyUuid)]), null);
    }

    /// <summary>
    /// A fake Maskinporten token for <paramref name="orgNumber"/> (default Digdir) and <paramref name="scopes"/> (default
    /// the service-owner instance scopes), exchanged through production <c>exchange/maskinporten?test=true</c>.
    /// </summary>
    public async Task<(string? Token, Failure? Failure)> ForOrg(string? orgNumber, string? scopes, CancellationToken cancellationToken)
    {
        var token = MaskinportenApi.Mint(
            maskinportenKey,
            maskinporten,
            string.IsNullOrWhiteSpace(orgNumber) ? DefaultOrgNumber : orgNumber.Trim(),
            string.IsNullOrWhiteSpace(scopes) ? DefaultServiceOwnerScopes : scopes.Trim(),
            MaskinportenApi.LocaltestClientId);
        try
        {
            var (exchanged, error) = await authentication.Exchange("maskinporten", token, cancellationToken);
            return exchanged is null ? (null, new Failure(StatusCodes.Status502BadGateway, error ?? "The exchange failed.")) : (exchanged, null);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return (null, new Failure(StatusCodes.Status502BadGateway, $"Could not reach Authentication for exchange/maskinporten: {ex.Message}. Is the authentication component running?"));
        }
    }
}
