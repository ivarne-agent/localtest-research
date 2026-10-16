namespace Altinn.Register.Jobs;

/// <summary>
/// Settings for <see cref="RecurringJobHostedService"/>.
/// </summary>
public class RecurringJobHostedSettings
{
    /// <summary>
    /// Gets or sets whether to disable the scheduler entirely. If the scheduler is disabled, only lifecycle jobs will run.
    /// </summary>
    public bool DisableScheduler { get; set; } = false;
}
