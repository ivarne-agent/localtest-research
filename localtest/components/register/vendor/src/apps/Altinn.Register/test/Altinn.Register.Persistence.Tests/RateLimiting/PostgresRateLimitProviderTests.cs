using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Persistence.RateLimiting;
using Altinn.Register.TestUtils;

namespace Altinn.Register.Persistence.Tests.RateLimiting;

public class PostgresRateLimitProviderTests
    : DatabaseTestBase
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(30);
    private const string Resource1 = "resource-1";
    private const string Resource2 = "resource-2";
    private const string LeadingPolicy = "leading";
    private const string TrailingPolicy = "trailing";
    private const string IgnoreBlockedPolicy = "ignore-blocked";
    private const string RenewBlockedPolicy = "renew-blocked";
    private const int StandardLimit = 3;
    private const int SingleAttemptLimit = 1;

    protected override bool SeedData => false;

    private PostgresRateLimitProvider? _target;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _target = GetRequiredService<PostgresRateLimitProvider>();
    }

    private PostgresRateLimitProvider Target => _target!;

    private static Policy GetPolicy(string policyName)
        => policyName switch
        {
            LeadingPolicy => new(StandardLimit, WindowDuration, RateLimitWindowBehavior.LeadingEdge, BlockDuration, BlockedRequestBehavior.Ignore),
            TrailingPolicy => new(StandardLimit, WindowDuration, RateLimitWindowBehavior.TrailingEdge, BlockDuration, BlockedRequestBehavior.Ignore),
            IgnoreBlockedPolicy => new(SingleAttemptLimit, WindowDuration, RateLimitWindowBehavior.LeadingEdge, BlockDuration, BlockedRequestBehavior.Ignore),
            RenewBlockedPolicy => new(SingleAttemptLimit, WindowDuration, RateLimitWindowBehavior.LeadingEdge, BlockDuration, BlockedRequestBehavior.Renew),
            _ => throw new ArgumentOutOfRangeException(nameof(policyName), policyName, null),
        };

    private ValueTask<RateLimitStatus> GetStatus(string policyName, string resource, string subject)
    {
        var policy = GetPolicy(policyName);
        return Target.GetStatus(policyName, resource, subject, policy.BlockedRequestBehavior, policy.BlockDuration, CancellationToken);
    }

    private ValueTask<RateLimitStatus> Record(string policyName, string resource, string subject, ushort cost)
    {
        var policy = GetPolicy(policyName);
        return Target.Record(policyName, resource, subject, cost, policy.Limit, policy.WindowDuration, policy.WindowBehavior, policy.BlockDuration, CancellationToken);
    }

    [Fact]
    public async Task GetStatus_NotFound_ReturnsNotFound()
    {
        var result = await GetStatus(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1");

        result.ShouldBeSameAs(RateLimitStatus.NotFound);
    }

    [Fact]
    public async Task Record_LeadingEdge_PreservesWindowWhileActive()
    {
        var now = TimeProvider.GetUtcNow();

        var first = await Record(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        first.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(1U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(now + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBeNull());

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var second = await Record(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        second.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(2U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(now + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBeNull());
    }

    [Theory]
    [InlineData(LeadingPolicy)]
    [InlineData(TrailingPolicy)]
    public async Task Record_AfterWindowExpiry_ResetsCountAndWindow(string policyName)
    {
        await Record(policyName, IRateLimiter.DefaultResource, "subject-1", cost: 1);

        TimeProvider.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        var now = TimeProvider.GetUtcNow();
        var result = await Record(policyName, IRateLimiter.DefaultResource, "subject-1", cost: 2);

        result.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(2U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(now + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBeNull());
    }

    [Fact]
    public async Task Record_TrailingEdge_RenewsWindowWhileActive()
    {
        var now = TimeProvider.GetUtcNow();

        var first = await Record(TrailingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        first.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(1U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(now + TimeSpan.FromHours(1)));

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var secondNow = TimeProvider.GetUtcNow();
        var second = await Record(TrailingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        second.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(2U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(secondNow + TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Record_WeightedCosts_AccumulateAcrossCalls()
    {
        var windowStart = TimeProvider.GetUtcNow();

        var first = await Record(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        first.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(1U),
            x => x.WindowStartedAt.ShouldBe(windowStart),
            x => x.WindowExpiresAt.ShouldBe(windowStart + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBeNull());

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var now = TimeProvider.GetUtcNow();
        var second = await Record(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 2);
        second.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeTrue(),
            x => x.Count.ShouldBe(3U),
            x => x.WindowStartedAt.ShouldBe(windowStart),
            x => x.WindowExpiresAt.ShouldBe(windowStart + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBe(now + TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task Record_CostCanBlockImmediately()
    {
        var now = TimeProvider.GetUtcNow();

        var result = await Record(LeadingPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 3);
        result.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeTrue(),
            x => x.Count.ShouldBe(3U),
            x => x.WindowStartedAt.ShouldBe(now),
            x => x.WindowExpiresAt.ShouldBe(now + TimeSpan.FromHours(1)),
            x => x.BlockedUntil.ShouldBe(now + TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task Record_WhileBlocked_ExtendsBlockedUntil()
    {
        var initial = await Record(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        initial.BlockedUntil.ShouldNotBeNull();

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var now = TimeProvider.GetUtcNow();
        var result = await Record(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);

        result.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeTrue(),
            x => x.Count.ShouldBe(2U),
            x => x.BlockedUntil.ShouldNotBeNull(),
            x => x.BlockedUntil.ShouldNotBe(initial.BlockedUntil),
            x => x.BlockedUntil.ShouldBe(now + TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task GetStatus_BlockedIgnore_DoesNotRenewBlock()
    {
        var initial = await Record(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);
        var blockedUntil = initial.BlockedUntil;

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var status = await GetStatus(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1");
        status.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U),
            x => x.BlockedUntil.ShouldBe(blockedUntil));
    }

    [Fact]
    public async Task GetStatus_BlockedRenew_RenewsBlock()
    {
        await Record(RenewBlockedPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);

        TimeProvider.Advance(TimeSpan.FromMinutes(10));

        var now = TimeProvider.GetUtcNow();
        var status = await GetStatus(RenewBlockedPolicy, IRateLimiter.DefaultResource, "subject-1");
        status.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U),
            x => x.BlockedUntil.ShouldBe(now + TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task GetStatus_ExpiredBlock_ReturnsUnblockedWithoutBlockedUntil()
    {
        await Record(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1", cost: 1);

        TimeProvider.Advance(TimeSpan.FromMinutes(31));

        var status = await GetStatus(IgnoreBlockedPolicy, IRateLimiter.DefaultResource, "subject-1");
        status.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.IsBlocked.ShouldBeFalse(),
            x => x.Count.ShouldBe(1U),
            x => x.BlockedUntil.ShouldBeNull(),
            x => x.WindowStartedAt.ShouldNotBeNull(),
            x => x.WindowExpiresAt.ShouldNotBeNull());
    }

    [Fact]
    public async Task Record_DifferentResources_TrackIndependentState()
    {
        var first = await Record(LeadingPolicy, Resource1, "subject-1", cost: 1);
        var second = await Record(LeadingPolicy, Resource2, "subject-1", cost: 1);
        var status1 = await GetStatus(LeadingPolicy, Resource1, "subject-1");
        var status2 = await GetStatus(LeadingPolicy, Resource2, "subject-1");

        first.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U));
        second.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U));
        status1.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U));
        status2.ShouldSatisfyAllConditions(
            x => x.Exists.ShouldBeTrue(),
            x => x.Count.ShouldBe(1U));
    }

    private readonly record struct Policy(
        int Limit,
        TimeSpan WindowDuration,
        RateLimitWindowBehavior WindowBehavior,
        TimeSpan BlockDuration,
        BlockedRequestBehavior BlockedRequestBehavior);
}
