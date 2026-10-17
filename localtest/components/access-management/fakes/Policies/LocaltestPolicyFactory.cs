#nullable enable
using System.IO;
using Altinn.AccessManagement.Core.Enums;
using Altinn.AccessManagement.Core.Repositories.Interfaces;
using Microsoft.Extensions.Options;

namespace Altinn.AccessManagement.Localtest.Policies;

/// <summary>
/// Replaces the Azure Blob backed <c>PolicyFactory</c>: one folder per blob container (<c>delegationpolicies</c>,
/// <c>metadata</c>, <c>resourceregistry</c>) under the policy store, with the same path resolution as production.
/// </summary>
public sealed class LocaltestPolicyFactory(IOptions<LocaltestAccessManagementOptions> options, TimeProvider timeProvider) : IPolicyFactory
{
    public IPolicyRepository Create(PolicyAccountType account, string filepath)
    {
        var container = account switch
        {
            PolicyAccountType.Delegations => "delegationpolicies",
            PolicyAccountType.ResourceRegister => "resourceregistry",
            _ => "metadata",
        };
        return new LocaltestFilePolicyRepository(Path.Combine(options.Value.PolicyStorePath, container), filepath, timeProvider);
    }

    public IPolicyRepository Create(string filepath) => filepath switch
    {
        var blob when blob.EndsWith("delegationpolicy.xml") => Create(PolicyAccountType.Delegations, filepath),
        var blob when blob.EndsWith("resourcepolicy.xml") => Create(PolicyAccountType.ResourceRegister, filepath),
        _ => Create(PolicyAccountType.Metadata, filepath),
    };
}
