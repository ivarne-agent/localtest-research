#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.Integration.Platform;
using Altinn.Common.AccessTokenClient.Services;
using Microsoft.Extensions.Options;

namespace Altinn.AccessManagement.Localtest.Infrastructure;

/// <summary>
/// Outgoing platform access tokens (to Register, Resource Registry, ...). Production fetches them from the test tool
/// or signs with a Key Vault certificate; locally the production <see cref="IAccessTokenGenerator"/> signs with the
/// shared test certificate, which the other components validate against.
/// </summary>
public sealed class LocaltestTokenGenerator(IAccessTokenGenerator generator, IOptions<AltinnIntegrationOptions> options) : ITokenGenerator
{
    public Task<string> CreateJWT(CancellationToken cancellationToken = default)
        => CreatePlatformAccessToken(cancellationToken);

    public Task<string> CreateJWT(string issuer, string app, CancellationToken cancellationToken = default)
        => CreatePlatformAccessToken(issuer, app, cancellationToken);

    public Task<string> CreatePlatformAccessToken(CancellationToken cancellationToken = default)
        => CreatePlatformAccessToken(options.Value.PlatformAccessToken.Issuer, options.Value.PlatformAccessToken.App, cancellationToken);

    public Task<string> CreatePlatformAccessToken(string issuer, string app, CancellationToken cancellationToken = default)
        => Task.FromResult(generator.GenerateAccessToken(issuer, app));
}
