using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// A result from checking if a job should run or not.
/// </summary>
public readonly struct JobShouldRunResult
{
    /// <summary>
    /// Gets a result indicating that the job should run.
    /// </summary>
    public static JobShouldRunResult Yes => default;

    /// <summary>
    /// Gets a result indicating that the job should not run, with a reason why.
    /// </summary>
    /// <param name="reason">The reason the job should not run.</param>
    /// <returns>A <see cref="JobShouldRunResult"/>.</returns>
    public static JobShouldRunResult No(string reason)
    {
        Guard.IsNotNullOrEmpty(reason);

        return new(reason);
    }

    /// <summary>
    /// Creates a <see cref="JobShouldRunResult"/> indicating whether a job should run, with an optional reason if it
    /// should not.
    /// </summary>
    /// <param name="reason">The explanation for why the job should not run. This value is ignored if <paramref name="shouldRun"/> is <see
    /// langword="true"/>.</param>
    /// <param name="shouldRun">A value indicating whether the job should run. If <see langword="true"/>, the result indicates the job should
    /// run; otherwise, the result includes the specified reason.</param>
    /// <returns>A <see cref="JobShouldRunResult"/> representing the decision to run the job. If <paramref name="shouldRun"/> is
    /// <see langword="true"/>, the result indicates the job should run; otherwise, it includes the provided reason.</returns>
    public static JobShouldRunResult Conditional(string reason, bool shouldRun)
        => shouldRun ? Yes : No(reason);

    private readonly string? _reason;

    private JobShouldRunResult(string reason)
    {
        _reason = reason;
    }

    /// <summary>
    /// Gets whether the job should run.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Reason))]
    public readonly bool ShouldRun => _reason is null;

    /// <summary>
    /// Gets the reason why the job should not run, or <see langword="null"/> if the job should run.
    /// </summary>
    public readonly string? Reason => _reason;
}
