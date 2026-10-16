using System.IO.Pipelines;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ImportJobs.FileProcessing;
using Altinn.Register.Integrations.Ccr.FileImport;
using Altinn.Register.Tests.Utils;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.Sftp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Altinn.Register.Tests.PartyImport.Ccr;

/// <summary>
/// Integration tests for <see cref="CcrDataTransfer"/> against a real SFTP server running in a
/// test-container. These don't need the web application - only the shared SFTP server.
/// </summary>
public class CcrDataTransferSftpTests
{
    private static readonly TestDataFileProvider _ccrFiles = TestDataFileProvider.For("Ccr/FlatFile");

    [Fact]
    public async Task ProcessNextFile_DownloadsUploadedFileFromSftp()
    {
        var ct = TestContext.Current.CancellationToken;
        var sftp = await TestContext.Current.GetRequiredFixture<SftpServerFixture>();
        var server = await sftp.CreateTestServer();

        var testData = _ccrFiles.GetFileInfo("test1.txt");

        // Local test data is "test1.txt" (the canonical fixture the parser tests share);
        // the SFTP server expects production's filename convention "baj{runId:D5}.txt".
        await using (var source = testData.CreateReadStream())
        {
            await server.UploadFileAsync("baj00001.txt", source, ct);
        }

        // Hand DefaultSftpClientFactory a TestOptionsMonitor preloaded with this test's SFTP
        // settings under the production-expected named-options key.
        var optionsMonitor = new TestOptionsMonitor<SftpClientSettings>(
            nameof(ICcrFlatFileService),
            new SftpClientSettings
            {
                Host = server.Host,
                Port = checked((ushort)server.Port),
                User = server.Username,
                Password = server.Password,
                RemotePath = server.UploadDirectory,
            });

        ICcrFlatFileService service = new CcrDataTransfer(
            new DefaultSftpClientFactory(optionsMonitor),
            NullLogger<CcrDataTransfer>.Instance);

        var processor = new CapturingProcessor();
        var result = await service.ProcessNextFile(processor, lastRunId: 0, ct);

        result.IsProblem.ShouldBeFalse();
        result.Value.ShouldBe(CcrFlatFileOperationResult.FileProcessed);

        processor.FileName.ShouldBe("baj00001.txt");
        processor.SequenceNumber.ShouldBe(1U);

        var expected = new byte[testData.Length];
        await using (var verify = testData.CreateReadStream())
        {
            await verify.ReadExactlyAsync(expected, ct);
        }

        processor.Content.ShouldBe(expected);
    }

    private static async Task<byte[]> ReadAllBytesAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);
            foreach (var segment in result.Buffer)
            {
                buffer.Write(segment.Span);
            }

            reader.AdvanceTo(result.Buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }

        await reader.CompleteAsync();
        return buffer.ToArray();
    }

    /// <summary>
    /// Captures the file name, sequence number and full byte content from a single
    /// <see cref="ICcrFlatFileService.ProcessNextFile"/> invocation.
    /// </summary>
    private sealed class CapturingProcessor
        : IFileProcessor<CcrOpenedFileInfo>
    {
        public string? FileName { get; private set; }

        public uint SequenceNumber { get; private set; }

        public byte[]? Content { get; private set; }

        public async Task ProcessFileAsync(CcrOpenedFileInfo fileInfo, CancellationToken cancellationToken)
        {
            FileName = fileInfo.Name;
            SequenceNumber = fileInfo.SequenceNumber;
            Content = await ReadAllBytesAsync(fileInfo.Reader, cancellationToken);
        }
    }
}
