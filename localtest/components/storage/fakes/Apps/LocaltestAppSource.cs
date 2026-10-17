#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Localtest.Apps;

/// <summary>
/// Application metadata and texts as the app itself has them. Production Storage only knows what Designer pushed into
/// Postgres; locally the running app is the source of truth. Order: the running app through the platform base URL
/// (<c>{org}/{app}/api/v1/applicationmetadata?checkOrgApp=false</c> and <c>{org}/{app}/api/v1/texts/{lang}</c>, the
/// app-lib endpoints today's localtest reads), then the mounted folder <c>{AppsPath}/{org}/{app}/applicationmetadata.json</c>
/// and <c>{AppsPath}/{org}/{app}/texts/resource.{lang}.json</c>. Answers are cached for <c>AppCacheSeconds</c>, misses
/// for two seconds, so a starting app is picked up quickly.
/// </summary>
public sealed class LocaltestAppSource(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<LocaltestStorageOptions> options,
    ILogger<LocaltestAppSource> logger)
{
    public const string HttpClientName = "localtest-app";

    private static readonly TimeSpan MissCacheDuration = TimeSpan.FromSeconds(2);
    private readonly LocaltestStorageOptions _options = options.Value;

    public Task<Application?> GetApplication(string org, string app, CancellationToken cancellationToken = default)
        => Cached($"localtest:app:{org}/{app}", async () =>
        {
            var application = await FromApp<Application>(org, app, "applicationmetadata?checkOrgApp=false", cancellationToken)
                ?? FromFolder<Application>(org, app, "applicationmetadata.json");
            if (application is not null)
            {
                application.Id ??= $"{org}/{app}";
                application.Org ??= org;
            }

            return application;
        });

    public Task<TextResource?> GetTexts(string org, string app, string language, CancellationToken cancellationToken = default)
        => Cached($"localtest:texts:{org}/{app}/{language}", async () =>
        {
            var texts = await FromApp<TextResource>(org, app, $"texts/{language}", cancellationToken)
                ?? FromFolder<TextResource>(org, app, Path.Combine("texts", $"resource.{language}.json"));
            if (texts is not null)
            {
                texts.Id = $"{org}-{app}-{language}";
                texts.Org = org;
                texts.Language = language;
            }

            return texts;
        });

    /// <summary>The <c>{org}/{app}</c> ids that have a metadata file in the mounted folder.</summary>
    public IReadOnlyList<(string Org, string App)> FolderApps()
    {
        if (!Directory.Exists(_options.AppsPath))
        {
            return [];
        }

        return Directory.EnumerateDirectories(_options.AppsPath)
            .SelectMany(orgDir => Directory.EnumerateDirectories(orgDir)
                .Where(appDir => File.Exists(Path.Combine(appDir, "applicationmetadata.json")))
                .Select(appDir => (Path.GetFileName(orgDir), Path.GetFileName(appDir))))
            .ToList();
    }

    private async Task<T?> Cached<T>(string key, Func<Task<T?>> factory)
        where T : class
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached;
        }

        var value = await factory();
        cache.Set(key, value, value is null ? MissCacheDuration : TimeSpan.FromSeconds(_options.AppCacheSeconds));
        return value;
    }

    private async Task<T?> FromApp<T>(string org, string app, string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_options.AppBaseUrl))
        {
            return null;
        }

        var url = $"{_options.AppBaseUrl.TrimEnd('/')}/{org}/{app}/api/v1/{path}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.AppRequestTimeoutSeconds));
            using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(url, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Localtest app source: {Url} answered {Status}", url, (int)response.StatusCode);
                return null;
            }

            return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogDebug("Localtest app source: {Url} not reachable: {Message}", url, e.Message);
            return null;
        }
    }

    private T? FromFolder<T>(string org, string app, string relativePath)
        where T : class
    {
        var file = Path.Combine(_options.AppsPath, org, app, relativePath);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(file));
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Localtest app source: {File} is not valid JSON", file);
            return null;
        }
    }
}
