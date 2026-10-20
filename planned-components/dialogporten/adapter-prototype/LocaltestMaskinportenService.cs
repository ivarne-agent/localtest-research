using System.Security.Cryptography.X509Certificates;
using Altinn.ApiClients.Maskinporten.Interfaces;
using Altinn.ApiClients.Maskinporten.Models;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>Compile-shape stand-in for the shared fake planned by the shared-prerequisites strand.</summary>
internal sealed class LocaltestMaskinportenService : IMaskinportenService
{
    public Task<TokenResponse> GetToken(JsonWebKey jwk, string environment, string clientId, string scope, string resource, string? consumerOrgNo = null, bool disableCaching = false) => throw new NotSupportedException();
    public Task<TokenResponse> GetToken(X509Certificate2 cert, string environment, string clientId, string scope, string resource, string? consumerOrgNo = null, bool disableCaching = false) => throw new NotSupportedException();
    public Task<TokenResponse> GetToken(string base64EncodedJWK, string environment, string clientId, string scope, string resource, string? consumerOrgNo = null, bool disableCaching = false) => throw new NotSupportedException();
    public Task<TokenResponse> GetToken(IClientDefinition clientDefinition, bool disableCaching = false) => GetToken(clientDefinition, null!, disableCaching);
    public Task<TokenResponse> GetToken(IClientDefinition clientDefinition, MaskinportenRequestContext requestContext, bool disableCaching = false) => throw new NotImplementedException("POST {localtest}/localtest/api/maskinporten/token, then exchange/maskinporten");
    public Task<TokenResponse> ExchangeToAltinnToken(TokenResponse tokenResponse, string environment, string? userName = null, string? password = null, bool disableCaching = false, bool isTestOrg = false) => throw new NotSupportedException();
}
