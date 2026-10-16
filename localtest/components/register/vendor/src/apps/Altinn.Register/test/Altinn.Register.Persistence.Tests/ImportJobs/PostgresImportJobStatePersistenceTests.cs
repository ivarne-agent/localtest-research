using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Persistence.Tests.ImportJobs;

public class PostgresImportJobStatePersistenceTests
    : DatabaseTestBase
{
    protected override bool SeedData => false;

    private IUnitOfWorkManager? _manager;
    private IUnitOfWork? _uow;
    private IImportJobStatePersistence? _persistence;

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        _manager = GetRequiredService<IUnitOfWorkManager>();
        _uow = await _manager.CreateAsync(activityName: "test", cancellationToken: CancellationToken);
        _persistence = _uow.GetRequiredService<IImportJobStatePersistence>();
    }

    protected override async ValueTask DisposeAsync()
    {
        if (_uow is { } uow)
        {
            await uow.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private IUnitOfWork UoW
        => _uow!;

    private IImportJobStatePersistence Persistence
        => _persistence!;

    [Fact]
    public async Task JobState_CanRoundTrip_StringState()
    {
        var jobId = "test";

        var state = new StringState { Value = "test" };
        await Persistence.SetState(jobId, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetState<StringState>(jobId, cancellationToken: CancellationToken);
        result.ShouldHaveValue().Value.ShouldBe(state.Value);
    }

    [Fact]
    public async Task JobState_CanRoundTrip_IntState()
    {
        var jobId = "test";

        var state = new IntState { Value = 42 };
        await Persistence.SetState(jobId, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetState<IntState>(jobId, cancellationToken: CancellationToken);
        result.ShouldHaveValue().Value.ShouldBe(state.Value);
    }

    [Fact]
    public async Task JobState_GetState_ReturnsUnset_WhenMissing()
    {
        var result = await Persistence.GetState<StringState>("test", cancellationToken: CancellationToken);

        result.ShouldBeUnset();
    }

    [Fact]
    public async Task JobState_GetState_ReturnsNull_WhenWrongStateType()
    {
        var jobId = "test";

        var state = new StringState { Value = "test" };
        await Persistence.SetState(jobId, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetState<IntState>(jobId, cancellationToken: CancellationToken);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task JobState_ClearState_ClearsState()
    {
        var jobId = "test";

        var state = new StringState { Value = "test" };
        await Persistence.SetState(jobId, state, cancellationToken: CancellationToken);
        var result = await Persistence.GetState<StringState>(jobId, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await Persistence.ClearState(jobId, cancellationToken: CancellationToken);

        result = await Persistence.GetState<StringState>(jobId, cancellationToken: CancellationToken);
        result.ShouldBeUnset();
    }

    [Fact]
    public async Task PartyState_CanRoundTrip_StringState()
    {
        var party = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var jobId = "test";

        var partyUuid = party.PartyUuid.Value;

        var state = new StringState { Value = "test" };
        await Persistence.SetPartyState(jobId, partyUuid, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetPartyState<StringState>(jobId, partyUuid, cancellationToken: CancellationToken);
        result.ShouldHaveValue().Value.ShouldBe(state.Value);
    }

    [Fact]
    public async Task PartyState_CanRoundTrip_IntState()
    {
        var party = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var jobId = "test";

        var partyUuid = party.PartyUuid.Value;

        var state = new IntState { Value = 42 };
        await Persistence.SetPartyState(jobId, partyUuid, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetPartyState<IntState>(jobId, partyUuid, cancellationToken: CancellationToken);
        result.ShouldHaveValue().Value.ShouldBe(state.Value);
    }

    [Fact]
    public async Task PartyState_GetPartyState_ReturnsUnset_WhenMissing()
    {
        var result = await Persistence.GetPartyState<StringState>("test", Guid.NewGuid(), cancellationToken: CancellationToken);

        result.ShouldBeUnset();
    }

    [Fact]
    public async Task PartyState_GetPartyState_ReturnsNull_WhenWrongStateType()
    {
        var party = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var jobId = "test";

        var partyUuid = party.PartyUuid.Value;

        var state = new StringState { Value = "test" };
        await Persistence.SetPartyState(jobId, partyUuid, state, cancellationToken: CancellationToken);

        var result = await Persistence.GetPartyState<IntState>(jobId, partyUuid, cancellationToken: CancellationToken);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task PartyState_ClearState_ClearsState()
    {
        var party = await UoW.CreateOrg(cancellationToken: CancellationToken);
        var jobId = "test";

        var partyUuid = party.PartyUuid.Value;

        var state = new StringState { Value = "test" };
        await Persistence.SetPartyState(jobId, partyUuid, state, cancellationToken: CancellationToken);
        var result = await Persistence.GetPartyState<StringState>(jobId, partyUuid, cancellationToken: CancellationToken);
        result.ShouldHaveValue();

        await Persistence.ClearPartyState(jobId, partyUuid, cancellationToken: CancellationToken);

        result = await Persistence.GetPartyState<StringState>(jobId, partyUuid, cancellationToken: CancellationToken);
        result.ShouldBeUnset();
    }

    private sealed class StringState
        : IImportJobState<StringState>
    {
        public static string StateType => nameof(StringState);

        public required string Value { get; init; }
    }

    private sealed class IntState
        : IImportJobState<IntState>
    {
        public static string StateType => nameof(IntState);

        public required int Value { get; init; }
    }
}
