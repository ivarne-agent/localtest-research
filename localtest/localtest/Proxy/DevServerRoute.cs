using System.Text.Json;

namespace Localtest.Proxy;

/// <summary>
/// The frontend dev server's route: every path on <see cref="PlatformRoutes.Hosts.FrontendDevServer"/> goes to the Vite
/// server the runner registered (<c>PUT localtest/api/routes/frontend-dev-server</c>, for <c>studioctl run
/// --dev-frontend</c>). Without one, that host is served like the main host, so the built app-frontend under
/// <c>/app-frontend</c> answers. Saved to a file next to the app routes, like them.
/// </summary>
public sealed class DevServerRoute
{
    public const string Name = "frontend-dev-server";

    private readonly string _file;
    private readonly ILogger<DevServerRoute> _logger;
    private readonly Lock _lock = new();
    private DevServer? _current;

    public DevServerRoute(string file, ILogger<DevServerRoute> logger)
    {
        _file = file;
        _logger = logger;
        try
        {
            if (File.Exists(_file))
            {
                _current = JsonSerializer.Deserialize(File.ReadAllText(_file), LocaltestJsonContext.Default.DevServer);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not read the frontend dev server route in {File}; starting without one", _file);
        }
    }

    public DevServer? Current => _current;

    /// <summary>The route for a request on <paramref name="host"/> (without port), or null.</summary>
    public Route? Resolve(string host)
        => _current is { } current && string.Equals(host, PlatformRoutes.Hosts.FrontendDevServer, StringComparison.OrdinalIgnoreCase)
            ? new Route(Name, current.Url, RouteKind.FrontendDevServer)
            : null;

    public void Set(DevServer? value)
    {
        lock (_lock)
        {
            _current = value;
            try
            {
                if (value is null)
                {
                    File.Delete(_file);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var temporary = _file + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(value, LocaltestJsonContext.Default.DevServer));
                File.Move(temporary, _file, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(e, "Could not save the frontend dev server route to {File}", _file);
            }
        }
    }
}

/// <summary>The registered frontend dev server.</summary>
public sealed record DevServer(string Url, DateTimeOffset Registered);
