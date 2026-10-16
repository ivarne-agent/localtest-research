using Altinn.Register.Ccr;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.Tests.UnitTests;

public class ConfigureUpdateFederationSettingsFromConfigurationTests
{
    [Fact]
    public void Configure_Reads_Target_Indexes_From_Configuration()
    {
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Federate:Enable", "true"),
            new("Altinn:register:Ccr:Federate:Targets:0:QueueName", "ccr-target-one"),
            new("Altinn:register:Ccr:Federate:Targets:1:QueueName", "ccr-target-two"),
        ]);

        var configure = new ConfigureUpdateFederationSettingsFromConfiguration(config);
        var settings = new CcrUpdateFederationSettings();

        configure.Configure(settings);

        settings.Enable.ShouldBeTrue();
        settings.Targets.ShouldBe(["ccr-federation:0", "ccr-federation:1"]);
    }

    [Fact]
    public void Configure_Uses_Empty_Targets_When_Targets_Are_Not_Configured()
    {
        using var config = new ConfigurationManager();
        var configure = new ConfigureUpdateFederationSettingsFromConfiguration(config);
        var settings = new CcrUpdateFederationSettings();

        configure.Configure(settings);

        settings.Targets.ShouldBeEmpty();
    }

    [Fact]
    public void Configure_Replaces_Existing_Targets()
    {
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Federate:Enable", "true"),
            new("Altinn:register:Ccr:Federate:Targets:0:QueueName", "ccr-target-one"),
        ]);

        var configure = new ConfigureUpdateFederationSettingsFromConfiguration(config);
        var settings = new CcrUpdateFederationSettings
        {
            Targets = ["existing"],
        };

        configure.Configure(settings);

        settings.Enable.ShouldBeTrue();
        settings.Targets.ShouldBe(["ccr-federation:0"]);
    }
}
