#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Localtest.Apps;

/// <summary>
/// Reads come from <see cref="LocaltestAppSource"/> (running app, then mounted folder) and fall back to the production
/// Postgres repository, which keeps whatever was posted to <c>storage/api/v1/applications</c>. Writes go to Postgres
/// unchanged. No instance SQL joins <c>storage.applications</c> (only texts and A2 XSLs reference it), so the table does
/// not have to be seeded for instances to work; <see cref="LocaltestTextRepository"/> seeds the one row it needs.
/// </summary>
public sealed class LocaltestApplicationRepository(PgApplicationRepository db, LocaltestAppSource apps) : IApplicationRepository
{
    public async Task<List<Application>> FindAll() => await Merge(await db.FindAll(), org: null);

    public async Task<List<Application>> FindByOrg(string org) => await Merge(await db.FindByOrg(org), org);

    public async Task<Application> FindOne(string appId, string org, CancellationToken cancellationToken = default)
    {
        var app = appId.Split('/')[1];
        return await apps.GetApplication(org, app, cancellationToken) ?? await db.FindOne(appId, org, cancellationToken);
    }

    public Task<Application> Create(Application item) => db.Create(item);

    public Task<Application> Update(Application item) => db.Update(item);

    public Task<bool> Delete(string appId, string org) => db.Delete(appId, org);

    public Task<Dictionary<string, string>> GetAllAppTitles() => db.GetAllAppTitles();

    private async Task<List<Application>> Merge(List<Application> stored, string? org)
    {
        var ids = stored.Select(a => a.Id)
            .Concat(apps.FolderApps().Where(f => org is null || f.Org == org).Select(f => $"{f.Org}/{f.App}"))
            .Distinct()
            .ToList();
        var result = new List<Application>(ids.Count);
        foreach (var id in ids)
        {
            var parts = id.Split('/');
            var application = await apps.GetApplication(parts[0], parts[1]) ?? stored.FirstOrDefault(a => a.Id == id);
            if (application is not null)
            {
                result.Add(application);
            }
        }

        return result;
    }
}
