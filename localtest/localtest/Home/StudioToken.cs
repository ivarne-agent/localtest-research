using System.Security.Cryptography.X509Certificates;
using Localtest.Proxy;
using Localtest.Tokens;

namespace Localtest.Home;

/// <summary>
/// Designer's credential against the platform (pushing application metadata, texts and policies): a platform access
/// token from issuer <c>studio</c> for app <c>studio.designer</c>, signed with the shared test certificate and exchanged
/// through production <c>exchange/altinnstudio</c>. The result carries <c>urn:altinn:app=studio.designer</c>, which
/// Storage's and Authorization's <c>StudioDesignerAccess</c> policies require. <c>/Home/GetTestStudioToken</c> answers
/// it, and the app provisioning uses it in-process.
/// </summary>
public sealed class StudioToken(LocaltestSettings localtest, AuthenticationClient authentication)
{
    /// <summary>The exchanged token, or the error and the status to answer it with.</summary>
    public async Task<(string? Token, string? Error, int Status)> Get(CancellationToken cancellationToken)
    {
        if (!File.Exists(localtest.AccessTokenCertificatePath))
        {
            return (null, $"The shared test certificate is not at {localtest.AccessTokenCertificatePath} (Localtest:AccessTokenCertificatePath).", StatusCodes.Status500InternalServerError);
        }

        string token;
        using (var certificate = X509CertificateLoader.LoadPkcs12FromFile(localtest.AccessTokenCertificatePath, password: null))
        using (var key = SigningKey.FromCertificate(certificate))
        {
            var now = DateTimeOffset.UtcNow;
            token = key.CreateJwt(writer =>
            {
                writer.WriteString("iss", "studio");
                writer.WriteString("urn:altinn:app", "studio.designer");
                writer.WriteNumber("nbf", now.AddSeconds(-5).ToUnixTimeSeconds());
                writer.WriteNumber("iat", now.ToUnixTimeSeconds());
                writer.WriteNumber("exp", now.AddMinutes(30).ToUnixTimeSeconds());
            });
        }

        try
        {
            var (exchanged, error) = await authentication.Exchange("altinnstudio", token, cancellationToken);
            return (exchanged, error, exchanged is null ? StatusCodes.Status502BadGateway : StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return (null, $"Could not reach Authentication for exchange/altinnstudio: {ex.Message}. Is the authentication component running?", StatusCodes.Status502BadGateway);
        }
    }
}
