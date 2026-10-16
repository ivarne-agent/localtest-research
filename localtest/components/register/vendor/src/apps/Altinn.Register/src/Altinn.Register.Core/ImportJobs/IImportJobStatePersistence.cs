using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Persistence interface for import job state.
/// </summary>
public interface IImportJobStatePersistence
{
    /// <summary>
    /// Gets state for a given job.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><see cref="FieldValue.Unset"/> if the state was not found.</item>
    ///   <item><see cref="FieldValue.Null"/> if the state was not found, but was of the wrong type (or could not be deserialized).</item>
    ///   <item><typeparamref name="T"/> if the state was found.</item>
    /// </list>
    /// </returns>
    public Task<FieldValue<T>> GetState<T>(string jobId, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>;

    /// <summary>
    /// Sets state for a given job.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="jobId">The job id.</param>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task SetState<T>(string jobId, T state, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>;

    /// <summary>
    /// Clears the job state for a given job.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task ClearState(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets state for a given party as part of a job.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="jobId">The job id.</param>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><see cref="FieldValue.Unset"/> if the state was not found.</item>
    ///   <item><see cref="FieldValue.Null"/> if the state was not found, but was of the wrong type (or could not be deserialized).</item>
    ///   <item><typeparamref name="T"/> if the state was found.</item>
    /// </list>
    /// </returns>
    public Task<FieldValue<T>> GetPartyState<T>(string jobId, Guid partyUuid, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>;

    /// <summary>
    /// Sets state for a given party as part of a job.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="jobId">The job id.</param>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task SetPartyState<T>(string jobId, Guid partyUuid, T state, CancellationToken cancellationToken = default)
        where T : IImportJobState<T>;

    /// <summary>
    /// Clears the job state for a given party and job-id combination.
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task ClearPartyState(string jobId, Guid partyUuid, CancellationToken cancellationToken = default);
}
