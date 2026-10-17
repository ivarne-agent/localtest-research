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

/// <summary>
/// Replaces the production <see cref="AccessTokenHandler"/> (certificates from Key Vault) for incoming platform access
/// tokens: a present <c>PlatformAccessToken</c> header is enough locally.
/// </summary>
public sealed class LocaltestAccessTokenHandler(IHttpContextAccessor httpContextAccessor, IOptions<AccessTokenSettings> settings)
    : AuthorizationHandler<IAccessTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, IAccessTokenRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext ?? httpContextAccessor.HttpContext;
        var header = string.IsNullOrEmpty(settings.Value.AccessTokenHeaderId) ? "PlatformAccessToken" : settings.Value.AccessTokenHeaderId;
        if (httpContext is not null && httpContext.Request.Headers.ContainsKey(header))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
