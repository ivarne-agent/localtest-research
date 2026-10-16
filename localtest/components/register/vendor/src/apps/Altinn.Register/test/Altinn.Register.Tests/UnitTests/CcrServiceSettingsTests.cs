using System.Net;
using Altinn.Register.Core.Ccr;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Tests.UnitTests;

public class CcrServiceSettingsTests
{
    [Fact]
    public void Can_Populate_From_Configuration()
    {
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Clients:client1:PasswordHash", "hash1"),
            new("Altinn:register:Ccr:Clients:client1:AllowedSourceNetworks:0", "0.0.0.0/0"),
            new("Altinn:register:Ccr:Clients:client1:AllowedSourceNetworks:1", "::/0"),
        ]);

        var servicesBuilder = new ServiceCollection();
        servicesBuilder.AddSingleton<IConfiguration>(config);
        servicesBuilder.AddRegisterCoreServices();

        using var services = servicesBuilder.BuildServiceProvider(new ServiceProviderOptions
        {
            // Dummy provider for testing - probably not valid
            ValidateOnBuild = false,
            ValidateScopes = false,
        });

        var settings = services.GetRequiredService<IOptionsMonitor<CcrServiceSettings>>().CurrentValue;

        settings.Clients.ShouldHaveSingleItem();
        settings.Clients.ShouldContainKey("client1");

        var client = settings.Clients["client1"];
        client.PasswordHash.ShouldBe("hash1");
        client.AllowedSourceNetworks.Count.ShouldBe(2);
        client.AllowedSourceNetworks[0].ShouldBe(IPNetwork.Parse("0.0.0.0/0"));
        client.AllowedSourceNetworks[1].ShouldBe(IPNetwork.Parse("::/0"));
    }
}
