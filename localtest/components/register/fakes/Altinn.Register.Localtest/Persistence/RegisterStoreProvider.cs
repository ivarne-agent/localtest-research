using System.Collections.Immutable;
using Altinn.Register.Localtest.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// Holds the current <see cref="RegisterStore"/> and rebuilds it when the set of registered apps changes or a
/// refresh is requested. Readers take <see cref="Current"/> once per operation; a rebuild swaps the whole snapshot,
/// so nobody sees a half-merged register.
/// </summary>
/// <remarks>
/// Merge rule (as the old localtest): when every registered app serves a <c>testData.json</c>, only the apps'
/// users exist; when no app is registered or at least one serves none, the apps' users are added to the seed.
/// </remarks>
public sealed class RegisterStoreProvider
{

    private readonly IOptions<LocaltestRegisterOptions> _options;
    private readonly StaticExternalRoleDefinitionPersistence _roleDefinitions;
    private readonly ILogger<RegisterStore> _storeLogger;
    private readonly ILogger<RegisterStoreProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ImmutableList<RegisteredApp> _apps = [];
    private ImmutableArray<string> _conflicts = [];
    private volatile RegisterStore _current;

    public RegisterStoreProvider(
        IOptions<LocaltestRegisterOptions> options,
        StaticExternalRoleDefinitionPersistence roleDefinitions,
        ILogger<RegisterStore> storeLogger,
        ILogger<RegisterStoreProvider> logger)
    {
        _options = options;
        _roleDefinitions = roleDefinitions;
        _storeLogger = storeLogger;
        _logger = logger;
        _current = Build([("seed", LoadSeed())], out _conflicts);
    }

    public RegisterStore Current => _current;

    public RegistrationState State => new(_apps, _conflicts, _current.Warnings, _current.Users.Length, _current.Parties.Length);

    /// <summary>
    /// Registers an app with the test data the app runner posted (the app's <c>App/wwwroot/testData.json</c> as is),
    /// or with none. The Register fetches nothing itself. Posting again replaces the earlier data.
    /// </summary>
    public async Task<RegistrationState> Register(string org, string app, TestDataFile? data, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var registered = new RegisteredApp(org, app, data is null ? AppTestDataStatus.NoTestData : AppTestDataStatus.TestData, data, DateTimeOffset.UtcNow);
            var existing = _apps.FirstOrDefault(a => a.Matches(org, app));
            _apps = existing is null ? _apps.Add(registered) : _apps.Replace(existing, registered);
            return RefreshLocked();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RegistrationState?> Unregister(string org, string app, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = _apps.FirstOrDefault(a => a.Matches(org, app));
            if (existing is null)
            {
                return null;
            }

            _apps = _apps.Remove(existing);
            return RefreshLocked();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RegistrationState> Refresh(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return RefreshLocked();
        }
        finally
        {
            _lock.Release();
        }
    }

    private RegistrationState RefreshLocked()
    {
        // The seed is re-read too, so edits to testData.json apply on refresh. A broken seed keeps the current store.
        var seed = LoadSeed();
        var withData = _apps.Where(a => a.Data is not null).ToList();
        var appsOnly = _apps.Count > 0 && withData.Count == _apps.Count;
        IEnumerable<(string, TestDataFile)> sources = withData.Select(a => ($"app {a.Org}/{a.App}", a.Data!));
        if (!appsOnly)
        {
            sources = sources.Prepend(("seed", seed));
        }

        _current = Build(sources, out _conflicts);
        _logger.LogInformation(
            "Localtest register rebuilt from {Sources}: {Users} users, {Conflicts} conflicts",
            appsOnly ? "registered apps only" : "seed and registered apps",
            _current.Users.Length,
            _conflicts.Length);
        return State;
    }

    private TestDataFile LoadSeed()
    {
        var path = _options.Value.TestDataPath;
        _logger.LogInformation("Loading localtest register seed from {Path}", path);
        return TestDataFile.Load(path);
    }

    private RegisterStore Build(IEnumerable<(string Source, TestDataFile Data)> sources, out ImmutableArray<string> conflicts)
    {
        var found = new List<string>();
        var merged = TestDataMerger.Merge(sources, found);
        foreach (var conflict in found)
        {
            _logger.LogWarning("Test data conflict: {Conflict}", conflict);
        }

        conflicts = [.. found];
        return new RegisterStore(merged, _roleDefinitions, _storeLogger);
    }
}

public enum AppTestDataStatus
{
    /// <summary>The runner posted a testData.json for the app.</summary>
    TestData,

    /// <summary>The app was registered without test data.</summary>
    NoTestData,
}

public sealed record RegisteredApp(string Org, string App, AppTestDataStatus Status, TestDataFile? Data, DateTimeOffset ReceivedAt)
{
    public bool Matches(string org, string app)
        => string.Equals(Org, org, StringComparison.OrdinalIgnoreCase) && string.Equals(App, app, StringComparison.OrdinalIgnoreCase);
}

public sealed record RegistrationState(IReadOnlyList<RegisteredApp> Apps, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Warnings, int Users, int Parties);
