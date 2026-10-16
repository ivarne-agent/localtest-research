using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.ImportJobs;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Defines the contract for a saga, which represents a long-running, stateful process that coordinates and manages a
/// sequence of operations or transactions.
/// </summary>
/// <remarks>A saga is used to manage complex workflows or business processes that span multiple operations and
/// may require compensation or rollback in case of failure. Implementations of this interface should encapsulate the
/// logic for handling state transitions and coordination between steps. The generic parameters enforce type safety for
/// both the saga implementation and its associated state data.</remarks>
/// <typeparam name="TSelf">The type that implements the saga interface. This enables fluent or strongly-typed usage patterns.</typeparam>
/// <typeparam name="TState">The type of the saga's state data. Must implement the <see cref="ISagaStateData{TSelf}"/> interface and be a reference type.</typeparam>
public interface ISaga<TSelf, TState>
    where TSelf : ISaga<TSelf, TState>
    where TState : class, ISagaStateData<TState>
{
    /// <summary>
    /// Gets the name of the saga (used in tracing/logging).
    /// </summary>
    public abstract static string Name { get; }
}

/// <summary>
/// Indicates that the saga can handle messages of a specific type.
/// </summary>
/// <remarks>
/// Implementers of this interface must respect at-least-once delivery semantics for message handling. This means that
/// messages may be delivered multiple times, and the saga implementation must ensure idempotent processing and that all
/// produced events are published even on retries.
/// </remarks>
/// <typeparam name="TSelf">The type of the saga.</typeparam>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TState">The state type.</typeparam>
public interface ISagaHandles<TSelf, TMessage, TState>
    where TSelf : ISagaHandles<TSelf, TMessage, TState>, ISaga<TSelf, TState>
    where TMessage : class, IMessageBase
    where TState : class, ISagaStateData<TState>
{
    /// <summary>
    /// Processes the specified message asynchronously.
    /// </summary>
    /// <param name="message">The message to be handled. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the message handling operation.</param>
    /// <returns>A task that represents the asynchronous handling operation.</returns>
    public Task Handle(TMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Defines a contract for saga types that can be initiated by a specific command and are responsible for creating their
/// initial state from that command.
/// </summary>
/// <remarks>Implement this interface to indicate that a saga can be started by a particular command type. The
/// saga is expected to create its initial state based on the incoming command, enabling the saga to begin its lifecycle
/// in response to that command.</remarks>
/// <typeparam name="TSelf">The saga type that implements this interface.</typeparam>
/// <typeparam name="TCommand">The type of command that starts the saga.</typeparam>
/// <typeparam name="TState">The type of state data associated with the saga.</typeparam>
public interface ISagaStartedBy<TSelf, TCommand, TState>
    : ISagaHandles<TSelf, TCommand, TState>
    where TSelf : ISagaStartedBy<TSelf, TCommand, TState>, ISaga<TSelf, TState>
    where TCommand : CommandBase
    where TState : class, ISagaStateData<TState>
{
    /// <summary>
    /// Creates a new initial state instance based on the specified command.
    /// </summary>
    /// <param name="services">Service-provider to use during state creation. Can be used to acquire uow-services.</param>
    /// <param name="command">The command used to generate the initial state. Cannot be null.</param>
    /// <returns>A new instance of the initial state corresponding to the provided command.</returns>
    public abstract static ValueTask<TState> CreateInitialState(IServiceProvider services, TCommand command);
}
