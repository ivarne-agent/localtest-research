using System.Buffers;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ImportJobs.FileProcessing;
using MassTransit;

namespace Altinn.Register.PartyImport.Ccr;

/// <summary>
/// Consumes <see cref="ImportManualCcrFlatFileCommand"/> messages that are manually
/// produced when an out-of-the-ordinary CCR flat file import is requested.
/// </summary>
public sealed partial class ImportManualCcrFlatFileConsumer
    : IConsumer<ImportManualCcrFlatFileCommand>
{
    private readonly ICcrFlatFileService _ccrService;
    private readonly ICommandSender _sender;
    private readonly ICcrFlatFileProcessor _flatFileProcessor;
    private readonly CcrImportJob.ImportMeters _meters;
    private readonly ILogger<ImportManualCcrFlatFileConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportManualCcrFlatFileConsumer"/> class.
    /// </summary>
    public ImportManualCcrFlatFileConsumer(
        ICcrFlatFileService ccrService,
        ICommandSender sender,
        ICcrFlatFileProcessor flatFileProcessor,
        IMetricsProvider metricsProvider,
        ILogger<ImportManualCcrFlatFileConsumer> logger)
    {
        _ccrService = ccrService;
        _sender = sender;
        _flatFileProcessor = flatFileProcessor;
        _meters = metricsProvider.Get<CcrImportJob.ImportMeters>();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ImportManualCcrFlatFileCommand> context)
    {
        var message = context.Message;

        Log.ConsumingManualCcrFlatFileImport(_logger, message.FileName, message.RunId);
        var processor = new FileProcessor(_sender, _flatFileProcessor, _meters, _logger);
        await _ccrService.ProcessManualFile(processor, message.FileName, message.RunId, context.CancellationToken);
    }

    private sealed class FileProcessor(
        ICommandSender sender,
        ICcrFlatFileProcessor processor,
        CcrImportJob.ImportMeters meter,
        ILogger logger)
        : IFileProcessor<CcrOpenedFileInfo>
    {
        public async Task ProcessFileAsync(CcrOpenedFileInfo fileInfo, CancellationToken cancellationToken)
        {
            await foreach (var item in processor.ProcessCcrFlatFile(fileInfo.Reader, cancellationToken))
            {
                var cmd = new ImportCcrXmlCommand
                {
                    BatchId = fileInfo.SequenceNumber,
                    OrganizationIdentifier = item.OrganizationIdentifier,
                    Document = item.Document.ToArray(),
                };

                Log.EnqueueForProcessing(logger, item.OrganizationIdentifier);
                await sender.Send(cmd, cancellationToken);
                meter.PartiesEnqueued.Add(1);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Information, "Consuming manual CCR flat file import for file {FileName} with run ID {RunId}.")]
        public static partial void ConsumingManualCcrFlatFileImport(ILogger logger, string fileName, uint runId);

        [LoggerMessage(2, LogLevel.Information, "Enqueue {OrganizationIdentifier} for import processing.")]
        public static partial void EnqueueForProcessing(ILogger logger, OrganizationIdentifier organizationIdentifier);
    }
}
