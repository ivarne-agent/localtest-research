using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Localtest.Auth;

/// <summary>
/// Replaces the production <c>AccessTokenHandler</c>, which validates the <c>PlatformAccessToken</c> header
/// against certificates in Key Vault. Locally a present header is enough (or nothing at all, when configured).
/// </summary>
public sealed class LocaltestAccessTokenHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AccessTokenSettings> accessTokenSettings,
    IOptions<LocaltestRegisterOptions> options)
    : AuthorizationHandler<IAccessTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, IAccessTokenRequirement requirement)
    {
        if (!options.Value.RequirePlatformAccessTokenHeader)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = context.Resource as HttpContext ?? httpContextAccessor.HttpContext;
        var headerName = accessTokenSettings.Value.AccessTokenHeaderId;
        if (httpContext is not null && !string.IsNullOrEmpty(headerName) && httpContext.Request.Headers.ContainsKey(headerName))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
