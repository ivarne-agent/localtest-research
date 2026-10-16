using System.IO.Pipelines;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ImportJobs.FileProcessing;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Client for retrieving SFTP files from BRG.
/// </summary>
internal sealed partial class CcrDataTransfer
    : ICcrFlatFileService
{
    private readonly INetworkFileSystemClientFactory _factory;
    private readonly ILogger<CcrDataTransfer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrDataTransfer"/>.
    /// </summary>
    public CcrDataTransfer(
        INetworkFileSystemClientFactory factory,
        ILogger<CcrDataTransfer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<CcrFlatFileOperationResult>> ProcessNextFile(
        IFileProcessor<CcrOpenedFileInfo> processor,
        uint lastRunId,
        CancellationToken cancellationToken = default)
    {
        await using var client = await _factory.Connect(nameof(ICcrFlatFileService), cancellationToken);

        var runId = checked(lastRunId + 1);
        var nextFile = FileName(runId);

        try
        {
            {
                await using var fs = await client.OpenReadAsync(nextFile, cancellationToken);
                Log.FileFound(_logger, nextFile);

                var reader = PipeReader.Create(fs);
                var fileInfo = new CcrOpenedFileInfo(nextFile, reader, runId);

                await processor.ProcessFileAsync(fileInfo, cancellationToken);
                Log.FileProcessed(_logger, nextFile);
            }

            var newName = $"{nextFile}retrieved";
            await client.RenameFileAsync(nextFile, newName, cancellationToken);
            Log.FileRenamed(_logger, nextFile, newName);

            return CcrFlatFileOperationResult.FileProcessed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            Log.FileNotFound(_logger, nextFile);

            var nextNextFile = FileName(lastRunId + 2);
            if (await Exists(client, nextNextFile, cancellationToken))
            {
                // If the file with the next runId + 1 does not exist, but the one after that does, it means we have missed a file - that's an error condition
                Log.MissingFile(_logger, nextFile, nextNextFile);
                throw new InvalidOperationException($"The expected CCR file '{nextFile}' was not found, but '{nextNextFile}' was found. This indicates a missing file and potential data loss.");
            }

            return CcrFlatFileOperationResult.NoFileToProcess;
        }
        catch (Exception)
        {
            // TODO: handle better
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Result> ProcessManualFile(
        IFileProcessor<CcrOpenedFileInfo> processor,
        string fileName,
        uint runId,
        CancellationToken cancellationToken = default)
    {
        await using var client = await _factory.Connect(nameof(ICcrFlatFileService), cancellationToken);

        {
            await using var fs = await client.OpenReadAsync(fileName, cancellationToken);
            Log.FileFound(_logger, fileName);

            var reader = PipeReader.Create(fs);
            var fileInfo = new CcrOpenedFileInfo(fileName, reader, runId);

            await processor.ProcessFileAsync(fileInfo, cancellationToken);
            Log.FileProcessed(_logger, fileName);
        }

        var newName = $"{fileName}retrieved";
        await client.RenameFileAsync(fileName, newName, cancellationToken);
        Log.FileRenamed(_logger, fileName, newName);

        return Result.Success;
    }

    private async Task<bool> Exists(INetworkFileSystemClient client, string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await client.OpenReadAsync(path, cancellationToken);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static string FileName(uint runId) => $"baj{runId:D5}.txt";

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "File found: {FileName}.")]
        public static partial void FileFound(ILogger logger, string fileName);

        [LoggerMessage(1, LogLevel.Information, "File processed: {FileName}.")]
        public static partial void FileProcessed(ILogger logger, string fileName);

        [LoggerMessage(2, LogLevel.Information, "File renamed from {OldName} to {NewName}.")]
        public static partial void FileRenamed(ILogger logger, string oldName, string newName);

        [LoggerMessage(3, LogLevel.Information, "File not found: {FileName}.")]
        public static partial void FileNotFound(ILogger logger, string fileName);

        [LoggerMessage(4, LogLevel.Error, "Expected file not found: {FileName}, yet '{NextFileName}' was found.")]
        public static partial void MissingFile(ILogger logger, string fileName, string nextFileName);
    }
}
