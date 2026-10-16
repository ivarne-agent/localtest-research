using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Helper for building a job-enabled condition from multiple checks.
/// </summary>
public class JobEnabledBuilder
{
    /// <summary>
    /// Default instance of <see cref="JobEnabledBuilder"/> with no checks.
    /// </summary>
    public static JobEnabledBuilder Default { get; } = new([]);

    private readonly ImmutableList<Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>> _checks;

    private JobEnabledBuilder(ImmutableList<Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>> checks)
    {
        _checks = checks;
    }

    /// <summary>
    /// Adds a check to the job enabled condition.
    /// </summary>
    /// <param name="check">The check to add.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with <paramref name="check"/> added.</returns>
    public JobEnabledBuilder WithCheck(Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>> check)
        => new(_checks.Add(check));

    /// <summary>
    /// Creates a check function from this builder.
    /// </summary>
    /// <returns>A check function.</returns>
    public Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>> ToFunc()
    {
        var checks = _checks.ToImmutableArray();

        return ToFunc(checks);

        // This needs to be a static method to make sure we avoid capturing the builder instance
        static Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>> ToFunc(ImmutableArray<Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>> checks)
        {
            return (sp, ct) => RunChecksSync(checks, sp, ct);
        }

        // This needs to be a static method to make sure we avoid capturing the builder instance
        static ValueTask<JobShouldRunResult> RunChecksSync(ImmutableArray<Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>> checks, IServiceProvider services, CancellationToken cancellationToken)
        {
            for (int index = 0; index < checks.Length; index++)
            {
                var check = checks[index];
                var task = check(services, cancellationToken);
                if (!task.IsCompletedSuccessfully)
                {
                    return RunChecksAsync(task, index, checks, services, cancellationToken);
                }

                var result = task.Result;
                if (!result.ShouldRun)
                {
                    return ValueTask.FromResult(result);
                }
            }

            return ValueTask.FromResult(JobShouldRunResult.Yes);
        }

        // This needs to be a static method to make sure we avoid capturing the builder instance
        static async ValueTask<JobShouldRunResult> RunChecksAsync(ValueTask<JobShouldRunResult> task, int index, ImmutableArray<Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>> checks, IServiceProvider services, CancellationToken cancellationToken)
        {
            var result = await task;
            if (!result.ShouldRun)
            {
                return result;
            }

            index++;
            for (; index < checks.Length; index++)
            {
                var check = checks[index];
                result = await check(services, cancellationToken);
                if (!result.ShouldRun)
                {
                    return result;
                }
            }

            return JobShouldRunResult.Yes;
        }
    }

    /// <summary>
    /// Creates a check function from the specified builder.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static implicit operator Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>(JobEnabledBuilder builder)
    {
        return builder.ToFunc();
    }
}

/// <summary>
/// Extensions for <see cref="JobEnabledBuilder"/> to allow for a fluent API.
/// </summary>
public static class JobEnabledBuilderExtensions
{
    /// <summary>
    /// Adds a check to the job enabled condition.
    /// </summary>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="check">The check to add.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with <paramref name="check"/> added.</returns>
    public static JobEnabledBuilder WithCheck(this JobEnabledBuilder builder, Func<IServiceProvider, JobShouldRunResult> check)
        => builder.WithCheck((sp, _) => ValueTask.FromResult(check(sp)));

    /// <summary>
    /// Adds a check to the job enabled condition.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="check">The check to add.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with <paramref name="check"/> added.</returns>
    public static JobEnabledBuilder WithCheck<T>(this JobEnabledBuilder builder, Func<T, CancellationToken, ValueTask<JobShouldRunResult>> check)
        where T : notnull
        => builder.WithCheck((sp, ct) => check(sp.GetRequiredService<T>(), ct));

    /// <summary>
    /// Adds a check to the job enabled condition.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="check">The check to add.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with <paramref name="check"/> added.</returns>
    public static JobEnabledBuilder WithCheck<T>(this JobEnabledBuilder builder, Func<T, JobShouldRunResult> check)
        where T : notnull
        => builder.WithCheck((sp, _) => ValueTask.FromResult(check(sp.GetRequiredService<T>())));

    /// <summary>
    /// Adds a check to the job enabled condition that checks the configuration.
    /// </summary>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="check">The check to add.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with <paramref name="check"/> added.</returns>
    public static JobEnabledBuilder WithConfigurationCheck(this JobEnabledBuilder builder, Func<IConfiguration, JobShouldRunResult> check)
        => builder.WithCheck(check);

    /// <summary>
    /// Adds a check to the job enabled condition that checks the configuration for a specific key/value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The configuration value.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with configuration-value check added.</returns>
    public static JobEnabledBuilder WithRequireConfigurationValue<T>(this JobEnabledBuilder builder, string key, T value)
    {
        var reason = $"Configuration key '{key}' does not match the required value '{value}'.";
        return builder.WithConfigurationCheck(config =>
        {
            var actualValue = config.GetValue<T>(key);
            return JobShouldRunResult.Conditional(reason, EqualityComparer<T>.Default.Equals(actualValue, value));
        });
    }

    /// <summary>
    /// Adds a check to the job enabled condition that checks the configuration that a specific key is set to <see langword="true"/>.
    /// </summary>
    /// <param name="builder">The <see cref="JobEnabledBuilder"/>.</param>
    /// <param name="key">The configuration key.</param>
    /// <returns>A new <see cref="JobEnabledBuilder"/> with configuration-value check added.</returns>
    public static JobEnabledBuilder WithRequireConfigurationValueEnabled(this JobEnabledBuilder builder, string key)
        => builder.WithRequireConfigurationValue(key, true);
}
