using Altinn.Register.Contracts.V1;

namespace Altinn.Register.Clients.Interfaces;

/// <summary>
/// Interface handling methods for operations related to persons
/// </summary>
public interface IPersonClient
{
    /// <summary>
    /// Method that fetches a person based on a national identity number of the person.
    /// </summary>
    /// <param name="nationalIdentityNumber">The national identity number of the person to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identified person.</returns>
    Task<Person?> GetPerson(string nationalIdentityNumber, CancellationToken cancellationToken = default);
}
