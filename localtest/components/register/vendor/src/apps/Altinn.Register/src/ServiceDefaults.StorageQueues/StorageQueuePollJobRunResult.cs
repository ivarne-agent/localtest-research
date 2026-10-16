namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// The result of a <see cref="StorageQueuePollJob{T}"/> run, indicating whether any messages were processed.
/// </summary>
internal enum StorageQueuePollJobRunResult
{
    NoPages,
    SinglePage,
    MultiplePages,
}
