using System.Buffers;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Defines a service for federating updates from a CCR XML file.
/// </summary>
public interface ICcrUpdateFederator
{
    /// <summary>
    /// Federates updates from a CCR XML file.
    /// </summary>
    /// <param name="xmlData">The CCR XML data to be federated.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task FederateUpdates(ReadOnlySequence<byte> xmlData, CancellationToken cancellationToken = default);
}
