using System.Collections.Immutable;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;
using Altinn.Register.Jobs;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// Adds a recurring job to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="T">The job type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A configuration delegate.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IJobRegistration AddRecurringJob<T>(this IServiceCollection services, Action<IJobSettings> configure)
        where T : class, IJob<Unit>
        => services.AddRecurringJob<T>(serviceKey: null, configure);

    /// <summary>
    /// Adds a recurring job to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="T">The job type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="serviceKey">A key to use when resolving the job from the service provider.</param>
    /// <param name="configure">A configuration delegate.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IJobRegistration AddRecurringJob<T>(this IServiceCollection services, object? serviceKey, Action<IJobSettings> configure)
        where T : class, IJob<Unit>
    {
        Guard.IsNotNull(services);
        Guard.IsNotNull(configure);

        var settings = new JobSettings(JobRegistration.GetJobNameForType(typeof(T)));
        configure(settings);

        services.TryAddKeyedScoped<T>(serviceKey);
        return services.AddRecurringJob<T>(settings, serviceKey);
    }

    private static IJobRegistration AddRecurringJob<T>(this IServiceCollection services, JobSettings settings, object? serviceKey)
        where T : class, IJob<Unit>
    {
        Guard.IsNotNull(services);
        if (settings.Interval <= TimeSpan.Zero && settings.RunAt == JobHostLifecycles.None)
        {
            ThrowHelper.ThrowArgumentException(nameof(settings), "Interval must be greater than zero or RunAt must be set.");
        }

        var registration = new JobRegistrationFromServiceProvider<T>(settings.JobName, settings.LeaseName, settings.Interval, settings.RunAt, settings.Tags, settings.Enabled, settings.WaitForReady, serviceKey);
        return services.AddRecurringJob(registration);
    }

    /// <summary>
    /// Adds a recurring job to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="registration">The <see cref="JobRegistration"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IJobRegistration AddRecurringJob(this IServiceCollection services, JobRegistration registration)
    {
        Guard.IsNotNull(services);
        Guard.IsNotNull(registration);

        services.AddRecurringJobHostedService();

        services.AddSingleton(registration);
        return registration;
    }

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="T">The job condition type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition<T>(this IServiceCollection services)
        where T : class, IJobCondition
    {
        Guard.IsNotNull(services);

        services.AddRecurringJobHostedService();

        services.Add(ServiceDescriptor.Singleton<IJobCondition, T>());
        return services;
    }

    private static IServiceCollection AddJobCondition(
        IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<IServiceProvider, Func<CancellationToken, ValueTask<bool>>> shouldRunFactory)
    {
        Guard.IsNotNull(services);
        Guard.IsNotNullOrWhiteSpace(name);
        Guard.IsNotNull(tags);
        Guard.IsNotNull(shouldRunFactory);

        services.AddRecurringJobHostedService();

        services.AddSingleton<IJobCondition>(s =>
        {
            var shouldRun = shouldRunFactory(s);

            return new FuncCondition(name, tags, shouldRun);
        });

        return services;
    }

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<CancellationToken, ValueTask<bool>> shouldRun)
        => AddJobCondition(
            services,
            name,
            tags,
            _ => shouldRun);

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<bool> shouldRun)
        => AddJobCondition(
            services,
            name,
            tags,
            _ => _ => ValueTask.FromResult(shouldRun()));

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition<T>(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<T, CancellationToken, ValueTask<bool>> shouldRun)
        where T : notnull
        => AddJobCondition(
            services,
            name,
            tags,
            s =>
            {
                var dep = s.GetRequiredService<T>();
                return (cancellationToken) => shouldRun(dep, cancellationToken);
            });

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition<T>(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<T, bool> shouldRun)
        where T : notnull
        => AddJobCondition(
            services,
            name,
            tags,
            s =>
            {
                var dep = s.GetRequiredService<T>();
                return _ => ValueTask.FromResult(shouldRun(dep));
            });

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition<T1, T2>(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<T1, T2, CancellationToken, ValueTask<bool>> shouldRun)
        where T1 : notnull
        where T2 : notnull
        => AddJobCondition(
            services,
            name,
            tags,
            s =>
            {
                var dep1 = s.GetRequiredService<T1>();
                var dep2 = s.GetRequiredService<T2>();
                return (cancellationToken) => shouldRun(dep1, dep2, cancellationToken);
            });

    /// <summary>
    /// Adds a job condition to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The name of the condition.</param>
    /// <param name="tags">The job-tags the condition targets.</param>
    /// <param name="shouldRun">The function that determines if the job condition should run.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddJobCondition<T1, T2>(
        this IServiceCollection services,
        string name,
        IEnumerable<string> tags,
        Func<T1, T2, bool> shouldRun)
        where T1 : notnull
        where T2 : notnull
        => AddJobCondition(
            services,
            name,
            tags,
            s =>
            {
                var dep1 = s.GetRequiredService<T1>();
                var dep2 = s.GetRequiredService<T2>();
                return _ => ValueTask.FromResult(shouldRun(dep1, dep2));
            });

    /// <summary>
    /// Adds the job-runner as a hosted service.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddRecurringJobHostedService(this IServiceCollection services)
    {
        Guard.IsNotNull(services);

        if (services.Contains(Marker.ServiceDescriptor))
        {
            // already registered
            return services;
        }

        services.Add(Marker.ServiceDescriptor);
        services.TryAddSingleton<JobsTelemetry>();
        services.AddHostedService<RecurringJobHostedService>();
        services.AddOptions<RecurringJobHostedSettings>()
            .Configure((RecurringJobHostedSettings settings, IConfiguration configuration) =>
            {
                // TODO: this should be a const originating in ServiceDefaults
                if (configuration.GetValue("Altinn:RunInitOnly", defaultValue: false))
                {
                    settings.DisableScheduler = true;
                }
            });

        services.AddOpenTelemetry()
            .WithTracing(t => t.AddSource(JobsTelemetry.Name))
            .WithMetrics(t => t.AddMeter(JobsTelemetry.Name));

        return services;
    }

    private sealed class JobRegistrationFromServiceProvider<T>(
        string jobName,
        string? leaseName,
        TimeSpan? interval,
        JobHostLifecycles runAt,
        IEnumerable<string> tags,
        Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
        Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady,
        object? serviceKey)
        : JobRegistration<Unit>(
            jobName,
            leaseName,
            interval is null ? null : new ConstantDelayStrategy<Unit>(interval.Value),
            runAt,
            tags,
            enabled,
            waitForReady)
        where T : IJob<Unit>
    {
        protected sealed override IJob<Unit> Create(IServiceProvider services)
            => services.GetRequiredKeyedService<T>(serviceKey);
    }

    private sealed class JobSettings(string jobName)
        : IJobSettings
    {
        /// <inheritdoc/>
        public string JobName { get; set; } = jobName;

        /// <inheritdoc/>
        public string? LeaseName { get; set; } = null;

        /// <inheritdoc/>
        public TimeSpan? Interval { get; set; } = null;

        /// <inheritdoc/>
        public JobHostLifecycles RunAt { get; set; } = JobHostLifecycles.None;

        /// <inheritdoc/>
        public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? Enabled { get; set; } = null;

        /// <inheritdoc/>
        public Func<IServiceProvider, CancellationToken, ValueTask>? WaitForReady { get; set; } = null;
    }

    private sealed class FuncCondition(
        string name,
        IEnumerable<string> tags,
        Func<CancellationToken, ValueTask<bool>> shouldRun)
        : IJobCondition
    {
        public string Name { get; } = name;

        public ImmutableArray<string> JobTags { get; } = [.. tags];

        public ValueTask<bool> ShouldRun(CancellationToken cancellationToken = default)
        {
            return shouldRun(cancellationToken);
        }
    }

    private sealed class Marker
    {
        public static readonly ServiceDescriptor ServiceDescriptor = ServiceDescriptor.Singleton<Marker, Marker>();
    }
}
