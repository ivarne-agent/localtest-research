using System.Diagnostics;
using Digdir.Domain.Dialogporten.Application.Externals;
using Digdir.Domain.Dialogporten.Application.Features.V1.ResourceRegistry.Commands.SyncPolicy;
using Digdir.Domain.Dialogporten.Application.Features.V1.ResourceRegistry.Commands.SyncSubjectMap;
using Digdir.Domain.Dialogporten.Domain.Dialogs.Entities.Contents;
using Digdir.Domain.Dialogporten.Infrastructure.Localtest;
using FastEndpoints;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ZiggyCreatures.Caching.Fusion;

namespace Digdir.Domain.Dialogporten.WebApi.Localtest.Endpoints;

// The localtest endpoints of the WebApi (api/v1/localtest/...), found by the host's own FastEndpoints assembly scan, so
// they need no hook. Anonymous like every api/localtest endpoint in the stack, and left out of the OpenAPI documents.
// None of them exists in production: there the Janitor runs the two syncs on a schedule, the caches expire on their own
// and dialogs are only removed by a purge.

public sealed class LocaltestGroup : Group
{
    public LocaltestGroup()
    {
        Configure(string.Empty, ep =>
        {
            ep.AllowAnonymous();
            ep.EndpointVersion(1);
            ep.Description(x => x.ExcludeFromDescription());
        });
    }
}

internal static class LocaltestEndpointConventions
{
    /// <summary><c>app_ttd_testapp</c> or the full urn, as the Resource Registry's ids are written either way.</summary>
    public static string? ResourceUrn(string? resource) =>
        string.IsNullOrWhiteSpace(resource) ? null
        : resource.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) ? resource
        : "urn:altinn:resource:" + resource;
}

/// <summary>
/// <c>POST api/v1/localtest/sync[?since=]</c>: the Janitor's two Resource Registry jobs (<c>sync-subject-resource-mappings</c>,
/// <c>sync-resource-policy-information</c>), then every named cache cleared, so a resource pushed a moment ago is known at
/// once. Startup's own sync is off (<c>DisableSubjectResourceSyncOnStartup</c>), so this is the only one.
/// </summary>
public sealed class LocaltestSyncEndpoint(ISender sender, IDialogDbContext db, IFusionCacheProvider caches)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("localtest/sync");
        Group<LocaltestGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Fetch the Resource Registry's changes since this time instead of since the newest row already synced. A query
        // parameter, not a body: the provisioner and a tester POST nothing.
        var since = Query<DateTimeOffset?>("since", isRequired: false);
        var watch = Stopwatch.StartNew();
        var subjects = await sender.Send(new SyncSubjectMapCommand { Since = since }, ct);
        if (subjects.TryPickT1(out var subjectError, out _))
        {
            await Send.ResultAsync(TypedResults.Problem(string.Join("; ", subjectError.Errors), statusCode: 400));
            return;
        }
        var policies = await sender.Send(new SyncPolicyCommand { Since = since }, ct);
        if (policies.TryPickT1(out var policyError, out _))
        {
            await Send.ResultAsync(TypedResults.Problem(string.Join("; ", policyError.Errors), statusCode: 400));
            return;
        }
        var cleared = await LocaltestCaches.ClearAll(caches, ct);

        var byResource = await db.SubjectResources.AsNoTracking()
            .GroupBy(x => x.Resource)
            .Select(g => new { Resource = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        await Send.OkAsync(new
        {
            since,
            subjectResources = byResource.Sum(x => x.Count),
            subjectResourcesByResource = byResource.OrderBy(x => x.Resource, StringComparer.Ordinal)
                .ToDictionary(x => x.Resource, x => x.Count),
            resourcePolicyInformation = await db.ResourcePolicyInformation.CountAsync(ct),
            cachesCleared = cleared,
            durationMs = watch.ElapsedMilliseconds,
        }, ct);
    }
}

/// <summary><c>POST api/v1/localtest/caches/clear</c>: every named FusionCache, in all three hosts (Redis backplane).</summary>
public sealed class LocaltestClearCachesEndpoint(IFusionCacheProvider caches) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("localtest/caches/clear");
        Group<LocaltestGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync(new { cachesCleared = await LocaltestCaches.ClearAll(caches, ct) }, ct);
}

