namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Represents a job registration that defines the configuration and metadata for a scheduled job.
/// </summary>
public interface IJobRegistration
{
    /// <summary>
    /// Gets the name of the job.
    /// </summary>
    public string JobName { get; }
}
