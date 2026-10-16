using System.IO.Pipelines;
using Altinn.Register.Core.Ccr;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Implementation of <see cref="ICcrFlatFileProcessor"/>.
/// </summary>
internal sealed class CcrFlatFileProcessorFactory
    : ICcrFlatFileProcessor
{
    private readonly ILogger<CcrFlatFileProcessor> _processorLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrFlatFileProcessorFactory"/> class.
    /// </summary>
    public CcrFlatFileProcessorFactory(ILogger<CcrFlatFileProcessor> processorLogger)
    {
        _processorLogger = processorLogger;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<IOrganizationUpdateDocument> ProcessCcrFlatFile(
        PipeReader reader,
        CancellationToken cancellationToken = default)
        => CcrFlatFileProcessor.ProcessAsync(_processorLogger, reader, cancellationToken);
}
