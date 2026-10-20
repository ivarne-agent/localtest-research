using System;
using System.Threading.Tasks;
using Altinn.Platform.Receipt.Services.Interfaces;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Receipt.Localtest;

/// <summary>
/// Production Storage reports each data element's <c>selfLinks.platform</c> on the production host name,
/// <c>https://platform.{Hostname}/storage/...</c>, which neither resolves nor serves TLS locally. The receipt's
/// frontend uses that link for the attachment and PDF downloads, and makes it relative only when its host equals the
/// page's. This decorator rewrites the prefix to the stack's public origin, so the links reach the same production
/// Storage endpoint with the same cookie. Nothing else in the instance is changed.
/// </summary>
internal sealed class SelfLinksStorage(IStorage inner, string productionPrefix, string localPrefix) : IStorage
{
    public async Task<Instance> GetInstance(int instanceOwnerId, Guid instanceGuid)
    {
        Instance instance = await inner.GetInstance(instanceOwnerId, instanceGuid);
        foreach (DataElement element in instance.Data ?? [])
        {
            if (
                element.SelfLinks?.Platform is { } link
                && link.StartsWith(productionPrefix, StringComparison.OrdinalIgnoreCase)
            )
            {
                element.SelfLinks.Platform = localPrefix + link[productionPrefix.Length..];
            }
        }

        return instance;
    }
}
