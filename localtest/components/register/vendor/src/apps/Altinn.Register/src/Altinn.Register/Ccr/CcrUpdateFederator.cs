using System.Buffers;
using System.Diagnostics;
using Altinn.Authorization.ServiceDefaults.StorageQueues;
using Altinn.Register.Core.Ccr;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Ccr;

/// <summary>
/// Implementation of <see cref="ICcrUpdateFederator"/> that sends CCR update messages to configured storage queues for federation to other systems.
/// </summary>
internal sealed class CcrUpdateFederator
    : ICcrUpdateFederator
{
    private readonly IStorageQueueMessageSenderFactory _queueFactory;
    private readonly IOptionsMonitor<CcrUpdateFederationSettings> _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrUpdateFederator"/> class.
    /// </summary>
    public CcrUpdateFederator(
        IStorageQueueMessageSenderFactory queueFactory,
        IOptionsMonitor<CcrUpdateFederationSettings> settings)
    {
        _queueFactory = queueFactory;
        _settings = settings;
    }

    /// <inheritdoc/>
    public Task FederateUpdates(ReadOnlySequence<byte> xmlData, CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;

        if (!settings.Enable || settings.Targets.IsDefaultOrEmpty)
        {
            Activity.Current?.AddTag("ccr.federation", "skipped");
            return Task.CompletedTask;
        }

        Activity.Current?.AddTag("ccr.federation.count", settings.Targets.Length.ToString());
        var data = ToBinaryData(xmlData);
        var tasks = settings.Targets.Select(target => FederateUpdateToTarget(target, data, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private Task FederateUpdateToTarget(string target, BinaryData xmlData, CancellationToken cancellationToken)
    {
        var sender = _queueFactory.CreateSender(target);
        return sender.SendMessageAsync(xmlData, cancellationToken);
    }

    private static BinaryData ToBinaryData(ReadOnlySequence<byte> data)
    {
        if (data.IsSingleSegment)
        {
            return new BinaryData(data.First);
        }

        return new BinaryData(data.ToArray());
    }
}
