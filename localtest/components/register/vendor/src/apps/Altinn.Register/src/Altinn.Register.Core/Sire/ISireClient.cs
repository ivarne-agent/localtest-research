using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// Interface for calling SIRE API.
/// </summary>
public interface ISireClient
{
    /// <summary>
    /// Gets the organization information from SIRE based on the provided <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The identifier of the organization.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Result{T}"/> containing the organization information.</returns>
    Task<Result<SireOrganization>> GetOrganization(OrganizationIdentifier organizationIdentifier, CancellationToken cancellationToken);
}
