using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit;

namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Context for a saga.
/// </summary>
/// <typeparam name="T">The saga state type.</typeparam>
public sealed class SagaContext<T>
    : ICommandSender
    where T : class, ISagaStateData<T>
{
    /// <summary>
    /// Creates a new saga context.
    /// </summary>
    /// <param name="context">The consume context.</param>
    /// <param name="commandSender">A command sender.</param>
    /// <param name="state">The state.</param>
    /// <returns>A <see cref="SagaContext{T}"/>.</returns>
    public static SagaContext<T> Create(
        ConsumeContext context,
        ICommandSender commandSender,
        SagaState<T> state)
    {
        return new SagaContext<T>(context, commandSender, state);
    }

    private readonly ConsumeContext _context;
    private readonly ICommandSender _commandSender;
    private readonly SagaState<T> _state;

    /// <summary>
    /// Gets the current state of the saga instance.
    /// </summary>
    public SagaState<T> State => _state;

    /// <summary>
    /// Gets the saga identifier.
    /// </summary>
    public Guid SagaId => _state.SagaId;

    private SagaContext(ConsumeContext context, ICommandSender commandSender, SagaState<T> state)
    {
        _context = context;
        _commandSender = commandSender;
        _state = state;
    }

    /// <inheritdoc/>
    public Task Send<T1>(T1 command, CancellationToken cancellationToken = default)
        where T1 : CommandBase
        => _commandSender.Send(command, cancellationToken);

    /// <inheritdoc/>
    public Task Send<T1>(IEnumerable<T1> commands, CancellationToken cancellationToken = default)
        where T1 : CommandBase
        => _commandSender.Send(commands, cancellationToken);

    /// <inheritdoc cref="IPublishEndpoint.Publish{T}(T, CancellationToken)"/>
    public Task Publish<T1>(T1 message, CancellationToken cancellationToken = default)
        where T1 : EventBase
        => _context.Publish(message, cancellationToken);
}
