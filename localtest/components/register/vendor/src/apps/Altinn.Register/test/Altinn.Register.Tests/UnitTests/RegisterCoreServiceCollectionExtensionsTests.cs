using Altinn.Register.Core.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Tests.UnitTests;

public class RegisterCoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRateLimitPolicy_NullName_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

#pragma warning disable CS8625 // Intentionally validating null argument behavior.
        var act = () => services.AddRateLimitPolicy(null);
#pragma warning restore CS8625

        var exn = Assert.Throws<ArgumentNullException>(act);
        exn.ParamName.ShouldBe("name");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void AddRateLimitPolicy_WhitespaceName_ThrowsArgumentException(string name)
    {
        var services = new ServiceCollection();

        var act = () => services.AddRateLimitPolicy(name);

        var exn = Assert.Throws<ArgumentException>(act);
        exn.ParamName.ShouldBe("name");
        exn.Message.ShouldContain("must not be null or whitespace");
    }

    [Fact]
    public void AddRateLimitPolicy_BindsNamedConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Altinn:Register:RateLimit:Policy:test:Limit", "3"),
                new("Altinn:Register:RateLimit:Policy:test:WindowDuration", "01:00:00"),
                new("Altinn:Register:RateLimit:Policy:test:WindowBehavior", "TrailingEdge"),
                new("Altinn:Register:RateLimit:Policy:test:BlockDuration", "00:30:00"),
                new("Altinn:Register:RateLimit:Policy:test:BlockedRequestBehavior", "Renew"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddRateLimitPolicy("test");

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptionsMonitor<RateLimitPolicySettings>>().Get("test");
        options.IsConfigured.ShouldBeTrue();
        options.Limit.ShouldBe(3);
        options.WindowDuration.ShouldBe(TimeSpan.FromHours(1));
        options.WindowBehavior.ShouldBe(RateLimitWindowBehavior.TrailingEdge);
        options.BlockDuration.ShouldBe(TimeSpan.FromMinutes(30));
        options.BlockedRequestBehavior.ShouldBe(BlockedRequestBehavior.Renew);
    }

    [Fact]
    public void AddRegisterRateLimiting_UnconfiguredPolicy_ThrowsValidationException()
    {
        var services = new ServiceCollection();
        services.AddRegisterRateLimiting();

        using var serviceProvider = services.BuildServiceProvider();

        var act = () => serviceProvider.GetRequiredService<IOptionsMonitor<RateLimitPolicySettings>>().Get("test");
        var exn = Assert.Throws<OptionsValidationException>(act);

        exn.Failures.ShouldHaveSingleItem().ShouldBe("Rate limit policy 'test' has not been configured.");
    }

    [Fact]
    public void AddRateLimitPolicy_WithInvalidConfiguration_ThrowsValidationException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Altinn:Register:RateLimit:Policy:test:Limit", "0"),
                new("Altinn:Register:RateLimit:Policy:test:WindowDuration", "00:00:30"),
                new("Altinn:Register:RateLimit:Policy:test:BlockDuration", "00:00:30"),
            ])
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddRateLimitPolicy("test");

        using var serviceProvider = services.BuildServiceProvider();

        var act = () => serviceProvider.GetRequiredService<IOptionsMonitor<RateLimitPolicySettings>>().Get("test");
        var exn = Assert.Throws<OptionsValidationException>(act);

        exn.Failures.ShouldContain(failure => failure.Contains(nameof(RateLimitPolicySettings.Limit)));
        exn.Failures.ShouldContain(failure => failure.Contains(nameof(RateLimitPolicySettings.WindowDuration)));
        exn.Failures.ShouldContain(failure => failure.Contains(nameof(RateLimitPolicySettings.BlockDuration)));
        exn.Failures.ShouldNotContain(failure => failure.Contains("has not been configured"));
    }

    [Fact]
    public void AddRateLimitPolicy_WhenCalledTwiceForSameName_DoesNotAddDuplicateBindings()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddRateLimitPolicy("test");

        var configureCount = services.Count(static sd => sd.ServiceType == typeof(IConfigureOptions<RateLimitPolicySettings>));
        var changeTokenCount = services.Count(static sd => sd.ServiceType == typeof(IOptionsChangeTokenSource<RateLimitPolicySettings>));

        services.AddRateLimitPolicy("test");

        services.Count(static sd => sd.ServiceType == typeof(IConfigureOptions<RateLimitPolicySettings>)).ShouldBe(configureCount);
        services.Count(static sd => sd.ServiceType == typeof(IOptionsChangeTokenSource<RateLimitPolicySettings>)).ShouldBe(changeTokenCount);
    }
}
