using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.Leases;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Jobs;

/// <summary>
/// A hosted service that runs recurring jobs.
/// </summary>
internal sealed partial class RecurringJobHostedService
    : IHostedLifecycleService
    , IAsyncDisposable
{
    private readonly Counter<int> _jobsStarted;
    private readonly Counter<int> _jobsFailed;
    private readonly Counter<int> _jobsSucceeded;
    private readonly Counter<int> _jobsSkipped;

    private readonly AltinnServiceDescriptor _serviceDescriptor;
    private readonly TimeProvider _timeProvider;
    private readonly LeaseManager _leaseManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RecurringJobHostedService> _logger;
    private readonly ImmutableArray<JobRegistration> _registrations;
    private readonly ImmutableArray<IJobCondition> _conditions;
    private readonly IOptionsMonitor<RecurringJobHostedSettings> _settings;
    private Task? _schedulerTask;
    private CancellationTokenSource? _stoppingCts;

    private readonly ScheduledJobTracker _tracker;
    private readonly DisposeHelper _dispose = new(nameof(RecurringJobHostedService));

    /// <summary>
    /// Wait for all scheduled jobs that are currently running to complete.
    /// </summary>
    /// <remarks>
    /// For test use only. Only 1 of these can exist at once (per instance of <see cref="RecurringJobHostedService"/>).
    /// If multiple tasks needs to wait for all scheduled running jobs, use the <see cref="ValueTask.AsTask"/> method
    /// to get a <see cref="Task"/> that can safely be shared.
    /// </remarks>
    internal ValueTask WaitForRunningScheduledJobs()
        => _tracker.WaitForRunningScheduledJobs();

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobHostedService"/> class.
    /// </summary>
    public RecurringJobHostedService(
        AltinnServiceDescriptor serviceDescriptor,
        JobsTelemetry telemetry,
        TimeProvider timeProvider,
        LeaseManager leaseManager,
        IServiceProvider serviceProvider,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RecurringJobHostedService> logger,
        IEnumerable<JobRegistration> registrations,
        IEnumerable<IJobCondition> conditions,
        IOptionsMonitor<RecurringJobHostedSettings> settings)
    {
        _jobsStarted = telemetry.CreateCounter<int>("register.jobs.started", unit: "jobs", description: "The number of jobs that have been started");
        _jobsFailed = telemetry.CreateCounter<int>("register.jobs.failed", unit: "jobs", description: "The number of jobs that have failed");
        _jobsSucceeded = telemetry.CreateCounter<int>("register.jobs.succeeded", unit: "jobs", description: "The number of jobs that have succeeded");
        _jobsSkipped = telemetry.CreateCounter<int>("register.jobs.skipped", unit: "jobs", description: "The number of jobs that have been skipped");

        _serviceDescriptor = serviceDescriptor;
        _timeProvider = timeProvider;
        _leaseManager = leaseManager;
        _serviceProvider = serviceProvider;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _settings = settings;
        _registrations = registrations.ToImmutableArray();
        _conditions = conditions.ToImmutableArray();
        _tracker = new ScheduledJobTracker(_timeProvider, _logger);

        foreach (var registration in _registrations)
        {
            Log.RegisteredJob(_logger, registration.JobName, registration.DelayStrategyDescription, registration.LeaseName, registration.RunAt);
        }
    }

    /// <inheritdoc/>
    public Task StartingAsync(CancellationToken cancellationToken)
        => RunLifecycleJobs(JobHostLifecycles.Starting, cancellationToken);

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken)
        => RunLifecycleJobs(JobHostLifecycles.Started, cancellationToken);

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken)
        => RunLifecycleJobs(JobHostLifecycles.Stopping, cancellationToken);

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken)
        => RunLifecycleJobs(JobHostLifecycles.Stopped, cancellationToken);

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // First, run all lifecycle jobs that should run at the start of the host
        await RunLifecycleJobs(JobHostLifecycles.Start, cancellationToken);

        if (_serviceDescriptor.RunInitOnly || _settings.CurrentValue.DisableScheduler)
        {
            Log.SchedulerDisabled(_logger);
            return;
        }

        // Create linked token to allow cancelling executing task from provided token
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Store the task we're executing
        _schedulerTask = RunScheduler(_stoppingCts.Token);

        // If the task is completed then await it, this will bubble cancellation and failure to the caller
        if (_schedulerTask.IsCompleted)
        {
            await _schedulerTask;
        }

        // Otherwise it's running
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // https://github.com/dotnet/aspnetcore/issues/40271
        if (_dispose.IsDisposed)
        {
            return;
        }

        // First, stop the scheduler
        await StopScheduler(cancellationToken);

        // Then, run lifecycle jobs
        await RunLifecycleJobs(JobHostLifecycles.Stop, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
        => _dispose.DisposeAsync(
            this,
            static async (self) =>
            {
                if (self._stoppingCts is not null)
                {
                    await self._stoppingCts.CancelAsync().WaitAsync(TimeSpan.FromMinutes(1));
                }

                if (self._schedulerTask is not null)
                {
                    await self._schedulerTask.WaitAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }

                self._stoppingCts?.Dispose();
                self._stoppingCts = null;

                self._tracker.Dispose();
            });

    private async Task StopScheduler(CancellationToken cancellationToken)
    {
        // Stop called without start, or scheduler was disabled
        if (_schedulerTask is null)
        {
            return;
        }

        try
        {
            await _stoppingCts!.CancelAsync().WaitAsync(TimeSpan.FromMinutes(1), cancellationToken);
        }
        finally
        {
            await _schedulerTask.WaitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task RunLifecycleJobs(
        JobHostLifecycles lifecycle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // https://github.com/dotnet/aspnetcore/issues/40271
        if (_dispose.IsDisposed)
        {
            return;
        }

        var lifecycleString = lifecycle.ToString();
        using var lifecycleActivity = JobsTelemetry.StartActivity($"run {lifecycleString} lifecycle jobs", ActivityKind.Internal);

        var registrations = WhenReady(_registrations.Where(r => r.RunAt.HasFlag(lifecycle)), cancellationToken);
        await foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<KeyValuePair<string, object?>> tags = [
                new("job.name", registration.JobName),
                new("job.source", lifecycleString),
                new("job.lease", null),
            ];

            var isEnabled = await IsEnabled(registration, throwOnException: true, tags, cancellationToken);
            if (!isEnabled)
            {
                continue;
            }

            if (registration.LeaseName is null)
            {
                await RunJob(registration, throwOnException: true, tags, cancellationToken);
            }
            else if (lifecycle == JobHostLifecycles.Starting)
            {
                ThrowHelper.ThrowInvalidOperationException("Cannot use leases at the starting lifecycle point, as database migrations might not have run yet");
            }
            else
            {
                await using var lease = await _leaseManager.AcquireLease(registration.LeaseName, cancellationToken);
                if (lease.Acquired)
                {
                    await RunJob(registration, throwOnException: true, tags, lease.Token);
                }
                else
                {
                    Log.LeaseNotAcquired(_logger, jobName: registration.JobName, registration.LeaseName, lifecycle);
                }
            }
        }
    }

    private Task<JobRunResult> RunJob(JobRegistration registration, bool throwOnException, ImmutableArray<KeyValuePair<string, object?>> tags, CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                Activity.Current = null;
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                using var outerActivity = JobsTelemetry.StartActivity($"job {registration.JobName}", ActivityKind.Internal, tags: tags.AsSpan());

                var telemetry = new JobTelemetry(this, registration, tags);
                var result = await registration.RunAsync(scope.ServiceProvider, telemetry, cancellationToken);

                if (throwOnException)
                {
                    result.RethrowIfFailure();
                }

                return result;
            },
            cancellationToken);
    }

    private async ValueTask<bool> IsEnabled(
        JobRegistration registration,
        bool throwOnException,
        ImmutableArray<KeyValuePair<string, object?>> tags,
        CancellationToken cancellationToken)
    {
        using var activity = JobsTelemetry.StartActivity($"check job-registration enabled", ActivityKind.Internal, tags: tags.AsSpan());

        foreach (var condition in _conditions)
        {
            if (!AppliesTo(condition, registration))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool canRun;
            using var conditionActivity = JobsTelemetry.StartActivity(
                $"check job-registration condition",
                ActivityKind.Internal,
                tags: [
                    .. tags.AsSpan(),
                    new("condition.name", condition.Name),
                ]);
            try
            {
                canRun = await condition.ShouldRun(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.JobConditionFailed(_logger, jobName: registration.JobName, condition.Name, e);
                conditionActivity?.SetStatus(ActivityStatusCode.Error);

                if (throwOnException)
                {
                    throw;
                }

                return false;
            }

            if (!canRun)
            {
                Log.JobBlockedByCondition(_logger, jobName: registration.JobName, condition.Name);

                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var enabledResult = await registration.Enabled(_serviceProvider, cancellationToken);

            if (!enabledResult.ShouldRun)
            {
                Log.JobIsDisabledByDelegate(_logger, jobName: registration.JobName, reason: enabledResult.Reason);
            }

            return enabledResult.ShouldRun;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Log.JobIsEnabledFailed(_logger, jobName: registration.JobName, e);

            if (throwOnException)
            {
                throw;
            }

            return false;
        }

        static bool AppliesTo(IJobCondition condition, JobRegistration registration)
        {
            foreach (var tag in condition.JobTags)
            {
                if (!registration.HasTag(tag))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private Task RunScheduler(CancellationToken cancellationToken)
    {
        var registrations = _registrations.Where(r => r.RunAt.HasFlag(JobHostLifecycles.Scheduled));
        var tasks = new List<Task>(_registrations.Length);

        foreach (var registration in registrations)
        {
            Log.CreatingSchedulerForJob(_logger, registration.JobName, registration.DelayStrategyDescription, registration.LeaseName);
            var scheduler = _tracker.CreateScheduler(registration.JobName);
            var leaseName = registration.LeaseName;
            if (leaseName is null)
            {
                tasks.Add(RunNormalScheduler(scheduler, registration, cancellationToken));
            }
            else
            {
                tasks.Add(RunLeaseScheduler(scheduler, leaseName, registration, cancellationToken));
            }
        }

        return Task.WhenAll(tasks);
    }

    private async Task RunNormalScheduler(
        JobScheduler scheduler,
        JobRegistration registration,
        CancellationToken cancellationToken)
    {
#pragma warning disable SA1312 // Variable names should begin with lower-case letter
        using var _scheduler = scheduler;
#pragma warning restore SA1312 // Variable names should begin with lower-case letter

        Exception? exn = null;
        ImmutableArray<KeyValuePair<string, object?>> tags = [
            new("job.name", registration.JobName),
            new("job.source", "scheduler"),
            new("job.lease", null),
        ];

        try
        {
            var start = _timeProvider.GetTimestamp();

            using (JobsTelemetry.StartActivity($"wait-for {registration.JobName} ready", kind: ActivityKind.Internal, tags: tags.AsSpan()))
            {
                await scheduler.WaitForReady(registration, _serviceProvider, cancellationToken);
            }

            var elapsed = _timeProvider.GetElapsedTime(start);
            var delay = registration.GetDelay(JobOutcome.None);

            while (!cancellationToken.IsCancellationRequested)
            {
                // we wait first - if the job should also be ran at startup, set RunAt to the appropriate lifecycle
                await scheduler.Sleep(delay - elapsed, cancellationToken).ConfigureAwait(false);
                elapsed = TimeSpan.Zero;

                // we check if the job is enabled. If it isn't, we sleep for 10x the interval to reduce usage for disabled jobs.
                // this still allows enabling jobs via feature-flags without having to restart the host.
                var isEnabled = await IsEnabled(registration, throwOnException: false, tags, cancellationToken);
                if (!isEnabled)
                {
                    delay = registration.GetDelay(JobOutcome.Disabled);
                    continue;
                }

                // run the job
                var result = await RunJob(registration, throwOnException: false, tags, cancellationToken);
                delay = result.Delay;
            }
        }
        catch (Exception e)
        {
            exn = e;
            scheduler.Poison(ExceptionDispatchInfo.Capture(e));
        }
        finally
        {
            Log.SchedulerExited(_logger, registration.JobName, exn);
        }
    }

    private async Task RunLeaseScheduler(
        JobScheduler scheduler,
        string leaseName,
        JobRegistration registration,
        CancellationToken cancellationToken)
    {
#pragma warning disable SA1312 // Variable names should begin with lower-case letter
        using var _scheduler = scheduler;
#pragma warning restore SA1312 // Variable names should begin with lower-case letter

        Exception? exn = null;
        ImmutableArray<KeyValuePair<string, object?>> tags = [
            new("job.name", registration.JobName),
            new("job.source", "lease-scheduler"),
            new("job.lease", leaseName),
        ];

        try
        {
            using (JobsTelemetry.StartActivity($"wait-for {registration.JobName} ready", kind: ActivityKind.Internal, tags: tags.AsSpan()))
            {
                await scheduler.WaitForReady(registration, _serviceProvider, cancellationToken);
            }

            var defaultDelay = registration.GetDelay(JobOutcome.None);
            var delay = defaultDelay;
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset lastCompleted;

                {
                    // check if the job is enabled. If it isn't, we can skip the lease acquisition.
                    var isEnabled = await IsEnabled(registration, throwOnException: false, tags, cancellationToken);
                    if (!isEnabled)
                    {
                        // here, we just sleep for 10x the interval to reduce usage for disabled jobs.
                        // this still allows enabling jobs via feature-flags without having to restart the host.
                        delay = registration.GetDelay(JobOutcome.Disabled);
                        await scheduler.Sleep(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // we try to grab the lease first, as it might have been longer than `interval` time since we last ran
                    await using var lease = await _leaseManager.AcquireLease(
                        leaseName,
                        ifUnacquiredFor: delay,
                        cancellationToken);

                    if (lease.Acquired)
                    {
                        // if we acquired the lease, run the job
                        var result = await RunJob(registration, throwOnException: false, tags, lease.Token);
                        delay = result.Delay;

                        var releaseResult = await lease.Release(cancellationToken);

                        Debug.Assert(releaseResult is not null);
                        if (releaseResult is { IsReleased: true, LastReleasedAt: { } lastReleasedAt })
                        {
                            lastCompleted = lastReleasedAt;
                        }
                        else
                        {
                            lastCompleted = releaseResult.Expires;
                        }
                    }
                    else
                    {
                        Log.LeaseNotAcquired(_logger, registration.JobName, leaseName, JobHostLifecycles.None);
                        delay = defaultDelay;

                        // else record when it was last ran
                        lastCompleted = lease.Expires;
                        if (lease.LastReleasedAt.HasValue && lease.LastReleasedAt > lastCompleted)
                        {
                            lastCompleted = lease.LastReleasedAt.Value;
                        }
                    }
                }

                // calculate when next the job should run, and wait for that, then loop
                var now = _timeProvider.GetUtcNow();
                var nextStart = lastCompleted + delay + RandomJitter();
                var tilThen = nextStart - now;

                if (tilThen > TimeSpan.Zero)
                {
                    // note, we grab the new delay task here to make sure we schedule the next run before releasing the tracker
                    Log.Sleep(_logger, registration.JobName, tilThen);
                    await scheduler.Sleep(tilThen, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            exn = e;
            scheduler.Poison(ExceptionDispatchInfo.Capture(e));
        }
        finally
        {
            Log.SchedulerExited(_logger, registration.JobName, exn);
        }
    }

    private async IAsyncEnumerable<JobRegistration> WhenReady(IEnumerable<JobRegistration> registrations, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<Task<JobRegistration>>? pending = null;

        foreach (var registration in registrations)
        {
            var ready = registration.WaitForReady(_serviceProvider, cancellationToken);
            if (!ready.IsCompleted)
            {
                pending ??= [];
                pending.Add(WaitFor(ready, registration));

                continue;
            }

            await ready; // propagates exceptions
            yield return registration;
        }

        if (pending is null)
        {
            yield break;
        }

        await foreach (var complete in Task.WhenEach(pending))
        {
            yield return await complete;
        }

        [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Task is not foreign")]
        static async Task<JobRegistration> WaitFor(Task ready, JobRegistration registration)
        {
            await ready;
            return registration;
        }
    }

    private static TimeSpan RandomJitter()
    {
        // add between 1 and 2 seconds of random jitter to avoid thundering herd issues
        var randomMs = Random.Shared.Next(1_000, 2_000);
        return TimeSpan.FromMilliseconds(randomMs);
    }

    private sealed class JobTelemetry(
        RecurringJobHostedService service,
        JobRegistration registration,
        ImmutableArray<KeyValuePair<string, object?>> tags)
        : IJobRunTelemetry
    {
        public void JobCompleted(TimeSpan duration)
        {
            service._jobsSucceeded.Add(1, tags.AsSpan());
            Log.JobCompleted(service._logger, registration.JobName, duration);
        }

        public void JobCreationFailed(Exception exception)
        {
            service._jobsFailed.Add(1, tags.AsSpan());
            Log.JobCreationFailed(service._logger, registration.JobName, exception);
        }

        public void JobFailed(Exception exception)
        {
            service._jobsFailed.Add(1, tags.AsSpan());
            Log.JobFailed(service._logger, registration.JobName, exception);
        }

        public void JobSkipped(string reason)
        {
            service._jobsSkipped.Add(1, tags.AsSpan());
            Log.JobSkipped(service._logger, registration.JobName, reason);
        }

        public void JobStarting()
        {
            service._jobsStarted.Add(1, tags.AsSpan());
            Log.JobStarting(service._logger, registration.JobName);
        }

        public void JobDisposalFailed(Exception exception)
        {
            Log.JobDisposalFailed(service._logger, registration.JobName, exception);
        }

        public Activity? StartRun()
        {
            return JobsTelemetry.StartActivity($"run job {registration.JobName}", ActivityKind.Internal, tags: tags.AsSpan());
        }

        public Activity? StartShouldRun()
        {
            return JobsTelemetry.StartActivity($"should run job {registration.JobName}", ActivityKind.Internal, tags: tags.AsSpan());
        }
    }

    private sealed class ScheduledJobTracker
        : IValueTaskSource
        , IDisposable
    {
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Lock _lock = new();
        private ManualResetValueTaskSourceCore<object?> _source; // mutable struct; do not make this readonly
        private int _totalSchedulers;
        private int _awakeSchedulers;
        private ExceptionDispatchInfo? _poison;

        public ScheduledJobTracker(TimeProvider timeProvider, ILogger logger)
        {
            _timeProvider = timeProvider;
            _logger = logger;

            // initially set
            _source.SetResult(null);
            _source.RunContinuationsAsynchronously = true;
        }

        public JobScheduler CreateScheduler(string jobName)
        {
            lock (_lock)
            {
                _totalSchedulers++;
            }

            return new(this, _timeProvider, _logger, jobName);
        }

        public void Poison(ExceptionDispatchInfo exception)
        {
            lock (_lock)
            {
                _poison = exception;
                _source.SetException(exception.SourceException);
            }
        }

        public void TrackAwake()
        {
            lock (_lock)
            {
                if (_awakeSchedulers == 0)
                {
                    _source.Reset();
                }

                _awakeSchedulers++;
                Debug.Assert(_awakeSchedulers <= _totalSchedulers);
            }
        }

        public void TrackSleeping()
        {
            lock (_lock)
            {
                _awakeSchedulers--;
                Debug.Assert(_awakeSchedulers >= 0);

                if (_awakeSchedulers == 0)
                {
                    _source.SetResult(null);
                }
            }
        }

        /// <remarks>
        /// This method should only be called by schedulers that are in the awake state.
        /// </remarks>
        public void Unregister(string jobName)
        {
            Log.SchedulerDisposed(_logger, jobName);

            lock (_lock)
            {
                _awakeSchedulers--;
                _totalSchedulers--;
                Debug.Assert(_totalSchedulers >= 0);
                Debug.Assert(_awakeSchedulers >= 0);
                Debug.Assert(_awakeSchedulers <= _totalSchedulers);
            }
        }

        public void Dispose()
        {
            _source.Reset();
            Debug.Assert(_awakeSchedulers == 0);
            Debug.Assert(_totalSchedulers == 0);
        }

        public ValueTask WaitForRunningScheduledJobs()
        {
            if (_poison is not null)
            {
                _poison.Throw();
            }

            return new(this, _source.Version);
        }

        void IValueTaskSource.GetResult(short token)
            => _source.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
            => _source.GetStatus(token);

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _source.OnCompleted(continuation, state, token, flags);
    }

    private sealed class JobScheduler
        : IValueTaskSource
        , IDisposable
    {
        private const byte STATE_RUNNING = 0;
        private const byte STATE_SLEEPING = 1;
        private const byte STATE_DISPOSED = 2;
        private const byte STATE_REGISTERING = 3;

        private static readonly TimerCallback _timerCallback = static state =>
        {
            var timer = (JobScheduler)state!;
            timer.TimerCallback();
        };

        private static readonly Action<object?, CancellationToken> _cancellationCallback = static (state, ct) =>
        {
            var timer = (JobScheduler)state!;
            timer.CancellationCallback(ct);
        };

        private readonly Lock _lock = new();
        private readonly ILogger _logger;
        private readonly string _jobName;
        private readonly ITimer _timer;
        private readonly ScheduledJobTracker _tracker;
        private readonly TimeProvider _timeProvider;
        private ManualResetValueTaskSourceCore<object?> _source; // mutable struct; do not make this readonly
        private CancellationTokenRegistration _cancellationRegistration;
        private byte _state = STATE_RUNNING;

        public JobScheduler(ScheduledJobTracker tracker, TimeProvider timeProvider, ILogger logger, string jobName)
        {
            _jobName = jobName;
            _timer = timeProvider.CreateTimer(_timerCallback, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timeProvider = timeProvider;
            _logger = logger;
            _tracker = tracker;
            _tracker.TrackAwake();
        }

        public async Task Sleep(TimeSpan duration, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetTimestamp();

            try
            {
                Log.StartSleep(_logger, _jobName, duration, cancellationToken.CanBeCanceled);
                await SleepCore(duration, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                var elapsed = _timeProvider.GetElapsedTime(now);
                Log.RecordSleep(_logger, _jobName, expectedDuration: duration, actualDuration: elapsed, cancellationToken.IsCancellationRequested);
            }
        }

        public void Poison(ExceptionDispatchInfo exceptionDispatchInfo)
        {
            _tracker.Poison(exceptionDispatchInfo);
        }

        private ValueTask SleepCore(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (duration <= TimeSpan.Zero)
            {
                return ValueTask.CompletedTask;
            }

            _source.Reset();
            _timer.Change(duration, Timeout.InfiniteTimeSpan);
            if (cancellationToken.CanBeCanceled)
            {
                lock (_lock)
                {
                    Debug.Assert(_state == STATE_RUNNING);
                    _state = STATE_REGISTERING;
                }

                _cancellationRegistration = cancellationToken.UnsafeRegister(_cancellationCallback, this);

                lock (_lock)
                {
                    Debug.Assert(_state == STATE_REGISTERING);
                    _state = STATE_RUNNING;
                }
            }

            return new(this, _source.Version);
        }

        public ValueTask WaitForReady(JobRegistration registration, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var task = registration.WaitForReady(serviceProvider, cancellationToken);
            if (task.IsCompleted)
            {
                return new(task);
            }

            var outerTask = WaitFor(this, task, cancellationToken);
            return new(outerTask);

            [SuppressMessage("Usage", "VSTHRD003:Avoid awaiting foreign Tasks", Justification = "Not a foreign task")]
            static async Task WaitFor(JobScheduler scheduler, Task inner, CancellationToken cancellationToken)
            {
                lock (scheduler._lock)
                {
                    // this should be the first thing that happens to a scheduler, and schedulers starts as running
                    Debug.Assert(scheduler._state == STATE_RUNNING);
                    scheduler._state = STATE_SLEEPING;
                    scheduler._tracker.TrackSleeping();
                }

                try
                {
                    await inner;
                }
                finally
                {
                    lock (scheduler._lock)
                    {
                        // still nothing should have invoked the scheduler
                        Debug.Assert(scheduler._state == STATE_SLEEPING);
                        scheduler._state = STATE_RUNNING;
                        scheduler._tracker.TrackAwake();
                    }
                }
            }
        }

        private void TimerCallback()
        {
            var transition = false;
            lock (_lock)
            {
                if (_state == STATE_SLEEPING)
                {
                    _state = STATE_RUNNING;
                    transition = true;
                    _tracker.TrackAwake();
                }
            }

            if (transition)
            {
                _cancellationRegistration.Unregister();
                _source.SetResult(null);
            }
        }

        private void CancellationCallback(CancellationToken cancellationToken)
        {
            var transition = false;
            lock (_lock)
            {
                if (_state == STATE_SLEEPING)
                {
                    _state = STATE_RUNNING;
                    transition = true;
                    _tracker.TrackAwake();
                }
                else if (_state == STATE_REGISTERING)
                {
                    // this happens if cancellation was requested before we started sleeping
                    transition = true;
                }
            }

            if (transition)
            {
                _cancellationRegistration.Unregister();
                _source.SetException(new OperationCanceledException(cancellationToken));
            }
        }

        void IValueTaskSource.GetResult(short token)
            => _source.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
            => _source.GetStatus(token);

        void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            lock (_lock)
            {
                if (_state == STATE_RUNNING)
                {
                    _state = STATE_SLEEPING;
                    _tracker.TrackSleeping();
                }
            }

            _source.OnCompleted(continuation, state, token, flags);
        }

        void IDisposable.Dispose()
        {
            int oldState;
            lock (_lock)
            {
                oldState = _state;
                _state = STATE_DISPOSED;

                if (oldState == STATE_SLEEPING)
                {
                    _tracker.TrackAwake();
                }
            }

            if (oldState == STATE_DISPOSED)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _timer.Dispose();
            _source.Reset();
            _tracker.Unregister(_jobName);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Job {JobName} starting")]
        public static partial void JobStarting(ILogger logger, string jobName);

        [LoggerMessage(1, LogLevel.Information, "Job {JobName} completed successfully in {Duration}")]
        public static partial void JobCompleted(ILogger logger, string jobName, TimeSpan duration);

        [LoggerMessage(2, LogLevel.Error, "Job {JobName} failed")]
        public static partial void JobFailed(ILogger logger, string jobName, Exception exception);

        [LoggerMessage(3, LogLevel.Information, "Skipping job {JobName} as lease {LeaseName} not acquired for lifecycle event {Lifecycle}")]
        public static partial void LeaseNotAcquired(ILogger logger, string jobName, string leaseName, JobHostLifecycles lifecycle);

        [LoggerMessage(4, LogLevel.Error, "Job registration enabled check for {JobName} failed")]
        public static partial void JobIsEnabledFailed(ILogger logger, string jobName, Exception exception);

        [LoggerMessage(5, LogLevel.Information, "Job {JobName} skipped as it is not enabled. Reason: {Reason}")]
        public static partial void JobSkipped(ILogger logger, string jobName, string reason);

        [LoggerMessage(6, LogLevel.Error, "Job condition {ConditionName} for job {JobName} failed")]
        public static partial void JobConditionFailed(ILogger logger, string jobName, string conditionName, Exception exception);

        [LoggerMessage(7, LogLevel.Information, "Job {JobName} blocked by condition {ConditionName}")]
        public static partial void JobBlockedByCondition(ILogger logger, string jobName, string conditionName);

        [LoggerMessage(8, LogLevel.Information, "Job {JobName} is disabled by registration enabled delegate: {Reason}")]
        public static partial void JobIsDisabledByDelegate(ILogger logger, string jobName, string reason);

        [LoggerMessage(9, LogLevel.Debug, "Sleep before re-attempting job {JobName} for {SleepDuration}")]
        public static partial void Sleep(ILogger logger, string jobName, TimeSpan sleepDuration);

        [LoggerMessage(10, LogLevel.Information, "Scheduler disabled")]
        public static partial void SchedulerDisabled(ILogger logger);

        [LoggerMessage(11, LogLevel.Information, "Creating scheduler for job {JobName} with strategy {DelayStrategyDescription} and lease {LeaseName}")]
        public static partial void CreatingSchedulerForJob(ILogger logger, string jobName, string delayStrategyDescription, string? leaseName);

        [LoggerMessage(12, LogLevel.Information, "Registered job {JobName} with strategy {DelayStrategyDescription}, runs at {RunAt}, and lease {LeaseName}")]
        public static partial void RegisteredJob(ILogger logger, string jobName, string delayStrategyDescription, string? leaseName, JobHostLifecycles runAt);

        [LoggerMessage(13, LogLevel.Information, "Scheduler for job {JobName} disposed")]
        public static partial void SchedulerDisposed(ILogger logger, string jobName);

        [LoggerMessage(14, LogLevel.Error, "Job {JobName} creation failed")]
        public static partial void JobCreationFailed(ILogger logger, string jobName, Exception exception);

        [LoggerMessage(15, LogLevel.Warning, "Scheduler for job {JobName} exited")]
        public static partial void SchedulerExited(ILogger logger, string jobName, Exception? exception);

        [LoggerMessage(16, LogLevel.Trace, "Starting sleep job {JobName} for {Duration} with CanBeCancelled: {CanBeCancelled}")]
        public static partial void StartSleep(ILogger logger, string jobName, TimeSpan duration, bool canBeCancelled);

        [LoggerMessage(17, LogLevel.Trace, "Finished sleep job {JobName}, expected sleep duration: {ExpectedDuration}, actual sleep duration: {ActualDuration}, cancellation token is cancelled: {IsCancelled}")]
        public static partial void RecordSleep(ILogger logger, string jobName, TimeSpan expectedDuration, TimeSpan actualDuration, bool isCancelled);

        [LoggerMessage(18, LogLevel.Error, "Job {JobName} disposal failed")]
        public static partial void JobDisposalFailed(ILogger logger, string jobName, Exception exception);
    }
}
