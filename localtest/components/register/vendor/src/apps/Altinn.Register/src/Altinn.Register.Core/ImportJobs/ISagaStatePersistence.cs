namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Defines a contract for persisting and retrieving saga state data in a durable store.
/// </summary>
public interface ISagaStatePersistence
{
    /// <summary>
    /// Asynchronously gets the state of a saga identified by the provided saga ID.
    /// </summary>
    /// <remarks>This locks the state in the database.</remarks>
    /// <typeparam name="T">The type of the saga state data. Must implement the <see cref="ISagaStateData{TSelf}"/> interface.</typeparam>
    /// <param name="sagaId">The unique identifier of the saga whose state is to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The saga state.</returns>
    public Task<SagaState<T>> GetState<T>(Guid sagaId, CancellationToken cancellationToken = default)
        where T : class, ISagaStateData<T>;

    /// <summary>
    /// Sets the state of the specified saga instance.
    /// </summary>
    /// <typeparam name="T">The type of the saga state data. Must implement the <see cref="ISagaStateData{TSelf}"/> interface.</typeparam>
    /// <param name="state">The new state to assign to the saga instance.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    public Task SaveState<T>(SagaState<T> state, CancellationToken cancellationToken = default)
        where T : class, ISagaStateData<T>;

    /// <summary>
    /// Deletes the persisted state associated with the specified saga identifier.
    /// </summary>
    /// <param name="sagaId">The unique identifier of the saga whose state is to be deleted.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the delete operation.</param>
    public Task DeleteState(Guid sagaId, CancellationToken cancellationToken = default);
}
