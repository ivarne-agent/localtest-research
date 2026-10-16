using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Core;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Services;
using Altinn.Register.Tests.Mocks;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Altinn.Register.Tests.UnitTests;

public sealed class PersonLookupServiceTests
    : IDisposable
{
    private readonly Mock<IPersonClient> _persons;
    private readonly MockRateLimitProvider _rateLimitProvider;
    private readonly ILogger<PersonLookupService> _logger;
    private readonly ServiceProvider _serviceProvider;
    private readonly HybridCache _cache;
    private readonly IRateLimiter _rateLimiter;
    private readonly PersonLookupSettings _lookupSettings;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public PersonLookupServiceTests()
    {
        _persons = new Mock<IPersonClient>();
        _rateLimitProvider = new MockRateLimitProvider();
        _logger = NullLogger<PersonLookupService>.Instance;
        _lookupSettings = new PersonLookupSettings();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHybridCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([
                new($"Altinn:Register:RateLimit:Policy:{PersonLookupService.FailedAttemptsRateLimitPolicyName}:Limit", "2"),
                new($"Altinn:Register:RateLimit:Policy:{PersonLookupService.FailedAttemptsRateLimitPolicyName}:WindowDuration", "01:00:00"),
                new($"Altinn:Register:RateLimit:Policy:{PersonLookupService.FailedAttemptsRateLimitPolicyName}:WindowBehavior", "TrailingEdge"),
                new($"Altinn:Register:RateLimit:Policy:{PersonLookupService.FailedAttemptsRateLimitPolicyName}:BlockDuration", "01:00:00"),
                new($"Altinn:Register:RateLimit:Policy:{PersonLookupService.FailedAttemptsRateLimitPolicyName}:BlockedRequestBehavior", "Ignore"),
            ])
            .Build());
        services.AddSingleton(_rateLimitProvider);
        services.AddSingleton<IRateLimitProvider>(sp => sp.GetRequiredService<MockRateLimitProvider>());
        services.AddRateLimitPolicy(PersonLookupService.FailedAttemptsRateLimitPolicyName);

        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<HybridCache>();
        _rateLimiter = _serviceProvider.GetRequiredService<IRateLimiter>();
    }

    [Fact]
    public async Task GetPerson_CorrectInput_ReturnsPersonAndCachesResult()
    {
        Person person = new() { SSN = "personnumber", Name = "lastname", LastName = "lastname", };
        _persons.Setup(s => s.GetPerson(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(person);

        var target = CreateTarget();
        var activeUser = Guid.Parse("00000000-0000-0000-0000-000000000777");

        var first = await target.GetPerson("personnumber", "lastname", activeUser, CancellationToken);
        var second = await target.GetPerson("personnumber", "lastname", activeUser, CancellationToken);

        first.ShouldHaveValue().LastName.ShouldBe(person.LastName);
        second.ShouldHaveValue().LastName.ShouldBe(person.LastName);
        _persons.Verify(s => s.GetPerson("personnumber", It.IsAny<CancellationToken>()), Times.Once);
        _rateLimitProvider.GetStatusCallCount.ShouldBe(2);
        _persons.Verify(s => s.GetPerson("personnumber", It.IsAny<CancellationToken>()), Times.Once);
        _rateLimitProvider.LastGetStatus.ShouldBe(new MockRateLimitProvider.GetStatusCall(
            PersonLookupService.FailedAttemptsRateLimitPolicyName,
            IRateLimiter.DefaultResource,
            activeUser.ToString("D"),
            BlockedRequestBehavior.Ignore,
            TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task GetPerson_WrongInput_ReturnsProblemAndRecordsFailure()
    {
        _persons.Setup(s => s.GetPerson(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Person?)null);

        var target = CreateTarget();
        var activeUser = Guid.Parse("00000000-0000-0000-0000-000000000777");

        var actual = await target.GetPerson("personnumber", "lastname", activeUser, CancellationToken);

        actual.ShouldBeProblem(Problems.PersonNotFound.ErrorCode);
        _rateLimitProvider.RecordCallCount.ShouldBe(1);
        _rateLimitProvider.LastRecord.ShouldBe(new MockRateLimitProvider.RecordCall(
            PersonLookupService.FailedAttemptsRateLimitPolicyName,
            IRateLimiter.DefaultResource,
            activeUser.ToString("D"),
            1,
            2,
            TimeSpan.FromHours(1),
            RateLimitWindowBehavior.TrailingEdge,
            TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task GetPerson_TooManyFailedAttempts_ReturnsProblem()
    {
        var target = CreateTarget();
        var activeUser = Guid.Parse("00000000-0000-0000-0000-000000000777");
        _rateLimitProvider.SetStatus(
            PersonLookupService.FailedAttemptsRateLimitPolicyName,
            IRateLimiter.DefaultResource,
            activeUser.ToString("D"),
            RateLimitStatus.Found(
                count: 2,
                windowStartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                windowExpiresAt: DateTimeOffset.UtcNow.AddMinutes(59),
                blockedUntil: DateTimeOffset.UtcNow.AddMinutes(59)));

        var result = await target.GetPerson("personnumber", "lastname", activeUser, CancellationToken);

        result.ShouldBeProblem(Problems.PartyLookupTooManyFailedAttempts.ErrorCode);

        _persons.Verify(s => s.GetPerson(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _rateLimitProvider.RecordCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetPerson_CachedResult_DoesNotInvokePersonClientAgain()
    {
        Person person = new() { SSN = "personnumber", Name = "lastname", LastName = "lastname", };
        _persons.Setup(s => s.GetPerson(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(person);

        var target = CreateTarget();

        await target.GetPerson("personnumber", "lastname", Guid.Parse("00000000-0000-0000-0000-000000000777"), CancellationToken);
        await target.GetPerson("personnumber", "lastname", Guid.Parse("00000000-0000-0000-0000-000000000888"), CancellationToken);

        _rateLimitProvider.GetStatusCallCount.ShouldBe(2);
        _persons.Verify(s => s.GetPerson("personnumber", It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private PersonLookupService CreateTarget()
        => new(_persons.Object, Options.Create(_lookupSettings), _cache, _rateLimiter, _logger);
}
