using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Localtest.Provisioning;

namespace Localtest.Proxy;

/// <summary>
/// The registered apps, one route each: <c>/{org}/{app}/…</c> → the app's own address, with the outcome of the last
/// provisioning. The app runner registers them (<c>PUT localtest/api/apps/{org}/{app}</c>, <see cref="AppsApi"/>) and
/// removes them. They are saved to a file on a volume, so a restarted front door keeps routing the apps that are still
/// running; nothing watches the file. An empty file name (<c>Proxy__AppRoutesFile=</c>, the minimal mode) keeps them in
/// memory only.
/// </summary>
public sealed partial class AppRoutes
{
    private readonly ConcurrentDictionary<string, AppRoute> _routes = new(StringComparer.Ordinal);
    private readonly string _file;
    private readonly ILogger<AppRoutes> _logger;
    private readonly Lock _saveLock = new();

    public AppRoutes(string file, ILogger<AppRoutes> logger)
    {
        _file = file;
        _logger = logger;
        Load();
    }

    /// <summary>Org codes and app names: lower-case letters, digits and hyphens.</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    public static partial Regex Segment();

    public IEnumerable<AppRoute> All => _routes.Values.OrderBy(r => r.Org).ThenBy(r => r.App);

    public AppRoute? Get(string org, string app) => _routes.GetValueOrDefault(Key(org, app));

    public void Set(AppRoute route)
    {
        _routes[Key(route.Org, route.App)] = route;
        Save();
    }

    public bool Remove(string org, string app)
    {
        bool removed = _routes.TryRemove(Key(org, app), out _);
        if (removed)
        {
            Save();
        }

        return removed;
    }

    /// <summary>The app route for <c>/{org}/{app}/…</c>, or null.</summary>
    public AppRoute? Resolve(string path, out bool withoutTrailingSlash)
    {
        withoutTrailingSlash = false;
        var segments = path.Split('/', 4);
        // "/ttd/app/rest" → ["", "ttd", "app", "rest"]; "/ttd/app" → ["", "ttd", "app"].
        if (segments.Length < 3 || !_routes.TryGetValue(Key(segments[1], segments[2]), out var route))
        {
            return null;
        }

        withoutTrailingSlash = segments.Length == 3;
        return route;
    }

    private static string Key(string org, string app) => org + "/" + app;

    private void Load()
    {
        try
        {
            if (_file.Length == 0 || !File.Exists(_file))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize(File.ReadAllText(_file), LocaltestJsonContext.Default.AppRouteArray) ?? [];
            // An entry without an address (a file from before the registration API) is dropped.
            foreach (var route in saved.Where(r => !string.IsNullOrEmpty(r.Url)))
            {
                _routes[Key(route.Org, route.App)] = route;
            }

            _logger.LogInformation("Loaded {Count} app routes from {File}", saved.Length, _file);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not read the app routes in {File}; starting with none", _file);
        }
    }

    private void Save()
    {
        if (_file.Length == 0)
        {
            return;
        }

        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temporary = _file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(All.ToArray(), LocaltestJsonContext.Default.AppRouteArray));
                File.Move(temporary, _file, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Routing still works; only a proxy restart would forget the routes.
                _logger.LogWarning(e, "Could not save the app routes to {File}", _file);
            }
        }
    }
}

/// <summary>
/// One app: requests to <c>/{Org}/{App}/…</c> go to <c>{Url}/{Org}/{App}/…</c>. <see cref="Provisioning"/> is the
/// outcome of the last provisioning from that address.
/// </summary>
public sealed record AppRoute(string Org, string App, string Url, DateTimeOffset Registered, ProvisioningStatus? Provisioning);

/// <summary>The body of <c>PUT localtest/api/apps/{org}/{app}</c> and <c>PUT localtest/api/routes/frontend-dev-server</c>.</summary>
public sealed record RouteRequest(string? Url);
