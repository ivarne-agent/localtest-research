using System.Security.Cryptography.X509Certificates;
using Altinn.Common.AccessTokenClient.Services;

namespace Altinn.Register.Tests.IntegrationTests.Utils;

internal class TestAccessTokenGenerator
    : IAccessTokenGenerator
{
    public string GenerateAccessToken(string issuer, string app)
        => $"{issuer}:{app}";

    public string GenerateAccessToken(string issuer, string app, X509Certificate2 certificate)
    {
        throw new NotImplementedException();
    }
}
