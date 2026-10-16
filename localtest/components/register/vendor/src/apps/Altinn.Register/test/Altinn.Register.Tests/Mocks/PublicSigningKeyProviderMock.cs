using System.Security.Cryptography.X509Certificates;
using Altinn.Common.AccessToken.Services;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.Tests.Mocks;

public class PublicSigningKeyProviderMock : IPublicSigningKeyProvider
{
    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
    {
        List<SecurityKey> signingKeys = new List<SecurityKey>();

        X509Certificate2 cert = X509CertificateLoader.LoadCertificateFromFile($"{issuer}-org.pem");
        SecurityKey key = new X509SecurityKey(cert);

        signingKeys.Add(key);

        return Task.FromResult(signingKeys.AsEnumerable());
    }
}
