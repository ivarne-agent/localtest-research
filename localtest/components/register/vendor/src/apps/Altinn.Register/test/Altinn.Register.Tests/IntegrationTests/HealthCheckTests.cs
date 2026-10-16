using System.Net;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Tests.IntegrationTests.Utils;
using Altinn.Register.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Tests.IntegrationTests
{
    public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

        public HealthCheckTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Altinn:IsTest", "true");
                builder.UseSetting(AltinnPreStartLogger.DisableConfigKey, "true");
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IAccessTokenGenerator, TestAccessTokenGenerator>();
                    services.AddSingleton<IExternalRoleDefinitionPersistence, MockExternalRoleDefinitionPersistence>();
                    services.AddSingleton<IRateLimitProvider, MockRateLimitProvider>();
                    services.AddSingleton<ILocationLookupProvider, MockLocationLookupProvider>();
                    services.AddScoped<ICommandSender, NoOpCommandSender>();
                    services.AddScoped<ICcrUpdateFederator, NoOpCcrUpdateFederator>();
                });
            });
        }

        [Fact]
        public async Task VerifyHealthCheck_OK()
        {
            HttpClient client = GetTestClient();

            HttpRequestMessage httpRequestMessage = new(HttpMethod.Get, "/health");

            HttpResponseMessage response = await client.SendAsync(httpRequestMessage, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task VerifyAliveCheck_OK()
        {
            HttpClient client = GetTestClient();

            HttpRequestMessage httpRequestMessage = new(HttpMethod.Get, "/alive");

            HttpResponseMessage response = await client.SendAsync(httpRequestMessage, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private HttpClient GetTestClient()
            => _factory.CreateClient();
    }
}
