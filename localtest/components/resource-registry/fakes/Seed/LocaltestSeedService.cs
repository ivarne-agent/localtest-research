#nullable enable
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.ResourceRegistry.Core;
using Altinn.ResourceRegistry.Core.Models;
using Altinn.ResourceRegistry.Core.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.ResourceRegistry.Localtest.Seed;

/// <summary>
/// The built-in seed of non-app resources, one folder per resource under <c>fakes/Seed</c> (embedded as
/// <c>Localtest.Seed/{id}/resource.json</c> and <c>policy.xml</c>), applied at start through the production service
/// layer: <c>CreateResource</c> and <c>StorePolicy</c>, the calls behind <c>POST resource</c> and
/// <c>POST resource/{id}/policy</c> (which derive the resource's subject pairs). A resource that already exists keeps
/// its row, and its policy is stored again only if the policy store has none, so a restart changes nothing and the
/// <c>updated</c> feed stays stable. App resources are not seeded: they come from Storage and the pushed policies.
/// </summary>
public sealed class LocaltestSeedService(
    IResourceRegistry registry,
    IPolicyRepository policies,
    ILogger<LocaltestSeedService> logger) : IHostedService
{
    private const string Prefix = "Localtest.Seed/";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(LocaltestSeedService).Assembly;
        var ids = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith("/resource.json", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^"/resource.json".Length])
            .Order(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            var resource = JsonSerializer.Deserialize<ServiceResource>(Read(assembly, $"{Prefix}{id}/resource.json"), Json)
                ?? throw new InvalidOperationException($"Seed resource {id} is empty");
            var policy = Read(assembly, $"{Prefix}{id}/policy.xml");

            var created = false;
            if (await registry.GetResource(id, null, cancellationToken) is null)
            {
                await registry.CreateResource(resource, cancellationToken);
                created = true;
            }

            if (created || !await policies.PolicyExistsAsync(id, cancellationToken))
            {
                await registry.StorePolicy(resource, new ReadOnlySequence<byte>(policy), cancellationToken);
            }

            logger.LogInformation("Localtest: seed resource {ResourceId} {State}", id, created ? "created" : "already present");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static byte[] Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing seed file {name}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