/// <summary>
/// <c>DELETE api/v1/localtest/dialogs</c>: every dialog gone for a fresh test run, without a restart (dialogs otherwise
/// persist, and <c>studioctl env reset</c> wipes Storage with them). Truncates <c>Dialog</c> and, by cascade, every table
/// that references it (contents, activities, transmissions, actors, the search index, ...), plus the two tables production
/// maintains by row trigger or by queue (party-resource references, the search rebuild queue), which a truncate does not
/// fire; the synced Resource Registry data, the lookup tables and the outbox stay. Then the caches are cleared.
/// </summary>
public sealed class LocaltestDeleteDialogsEndpoint(IDialogDbContext db, IFusionCacheProvider caches) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("localtest/dialogs");
        Group<LocaltestGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var count = await db.Dialogs.IgnoreQueryFilters().CountAsync(ct);
        await ((DbContext)db).Database.ExecuteSqlRawAsync(
            """TRUNCATE "Dialog", partyresource."PartyResource", search."DialogSearchRebuildQueue" CASCADE""", ct);
        var cleared = await LocaltestCaches.ClearAll(caches, ct);
        await Send.OkAsync(new { dialogsDeleted = count, cachesCleared = cleared }, ct);
    }
}

public sealed class LocaltestListDialogsRequest
{
    /// <summary>The party urn, as on the dialog (<c>urn:altinn:person:identifier-no:...</c>, <c>urn:altinn:organization:identifier-no:...</c>).</summary>
    public string? Party { get; init; }

    /// <summary>The service resource, <c>app_ttd_testapp</c> or the full urn.</summary>
    public string? Resource { get; init; }

    /// <summary>The <c>next</c> of the previous page (an offset into the creation order).</summary>
    public int? After { get; init; }

    public int? Limit { get; init; }
}

/// <summary>
/// <c>GET api/v1/localtest/dialogs?party=&amp;resource=&amp;after=&amp;limit=</c>: plain reads of the dialog table for the
/// inspection GUI and for tests, past authorization and including soft-deleted dialogs, in creation order,
/// <c>{ items, next }</c> (<c>next</c>, the <c>after</c> of the next page, left out on the last page) like the Events component's inspection API.
/// </summary>
public sealed class LocaltestListDialogsEndpoint(IDialogDbContext db) : Endpoint<LocaltestListDialogsRequest>
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    public override void Configure()
    {
        Get("localtest/dialogs");
        Group<LocaltestGroup>();
    }

    public override async Task HandleAsync(LocaltestListDialogsRequest req, CancellationToken ct)
    {
        var limit = Math.Clamp(req.Limit ?? DefaultLimit, 1, MaxLimit);
        var resource = LocaltestEndpointConventions.ResourceUrn(req.Resource);
        var query = db.Dialogs.IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(req.Party))
            query = query.Where(x => x.Party == req.Party);
        if (resource is not null)
            query = query.Where(x => x.ServiceResource == resource);
        var offset = Math.Max(req.After ?? 0, 0);

        var page = await query
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset)
            .Take(limit + 1)
            .Select(x => new
            {
                x.Id,
                x.Org,
                x.ServiceResource,
                x.Party,
                x.StatusId,
                x.ExtendedStatus,
                x.ExternalReference,
                x.Process,
                x.CreatedAt,
                x.UpdatedAt,
                x.Deleted,
                x.DeletedAt,
            })
            .ToListAsync(ct);
        var items = page.Take(limit).ToList();
        var ids = items.Select(x => x.Id).ToList();
        var titles = (await db.DialogContents.AsNoTracking()
                .Where(x => ids.Contains(x.DialogId) && x.TypeId == DialogContentType.Values.Title)
                .Select(x => new
                {
                    x.DialogId,
                    Values = x.Value.Localizations.Select(l => new { l.LanguageCode, l.Value }).ToList(),
                })
                .ToListAsync(ct))
            .ToDictionary(x => x.DialogId, x => x.Values.ToDictionary(l => l.LanguageCode, l => l.Value));

        await Send.OkAsync(new
        {
            items = items.Select(x => new
            {
                id = x.Id,
                org = x.Org,
                serviceResource = x.ServiceResource,
                party = x.Party,
                status = x.StatusId.ToString(),
                extendedStatus = x.ExtendedStatus,
                externalReference = x.ExternalReference,
                process = x.Process,
                title = titles.GetValueOrDefault(x.Id),
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt,
                deleted = x.Deleted,
                deletedAt = x.DeletedAt,
            }),
            next = page.Count > limit ? offset + limit : (int?)null,
        }, ct);
    }
}
