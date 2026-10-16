using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts.V1;

namespace Altinn.Register.Core.A2;

/// <summary>
/// Operation for checking whether a given national identity number is connected to a person.
/// </summary>
public interface IPersonLookup
{
    /// <summary>
    /// Gets the identified <see cref="Person"/> if the supplied last name matches.
    /// </summary>
    /// <param name="nationalIdentityNumber">The national identity number to check.</param>
    /// <param name="lastName">The last name of the person. Must match the person's last name.</param>
    /// <param name="activeUser">The unique party UUID of the user performing the check.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A successful <see cref="Result{TValue}"/> containing the identified <see cref="Person"/> when the lookup succeeds;
    /// otherwise a failed <see cref="Result{TValue}"/> containing the relevant error or problem details when lookup or validation fails.
    /// </returns>
    ValueTask<Result<Person>> GetPerson(string nationalIdentityNumber, string lastName, Guid activeUser, CancellationToken cancellationToken = default);
}
