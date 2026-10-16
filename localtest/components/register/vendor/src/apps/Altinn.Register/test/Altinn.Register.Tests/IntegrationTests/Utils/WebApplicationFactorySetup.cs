using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Register.Clients;
using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Configuration;
using Altinn.Register.Core.A2;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Tests.Mocks;
using Altinn.Register.Tests.Mocks.Authentication;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Altinn.Register.Tests.IntegrationTests.Utils
{
    public class WebApplicationFactorySetup
        : WebApplicationFactory<WebApplicationFactorySetup>
    {
        private readonly WebApplicationFactory<Program> _webApplicationFactory;

        public WebApplicationFactorySetup()
        {
            _webApplicationFactory = new WebApplicationFactory<Program>();
        }

        public WebApplicationFactorySetup(WebApplicationFactory<Program> webApplicationFactory)
        {
            _webApplicationFactory = webApplicationFactory;
        }

        public Mock<ILogger<PartiesClient>> PartiesClientLogger { get; set; } = new();

        public Mock<ILogger<PersonClient>> PersonsClientLogger { get; set; } = new();

        public Mock<ILogger<OrganizationClient>> OrganizationsClientsLogger { get; set; } = new();

        public Mock<IOptions<GeneralSettings>> GeneralSettingsOptions { get; set; } = new();

        public MockRateLimitProvider RateLimitProvider { get; } = new();

        public MemoryCache MemoryCache { get; set; } = new MemoryCache(new MemoryCacheOptions());

        public HttpMessageHandler SblBridgeHttpMessageHandler { get; set; } = new DelegatingHandlerStub();

        public HttpClient GetTestServerClient()
        {
            return _webApplicationFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Altinn:IsTest", "true");
                builder.UseSetting(AltinnPreStartLogger.DisableConfigKey, "true");

                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                    services.AddSingleton<IMemoryCache>(MemoryCache);
                    services.AddSingleton<IAccessTokenGenerator, TestAccessTokenGenerator>();
                    services.AddSingleton<IExternalRoleDefinitionPersistence, MockExternalRoleDefinitionPersistence>();
                    services.AddSingleton(RateLimitProvider);
                    services.AddSingleton<IRateLimitProvider>(sp => sp.GetRequiredService<MockRateLimitProvider>());
                    services.AddScoped<ICommandSender, NoOpCommandSender>();
                    services.AddScoped<ICcrUpdateFederator, NoOpCcrUpdateFederator>();

                    // Using the real/actual implementation of IParties and IPersons, but with a mocked message handler.
                    // Haven't found any other ways of injecting a mocked message handler to simulate SBL Bridge.
                    services.AddSingleton<IV1PartyService>(
                        new PartiesClient(
                            new HttpClient(SblBridgeHttpMessageHandler),
                            GeneralSettingsOptions.Object,
                            PartiesClientLogger.Object,
                            MemoryCache));
                    services.AddSingleton<IPersonClient>(
                        new PersonClient(
                            new HttpClient(SblBridgeHttpMessageHandler),
                            GeneralSettingsOptions.Object,
                            PersonsClientLogger.Object));
                    services.AddSingleton<IOrganizationClient>(
                        new OrganizationClient(
                            new HttpClient(SblBridgeHttpMessageHandler),
                            GeneralSettingsOptions.Object,
                            OrganizationsClientsLogger.Object));
                });
            }).CreateClient();
        }

        protected virtual WebApplicationBuilder CreateWebApplicationBuilder()
        {
            var partManager = new ApplicationPartManager();
            ConfigureApplicationParts(partManager);

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(partManager);

            return builder;
        }

        protected virtual WebApplication CreateWebApplication(WebApplicationBuilder builder)
            => builder.Build();

        protected virtual void ConfigureWebApplication(WebApplication app)
        {
        }

        protected virtual void ConfigureApplicationParts(ApplicationPartManager partManager)
        {
        }

        protected sealed override IHostBuilder? CreateHostBuilder()
            => new TestHostBuilderWrapper(CreateWebApplicationBuilder());

        protected sealed override IHost CreateHost(IHostBuilder builder)
        {
            var appBuilder = ((TestHostBuilderWrapper)builder).Builder;
            var app = CreateWebApplication(appBuilder);

            ConfigureWebApplication(app);
            app.Start();

            return app;
        }

        private class TestHostBuilderWrapper(WebApplicationBuilder builder)
            : IHostBuilder
        {
            public IDictionary<object, object> Properties => builder.Host.Properties;

            internal WebApplicationBuilder Builder => builder;

            IHost IHostBuilder.Build()
            {
                throw new NotSupportedException();
            }

            [SuppressMessage("Usage", "ASP0013:Suggest switching from using Configure methods to WebApplicationBuilder.Configuration", Justification = "Forwarding interface")]
            public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
            {
                builder.Host.ConfigureAppConfiguration(configureDelegate);

                return this;
            }

            public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
            {
                builder.Host.ConfigureContainer(configureDelegate);

                return this;
            }

            [SuppressMessage("Usage", "ASP0013:Suggest switching from using Configure methods to WebApplicationBuilder.Configuration", Justification = "Forwarding interface")]
            public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
            {
                builder.Host.ConfigureHostConfiguration(configureDelegate);

                return this;
            }

            public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
            {
                builder.Host.ConfigureServices(configureDelegate);

                return this;
            }

            public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
                where TContainerBuilder : notnull
            {
                builder.Host.UseServiceProviderFactory(factory);

                return this;
            }

            public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
                where TContainerBuilder : notnull
            {
                builder.Host.UseServiceProviderFactory(factory);

                return this;
            }
        }
    }
}
