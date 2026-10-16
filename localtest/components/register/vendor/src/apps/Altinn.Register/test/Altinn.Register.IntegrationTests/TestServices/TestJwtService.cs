using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.IntegrationTests.TestServices;

internal class TestJwtService
{
    public const string ISSUER = "jwt";

    private readonly TestCertificateService _certs;

    public TestJwtService(TestCertificateService certs)
    {
        _certs = certs;
    }

    public X509Certificate2 GetCertificate()
    {
        return _certs.GetIssuerCertificate(ISSUER);
    }

    public string GenerateToken()
    {
        // generates a admin token
        var identity = new ClaimsIdentity([
            new("scope", "altinn:register/partylookup.admin"),
        ]);

        return GenerateToken(identity);
    }

    public string GenerateToken(ClaimsIdentity identity)
        => GenerateToken(identity, TimeSpan.FromHours(1));

    public string GenerateToken(ClaimsIdentity identity, TimeSpan tokenExpiry)
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Expires = DateTimeOffset.UtcNow.Add(tokenExpiry).UtcDateTime,
            SigningCredentials = _certs.GetSigningCredentials(ISSUER),
            Audience = "altinn.no",
            Issuer = ISSUER,
        };

        var token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }
}
