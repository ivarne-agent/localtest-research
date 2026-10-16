using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.IntegrationTests.TestServices;

internal class TestCertificateService
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _certs = new();

    public X509Certificate2 GetIssuerCertificate(string issuer)
        => _certs.GetOrAdd(issuer, CreateCertificate);

    public SigningCredentials GetSigningCredentials(string issuer)
    {
        var cert = GetIssuerCertificate(issuer);

        return new X509SigningCredentials(cert, SecurityAlgorithms.RsaSha256);
    }

    private static X509Certificate2 CreateCertificate(string issuer)
    {
        using var rsa = RSA.Create(2048);
        var distinguishedName = new X500DistinguishedName($"CN={issuer}");
        var req = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
                false));

        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
               new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        return req.CreateSelfSigned(
            notBefore: DateTimeOffset.UtcNow.AddDays(-1),
            notAfter: DateTimeOffset.UtcNow.AddDays(1));
    }
}
