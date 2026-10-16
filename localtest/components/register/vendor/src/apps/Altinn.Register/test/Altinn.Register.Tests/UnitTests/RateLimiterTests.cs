using Altinn.Register.Core.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Tests.UnitTests;

public class RateLimiterTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetStatus_ConfiguredPolicy_ForwardsMaterializedSettings()
    {
        var provider = new CapturingRateLimitProvider
        {
            GetStatusResult = RateLimitStatus.NotFound,
        };

        using var serviceProvider = CreateServiceProvider(provider, static options =>
        {
            options.IsConfigured = true;
            options.Limit = 3;
            options.WindowDuration = TimeSpan.FromHours(1);
            options.WindowBehavior = RateLimitWindowBehavior.TrailingEdge;
            options.BlockDuration = TimeSpan.FromMinutes(30);
            options.BlockedRequestBehavior = BlockedRequestBehavior.Renew;
        });

        var sut = serviceProvider.GetRequiredService<IRateLimiter>();
        var result = await sut.GetStatus("test", "resource-1", "subject-1", CancellationToken);

        result.ShouldBeSameAs(RateLimitStatus.NotFound);
        provider.LastGetStatus.ShouldNotBeNull();
        provider.LastGetStatus!.Value.ShouldBe(new CapturingRateLimitProvider.GetStatusCall(
            "test",
            "resource-1",
            "subject-1",
            BlockedRequestBehavior.Renew,
            TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task Record_ConfiguredPolicy_ForwardsMaterializedSettings()
    {
        var provider = new CapturingRateLimitProvider
        {
            RecordResult = RateLimitStatus.NotFound,
        };

        using var serviceProvider = CreateServiceProvider(provider, static options =>
        {
            options.IsConfigured = true;
            options.Limit = 3;
            options.WindowDuration = TimeSpan.FromHours(1);
            options.WindowBehavior = RateLimitWindowBehavior.LeadingEdge;
            options.BlockDuration = TimeSpan.FromMinutes(30);
            options.BlockedRequestBehavior = BlockedRequestBehavior.Ignore;
        });

        var sut = serviceProvider.GetRequiredService<IRateLimiter>();
        var result = await sut.Record("test", "resource-1", "subject-1", cost: 2, cancellationToken: CancellationToken);

        result.ShouldBeSameAs(RateLimitStatus.NotFound);
        provider.LastRecord.ShouldNotBeNull();
        provider.LastRecord!.Value.ShouldBe(new CapturingRateLimitProvider.RecordCall(
            "test",
            "resource-1",
            "subject-1",
            2,
            3,
            TimeSpan.FromHours(1),
            RateLimitWindowBehavior.LeadingEdge,
            TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task GetStatus_UnconfiguredPolicy_ThrowsOptionsValidationException()
    {
        using var serviceProvider = CreateServiceProvider(new CapturingRateLimitProvider(), configure: null);

        var sut = serviceProvider.GetRequiredService<IRateLimiter>();
        var act = () => sut.GetStatus("test", "resource-1", "subject-1").AsTask();

        var exn = await Assert.ThrowsAsync<OptionsValidationException>(act);
        exn.Failures.ShouldHaveSingleItem().ShouldBe("Rate limit policy 'test' has not been configured.");
    }

    [Fact]
    public async Task Record_UnconfiguredPolicy_ThrowsOptionsValidationException()
    {
        using var serviceProvider = CreateServiceProvider(new CapturingRateLimitProvider(), configure: null);

        var sut = serviceProvider.GetRequiredService<IRateLimiter>();
        var act = () => sut.Record("test", "resource-1", "subject-1", cost: 1).AsTask();

        var exn = await Assert.ThrowsAsync<OptionsValidationException>(act);
        exn.Failures.ShouldHaveSingleItem().ShouldBe("Rate limit policy 'test' has not been configured.");
    }

    private static ServiceProvider CreateServiceProvider(
        CapturingRateLimitProvider provider,
        Action<RateLimitPolicySettings>? configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRateLimitProvider>(provider);
        services.AddRegisterRateLimiting();

        if (configure is not null)
        {
            services.Configure("test", configure);
        }

        return services.BuildServiceProvider();
    }

    private sealed class CapturingRateLimitProvider
        : IRateLimitProvider
    {
        public GetStatusCall? LastGetStatus { get; private set; }

        public RecordCall? LastRecord { get; private set; }

        public RateLimitStatus GetStatusResult { get; init; } = RateLimitStatus.NotFound;

        public RateLimitStatus RecordResult { get; init; } = RateLimitStatus.NotFound;

        public ValueTask<RateLimitStatus> GetStatus(
            string policyName,
            string resource,
            string subject,
            BlockedRequestBehavior blockedRequestBehavior,
            TimeSpan blockDuration,
            CancellationToken cancellationToken = default)
        {
            LastGetStatus = new(policyName, resource, subject, blockedRequestBehavior, blockDuration);
            return ValueTask.FromResult(GetStatusResult);
        }

        public ValueTask<RateLimitStatus> Record(
            string policyName,
            string resource,
            string subject,
            ushort cost,
            int limit,
            TimeSpan windowDuration,
            RateLimitWindowBehavior windowBehavior,
            TimeSpan blockDuration,
            CancellationToken cancellationToken = default)
        {
            LastRecord = new(policyName, resource, subject, cost, limit, windowDuration, windowBehavior, blockDuration);
            return ValueTask.FromResult(RecordResult);
        }

        public readonly record struct GetStatusCall(
            string PolicyName,
            string Resource,
            string Subject,
            BlockedRequestBehavior BlockedRequestBehavior,
            TimeSpan BlockDuration);

        public readonly record struct RecordCall(
            string PolicyName,
            string Resource,
            string Subject,
            ushort Cost,
            int Limit,
            TimeSpan WindowDuration,
            RateLimitWindowBehavior WindowBehavior,
            TimeSpan BlockDuration);
    }
}
