using System.Buffers;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Defines a contract for processing CCR XML data and extracting party updates.
/// </summary>
/// <remarks>Implementations of this interface provide a method to parse CCR XML data
/// and return a collection of party updates. The static abstract method enables generic code to invoke XML processing
/// on different implementations without knowing the concrete type.</remarks>
public interface ICcrXmlProcessor
{
    /// <summary>
    /// Parses CCR XML data and returns a sequence of party updates represented by <see cref="CcrOrganizationUpdate"/> objects.
    /// </summary>
    /// <param name="xmlData">A read-only sequence of bytes containing the CCR XML data to process. The data must be well-formed XML in the
    /// expected CCR format.</param>
    /// <param name="roleDefs">Defines a lookup service for external role definitions, allowing retrieval of role definitions by source/identifier or role-code without asynchronous operations.</param>
    /// <param name="locations">Gets static countrycode lookup</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation. The default value is <see
    /// cref="CancellationToken.None"/>.</param>
    /// <returns>An enumerable collection of <see cref="CcrOrganizationUpdate"/> objects extracted from the provided XML data. The
    /// collection is empty if no party updates are found.</returns>
    IEnumerable<CcrOrganizationUpdate> ProcessCcrXml(
        ReadOnlySequence<byte> xmlData,
        IExternalRoleDefinitionLookup roleDefs,
        ILocationLookup locations,
        CancellationToken cancellationToken = default);
}
