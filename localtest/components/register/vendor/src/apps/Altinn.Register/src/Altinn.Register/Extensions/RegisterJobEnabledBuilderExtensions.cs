using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Register.Core.ImportJobs;

namespace Altinn.Register.Model.Extensions;

/// <summary>
/// Extension methods for <see cref="JobEnabledBuilder"/>.
/// </summary>
public static class RegisterJobEnabledBuilderExtensions
{
    /// <summary>
    /// Adds a check to the job enabled condition that requires a specific import job to have finished.
    /// </summary>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="jobName">The name of the import job that's required to be finished.</param>
    /// <param name="threshold">How many pending items are allowed before the job is considered finished.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with the job finished check added.</returns>
    public static JobEnabledBuilder WithRequireImportJobFinished(this JobEnabledBuilder builder, string jobName, ulong threshold)
    {
        var noSourceMaxReason = $"Job '{jobName}' does not have a source-max, hence we can't know if it's finished or not";

        return builder.WithCheck(async (IImportJobTracker tracker, CancellationToken cancellationToken) =>
        {
            var status = await tracker.GetStatus(jobName, cancellationToken);
            if (status.SourceMax is not { } sourceMax)
            {
                // Job does not have a source-max, hence we can't know if it's finished or not
                return JobShouldRunResult.No(noSourceMaxReason);
            }

            var pending = sourceMax - status.ProcessedMax;
            if (pending > threshold)
            {
                return JobShouldRunResult.No($"Job '{jobName}' is not finished ({pending} items pending, more than {threshold} theshold)");
            }

            return JobShouldRunResult.Yes;
        });
    }
}
