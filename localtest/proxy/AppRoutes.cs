using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocaltestProxy;

/// <summary>
/// The running apps, one route each: <c>/{org}/{app}/…</c> → the app's own address. The app runner pushes them
/// (<c>PUT proxy/api/localtest/apps/{org}/{app}</c>) and removes them on stop. They are saved to a file on a volume, so
/// a restarted proxy keeps routing the apps that are still running; nothing watches the file.
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
            if (!File.Exists(_file))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize(File.ReadAllText(_file), ProxyJsonContext.Default.AppRouteArray) ?? [];
            foreach (var route in saved)
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
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temporary = _file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(All.ToArray(), ProxyJsonContext.Default.AppRouteArray));
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

/// <summary>One app: requests to <c>/{Org}/{App}/…</c> go to <c>{Upstream}/{Org}/{App}/…</c>.</summary>
public sealed record AppRoute(string Org, string App, string Upstream, DateTimeOffset Registered);

/// <summary>The body of <c>PUT proxy/api/localtest/apps/{org}/{app}</c>.</summary>
public sealed record AppRouteRequest(string? Upstream);
