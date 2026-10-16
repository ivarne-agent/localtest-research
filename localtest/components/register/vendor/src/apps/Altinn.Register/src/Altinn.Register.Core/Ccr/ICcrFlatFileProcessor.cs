using System.IO.Pipelines;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Defines a processor for CCR flat files.
/// </summary>
public interface ICcrFlatFileProcessor
{
    /// <summary>
    /// Process a CCR flat file into a sequence of <see cref="IOrganizationUpdateDocument"/>.
    /// </summary>
    /// <param name="reader">The flat file reader.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A sequence of <see cref="IOrganizationUpdateDocument"/>.</returns>
    public IAsyncEnumerable<IOrganizationUpdateDocument> ProcessCcrFlatFile(
        PipeReader reader,
        CancellationToken cancellationToken = default);
}
