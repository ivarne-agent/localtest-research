using Altinn.Common.AccessToken.Services;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Register.Localtest.Auth;

/// <summary>
/// Stands in for the Key Vault backed provider. Platform access tokens are not signature-checked locally
/// (see <see cref="LocaltestAccessTokenHandler"/>), so no keys are ever needed.
/// </summary>
public sealed class LocaltestPublicSigningKeyProvider : IPublicSigningKeyProvider
{
    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
        => Task.FromResult<IEnumerable<SecurityKey>>([]);
}
