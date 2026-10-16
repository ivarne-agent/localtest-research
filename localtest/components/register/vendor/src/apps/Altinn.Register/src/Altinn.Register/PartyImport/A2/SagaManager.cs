using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.UnitOfWork;
using MassTransit;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Provides functionality for starting and handling sagas by coordinating saga state persistence and message processing
/// within a unit of work.
/// </summary>
/// <remarks>SagaManager is intended for internal use in orchestrating saga lifecycles, ensuring idempotent
/// message handling and consistent state transitions. It manages the creation and retrieval of saga state, enforces
/// correct saga initiation, and delegates message handling to the appropriate saga implementation. This type is not
/// thread-safe and should be scoped appropriately within the application's dependency injection container.</remarks>
public sealed class SagaManager
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ICommandSender _commandSender;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaManager"/> class.
    /// </summary>
    public SagaManager(
        IUnitOfWorkManager uowManager,
        ICommandSender commandSender)
    {
        _uowManager = uowManager;
        _commandSender = commandSender;
    }

    /// <summary>
    /// Starts a new saga instance using the specified command, initializing its state and processing the initial
    /// message. If the saga has already been started, the handler runs again to ensure any needed events are published.
    /// </summary>
    /// <typeparam name="TSaga">The saga type to start.</typeparam>
    /// <typeparam name="TCommand">The type of command that initiates the saga. Must inherit from <see cref="CommandBase"/>.</typeparam>
    /// <typeparam name="TState">The type of state data associated with the saga. Must implement <see cref="ISagaStateData{TSelf}"/>.</typeparam>
    /// <param name="context">The <see cref="ConsumeContext{T}"/> of the command.</param>
    /// <returns>A task that represents the asynchronous operation of starting the saga.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the saga has already been started by a different command.</exception>
    public async Task StartSaga<TSaga, TCommand, TState>(ConsumeContext<TCommand> context)
        where TSaga : ISagaStartedBy<TSaga, TCommand, TState>, ISaga<TSaga, TState>
        where TCommand : CommandBase
        where TState : class, ISagaStateData<TState>
    {
        var command = context.Message;
        var cancellationToken = context.CancellationToken;
        var sagaId = command.CorrelationId;

        await using var uow = await _uowManager.CreateAsync(
            tags: [new("saga.id", sagaId), new("saga.name", TSaga.Name)],
            cancellationToken: cancellationToken,
            activityName: $"start {TSaga.Name}");

        var persistence = uow.GetRequiredService<ISagaStatePersistence>();
        var state = await GetState<TState>(persistence, sagaId, cancellationToken);

        if (state.Data is null)
        {
            state.Data = await TSaga.CreateInitialState(uow, command);
        }

        var sagaContext = SagaContext<TState>.Create(context, _commandSender, state);
        await HandleMessage<TSaga, TCommand, TState>(uow, sagaContext, command, cancellationToken);

        await persistence.SaveState(state, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        await PostHandle(sagaContext, cancellationToken);
    }

    /// <summary>
    /// Handles a saga message by retrieving the corresponding saga state and invoking the appropriate handler logic.
    /// </summary>
    /// <typeparam name="TSaga">The saga type that processes the message and maintains state. Must implement both ISagaHandles and ISaga
    /// interfaces for the specified message and state types.</typeparam>
    /// <typeparam name="TMessage">The type of message to be handled by the saga. Must be a class implementing <see cref="CorrelatedBy{TKey}"/>.</typeparam>
    /// <typeparam name="TState">The type of state data associated with the saga. Must be a class implementing <see cref="ISagaStateData{TSelf}"/>.</typeparam>
    /// <param name="context">The <see cref="ConsumeContext{T}"/> of the message.</param>
    /// <returns>A task that represents the asynchronous handling operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the saga state for the specified message has not been started or cannot be found.</exception>
    public async Task HandleMessage<TSaga, TMessage, TState>(ConsumeContext<TMessage> context)
        where TSaga : ISagaHandles<TSaga, TMessage, TState>, ISaga<TSaga, TState>
        where TMessage : class, IMessageBase
        where TState : class, ISagaStateData<TState>
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var sagaId = message.CorrelationId;

        await using var uow = await _uowManager.CreateAsync(
            tags: [new("saga.id", sagaId), new("saga.name", TSaga.Name)],
            cancellationToken: cancellationToken,
            activityName: $"run {TSaga.Name}");

        var persistence = uow.GetRequiredService<ISagaStatePersistence>();
        var state = await GetState<TState>(persistence, sagaId, cancellationToken);

        if (state.Data is null)
        {
            throw new InvalidOperationException($"Saga '{sagaId}' is not started.");
        }

        var sagaContext = SagaContext<TState>.Create(context, _commandSender, state);
        await HandleMessage<TSaga, TMessage, TState>(uow, sagaContext, message, cancellationToken);

        await persistence.SaveState(state, cancellationToken);
        await uow.CommitAsync(cancellationToken);

        await PostHandle(sagaContext, cancellationToken);
    }

    private async Task<SagaState<TState>> GetState<TState>(ISagaStatePersistence persistence, Guid sagaId, CancellationToken cancellationToken)
        where TState : class, ISagaStateData<TState>
    {
        using var tsc = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        tsc.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            return await persistence.GetState<TState>(sagaId, tsc.Token);
        }
        catch (OperationCanceledException ex) when (tsc.Token.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(message: $"GetState for '{sagaId}' cancelled", innerException: ex, cancellationToken);
            }
            else
            {
                throw new TimeoutException($"GetState for '{sagaId}' timed out", ex);
            }
        }
    }

    private async Task HandleMessage<TSaga, TMessage, TState>(
        IServiceProvider services,
        SagaContext<TState> context,
        TMessage message,
        CancellationToken cancellationToken)
        where TSaga : ISagaHandles<TSaga, TMessage, TState>, ISaga<TSaga, TState>
        where TMessage : class, IMessageBase
        where TState : class, ISagaStateData<TState>
    {
        // Note: Duplicate message deliveries are intentionally re-processed to ensure events and
        // follow-up commands are emitted. Idempotency is handled at the saga level, not here.
        // TODO: Consider implementing event persistence in saga state for guaranteed delivery.
        var messageId = message.MessageId;
        context.State.Messages.Add(messageId);

        var saga = Factory<TSaga, TState>.Create(services, context);
        try
        {
            await saga.Handle(message, cancellationToken);
        }
        finally
        {
            if (saga is IAsyncDisposable asyncDisp)
            {
                await asyncDisp.DisposeAsync();
            }
            else if (saga is IDisposable disp)
            {
                disp.Dispose();
            }
        }
    }

    private async Task PostHandle<TState>(SagaContext<TState> context, CancellationToken cancellationToken)
        where TState : class, ISagaStateData<TState>
    {
        var state = context.State;

        switch (state.Status)
        {
            case SagaStatus.Faulted:
                await context.Publish(
                    new SagaCompletedEvent
                    {
                        CorrelationId = state.SagaId,
                        Success = false,
                    },
                    cancellationToken);
                break;

            case SagaStatus.Completed:
                await context.Publish(
                    new SagaCompletedEvent
                    {
                        CorrelationId = state.SagaId,
                        Success = true,
                    },
                    cancellationToken);
                break;
        }
    }

    private static class Factory<TSaga, TState>
        where TSaga : ISaga<TSaga, TState>
        where TState : class, ISagaStateData<TState>
    {
        private static readonly ObjectFactory<TSaga> _factory
            = ActivatorUtilities.CreateFactory<TSaga>([typeof(SagaContext<TState>)]);

        public static TSaga Create(IServiceProvider services, SagaContext<TState> state)
            => _factory(services, [state]);
    }
}
