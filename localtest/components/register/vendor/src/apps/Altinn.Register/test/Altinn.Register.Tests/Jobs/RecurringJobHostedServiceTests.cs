using System.Collections.Immutable;
using System.Data;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;
using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Authorization.ServiceDefaults.Npgsql;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Jobs;
using Altinn.Register.Persistence;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Altinn.Register.Tests.Jobs;

[RunTestsSerially]
public class RecurringJobHostedServiceTests
    : DatabaseTestBase
{
    private static readonly ObjectFactory<RecurringJobHostedService> _factory
        = ActivatorUtilities.CreateFactory<RecurringJobHostedService>([typeof(IEnumerable<JobRegistration>), typeof(IEnumerable<IJobCondition>)]);

    protected override bool SeedData => false;

    private ILeaseProvider? _provider;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _provider = GetRequiredService<ILeaseProvider>();
    }

    protected override async ValueTask ConfigureServices(IServiceCollection services)
    {
        await base.ConfigureServices(services);

        services.AddLeaseManager();
        services.TryAddSingleton<JobsTelemetry>();
    }

    private ILeaseProvider Provider
        => _provider!;

    [Fact(Timeout = 10_000)]
    public async Task CanRun_WithNo_JobRegistrations()
    {
        await using var sut = CreateService([]);

        await Run(sut, CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Starting()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Starting, counter)]);

        counter.Value.ShouldBe(0U);
        await sut.StartingAsync(CancellationToken);
        counter.Value.ShouldBe(1U);

        await sut.StartAsync(CancellationToken);
        await sut.StartedAsync(CancellationToken);
        await sut.StoppingAsync(CancellationToken);
        await sut.StopAsync(CancellationToken);
        await sut.StoppedAsync(CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Start()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Start, counter)]);

        await sut.StartingAsync(CancellationToken);

        counter.Value.ShouldBe(0U);
        await sut.StartAsync(CancellationToken);
        counter.Value.ShouldBe(1U);

        await sut.StartedAsync(CancellationToken);
        await sut.StoppingAsync(CancellationToken);
        await sut.StopAsync(CancellationToken);
        await sut.StoppedAsync(CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Started()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Started, counter)]);

        await sut.StartingAsync(CancellationToken);
        await sut.StartAsync(CancellationToken);

        counter.Value.ShouldBe(0U);
        await sut.StartedAsync(CancellationToken);
        counter.Value.ShouldBe(1U);

        await sut.StoppingAsync(CancellationToken);
        await sut.StopAsync(CancellationToken);
        await sut.StoppedAsync(CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Stopping()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stopping, counter)]);

        await sut.StartingAsync(CancellationToken);
        await sut.StartAsync(CancellationToken);
        await sut.StartedAsync(CancellationToken);

        counter.Value.ShouldBe(0U);
        await sut.StoppingAsync(CancellationToken);
        counter.Value.ShouldBe(1U);

        await sut.StopAsync(CancellationToken);
        await sut.StoppedAsync(CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Stop()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stop, counter)]);

        await sut.StartingAsync(CancellationToken);
        await sut.StartAsync(CancellationToken);
        await sut.StartedAsync(CancellationToken);
        await sut.StoppingAsync(CancellationToken);

        counter.Value.ShouldBe(0U);
        await sut.StopAsync(CancellationToken);
        counter.Value.ShouldBe(1U);

        await sut.StoppedAsync(CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_Stopped()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stopped, counter)]);

        await sut.StartingAsync(CancellationToken);
        await sut.StartAsync(CancellationToken);
        await sut.StartedAsync(CancellationToken);
        await sut.StoppingAsync(CancellationToken);
        await sut.StopAsync(CancellationToken);

        counter.Value.ShouldBe(0U);
        await sut.StoppedAsync(CancellationToken);
        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanRun_JobAt_All()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(
            JobHostLifecycles.Starting | JobHostLifecycles.Start | JobHostLifecycles.Started | JobHostLifecycles.Stopping | JobHostLifecycles.Stop | JobHostLifecycles.Stopped,
            counter)]);

        counter.Value.ShouldBe(0U);
        await sut.StartingAsync(CancellationToken);
        counter.Value.ShouldBe(1U);
        await sut.StartAsync(CancellationToken);
        counter.Value.ShouldBe(2U);
        await sut.StartedAsync(CancellationToken);
        counter.Value.ShouldBe(3U);
        await sut.StoppingAsync(CancellationToken);
        counter.Value.ShouldBe(4U);
        await sut.StopAsync(CancellationToken);
        counter.Value.ShouldBe(5U);
        await sut.StoppedAsync(CancellationToken);
        counter.Value.ShouldBe(6U);
    }

    [Fact(Timeout = 10_000)]
    public async Task Cannot_UseLease_At_Starting()
    {
        var counter = new AtomicCounter();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Starting, "test", counter)]);

        await Should.ThrowAsync<InvalidOperationException>(() => sut.StartingAsync(CancellationToken));
    }

    [Fact(Timeout = 10_000)]
    public async Task Can_UseLease_At_Start()
    {
        var counter = new AtomicCounter();
        var start = TimeProvider.GetUtcNow();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Start, "test", counter)]);

        await Run(sut, CancellationToken);
        var end = TimeProvider.GetUtcNow();

        counter.Value.ShouldBe(1U);
        end.ShouldBe(start + TimeSpan.FromSeconds(10));

        var lease = await GetLeaseInfo("test");
        lease.ShouldNotBeNull();
        lease.LastAcquiredAt.ShouldBe(start);
        lease.LastReleasedAt.ShouldBe(end);
    }

    [Fact(Timeout = 10_000)]
    public async Task Can_UseLease_At_Started()
    {
        var counter = new AtomicCounter();
        var start = TimeProvider.GetUtcNow();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Started, "test", counter)]);

        await Run(sut, CancellationToken);
        var end = TimeProvider.GetUtcNow();

        counter.Value.ShouldBe(1U);
        end.ShouldBe(start + TimeSpan.FromSeconds(10));

        var lease = await GetLeaseInfo("test");
        lease.ShouldNotBeNull();
        lease.LastAcquiredAt.ShouldBe(start);
        lease.LastReleasedAt.ShouldBe(end);
    }

    [Fact(Timeout = 10_000)]
    public async Task Can_UseLease_At_Stopping()
    {
        var counter = new AtomicCounter();
        var start = TimeProvider.GetUtcNow();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stopping, "test", counter)]);

        await Run(sut, CancellationToken);
        var end = TimeProvider.GetUtcNow();

        counter.Value.ShouldBe(1U);
        end.ShouldBe(start + TimeSpan.FromSeconds(10));

        var lease = await GetLeaseInfo("test");
        lease.ShouldNotBeNull();
        lease.LastAcquiredAt.ShouldBe(start);
        lease.LastReleasedAt.ShouldBe(end);
    }

    [Fact(Timeout = 10_000)]
    public async Task Can_UseLease_At_Stop()
    {
        var counter = new AtomicCounter();
        var start = TimeProvider.GetUtcNow();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stop, "test", counter)]);

        await Run(sut, CancellationToken);
        var end = TimeProvider.GetUtcNow();

        counter.Value.ShouldBe(1U);
        end.ShouldBe(start + TimeSpan.FromSeconds(10));

        var lease = await GetLeaseInfo("test");
        lease.ShouldNotBeNull();
        lease.LastAcquiredAt.ShouldBe(start);
        lease.LastReleasedAt.ShouldBe(end);
    }

    [Fact(Timeout = 10_000)]
    public async Task Can_UseLease_At_Stopped()
    {
        var counter = new AtomicCounter();
        var start = TimeProvider.GetUtcNow();
        await using var sut = CreateService([Counter.RunAt(JobHostLifecycles.Stopped, "test", counter)]);

        await Run(sut, CancellationToken);
        var end = TimeProvider.GetUtcNow();

        counter.Value.ShouldBe(1U);
        end.ShouldBe(start + TimeSpan.FromSeconds(10));

        var lease = await GetLeaseInfo("test");
        lease.ShouldNotBeNull();
        lease.LastAcquiredAt.ShouldBe(start);
        lease.LastReleasedAt.ShouldBe(end);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_Propagate_Errors(JobHostLifecycles lifecycle)
    {
        await using var sut = CreateService([Registration.RunAt(lifecycle, new Func<IServiceProvider, Task>(_ => throw new InvalidOperationException("I died miserably")), services => ValueTask.FromResult(JobShouldRunResult.Yes))]);

        var assert = await Should.ThrowAsync<InvalidOperationException>(() => Run(sut, CancellationToken));
        assert.Message.ShouldBe("I died miserably");
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_Propagate_ShouldRun_Errors(JobHostLifecycles lifecycle)
    {
        await using var sut = CreateService([Registration.RunAt(lifecycle, new Func<IServiceProvider, Task>(_ => throw new InvalidOperationException("I died miserably")), services => throw new InvalidOperationException("I died miserably (shouldrun)"))]);

        var assert = await Should.ThrowAsync<InvalidOperationException>(() => Run(sut, CancellationToken));
        assert.Message.ShouldBe("I died miserably (shouldrun)");
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_CanBeDisabled(JobHostLifecycles lifecycle)
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.RunAt(lifecycle, counter, enabled: (_, _) => ValueTask.FromResult(JobShouldRunResult.No("test"))),
        ]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_CanBeEnabled_ByCondition(JobHostLifecycles lifecycle)
    {
        var condition = new ConstCondition(true, tags: []);
        var counter = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(lifecycle, counter)],
            [condition]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_CanBeDisabled_ByCondition(JobHostLifecycles lifecycle)
    {
        var condition = new ConstCondition(false, tags: []);
        var counter = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(lifecycle, counter)],
            [condition]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Starting)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task LifecycleJobs_Can_WaitFor(JobHostLifecycles lifecycle)
    {
        var counter = new AtomicCounter();

        var tcs = new TaskCompletionSource();
        await using var sut = CreateService([
            Counter.RunAt(lifecycle, counter, tcs.Task),
        ]);

        var task = Run(sut, CancellationToken);

        await Task.Yield();
        counter.Value.ShouldBe(0U);
        task.IsCompleted.ShouldBeFalse();

        tcs.SetResult();
        await task;
        counter.Value.ShouldBe(1U);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task Leased_LifecycleJobs_IsSkipped_WhenLease_IsTaken(JobHostLifecycles lifecycle)
    {
        var counter = new AtomicCounter();

        await Provider.TryAcquireLease("test", TimeSpan.FromMinutes(1), CancellationToken);
        await using var sut = CreateService([Counter.RunAt(lifecycle, "test", counter)]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(JobHostLifecycles.Start)]
    [InlineData(JobHostLifecycles.Started)]
    [InlineData(JobHostLifecycles.Stopping)]
    [InlineData(JobHostLifecycles.Stop)]
    [InlineData(JobHostLifecycles.Stopped)]
    public async Task Leased_LifecycleJobs_CanBeDisabled(JobHostLifecycles lifecycle)
    {
        var counter = new AtomicCounter();

        var leaseName = "test";
        var initialLeaseInfo = await GetLeaseInfo(leaseName);
        initialLeaseInfo.Expires.ShouldBe(DateTimeOffset.MinValue);
        initialLeaseInfo.LastAcquiredAt.ShouldBeNull();
        initialLeaseInfo.LastReleasedAt.ShouldBeNull();

        await using var sut = CreateService([
            Counter.RunAt(lifecycle, leaseName, counter, enabled: (_, _) => ValueTask.FromResult(JobShouldRunResult.No("test"))),
        ]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);

        var postLeaseInfo = await GetLeaseInfo(leaseName);
        postLeaseInfo.ShouldBe(initialLeaseInfo);
    }

    [Fact(Timeout = 10_000)]
    public async Task DisposableJobs_AreDisposed()
    {
        var job1Dispose = new AtomicCounter();
        var job2DisposeAsync = new AtomicCounter();
        var job3Dispose = new AtomicCounter();
        var job3DisposeAsync = new AtomicCounter();

        await using var sut = CreateService([
            Registration.RunAt(JobHostLifecycles.Start, _ => new DisposableJob(job1Dispose)),
            Registration.RunAt(JobHostLifecycles.Start, _ => new AsyncDisposableJob(job2DisposeAsync)),
            Registration.RunAt(JobHostLifecycles.Start, _ => new BothDisposableJob(job3Dispose, job3DisposeAsync)),
        ]);

        await Run(sut, CancellationToken);

        job1Dispose.Value.ShouldBe(1U);
        job2DisposeAsync.Value.ShouldBe(1U);

        // async dispose is prioritized over sync dispose
        job3Dispose.Value.ShouldBe(0U);
        job3DisposeAsync.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithoutLease_CanBeDisabled()
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), counter, enabled: (_, _) => ValueTask.FromResult(JobShouldRunResult.No("test"))),
        ]);

        await Start(sut, CancellationToken);
        TimeProvider.Advance(TimeSpan.FromHours(1));
        await Stop(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithoutLease_AreNotRunImmediately()
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), counter),
        ]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithoutLease_CanWaitFor()
    {
        var counter = new AtomicCounter();
        var tcs = new TaskCompletionSource();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), counter, tcs.Task),
        ]);

        await Start(sut, CancellationToken);
        TimeProvider.Advance(TimeSpan.FromHours(1));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(0U);

        tcs.SetResult();
        await Task.Yield();
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_RunMultipleTimes()
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), counter),
        ]);

        await Start(sut, CancellationToken);

        for (var i = 0; i < 10; i++)
        {
            TimeProvider.Advance(TimeSpan.FromHours(1));
            await sut.WaitForRunningScheduledJobs();
        }

        await Stop(sut, CancellationToken);

        counter.Value.ShouldBe(10U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_CanBeDynamicallyEnabled()
    {
        var counter = new AtomicCounter();
        var enabled = new AtomicBool();
        var checksRan = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), counter, enabled: (_, _) =>
            {
                checksRan.Increment();
                return ValueTask.FromResult(JobShouldRunResult.Conditional(nameof(enabled), enabled.Value));
            }),
        ]);

        await Start(sut, CancellationToken);
        TimeProvider.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(10));
        await sut.WaitForRunningScheduledJobs();

        counter.Value.ShouldBe(0U);
        checksRan.Value.ShouldBe(1U);

        enabled.Set(true);
        for (var i = 0; i < 10; i++)
        {
            // advance forward 10 hours in 1 hour increments to trigger interim timers
            TimeProvider.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(10));
            await sut.WaitForRunningScheduledJobs();
        }

        checksRan.Value.ShouldBeGreaterThan(1U);
        counter.Value.ShouldBeGreaterThanOrEqualTo(1U);

        await Stop(sut, CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_Interval_IsBetweenRuns()
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Registration.Scheduled(
                TimeSpan.FromHours(1),
                services =>
                {
                    var timeProvider = services.GetRequiredService<FakeTimeProvider>();

                    counter.Increment();

                    // this job took 10 minutes
                    timeProvider.Advance(TimeSpan.FromMinutes(10));

                    return Task.CompletedTask;
                },
                services => ValueTask.FromResult(JobShouldRunResult.Yes)),
        ]);

        await Start(sut, CancellationToken);

        var start = TimeProvider.GetUtcNow();
        TimeProvider.SetUtcNow(start + TimeSpan.FromHours(1));
        await sut.WaitForRunningScheduledJobs();

        var afterFirst = TimeProvider.GetUtcNow();
        counter.Value.ShouldBe(1U);

        TimeProvider.SetUtcNow(start + TimeSpan.FromHours(2));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(1U);

        TimeProvider.SetUtcNow(afterFirst + TimeSpan.FromHours(1));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(2U);

        await Stop(sut, CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithLease_RunsImmediately()
    {
        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), "test", counter),
        ]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithLease_ThatRanRecently_AreNotRunImmediately()
    {
        var result = await Provider.TryAcquireLease("test", TimeSpan.FromMinutes(1), CancellationToken);
        Assert.True(result.IsLeaseAcquired);
        await Provider.ReleaseLease(result.Lease, CancellationToken);

        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), "test", counter),
        ]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithLease_CanBeDisabled()
    {
        var counter = new AtomicCounter();
        var enabled = new AtomicBool();

        var leaseName = "test";
        var initialLeaseInfo = await GetLeaseInfo(leaseName);

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), "test", counter, enabled: (_, _) =>
            {
                return ValueTask.FromResult(JobShouldRunResult.Conditional(nameof(enabled), enabled.Value));
            }),
        ]);

        await Start(sut, CancellationToken);
        TimeProvider.Advance(TimeSpan.FromHours(1));
        await sut.WaitForRunningScheduledJobs();

        var postLeaseInfo = await GetLeaseInfo(leaseName);
        counter.Value.ShouldBe(0U);
        postLeaseInfo.LastAcquiredAt.ShouldBe(initialLeaseInfo.LastAcquiredAt);

        enabled.Set(true);
        TimeProvider.Advance(TimeSpan.FromMinutes(10)); // disabled jobs are checkd again more frequently

        await sut.WaitForRunningScheduledJobs();
        postLeaseInfo = await GetLeaseInfo(leaseName);

        counter.Value.ShouldBe(1U);
        postLeaseInfo.LastAcquiredAt.ShouldNotBe(initialLeaseInfo.LastAcquiredAt);

        await Stop(sut, CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task ScheduledJobs_WithLease_UsesLeaseTime_ToScheduleRuns()
    {
        var result = await Provider.TryAcquireLease("test", TimeSpan.FromMinutes(1), CancellationToken);
        Assert.True(result.IsLeaseAcquired);
        await Provider.ReleaseLease(result.Lease, CancellationToken);

        // advance 40 minutes since the lease was released
        TimeProvider.Advance(TimeSpan.FromMinutes(40));

        var counter = new AtomicCounter();

        await using var sut = CreateService([
            Counter.Scheduled(TimeSpan.FromHours(1), "test", counter),
        ]);

        await Start(sut, CancellationToken);
        counter.Value.ShouldBe(0U);

        // advance 20 minutes, 1 hour since the lease was released (+ some seconds due to random jitter)
        TimeProvider.Advance(TimeSpan.FromMinutes(21));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(1U);
        var leaseInfo = await GetLeaseInfo("test");
        leaseInfo.LastReleasedAt.ShouldBe(TimeProvider.GetUtcNow());

        // advance 40 more minutes and renew the lease
        TimeProvider.Advance(TimeSpan.FromMinutes(40));
        result = await Provider.TryAcquireLease("test", TimeSpan.FromMinutes(1), CancellationToken);
        Assert.True(result.IsLeaseAcquired);
        await Provider.ReleaseLease(result.Lease, CancellationToken);

        // advance 20 more minutes, 1 hour since the job last completed (+ some seconds due to random jitter)
        TimeProvider.Advance(TimeSpan.FromMinutes(21));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(1U);

        // advance 40 more minutes, 1 hour since the lease was last released (+ some seconds due to random jitter)
        TimeProvider.Advance(TimeSpan.FromMinutes(40));
        await sut.WaitForRunningScheduledJobs();
        counter.Value.ShouldBe(2U);
        leaseInfo = await GetLeaseInfo("test");
        leaseInfo.LastReleasedAt.ShouldBe(TimeProvider.GetUtcNow());
    }

    [Fact(Timeout = 10_000)]
    public async Task MultipleConditions_AllTrue_Runs()
    {
        var conditions = Enumerable.Range(0, 10).Select(_ => new ConstCondition(true, tags: []));
        var counter = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(JobHostLifecycles.Start, counter)],
            [.. conditions]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task MultipleConditions_SomeFalse_DoesNotRun()
    {
        var conditions = Enumerable.Range(0, 10).Select(_ => new ConstCondition(true, tags: []));
        var counter = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(JobHostLifecycles.Start, counter)],
            [.. conditions, new ConstCondition(false, tags: [])]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Fact(Timeout = 10_000)]
    public async Task Conditions_CanTarget_AllJobs()
    {
        var condition = new ConstCondition(false, tags: []);
        var counter = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(JobHostLifecycles.Start, counter, tags: ["foo"]), Counter.RunAt(JobHostLifecycles.Start, counter, tags: ["bar"]), Counter.RunAt(JobHostLifecycles.Start, counter)],
            [condition]);

        await Run(sut, CancellationToken);

        counter.Value.ShouldBe(0U);
    }

    [Fact(Timeout = 10_000)]
    public async Task Conditions_CanTarget_TaggedJobs()
    {
        var condition = new ConstCondition(false, tags: ["foo"]);
        var fooCounter = new AtomicCounter();
        var barCounter = new AtomicCounter();
        var noTag = new AtomicCounter();

        await using var sut = CreateService(
            [Counter.RunAt(JobHostLifecycles.Start, fooCounter, tags: ["foo"]), Counter.RunAt(JobHostLifecycles.Start, barCounter, tags: ["bar"]), Counter.RunAt(JobHostLifecycles.Start, noTag)],
            [condition]);

        await Run(sut, CancellationToken);

        fooCounter.Value.ShouldBe(0U);
        barCounter.Value.ShouldBe(1U);
        noTag.Value.ShouldBe(1U);
    }

    [Fact(Timeout = 10_000)]
    public async Task CanBeDisposed_MultipleTimes()
    {
        await using var sut = CreateService([]);

        await Run(sut, CancellationToken);
        await sut.DisposeAsync();
        await sut.DisposeAsync(); // should not throw
    }

    [Fact(Timeout = 10_000)]
    public async Task CanBeDisposed_MultipleTimes_InParallel()
    {
        const int THREAD_COUNT = 10;

        await using var sut = CreateService([]);

        await Run(sut, CancellationToken);

        var rst = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, THREAD_COUNT).Select(_ =>
        {
            var thread = new Thread(async () =>
            {
                rst.Wait();
                await sut.DisposeAsync();
            });

            thread.Start();
            return thread;
        }).ToList();

        // wait for all the threads to be ready
        await Task.Delay(10, CancellationToken);
        rst.Set();

        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    private record LeaseInfo(string LeaseName, Guid? Token, DateTimeOffset Expires, DateTimeOffset? LastAcquiredAt, DateTimeOffset? LastReleasedAt);

    private async Task<LeaseInfo> GetLeaseInfo(string leaseName)
    {
        await using var uow = await GetRequiredService<IUnitOfWorkManager>().CreateAsync();
        var conn = uow.GetRequiredService<NpgsqlConnection>();
        await using var cmd = conn.CreateCommand(/*strpsql*/"SELECT id, token, expires, acquired, released FROM register.lease WHERE id = @id");

        cmd.Parameters.Add<string>("id", NpgsqlTypes.NpgsqlDbType.Text).TypedValue = leaseName;
        await cmd.PrepareAsync();

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
        {
            return new(leaseName, null, DateTimeOffset.MinValue, null, null);
        }

        var dbLeaseName = await reader.GetFieldValueAsync<string>("id");
        var dbToken = await reader.GetFieldValueAsync<Guid>("token");
        var dbExpires = await reader.GetFieldValueAsync<DateTimeOffset>("expires");
        var dbLastAcquiredAt = await reader.GetFieldValueAsync<DateTimeOffset?>("acquired");
        var dbLastReleasedAt = await reader.GetFieldValueAsync<DateTimeOffset?>("released");

        return new(dbLeaseName, dbToken, dbExpires, dbLastAcquiredAt, dbLastReleasedAt);
    }

    private RecurringJobHostedService CreateService(IEnumerable<JobRegistration> registrations, IEnumerable<IJobCondition>? conditions = null)
        => _factory(Services, [registrations, conditions ?? []]);

    private static async Task Start(RecurringJobHostedService service, CancellationToken cancellationToken = default)
    {
        await service.StartingAsync(cancellationToken);
        await service.StartAsync(cancellationToken);
        await service.StartedAsync(cancellationToken);
        await service.WaitForRunningScheduledJobs();
    }

    private static async Task Stop(RecurringJobHostedService service, CancellationToken cancellationToken = default)
    {
        await service.StoppingAsync(cancellationToken);
        await service.StopAsync(cancellationToken);
        await service.StoppedAsync(cancellationToken);
    }

    private static async Task Run(RecurringJobHostedService service, CancellationToken cancellationToken = default)
    {
        await Start(service, cancellationToken);
        await Stop(service, cancellationToken);
    }

    private sealed class DelegateJob(
        Func<IServiceProvider, Task> run,
        Func<IServiceProvider, ValueTask<JobShouldRunResult>> shouldRun,
        IServiceProvider services)
        : Job
    {
        protected override Task RunAsync(CancellationToken cancellationToken)
            => run(services);

        protected override ValueTask<JobShouldRunResult> ShouldRun(CancellationToken cancellationToken)
            => shouldRun(services);
    }

    private sealed class Registration(
        Func<IServiceProvider, Job> job,
        string name,
        string? leaseName,
        TimeSpan? interval,
        JobHostLifecycles runAt,
        IEnumerable<string> tags,
        Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
        Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
        : JobRegistration<Unit>(
            name,
            leaseName,
            interval.HasValue ? new ConstantDelayStrategy<Unit>(interval.Value) : null,
            runAt,
            tags,
            enabled,
            waitForReady)
    {
        public static JobRegistration Create(
            Func<IServiceProvider, Job> job,
            string? leaseName,
            TimeSpan? interval,
            JobHostLifecycles runAt,
            IEnumerable<string> tags,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
            => new Registration(job, "test", leaseName, interval, runAt, tags, enabled, waitForReady);

        public static JobRegistration Create(
            Func<IServiceProvider, Task> job,
            Func<IServiceProvider, ValueTask<JobShouldRunResult>> shouldRun,
            string? leaseName,
            TimeSpan? interval,
            JobHostLifecycles runAt,
            IEnumerable<string> tags,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
            => new Registration(services => new DelegateJob(job, shouldRun, services), "test", leaseName, interval, runAt, tags, enabled, waitForReady);

        public static new JobRegistration RunAt(JobHostLifecycles runAt, Func<IServiceProvider, Job> job)
            => Create(job, null, null, runAt, [], null, null);

        public static new JobRegistration RunAt(JobHostLifecycles runAt, Func<IServiceProvider, Task> job, Func<IServiceProvider, ValueTask<JobShouldRunResult>> shouldRun)
            => Create(job, shouldRun, null, null, runAt, [], null, null);

        public static new JobRegistration RunAt(JobHostLifecycles runAt, string leaseName, Func<IServiceProvider, Job> job)
            => Create(job, leaseName, null, runAt, [], null, null);

        public static new JobRegistration RunAt(JobHostLifecycles runAt, string leaseName, Func<IServiceProvider, Task> job, Func<IServiceProvider, ValueTask<JobShouldRunResult>> shouldRun)
            => Create(job, shouldRun, leaseName, null, runAt, [], null, null);

        public static JobRegistration Scheduled(TimeSpan interval, Func<IServiceProvider, Task> job, Func<IServiceProvider, ValueTask<JobShouldRunResult>> shouldRun)
            => Create(job, shouldRun, null, interval, JobHostLifecycles.None, [], null, null);

        protected override Job Create(IServiceProvider services)
            => job(services);
    }

    private static class Counter
    {
        public static JobRegistration Create(
            AtomicCounter counter,
            AtomicBool shouldRun,
            string? leaseName,
            TimeSpan? interval,
            JobHostLifecycles runAt,
            IEnumerable<string>? tags = null,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled = null,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady = null)
        {
            var shouldRunFn = (IServiceProvider services) =>
            {
                return ValueTask.FromResult(JobShouldRunResult.Conditional("atomic bool disabled", shouldRun.Value));
            };

            var job = (IServiceProvider services) =>
            {
                var timeProvider = services.GetRequiredService<FakeTimeProvider>();

                counter.Increment();
                timeProvider.Advance(TimeSpan.FromSeconds(10));

                return Task.CompletedTask;
            };

            return Registration.Create(job, shouldRunFn, leaseName, interval, runAt, tags ?? [], enabled, waitForReady);
        }

        public static JobRegistration RunAt(
            JobHostLifecycles runAt,
            AtomicCounter counter,
            IEnumerable<string>? tags = null,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled = null,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady = null)
            => Create(counter, new AtomicBool(true), null, null, runAt, tags, enabled, waitForReady);

        public static JobRegistration RunAt(
            JobHostLifecycles runAt,
            string? leaseName,
            AtomicCounter counter,
            IEnumerable<string>? tags = null,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled = null,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady = null)
            => Create(counter, new AtomicBool(true), leaseName, null, runAt, tags, enabled, waitForReady);

        public static JobRegistration RunAt(JobHostLifecycles runAt, AtomicCounter counter, Task waitForReady)
            => Create(counter, new AtomicBool(true), null, null, runAt, null, null, (_, _) => new(waitForReady));

        public static JobRegistration RunAt(JobHostLifecycles runAt, string leaseName, AtomicCounter counter)
            => Create(counter, new AtomicBool(true), leaseName, null, runAt);

        public static JobRegistration Scheduled(
            TimeSpan interval,
            AtomicCounter counter,
            IEnumerable<string>? tags = null,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled = null,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady = null)
            => Create(counter, new AtomicBool(true), null, interval, JobHostLifecycles.None, tags, enabled, waitForReady);

        public static JobRegistration Scheduled(
            TimeSpan interval,
            string? leaseName,
            AtomicCounter counter,
            IEnumerable<string>? tags = null,
            Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled = null,
            Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady = null)
            => Create(counter, new AtomicBool(true), leaseName, interval, JobHostLifecycles.None, tags, enabled, waitForReady);

        public static JobRegistration Scheduled(TimeSpan interval, AtomicCounter counter, Task waitForReady)
            => Create(counter, new AtomicBool(true), null, interval, JobHostLifecycles.None, null, null, (_, _) => new(waitForReady));

        public static JobRegistration Scheduled(TimeSpan interval, string leaseName, AtomicCounter counter)
            => Create(counter, new AtomicBool(true), leaseName, interval, JobHostLifecycles.None);
    }

    private sealed class ConstCondition(bool value, IEnumerable<string> tags)
        : IJobCondition
    {
        public string Name => $"const {value}";

        public ImmutableArray<string> JobTags { get; } = [.. tags];

        public ValueTask<bool> ShouldRun(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(value);
    }

    private sealed class DisposableJob(AtomicCounter disposeCounter)
        : Job
        , IDisposable
    {
        public void Dispose()
        {
            disposeCounter.Increment();
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncDisposableJob(AtomicCounter disposeCounter)
        : Job
        , IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposeCounter.Increment();

            return ValueTask.CompletedTask;
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class BothDisposableJob(AtomicCounter disposeCounter, AtomicCounter asyncDisposeCounter)
        : Job
        , IAsyncDisposable
    {
        public void Dispose()
        {
            disposeCounter.Increment();
        }

        public ValueTask DisposeAsync()
        {
            asyncDisposeCounter.Increment();

            return ValueTask.CompletedTask;
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
