using Altinn.Register.Contracts;

namespace Altinn.Register.Core.A2;

/// <summary>
/// Interface handling methods for operations related to organizations
/// </summary>
public interface IOrganizationClient
{
    /// <summary>
    /// Method that fetches a organization based on a organization number
    /// </summary>
    /// <param name="orgNr">The organization number</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns></returns>
    Task<Contracts.V1.Organization?> GetOrganization(OrganizationIdentifier orgNr, CancellationToken cancellationToken = default);
}
