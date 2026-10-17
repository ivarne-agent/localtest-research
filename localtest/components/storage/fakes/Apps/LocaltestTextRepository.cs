#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Localtest.Apps;

/// <summary>
/// Texts from <see cref="LocaltestAppSource"/> first, then the production Postgres repository. Writes go to Postgres;
/// <see cref="Create"/> first inserts the application row from the app source when only the app knows it, because the
/// production insert looks up <c>storage.applications</c> for the foreign key.
/// </summary>
public sealed class LocaltestTextRepository(PgTextRepository db, PgApplicationRepository applications, LocaltestAppSource apps)
    : ITextRepository
{
    public async Task<TextResource> Get(string org, string app, string language)
    {
        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(app) || !LanguageHelper.IsTwoLetters(language))
        {
            return await db.Get(org, app, language); // throws the production ArgumentException
        }

        return await apps.GetTexts(org, app, language) ?? await db.Get(org, app, language);
    }

    public async Task<List<TextResource>> Get(List<string> appIds, string language)
    {
        List<TextResource> result = [];
        foreach (var appId in appIds)
        {
            try
            {
                var parts = appId.Split('/');
                if (await Get(parts[0], parts[1], language) is { } resource)
                {
                    result.Add(resource);
                }
            }
            catch (Exception)
            {
                // Same as production: only valid text resources are returned (messagebox).
            }
        }

        return result;
    }

    public async Task<TextResource> Create(string org, string app, TextResource textResource)
    {
        if (await applications.FindOne($"{org}/{app}", org) is null && await apps.GetApplication(org, app) is { } known)
        {
            await applications.Create(known);
        }

        return await db.Create(org, app, textResource);
    }

    public Task<TextResource> Update(string org, string app, TextResource textResource) => db.Update(org, app, textResource);

    public Task<bool> Delete(string org, string app, string language) => db.Delete(org, app, language);
}
