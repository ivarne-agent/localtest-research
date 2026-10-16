namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Status/progress tracker for import jobs.
/// </summary>
public interface IImportJobTracker
{
    /// <summary>
    /// Gets queue status of the import job.
    /// </summary>
    /// <param name="id">The job id.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The queue status for the given job, or <see langword="default"/> if the job is not yet created.</returns>
    Task<ImportJobStatus> GetStatus(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the queue status for the given job.
    /// </summary>
    /// <param name="id">The job id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// This should only update the status if the <see cref="ImportJobQueueStatus.EnqueuedMax"/> is higher than the current enqueued max,
    /// or if the <see cref="ImportJobQueueStatus.SourceMax"/> is higher than the current source max. In other words, this is not only
    /// idempotent, but also only incrementing with respect to both <see cref="ImportJobQueueStatus.EnqueuedMax"/>, and
    /// <see cref="ImportJobQueueStatus.SourceMax"/>.
    /// </remarks>
    /// <returns>Whether or not the job status was updated.</returns>
    Task<(ImportJobStatus Status, bool Updated)> TrackQueueStatus(string id, ImportJobQueueStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the processing status for the given job.
    /// </summary>
    /// <param name="id">The job id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// This should only update the status if the new status is higher than the current status. In other words, this is not only
    /// idempotent, but also only incrementing with respect to <see cref="ImportJobProcessingStatus.ProcessedMax"/>.
    /// </remarks>
    /// <returns>Whether or not the job status was updated.</returns>
    Task<(ImportJobStatus Status, bool Updated)> TrackProcessedStatus(string id, ImportJobProcessingStatus status, CancellationToken cancellationToken = default);
}
