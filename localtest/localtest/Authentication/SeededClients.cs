namespace Localtest.Authentication;

/// <summary>
/// The OIDC clients production Authentication is seeded with (<c>components/authentication/fakes/LocaltestOidcClients.cs</c>,
/// <c>Localtest:Authentication:OidcClients</c>): the id, the plain secret and the registered redirect URI the code-flow
/// helper needs to act as the client. Keep the two in step.
/// </summary>
public static class SeededClients
{
    public const string SmokeClientId = "localtest-smoke";

    /// <summary>The four scopes both clients may request, as Arbeidsflate's BFF requests them.</summary>
    public const string DefaultScope = "openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:correspondence.forward.check";

    public sealed record Client(string ClientId, string Secret, string RedirectUri);

    public static Client[] All(LocaltestSettings localtest) =>
    [
        // The helper's own callback: the helper reads the code from the redirect without following it, and a browser
        // that does follow it sees the code and state (AuthenticationApi's callback).
        new(SmokeClientId, "localtest-smoke-secret", localtest.PlatformEndpoint + AuthenticationApi.CallbackPath.TrimStart('/')),
        new("arbeidsflate-localtest", "arbeidsflate-localtest-secret", localtest.OriginOf(LocaltestSettings.ArbeidsflateSubdomain) + "/api/cb"),
    ];

    public static Client? Find(string clientId, LocaltestSettings localtest)
        => All(localtest).FirstOrDefault(c => c.ClientId == clientId);
}
