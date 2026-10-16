using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Tests.TestingControllers.Utils;

public class WebApplicationFixture
    : IAsyncLifetime
{
    private readonly WebApplicationFactory _factory = new();

    ValueTask IAsyncLifetime.InitializeAsync()
        => ValueTask.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync()
        => _factory.DisposeAsync();

    public WebApplicationFactory<Program> CreateServer(
        Action<IConfigurationBuilder>? configureConfiguration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            if (configureConfiguration is not null)
            {
                var settings = new ConfigurationBuilder();
                configureConfiguration(settings);
                builder.UseConfiguration(settings.Build());
            }

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
    }

    private class WebApplicationFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection([
                    new(AltinnPreStartLogger.DisableConfigKey, "true"),
                    new("Altinn:IsTest", "true"),
                ])
                .Build();

            builder.UseConfiguration(settings);
            builder.ConfigureLogging(builder => builder.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICommandSender, NoOpCommandSender>();
            });

            base.ConfigureWebHost(builder);
        }
    }
}
