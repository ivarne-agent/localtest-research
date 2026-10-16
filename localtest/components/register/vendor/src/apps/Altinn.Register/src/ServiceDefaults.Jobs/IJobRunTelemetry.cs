using System.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Helper for emitting telemetry for job runs.
/// </summary>
internal interface IJobRunTelemetry
{
    /// <summary>
    /// Emits telemetry for a failed job run with the given exception.
    /// </summary>
    /// <param name="exception">The exception that caused the job to fail.</param>
    public void JobFailed(Exception exception);

    /// <summary>
    /// Emits telemetry for a job that failed to be created with the given exception.
    /// </summary>
    /// <param name="exception">The exception that caused the job creation to fail.</param>
    public void JobCreationFailed(Exception exception);

    /// <summary>
    /// Emits telemetry for a skipped job run with the given reason.
    /// </summary>
    /// <param name="reason">The skip reason.</param>
    public void JobSkipped(string reason);

    /// <summary>
    /// Emits telemetry for a job starting.
    /// </summary>
    public void JobStarting();

    /// <summary>
    /// Emits telemetry for a completed job run with the given duration.
    /// </summary>
    /// <param name="duration">The job duration.</param>
    public void JobCompleted(TimeSpan duration);

    /// <summary>
    /// Emits telemetry for a job that failed during disposal with the given exception.
    /// </summary>
    /// <param name="exception">The exception.</param>
    public void JobDisposalFailed(Exception exception);

    /// <summary>
    /// Starts an activity for the ShouldRun check of a job.
    /// </summary>
    /// <returns>The activity for the ShouldRun check.</returns>
    public Activity? StartShouldRun();

    /// <summary>
    /// Starts an activity for the actual run of a job.
    /// </summary>
    /// <returns>The activity for the job run.</returns>
    public Activity? StartRun();
}
