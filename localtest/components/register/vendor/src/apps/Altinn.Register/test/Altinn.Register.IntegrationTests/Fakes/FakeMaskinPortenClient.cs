using Altinn.Authorization.ServiceDefaults.HttpClient.MaskinPorten;

namespace Altinn.Register.IntegrationTests.Fakes;

internal sealed class FakeMaskinPortenClient(TimeProvider timeProvider)
    : IMaskinPortenClient
{
    public Task<MaskinPortenToken> GetAccessToken(string clientName, CancellationToken cancellationToken = default)
        => Task.FromResult(MaskinPortenToken.Create(
            clientName: clientName,
            clientId: clientName,
            scope: $"scope/{clientName}",
            resource: null,
            consumerOrg: null,
            accessToken: $"token-{clientName}",
            validTo: timeProvider.GetUtcNow() + TimeSpan.FromMinutes(2)));
}
