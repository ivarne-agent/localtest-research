namespace Altinn.Register.Core.ImportJobs;

/// <summary>
/// Specifies the status of a saga operation.
/// </summary>
public enum SagaStatus
{
    /// <summary>
    /// Indicates that the operation is currently in progress.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// Indicates that the operation has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Indicates that the operation has failed.
    /// </summary>
    Faulted,
}
