namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Interface used to explicitly set the job name on a job type.
/// </summary>
/// <typeparam name="TSelf">Self type.</typeparam>
public interface IHasJobName<TSelf>
    where TSelf : IHasJobName<TSelf>
{
    /// <summary>
    /// Gets the name of the job.
    /// </summary>
    public static abstract string JobName { get; }
}
