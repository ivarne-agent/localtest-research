using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Npr;

/// <summary>
/// Interface for calling NPR API.
/// </summary>
public interface INprClient
{
    /// <summary>
    /// Gets the person information from NPR based on the provided <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The identifier of the person.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Result{T}"/> containing the person information.</returns>
    Task<Result<NprPerson>> GetPerson(PersonIdentifier personIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the changes that have occurred in NPR data since the given sequence number.
    /// </summary>
    /// <param name="fromInclusive">The sequence number from which to start retrieving updates, inclusive.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="NprUpdatePage"/> containing the updates.</returns>
    IAsyncEnumerable<NprUpdatePage> GetUpdates(uint fromInclusive = 1, CancellationToken cancellationToken = default);
}
