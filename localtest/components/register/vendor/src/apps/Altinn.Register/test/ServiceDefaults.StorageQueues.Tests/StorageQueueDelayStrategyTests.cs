using Altinn.Authorization.ServiceDefaults.Jobs;
using Strategy = Microsoft.Extensions.DependencyInjection.StorageQueuesServiceCollectionExtensions.RandomizedExponentialBackoffStrategy;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests;

public sealed class StorageQueueDelayStrategyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenMinimumIntervalIsNotPositive_Throws(int milliseconds)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => new Strategy(
            TimeSpan.FromMilliseconds(milliseconds),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)));

        ex.ParamName.ShouldBe("minimumInterval");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenMaximumIntervalIsNotPositive_Throws(int milliseconds)
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() => new Strategy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(milliseconds),
            TimeSpan.FromSeconds(1)));

        ex.ParamName.ShouldBe("maximumInterval");
    }

    [Fact]
    public void Constructor_WhenMinimumIsGreaterThanMaximum_Throws()
    {
        var ex = Should.Throw<ArgumentException>(() => new Strategy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1)));

        ex.ParamName.ShouldBe("minimumInterval");
        ex.Message.ShouldContain("must not be greater than the maximumInterval");
    }

    [Fact]
    public void Description_ContainsConfiguredIntervals()
    {
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3));

        strategy.Description.ShouldBe("randomized-exponential-backoff (min: 00:00:02, max: 00:00:10, delta: 00:00:03)");
    }

    [Fact]
    public void GetDelay_WhenInitialOutcome_ReturnsMinimumInterval()
    {
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3));

        var delay = strategy.GetDelay(JobOutcome.None);

        delay.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetDelay_WhenSinglePageOutcome_KeepsCurrentDelay()
    {
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3));

        var first = strategy.GetDelay(JobOutcome.None);
        var second = strategy.GetDelay(JobOutcome.Succeeded(StorageQueuePollJobRunResult.SinglePage));

        first.ShouldBe(TimeSpan.FromSeconds(2));
        second.ShouldBe(first);
    }

    [Fact]
    public void GetDelay_WhenMultiplePagesOutcome_ResetsToMinimumInterval()
    {
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3));

        _ = strategy.GetDelay(JobOutcome.None);
        _ = strategy.GetDelay(JobOutcome.Failed(new InvalidOperationException("boom")));

        var delay = strategy.GetDelay(JobOutcome.Succeeded(StorageQueuePollJobRunResult.MultiplePages));

        delay.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetDelay_WhenFailuresContinue_IncreasesDelayWithinExpectedRange()
    {
        var minimum = TimeSpan.FromSeconds(2);
        var maximum = TimeSpan.FromSeconds(20);
        var delta = TimeSpan.FromSeconds(3);
        var strategy = new Strategy(minimum, maximum, delta);

        var first = strategy.GetDelay(JobOutcome.None);
        var second = strategy.GetDelay(JobOutcome.Failed(new InvalidOperationException("boom")));
        var third = strategy.GetDelay(JobOutcome.Failed(new InvalidOperationException("boom")));

        first.ShouldBe(minimum);
        second.ShouldBeGreaterThanOrEqualTo(minimum + TimeSpan.FromSeconds(2.4));
        second.ShouldBeLessThan(minimum + TimeSpan.FromSeconds(3.6));
        third.ShouldBeGreaterThanOrEqualTo(minimum + TimeSpan.FromSeconds(4.8));
        third.ShouldBeLessThan(minimum + TimeSpan.FromSeconds(7.2));
    }

    [Fact]
    public void GetDelay_WhenSkipped_ReturnsTenMinutesAndSetsCurrentIntervalToMaximum()
    {
        var maximum = TimeSpan.FromSeconds(5);
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            maximum,
            TimeSpan.FromSeconds(3));

        _ = strategy.GetDelay(JobOutcome.None);

        var skipped = strategy.GetDelay(JobOutcome.Skipped);
        var afterSkippedFailure = strategy.GetDelay(JobOutcome.Failed(new InvalidOperationException("boom")));

        skipped.ShouldBe(TimeSpan.FromMinutes(10));
        afterSkippedFailure.ShouldBe(maximum);
    }

    [Fact]
    public void GetDelay_WhenBackoffExceedsMaximum_ClampsToMaximumAndStaysThere()
    {
        var maximum = TimeSpan.FromSeconds(5);
        var strategy = new Strategy(
            TimeSpan.FromSeconds(2),
            maximum,
            TimeSpan.FromSeconds(10));

        var first = strategy.GetDelay(JobOutcome.None);
        var second = strategy.GetDelay(JobOutcome.Failed(new InvalidOperationException("boom")));
        var third = strategy.GetDelay(JobOutcome.Disabled);

        first.ShouldBe(TimeSpan.FromSeconds(2));
        second.ShouldBe(maximum);
        third.ShouldBe(maximum);
    }
}
