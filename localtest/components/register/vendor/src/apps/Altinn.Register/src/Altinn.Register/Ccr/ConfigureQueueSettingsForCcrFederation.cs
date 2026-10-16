using Altinn.Authorization.ServiceDefaults.StorageQueues;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Ccr;

/// <summary>
/// Configures the <see cref="StorageQueueSettings"/> for the CCR federation queues.
/// </summary>
internal sealed class ConfigureQueueSettingsForCcrFederation
    : IConfigureNamedOptions<StorageQueueSettings>
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureQueueSettingsForCcrFederation"/> class.
    /// </summary>
    public ConfigureQueueSettingsForCcrFederation(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public void Configure(string? name, StorageQueueSettings options)
    {
        // we only configure queues for CCR federation here.
        if (name is null || !name.StartsWith("ccr-federation:"))
        {
            return;
        }

        var index = name["ccr-federation:".Length..];
        var storageAccountName = _configuration.GetValue<string>("Altinn:register:Ccr:Federate:StorageAccountName");
        var queueName = _configuration.GetValue<string>($"Altinn:register:Ccr:Federate:Targets:{index}:QueueName");

        if (!string.IsNullOrWhiteSpace(storageAccountName))
        {
            options.StorageAccountUri = new Uri($"https://{storageAccountName}.queue.core.windows.net/");
        }

        if (!string.IsNullOrWhiteSpace(queueName))
        {
            options.QueueName = queueName;
        }
    }

    /// <inheritdoc/>
    public void Configure(StorageQueueSettings options)
    {
    }
}
