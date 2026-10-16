using CommunityToolkit.Diagnostics;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.IntegrationTests.TestServices;

internal class TestOpenIdConnectConfigurationManager
    : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly TestJwtService _jwt;

    public TestOpenIdConnectConfigurationManager(TestJwtService certs)
    {
        _jwt = certs;
    }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        var config = new OpenIdConnectConfiguration();
        var cert = _jwt.GetCertificate();
        config.SigningKeys.Add(new X509SecurityKey(cert));

        return Task.FromResult(config);
    }

    public void RequestRefresh()
    {
        ThrowHelper.ThrowNotSupportedException();
    }
}
