using System.ComponentModel.DataAnnotations;

namespace Altinn.Register.Cleanup;

/// <summary>
/// Provides configuration settings for automatic cleanup of saga state data, including retention periods for completed,
/// faulted, and in-progress saga states.
/// </summary>
public class SagaStateCleanupSettings
    : IValidatableObject
{
    /// <summary>
    /// Gets or sets the number of days to retain completed saga states before they are deleted.
    /// </summary>
    public uint DeleteCompletedSagaStatesAfterDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets the number of days after which faulted saga states are deleted.
    /// </summary>
    public uint DeleteFaultedSagaStatesAfterDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the number of days after which in-progress saga states are deleted.
    /// </summary>
    public uint DeleteInProgressSagaStatesAfterDays { get; set; } = 90;

    /// <inheritdoc/>
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        if (DeleteCompletedSagaStatesAfterDays < 1)
        {
            yield return new ValidationResult(
                $"Value must be greater than 0.",
                [nameof(DeleteCompletedSagaStatesAfterDays)]);
        }

        if (DeleteFaultedSagaStatesAfterDays < 1)
        {
            yield return new ValidationResult(
                $"Value must be greater than 0.",
                [nameof(DeleteFaultedSagaStatesAfterDays)]);
        }

        if (DeleteInProgressSagaStatesAfterDays < 7)
        {
            yield return new ValidationResult(
                $"Value must be greater than or equal to 7.",
                [nameof(DeleteInProgressSagaStatesAfterDays)]);
        }
    }
}
