using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;
using Altinn.Authorization.ServiceDefaults.StorageQueues;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Utils;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class StorageQueuesServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds a storage queue job to the service collection.
        /// </summary>
        /// <typeparam name="T">The handler type.</typeparam>
        /// <param name="name">The job name.</param>
        /// <param name="configure">A delegate to configure the job settings.</param>
        /// <returns>The job registration.</returns>
        /// <remarks>
        /// The <paramref name="name"/> parameter is used to look up the storage queue settings.
        /// </remarks>
        public OptionsBuilder<StorageQueueSettings> AddStorageQueueJob<T>(
            string name,
            Action<IQueueJobSettings> configure)
            where T : class, IStorageQueueMessageHandler
        {
            var settings = new QueueJobSettings();
            configure(settings);

            var builder = services.AddStorageQueue(name);

            services.TryAddScoped<T>();
            services.AddRecurringJob(new QueueJobRegistration<T>(
                name: name,
                minimumInterval: settings.MinimumInterval,
                maximumInterval: settings.MaximumInterval,
                deltaBackoff: settings.DeltaBackoff ?? settings.MinimumInterval,
                tags: settings.Tags,
                enabled: settings.Enabled,
                waitForReady: settings.WaitForReady));

            return builder;
        }

        /// <summary>
        /// Adds a storage queue client factory and configures storage queue settings for the specified name.
        /// </summary>
        /// <param name="name">The name of the storage queue.</param>
        /// <returns>The options builder for the storage queue settings.</returns>
        public OptionsBuilder<StorageQueueSettings> AddStorageQueue(string name)
        {
            services.AddStorageQueueSenderFactory();

            var builder = services.AddOptions<StorageQueueSettings>(name)
                .ValidateDataAnnotations();

            return builder;
        }

        /// <summary>
        /// Adds a storage queue message sender factory to the service collection, along with its dependencies.
        /// </summary>
        /// <returns><paramref name="services"/>.</returns>
        public IServiceCollection AddStorageQueueSenderFactory()
        {
            services.AddTokenCredentialProvider();
            services.TryAddSingleton<StorageQueueFactory>();
            services.TryAddSingleton<IStorageQueueClientFactory, DefaultStorageQueueClientFactory>();
            services.TryAddSingleton<IStorageQueueMessageSenderFactory>(s => s.GetRequiredService<StorageQueueFactory>());

            return services;
        }
    }

    private sealed class QueueJobRegistration<T>
        : JobRegistration<StorageQueuePollJobRunResult>
        where T : class, IStorageQueueMessageHandler
    {
        private static readonly ObjectFactory<StorageQueuePollJob<T>> _factory
            = ActivatorUtilities.CreateFactory<StorageQueuePollJob<T>>([typeof(StorageQueueReceiver)]);

        private readonly string _name;

        public QueueJobRegistration(
            string name,
            TimeSpan minimumInterval,
            TimeSpan maximumInterval,
            TimeSpan deltaBackoff,
            IEnumerable<string> tags,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
            : base(
                name: $"storage-queue-poll: {name}",
                leaseName: null, // all instances can process queue message
                delayStrategy: new RandomizedExponentialBackoffStrategy(minimumInterval, maximumInterval, deltaBackoff),
                runAt: JobHostLifecycles.None, // only run as a scheduled job, not on startup or shutdown
                tags: tags,
                enabled: enabled,
                waitForReady: waitForReady)
        {
            _name = name;
        }

        protected override IJob<StorageQueuePollJobRunResult> Create(IServiceProvider services)
        {
            var factory = services.GetRequiredService<StorageQueueFactory>();
            var receiver = factory.CreateReceiver(_name);

            return _factory(services, [receiver]);
        }
    }

    /// <summary>
    /// A delay strategy that implements a randomized exponential backoff algorithm, with the ability to reset the backoff when certain job results are observed.
    /// </summary>
    internal sealed class RandomizedExponentialBackoffStrategy
        : IDelayStrategy<StorageQueuePollJobRunResult>
    {
        private static readonly TimeSpan SkippedDelay = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The maximum randomization factor to apply to the calculated backoff interval.
        /// </summary>
        public const double RandomizationFactor = 0.2;

        private readonly TimeSpan _minimumInterval;
        private readonly TimeSpan _maximumInterval;
        private readonly TimeSpan _deltaBackoff;

        private readonly Random _random;

        private TimeSpan _currentInterval;
        private uint _backoffExponent;

        /// <summary>
        /// Initializes a new instance of the <see cref="RandomizedExponentialBackoffStrategy"/> class.
        /// </summary>
        /// <param name="minimumInterval">The minimum interval between job executions.</param>
        /// <param name="maximumInterval">The maximum interval between job executions.</param>
        /// <param name="deltaBackoff">The delta backoff interval.</param>
        public RandomizedExponentialBackoffStrategy(
            TimeSpan minimumInterval,
            TimeSpan maximumInterval,
            TimeSpan deltaBackoff)
        {
            Guard.IsGreaterThan(minimumInterval, TimeSpan.Zero);
            Guard.IsGreaterThan(maximumInterval, TimeSpan.Zero);
            Guard.IsGreaterThan(deltaBackoff, TimeSpan.Zero);

            if (minimumInterval.Ticks > maximumInterval.Ticks)
            {
                ThrowHelper.ThrowArgumentException(nameof(minimumInterval), "The minimumInterval must not be greater than the maximumInterval.");
            }

            _minimumInterval = minimumInterval;
            _maximumInterval = maximumInterval;
            _deltaBackoff = deltaBackoff;
            _random = new();
        }

        /// <inheritdoc/>
        public string Description
            => $"randomized-exponential-backoff (min: {_minimumInterval}, max: {_maximumInterval}, delta: {_deltaBackoff})";

        /// <inheritdoc/>
        public TimeSpan GetDelay(JobOutcome<StorageQueuePollJobRunResult> outcome)
        {
            if (outcome is { IsJobSuccess: true, Result: StorageQueuePollJobRunResult.MultiplePages })
            {
                // we received multiple pages of messages, so there is likely to be more work to do shortly. Delay the minimum amount of time.
                _currentInterval = _minimumInterval;
                _backoffExponent = 1;
            }
            else if (outcome.IsJobSkipped)
            {
                _currentInterval = _maximumInterval;
                return SkippedDelay;
            }
            else if (

                // if there is exactly 1 page of messages, we assume that we're in somewhat of an equalibrium, so we keep the current delay.
                outcome is not { IsJobSuccess: true, Result: StorageQueuePollJobRunResult.SinglePage }

                // if the currentInterval is already at the maximum, we also keep it
                && _currentInterval != _maximumInterval)
            {
                TimeSpan backoffInterval = _minimumInterval;

                if (_backoffExponent > 0)
                {
                    double incrementMsec = _random.Next(1.0 - RandomizationFactor, 1.0 + RandomizationFactor)
                        * Math.Pow(2.0, _backoffExponent - 1)
                        * _deltaBackoff.TotalMilliseconds;

                    backoffInterval += TimeSpan.FromMilliseconds(incrementMsec);
                }

                if (backoffInterval < _maximumInterval)
                {
                    _currentInterval = backoffInterval;
                    _backoffExponent++;
                }
                else
                {
                    _currentInterval = _maximumInterval;
                }
            }

            return _currentInterval;
        }
    }

    private sealed class QueueJobSettings()
        : IQueueJobSettings
    {
        /// <inheritdoc/>
        public ISet<string> Tags { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? Enabled { get; set; } = null;

        /// <inheritdoc/>
        public Func<IServiceProvider, CancellationToken, ValueTask>? WaitForReady { get; set; } = null;

        /// <inheritdoc/>
        /// <remarks>
        /// The default value of 100ms is taken from Azure Function's backoff strategy.
        /// </remarks>
        public TimeSpan MinimumInterval { get; set; }
            = TimeSpan.FromMilliseconds(100);

        /// <inheritdoc/>
        /// <remarks>
        /// The default value of 1 minute is taken from Azure Function's backoff strategy.
        /// </remarks>
        public TimeSpan MaximumInterval { get; set; }
            = TimeSpan.FromMinutes(1);

        /// <inheritdoc/>
        public TimeSpan? DeltaBackoff { get; set; }
    }
}
