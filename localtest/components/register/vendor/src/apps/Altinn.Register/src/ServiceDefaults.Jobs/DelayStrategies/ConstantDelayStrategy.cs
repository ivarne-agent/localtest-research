using System.Diagnostics;
using CommunityToolkit.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;

/// <summary>
/// A delay strategy that returns a constant delay regardless of the job outcome.
/// </summary>
/// <typeparam name="T">The job outcome type.</typeparam>
internal sealed class ConstantDelayStrategy<T>
    : IDelayStrategy<T>
    where T : notnull
{
    private static readonly TimeSpan MaxDisabledDelay = TimeSpan.FromMinutes(10);

    private readonly TimeSpan _successDelay;
    private readonly TimeSpan _failureDelay;
    private readonly TimeSpan _disabledDelay;

    /// <inheritdoc/>
    public string Description => $"Constant {_successDelay} ({_disabledDelay} if disabled)";

    /// <summary>
    /// Creates a new instance of the <see cref="ConstantDelayStrategy{T}"/> class with the specified
    /// interval for all outcomes.
    /// </summary>
    /// <param name="interval">The interval for all outcomes.</param>
    public ConstantDelayStrategy(TimeSpan interval)
    {
        Guard.IsGreaterThan(interval, TimeSpan.FromSeconds(30));

        _successDelay = interval;
        _failureDelay = interval;
        _disabledDelay = interval * 10;

        if (_disabledDelay > MaxDisabledDelay)
        {
            _disabledDelay = MaxDisabledDelay;
        }
    }

    /// <inheritdoc/>
    public TimeSpan GetDelay(JobOutcome<T> outcome)
    {
        if (outcome.IsInitial)
        {
            // For the initial run, we should delay it by the success delay.
            return _successDelay;
        }

        if (outcome.IsJobDisabled)
        {
            return _disabledDelay;
        }

        Debug.Assert(outcome.IsJobSkipped | outcome.IsJobSuccess | outcome.IsJobFailure);
        return _successDelay;
    }
}
