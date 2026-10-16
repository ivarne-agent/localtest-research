using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit.Commands;

/// <summary>
/// Implementation of <see cref="ICommandSender"/>.
/// </summary>
internal class CommandSender
    : ICommandSender
{
    private readonly ICommandQueueResolver _queueResolver;
    private readonly ISendEndpointProvider _sendEndpointProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandSender"/> class.
    /// </summary>
    public CommandSender(
        ICommandQueueResolver queueResolver,
        ISendEndpointProvider sendEndpointProvider)
    {
        _queueResolver = queueResolver;
        _sendEndpointProvider = sendEndpointProvider;
    }

    /// <inheritdoc/>
    public async Task Send<T>(T command, CancellationToken cancellationToken = default)
        where T : CommandBase
    {
        var uri = _queueResolver.GetQueueUriForCommandType<T>();
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(uri).WaitAsync(cancellationToken);
        await endpoint.Send(command, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task Send<T>(IEnumerable<T> commands, CancellationToken cancellationToken = default)
        where T : CommandBase
    {
        var uri = _queueResolver.GetQueueUriForCommandType<T>();
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(uri).WaitAsync(cancellationToken);
        await endpoint.SendBatch(commands, cancellationToken);
    }
}
