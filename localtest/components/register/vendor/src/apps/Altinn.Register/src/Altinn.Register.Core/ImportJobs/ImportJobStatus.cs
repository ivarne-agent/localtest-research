namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Import job queue status, consisting of the highest enqueued item and the highest known source item at the time of
/// last queue update.
/// </summary>
/// <remarks>
/// It's important that <c>default(ImportJobStatus)</c> results in a value where <see cref="EnqueuedMax"/>,
/// <see cref="SourceMax"/>, and <see cref="ProcessedMax"/> are <c>0</c>, as this is used to indicate that the job has
/// not yet been created.
/// </remarks>
public readonly record struct ImportJobStatus
{
    private readonly ImportJobQueueStatus _queueStatus;
    private readonly ImportJobProcessingStatus _processingStatus;

    /// <inheritdoc cref="ImportJobQueueStatus.EnqueuedMax"/>
    public readonly required ulong EnqueuedMax
    {
        get => _queueStatus.EnqueuedMax;
        init => _queueStatus = _queueStatus with { EnqueuedMax = value };
    }

    /// <inheritdoc cref="ImportJobQueueStatus.SourceMax"/>
    public readonly required ulong? SourceMax
    {
        get => _queueStatus.SourceMax;
        init => _queueStatus = _queueStatus with { SourceMax = value };
    }

    /// <inheritdoc cref="ImportJobProcessingStatus.ProcessedMax"/>
    public readonly required ulong ProcessedMax
    {
        get => _processingStatus.ProcessedMax;
        init => _processingStatus = _processingStatus with { ProcessedMax = value };
    }
}
