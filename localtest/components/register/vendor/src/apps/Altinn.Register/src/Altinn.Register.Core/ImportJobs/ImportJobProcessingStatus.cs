namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Import job processing status, consisting of the highest processed item.
/// </summary>
public readonly record struct ImportJobProcessingStatus
{
    /// <summary>
    /// Gets the highest processed item.
    /// </summary>
    public readonly required ulong ProcessedMax { get; init; }
}
