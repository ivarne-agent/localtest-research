using Altinn.Correspondence.Core.Models.Entities;
using Altinn.Correspondence.Core.Services;
using Azure.ResourceManager.Network.Models;

namespace Altinn.Correspondence.Localtest.AzureResources;

/// <summary>
/// <see cref="IResourceManager"/> without Azure Resource Manager. Production deploys a storage account per service owner
/// (skipped by upstream in Development anyway) and keeps the container app's IP restrictions in step with APIM and Event
/// Grid (the daily "Update IP restrictions" Hangfire job). Here there is one file store and no container app: deploying is
/// a no-op and there are no IP ranges, so the daily job logs one line and returns instead of failing ten retries on
/// <c>DefaultAzureCredential</c>.
/// </summary>
public sealed class LocaltestResourceManager(ILogger<LocaltestResourceManager> logger) : IResourceManager
{
    public Task DeployStorageAccount(ServiceOwnerEntity serviceOwnerEntity, bool virusScan, CancellationToken cancellationToken)
    {
        logger.LogInformation("Localtest: no storage account is deployed for service owner {ServiceOwnerId}; the file store is used", serviceOwnerEntity.Id);
        return Task.CompletedTask;
    }

    public void DeployStorageAccountsForServiceOwner(ServiceOwnerEntity serviceOwnerEntity, CancellationToken cancellationToken)
        => logger.LogInformation("Localtest: no storage accounts are deployed for service owner {ServiceOwnerId}; the file store is used", serviceOwnerEntity.Id);

    public Task UpdateContainerAppIpRestrictionsAsync(Dictionary<string, string> newIps, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ServiceTagsListResult?> RetrieveServiceTags(CancellationToken cancellationToken)
        => Task.FromResult<ServiceTagsListResult?>(null);

    public Task<Dictionary<string, string>> RetrieveCurrentIpRanges(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string>());
}
