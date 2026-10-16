using Altinn.Authorization.ServiceDefaults.StorageQueues;
using Altinn.Register.Ccr;
using Microsoft.Extensions.Configuration;

namespace Altinn.Register.Tests.UnitTests;

public class ConfigureQueueSettingsForCcrFederationTests
{
    [Fact]
    public void Configure_Configures_Ccr_Federation_Queue()
    {
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Federate:StorageAccountName", "registerccr"),
            new("Altinn:register:Ccr:Federate:Targets:1:QueueName", "ccr-target-two"),
        ]);

        var configure = new ConfigureQueueSettingsForCcrFederation(config);
        var settings = new StorageQueueSettings();

        configure.Configure("ccr-federation:1", settings);

        settings.StorageAccountUri.ShouldBe(new Uri("https://registerccr.queue.core.windows.net/"));
        settings.QueueName.ShouldBe("ccr-target-two");
    }

    [Fact]
    public void Configure_Does_Not_Configure_Non_Ccr_Federation_Queue()
    {
        using var config = CreateConfig();
        var configure = new ConfigureQueueSettingsForCcrFederation(config);
        var settings = CreateExistingSettings();

        configure.Configure("regular-queue", settings);

        settings.StorageAccountUri.ShouldBe(new Uri("https://existing.queue.core.windows.net/"));
        settings.QueueName.ShouldBe("existing-queue");
    }

    [Fact]
    public void Configure_Does_Not_Configure_Unnamed_Queue()
    {
        using var config = CreateConfig();
        var configure = new ConfigureQueueSettingsForCcrFederation(config);
        var settings = CreateExistingSettings();

        configure.Configure(null, settings);

        settings.StorageAccountUri.ShouldBe(new Uri("https://existing.queue.core.windows.net/"));
        settings.QueueName.ShouldBe("existing-queue");
    }

    [Fact]
    public void Configure_Leaves_Existing_Values_When_Config_Values_Are_Blank()
    {
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Federate:StorageAccountName", " "),
            new("Altinn:register:Ccr:Federate:Targets:1:QueueName", string.Empty),
        ]);

        var configure = new ConfigureQueueSettingsForCcrFederation(config);
        var settings = CreateExistingSettings();

        configure.Configure("ccr-federation:1", settings);

        settings.StorageAccountUri.ShouldBe(new Uri("https://existing.queue.core.windows.net/"));
        settings.QueueName.ShouldBe("existing-queue");
    }

    [Fact]
    public void Configure_Unnamed_Configure_Is_No_Op()
    {
        using var config = CreateConfig();
        var configure = new ConfigureQueueSettingsForCcrFederation(config);
        var settings = CreateExistingSettings();

        configure.Configure(settings);

        settings.StorageAccountUri.ShouldBe(new Uri("https://existing.queue.core.windows.net/"));
        settings.QueueName.ShouldBe("existing-queue");
    }

    private static ConfigurationManager CreateConfig()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection([
            new("Altinn:register:Ccr:Federate:StorageAccountName", "registerccr"),
            new("Altinn:register:Ccr:Federate:Targets:1:QueueName", "ccr-target-two"),
        ]);

        return config;
    }

    private static StorageQueueSettings CreateExistingSettings()
        => new()
        {
            StorageAccountUri = new Uri("https://existing.queue.core.windows.net/"),
            QueueName = "existing-queue",
        };
}
