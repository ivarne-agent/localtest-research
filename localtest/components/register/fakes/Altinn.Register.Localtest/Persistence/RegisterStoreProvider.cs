using System.Collections.Immutable;
using System.Net;
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
    public const string HttpClientName = "localtest-register-apps";

    private readonly IOptions<LocaltestRegisterOptions> _options;
    private readonly StaticExternalRoleDefinitionPersistence _roleDefinitions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RegisterStore> _storeLogger;
    private readonly ILogger<RegisterStoreProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ImmutableList<RegisteredApp> _apps = [];
    private ImmutableArray<string> _conflicts = [];
    private volatile RegisterStore _current;

    public RegisterStoreProvider(
        IOptions<LocaltestRegisterOptions> options,
        StaticExternalRoleDefinitionPersistence roleDefinitions,
        IHttpClientFactory httpClientFactory,
        ILogger<RegisterStore> storeLogger,
        ILogger<RegisterStoreProvider> logger)
    {
        _options = options;
        _roleDefinitions = roleDefinitions;
        _httpClientFactory = httpClientFactory;
        _storeLogger = storeLogger;
        _logger = logger;
        _current = Build([("seed", LoadSeed())], out _conflicts);
    }

    public RegisterStore Current => _current;

    public RegistrationState State => new(_apps, _conflicts, _current.Warnings, _current.Users.Length, _current.Parties.Length);

    public async Task<RegistrationState> Register(string org, string app, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_apps.Any(a => a.Matches(org, app)))
            {
                _apps = _apps.Add(new RegisteredApp(org, app, AppTestDataStatus.Pending, null, null, null));
            }

            return await RefreshLocked(cancellationToken);
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
            return await RefreshLocked(cancellationToken);
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
            return await RefreshLocked(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<RegistrationState> RefreshLocked(CancellationToken cancellationToken)
    {
        var fetched = new List<RegisteredApp>();
        foreach (var app in _apps)
        {
            fetched.Add(await Fetch(app, cancellationToken));
        }

        _apps = [.. fetched];

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

    private async Task<RegisteredApp> Fetch(RegisteredApp app, CancellationToken cancellationToken)
    {
        var url = new Uri(new Uri(_options.Value.AppsBaseUrl.TrimEnd('/') + "/"), $"{Uri.EscapeDataString(app.Org)}/{Uri.EscapeDataString(app.App)}/testData.json");
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return app with { Status = AppTestDataStatus.NoTestData, Data = null, Error = null, FetchedAt = DateTimeOffset.UtcNow };
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var data = TestDataFile.Parse(json, url.ToString());
            return app with { Status = AppTestDataStatus.TestData, Data = data, Error = null, FetchedAt = DateTimeOffset.UtcNow };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Keep the last good data: an app that is restarting should not take its users away.
            _logger.LogWarning(e, "Fetching test data for {Org}/{App} from {Url} failed", app.Org, app.App, url);
            return app with { Status = app.Data is null ? AppTestDataStatus.Unreachable : AppTestDataStatus.Stale, Error = $"{url}: {e.Message}" };
        }
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
    /// <summary>Registered, not fetched yet.</summary>
    Pending,

    /// <summary>The app serves a <c>testData.json</c>.</summary>
    TestData,

    /// <summary>The app answered 404: it has no test data.</summary>
    NoTestData,

    /// <summary>The app could not be reached and nothing was fetched before.</summary>
    Unreachable,

    /// <summary>The last fetch failed; the data from an earlier fetch is still used.</summary>
    Stale,
}

public sealed record RegisteredApp(string Org, string App, AppTestDataStatus Status, TestDataFile? Data, string? Error, DateTimeOffset? FetchedAt)
{
    public bool Matches(string org, string app)
        => string.Equals(Org, org, StringComparison.OrdinalIgnoreCase) && string.Equals(App, app, StringComparison.OrdinalIgnoreCase);
}

public sealed record RegistrationState(IReadOnlyList<RegisteredApp> Apps, IReadOnlyList<string> Conflicts, IReadOnlyList<string> Warnings, int Users, int Parties);
