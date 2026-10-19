namespace Localtest;

/// <summary>
/// Settings several features share. <see cref="PlatformEndpoint"/> is the public origin of the stack (what the browser
/// and the apps use), with a trailing slash: <c>Localtest:PlatformEndpoint</c>, default <c>http://local.altinn.cloud:8000/</c>.
/// <see cref="AccessTokenCertificatePath"/> is the shared test certificate every app and component signs platform access
/// tokens with (<c>Localtest:AccessTokenCertificatePath</c>, default <c>/certs/localtest-accesstoken.pfx</c>).
/// </summary>
public sealed record LocaltestSettings(string PlatformEndpoint, string AccessTokenCertificatePath)
{
    public static LocaltestSettings FromConfiguration(IConfiguration configuration)
        => new(
            (configuration["Localtest:PlatformEndpoint"] is { Length: > 0 } platform ? platform : "http://local.altinn.cloud:8000/").TrimEnd('/') + "/",
            configuration["Localtest:AccessTokenCertificatePath"] is { Length: > 0 } path ? path : "/certs/localtest-accesstoken.pfx");

    /// <summary>The public host and port, for the <c>Host</c> header of calls the localtest app makes to a component.</summary>
    public string PublicHost => new Uri(PlatformEndpoint).Authority;
}
