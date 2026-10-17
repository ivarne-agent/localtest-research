#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Authorization.Localtest.Infrastructure;

/// <summary>No Key Vault: platform access tokens are not signature-checked locally (see <see cref="LocaltestAccessTokenHandler"/>).</summary>
public sealed class LocaltestPublicSigningKeyProvider : IPublicSigningKeyProvider
{
    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer) => Task.FromResult<IEnumerable<SecurityKey>>([]);
}
