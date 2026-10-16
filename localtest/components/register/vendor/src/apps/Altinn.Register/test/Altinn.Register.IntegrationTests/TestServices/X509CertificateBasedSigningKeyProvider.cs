using System.Security.Cryptography.X509Certificates;
using Altinn.Common.AccessToken.Services;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.IntegrationTests.TestServices;

/// <summary>
/// Base class for <see cref="IPublicSigningKeyProvider"/> implementations that use X509 certificates.
/// </summary>
internal abstract class X509CertificateBasedSigningKeyProvider
    : IPublicSigningKeyProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="X509CertificateBasedSigningKeyProvider"/> class.
    /// </summary>
    protected X509CertificateBasedSigningKeyProvider()
    {
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
    {
        X509Certificate2? cert = await GetCertificate(issuer, TestContext.Current.CancellationToken);
        if (cert is null)
        {
            return [];
        }

        SecurityKey key = new X509SecurityKey(cert);
        return [key];
    }

    /// <summary>
    /// Get the public key of the given issuer from a key vault and cache it for a configurable time.
    /// </summary>
    /// <param name="issuer">The token issuer</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>Returns the issuer public key as a x509 certificate object.</returns>
    protected abstract Task<X509Certificate2?> GetCertificate(string issuer, CancellationToken cancellationToken);
}
