using System.Security.Claims;
using Altinn.Common.AccessToken.Constants;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.IntegrationTests.TestServices;
using AltinnCore.Authentication.Constants;

namespace Altinn.Register.IntegrationTests.Utils;

/// <summary>
/// Utility class for authorization related operations in integration tests.
/// </summary>
public static class HttpRequestUtils
{
    private const string ISSUER = TestJwtService.ISSUER;

    private static readonly HttpRequestOptionsKey<AuthorizationTokenInfo> AuthorizationTokenInfoKey
        = new($"{nameof(HttpRequestUtils)}.{nameof(AuthorizationTokenInfoKey)}");

    private static readonly HttpRequestOptionsKey<PlatformTokenInfo> PlatformTokenInfoKey
        = new($"{nameof(HttpRequestUtils)}.{nameof(PlatformTokenInfoKey)}");

    extension(HttpRequestMessage request)
    {
        /// <summary>
        /// Attaches a person bearer token to the request.
        /// </summary>
        /// <param name="person">The authenticated person.</param>
        /// <param name="authenticationLevel">The authentication level claim to emit.</param>
        /// <param name="scope">An optional scope claim.</param>
        /// <returns>The request.</returns>
        public HttpRequestMessage WithPersonToken(PersonRecord person, int authenticationLevel = 4, string? scope = null)
        {
            var tokenInfo = new PersonTokenInfo(person, authenticationLevel, scope);
            request.Options.Set(AuthorizationTokenInfoKey, tokenInfo);

            return request;
        }

        /// <summary>
        /// Attaches an organization bearer token to the request.
        /// </summary>
        /// <param name="organization">The authenticated organization identifier.</param>
        /// <param name="orgCode">The optional organization code claim.</param>
        /// <param name="authenticationLevel">The authentication level claim to emit.</param>
        /// <param name="scope">An optional scope claim.</param>
        /// <returns>The request.</returns>
        public HttpRequestMessage WithOrganizationToken(OrganizationIdentifier organization, string? orgCode, int authenticationLevel = 4, string? scope = null)
        {
            var tokenInfo = new OrganizationTokenInfo(organization, orgCode, authenticationLevel, scope);
            request.Options.Set(AuthorizationTokenInfoKey, tokenInfo);

            return request;
        }

        /// <summary>
        /// Attaches a platform access token to the request.
        /// </summary>
        /// <param name="app">The app claim value to emit.</param>
        /// <returns>The request.</returns>
        public HttpRequestMessage WithPlatformToken(string app = "unittest")
        {
            var tokenInfo = new PlatformTokenInfo(app);
            request.Options.Set(PlatformTokenInfoKey, tokenInfo);

            return request;
        }

        /// <summary>
        /// Adds a request header.
        /// </summary>
        /// <param name="name">The header name.</param>
        /// <param name="value">The header value.</param>
        /// <returns>The request.</returns>
        public HttpRequestMessage WithHeader(string name, string value)
        {
            request.Headers.Add(name, value);
            return request;
        }
    }

    internal static void ApplyIntegrationTestAuthorization(HttpRequestMessage message, TestJwtService jwtService)
    {
        if (message.Options.TryGetValue(AuthorizationTokenInfoKey, out var authTokenInfo))
        {
            var token = authTokenInfo.ToJwt(jwtService);
            message.Headers.Authorization = new("Bearer", token);
        }

        if (message.Options.TryGetValue(PlatformTokenInfoKey, out var platformTokenInfo))
        {
            var token = platformTokenInfo.ToJwt(jwtService);
            message.Headers.Add("PlatformAccessToken", token);
        }

        if (message.Headers.Authorization is null)
        {
            var token = jwtService.GenerateToken();
            message.Headers.Authorization = new("Bearer", token);
        }
    }

    private sealed class PlatformTokenInfo(string app)
    {
        private IEnumerable<Claim> GetClaims()
        {
            yield return new Claim(AccessTokenClaimTypes.App, app, ClaimValueTypes.String, ISSUER);
        }

        public string ToJwt(TestJwtService jwtService)
        {
            var identity = new ClaimsIdentity(GetClaims());
            return jwtService.GenerateToken(identity);
        }
    }

    private abstract class AuthorizationTokenInfo(int authenticationLevel, string? scope)
    {
        protected virtual IEnumerable<Claim> GetClaims()
        {
            yield return new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "integration-test", ClaimValueTypes.String, ISSUER);
            yield return new Claim(AltinnCoreClaimTypes.AuthenticationLevel, authenticationLevel.ToString(), ClaimValueTypes.Integer32, ISSUER);

            if (!string.IsNullOrWhiteSpace(scope))
            {
                yield return new Claim("scope", scope, ClaimValueTypes.String, ISSUER);
            }
        }

        public string ToJwt(TestJwtService jwtService)
        {
            var identity = new ClaimsIdentity(GetClaims());
            return jwtService.GenerateToken(identity);
        }
    }

    private sealed class PersonTokenInfo(PersonRecord person, int authenticationLevel, string? scope)
        : AuthorizationTokenInfo(authenticationLevel, scope)
    {
        protected override IEnumerable<Claim> GetClaims()
        {
            if (person.UserIds.CurrentValue is { HasValue: true, Value: var userId })
            {
                yield return new Claim(AltinnCoreClaimTypes.UserId, userId.ToString(), ClaimValueTypes.Integer32, ISSUER);
            }

            if (person.Usernames.CurrentValue is { HasValue: true, Value: var username })
            {
                yield return new Claim(AltinnCoreClaimTypes.UserName, username, ClaimValueTypes.String, ISSUER);
            }

            yield return new Claim(AltinnCoreClaimTypes.PartyID, person.PartyId.Value.ToString(), ClaimValueTypes.Integer32, ISSUER);
            yield return new Claim(AltinnCoreClaimTypes.PartyUUID, person.PartyUuid.Value.ToString("D"), ClaimValueTypes.String, ISSUER);

            foreach (var claim in base.GetClaims())
            {
                yield return claim;
            }
        }
    }

    private sealed class OrganizationTokenInfo(OrganizationIdentifier organization, string? orgCode, int authenticationLevel, string? scope)
        : AuthorizationTokenInfo(authenticationLevel, scope)
    {
        protected override IEnumerable<Claim> GetClaims()
        {
            if (!string.IsNullOrWhiteSpace(orgCode))
            {
                yield return new Claim(AltinnCoreClaimTypes.Org, orgCode, ClaimValueTypes.String, ISSUER);
                yield return new Claim("urn:altinn:orgCode", orgCode, ClaimValueTypes.String, ISSUER);
            }

            yield return new Claim("urn:altinn:orgNumber", organization.ToString(), ClaimValueTypes.String, ISSUER);

            foreach (var claim in base.GetClaims())
            {
                yield return claim;
            }
        }
    }
}
