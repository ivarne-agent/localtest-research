using System.Security.Cryptography.X509Certificates;

namespace Altinn.Register.IntegrationTests.TestServices;

internal class TestPublicSigningKeyProvider
    : X509CertificateBasedSigningKeyProvider
{
    private readonly TestCertificateService _certificatesService;

    public TestPublicSigningKeyProvider(TestCertificateService certificatesService)
    {
        _certificatesService = certificatesService;
    }

    protected override Task<X509Certificate2?> GetCertificate(string issuer, CancellationToken cancellationToken)
        => Task.FromResult<X509Certificate2?>(_certificatesService.GetIssuerCertificate(issuer));
}
