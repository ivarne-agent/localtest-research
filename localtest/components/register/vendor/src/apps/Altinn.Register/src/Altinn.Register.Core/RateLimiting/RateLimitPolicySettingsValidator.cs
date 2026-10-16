using Microsoft.Extensions.Options;

namespace Altinn.Register.Core.RateLimiting;

/// <summary>
/// Validates <see cref="RateLimitPolicySettings"/> instances.
/// </summary>
internal sealed class RateLimitPolicySettingsValidator
    : IValidateOptions<RateLimitPolicySettings>
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, RateLimitPolicySettings options)
    {
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Fail($"Rate limit policy '{name}' has not been configured.");
        }

        List<string>? failures = null;

        if (options.Limit <= 0)
        {
            (failures ??= []).Add($"{nameof(RateLimitPolicySettings.Limit)} must be greater than zero.");
        }

        if (options.WindowDuration < MinimumDuration)
        {
            (failures ??= []).Add($"{nameof(RateLimitPolicySettings.WindowDuration)} must be at least 1 minute.");
        }

        if (options.BlockDuration < MinimumDuration)
        {
            (failures ??= []).Add($"{nameof(RateLimitPolicySettings.BlockDuration)} must be at least 1 minute.");
        }

        if (!Enum.IsDefined(options.WindowBehavior))
        {
            (failures ??= []).Add($"{nameof(RateLimitPolicySettings.WindowBehavior)} has an invalid value.");
        }

        if (!Enum.IsDefined(options.BlockedRequestBehavior))
        {
            (failures ??= []).Add($"{nameof(RateLimitPolicySettings.BlockedRequestBehavior)} has an invalid value.");
        }

        return failures is { Count: > 0 }
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
