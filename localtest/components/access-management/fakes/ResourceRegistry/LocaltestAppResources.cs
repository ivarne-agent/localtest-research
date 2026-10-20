#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.AccessManagement.Localtest.ResourceRegistry;

/// <summary>
/// The app resources of this stack, derived the way the production Resource Registry derives them: one
/// <c>app_{org}_{app}</c> resource per application in Storage (pushed there by the app runner), with the resource's
/// subjects (role codes and access packages) read from the app policy the runner pushed to the shared policy store.
/// Shared by the two Resource Registry stand-ins (<see cref="LocaltestResourceRegistryClient"/> for the delegation
/// services, <see cref="LocaltestAltinnResourceRegistry"/> for the production resource sync).
/// </summary>
public sealed class LocaltestAppResources(
    IHttpClientFactory httpClientFactory,
    IOptions<LocaltestAccessManagementOptions> options,
    TimeProvider timeProvider,
    ILogger<LocaltestAppResources> logger)
{
    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly XNamespace Xacml = "urn:oasis:names:tc:xacml:3.0:core:schema:wd-17";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<AppResource> Apps)? _cached;

    /// <summary>
    /// Service-owner organisation numbers for the org codes test apps use. Real owners have their number from
    /// altinncdn.no/orgs/altinn-orgs.json; <c>ttd</c> has none there and gets Digdir's (991825827), the convention the app
    /// backend and studioctl use for test apps (altinn-studio <c>operatorcontext.go</c>). The register seed's
    /// Testdepartementet party (405003309) is a different, local organisation.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string OrgNumber, string Name)> ServiceOwners = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["ttd"] = ("991825827", "Testdepartementet"),
        ["digdir"] = ("991825827", "Digitaliseringsdirektoratet"),
        ["brg"] = ("974760673", "Brønnøysundregistrene"),
        ["skd"] = ("974761076", "Skatteetaten"),
        ["nav"] = ("889640782", "Arbeids- og velferdsetaten"),
        ["ssb"] = ("971526920", "Statistisk sentralbyrå"),
    };

    public async Task<IReadOnlyList<AppResource>> Get(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (_cached is { } fresh && now - fresh.At < Reuse)
        {
            return fresh.Apps;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is { } again && now - again.At < Reuse)
            {
                return again.Apps;
            }

            var apps = await Load(cancellationToken);
            _cached = (now, apps);
            return apps;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>A deterministic provider id per service-owner code, stable across restarts and components.</summary>
    public static Guid ServiceOwnerId(string orgCode)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("localtest-service-owner:" + orgCode.ToLowerInvariant()));
        return new Guid(hash);
    }

    private async Task<IReadOnlyList<AppResource>> Load(CancellationToken cancellationToken)
    {
        var url = options.Value.PlatformEndpoint.TrimEnd('/') + "/storage/api/v1/applications";
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(LocaltestAppResources));
            var list = await client.GetFromJsonAsync<ApplicationList>(url, Json, cancellationToken);
            var apps = (list?.Applications ?? [])
                .Where(a => !string.IsNullOrEmpty(a.Id) && a.Id.Contains('/'))
                .Select(a =>
                {
                    var app = a.Id![(a.Id.IndexOf('/') + 1)..];
                    var org = (a.Org ?? a.Id[..a.Id.IndexOf('/')]).ToLowerInvariant();
                    return new AppResource(org, app, $"app_{org}_{app}", a.Id, a.Title ?? [], PolicySubjects(org, app));
                })
                .ToList();
            logger.LogDebug("Localtest resource registry: {Count} app resources from Storage", apps.Count);
            return apps;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Localtest resource registry: Storage application list unavailable ({Message}); no app resources", e.Message);
            return [];
        }
    }

    // What the production Resource Registry's policy/subjects endpoint computes from the app policy: every role code and
    // access package named in a subject match. Read from the policy the runner pushed (the PDP's metadata container).
    private IReadOnlyList<string> PolicySubjects(string org, string app)
    {
        var file = Path.Combine(options.Value.PolicyStorePath, "metadata", org, app, "policy.xml");
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var doc = XDocument.Load(file);
            return doc.Descendants(Xacml + "Match")
                .Select(m => (Id: (string?)m.Element(Xacml + "AttributeDesignator")?.Attribute("AttributeId"), Value: (string?)m.Element(Xacml + "AttributeValue")))
                .Where(m => m.Value is { Length: > 0 } && m.Id is "urn:altinn:rolecode" or "urn:altinn:accesspackage")
                .Select(m => $"{m.Id}:{m.Value!.ToLowerInvariant()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException)
        {
            logger.LogWarning("Localtest resource registry: policy for {Org}/{App} could not be read ({Message}); no subjects", org, app, e.Message);
            return [];
        }
    }

    public sealed record AppResource(string Org, string App, string Identifier, string AppId, Dictionary<string, string> Title, IReadOnlyList<string> SubjectUrns);

    private sealed class ApplicationList
    {
        [JsonPropertyName("applications")]
        public List<Application>? Applications { get; set; }
    }

    private sealed class Application
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("org")]
        public string? Org { get; set; }

        [JsonPropertyName("title")]
        public Dictionary<string, string>? Title { get; set; }
    }
}
