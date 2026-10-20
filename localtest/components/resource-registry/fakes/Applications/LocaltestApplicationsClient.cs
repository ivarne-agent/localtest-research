#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.ResourceRegistry.Core.Configuration;
using Altinn.ResourceRegistry.Core.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Altinn.ResourceRegistry.Localtest.Applications;

/// <summary>
/// Production's <c>ApplicationsClient</c> without its 10-minute memory cache of the application list: every call asks
/// Storage, so an app the runner has just pushed is in <c>resourcelist</c> at once (production's single-app lookup
/// is uncached already). Same requests, same filtering of <c>a1-</c>/<c>a2-</c> apps.
/// </summary>
public sealed class LocaltestApplicationsClient(HttpClient client, IOptions<PlatformSettings> settings) : IApplications
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<ApplicationList> GetApplicationList(bool includeMigratedApps, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(settings.Value.StorageApiEndpoint + "applications", cancellationToken);
        response.EnsureSuccessStatusCode();
        var applications = await response.Content.ReadFromJsonAsync<ApplicationList>(SerializerOptions, cancellationToken)
            ?? new ApplicationList { Applications = [] };

        return includeMigratedApps
            ? applications
            : new ApplicationList
            {
                Applications = applications.Applications.Where(a => !a.Id.Contains("/a2-") && !a.Id.Contains("/a1-")).ToList(),
            };
    }

    public async Task<Application> GetApplication(string org, string app, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(settings.Value.StorageApiEndpoint + $"applications/{org}/{app}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null!;
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Application>(SerializerOptions, cancellationToken))!;
    }
}
