using System.Text;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ImportJobs.FileProcessing;
using Altinn.Register.Integrations.Ccr.FileImport;
using Altinn.Register.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Altinn.Register.Tests.UnitTests;

public class CcrDataTransferTests
{
    [Fact]
    public async Task ProcessNextFile_ProcessesExpectedNextFile()
    {
        var content = Encoding.UTF8.GetBytes("content of file 05781");
        var fileSystem = new MemoryNetworkFileSystemClient(new Dictionary<string, byte[]>
        {
            ["baj05780.txt"] = Encoding.UTF8.GetBytes("previous file"),
            ["baj05781.txt"] = content,
            ["baj05782.txt"] = Encoding.UTF8.GetBytes("future file"),
        });

        var processor = new CapturingCcrFileProcessor();
        var client = new CcrDataTransfer(
            new MemoryNetworkFileSystemClientFactory(fileSystem),
            NullLogger<CcrDataTransfer>.Instance);

        var result = await client.ProcessNextFile(processor, 5780, TestContext.Current.CancellationToken);

        result.ShouldHaveValue().ShouldBe(CcrFlatFileOperationResult.FileProcessed);
        processor.FileName.ShouldBe("baj05781.txt");
        processor.SequenceNumber.ShouldBe(5781U);
        processor.Content.ShouldBe("content of file 05781");
        fileSystem.Files.ContainsKey("baj05781.txt").ShouldBeFalse();
        fileSystem.Files["baj05781.txtretrieved"].ShouldBe(content);
        fileSystem.Files.ContainsKey("baj05780.txt").ShouldBeTrue();
        fileSystem.Files.ContainsKey("baj05782.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessNextFile_ReturnsNoFileToProcess_WhenNextFileDoesNotExist()
    {
        var fileSystem = new MemoryNetworkFileSystemClient(new Dictionary<string, byte[]>
        {
            ["baj05780.txt"] = Encoding.UTF8.GetBytes("previous file"),
        });

        var processor = new CapturingCcrFileProcessor();
        var client = new CcrDataTransfer(
            new MemoryNetworkFileSystemClientFactory(fileSystem),
            NullLogger<CcrDataTransfer>.Instance);

        var result = await client.ProcessNextFile(processor, 5780, TestContext.Current.CancellationToken);

        result.ShouldHaveValue().ShouldBe(CcrFlatFileOperationResult.NoFileToProcess);
        processor.ProcessCallCount.ShouldBe(0);
        fileSystem.Files.ContainsKey("baj05781.txtretrieved").ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessNextFile_Throws_WhenNextFileIsMissingButNextNextFileExists()
    {
        var fileSystem = new MemoryNetworkFileSystemClient(new Dictionary<string, byte[]>
        {
            ["baj05780.txt"] = Encoding.UTF8.GetBytes("previous file"),
            ["baj05782.txt"] = Encoding.UTF8.GetBytes("future file"),
        });

        var processor = new CapturingCcrFileProcessor();
        var client = new CcrDataTransfer(
            new MemoryNetworkFileSystemClientFactory(fileSystem),
            NullLogger<CcrDataTransfer>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ProcessNextFile(processor, 5780, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("baj05781.txt");
        processor.ProcessCallCount.ShouldBe(0);
        fileSystem.Files.ContainsKey("baj05782.txt").ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessNextFile_DoesNotRenameFile_WhenProcessorFails()
    {
        var content = Encoding.UTF8.GetBytes("content of file 05781");
        var fileSystem = new MemoryNetworkFileSystemClient(new Dictionary<string, byte[]>
        {
            ["baj05781.txt"] = content,
        });

        var processor = new ThrowingCcrFileProcessor();
        var client = new CcrDataTransfer(
            new MemoryNetworkFileSystemClientFactory(fileSystem),
            NullLogger<CcrDataTransfer>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(
            () => client.ProcessNextFile(processor, 5780, TestContext.Current.CancellationToken));

        fileSystem.Files["baj05781.txt"].ShouldBe(content);
        fileSystem.Files.ContainsKey("baj05781.txtretrieved").ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessNextFile_DisposesNetworkFileSystemClient()
    {
        var fileSystem = new MemoryNetworkFileSystemClient(new Dictionary<string, byte[]>
        {
            ["baj00001.txt"] = Encoding.UTF8.GetBytes("content"),
        });

        var client = new CcrDataTransfer(
            new MemoryNetworkFileSystemClientFactory(fileSystem),
            NullLogger<CcrDataTransfer>.Instance);

        await client.ProcessNextFile(new CapturingCcrFileProcessor(), 0, TestContext.Current.CancellationToken);

        fileSystem.DisposeCount.ShouldBe(1);
    }

    private sealed class MemoryNetworkFileSystemClientFactory
        : INetworkFileSystemClientFactory
    {
        private readonly INetworkFileSystemClient _client;

        public MemoryNetworkFileSystemClientFactory(INetworkFileSystemClient client)
        {
            _client = client;
        }

        public Task<INetworkFileSystemClient> Connect(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_client);
        }
    }

    private sealed class CapturingCcrFileProcessor
        : IFileProcessor<CcrOpenedFileInfo>
    {
        public int ProcessCallCount { get; private set; }

        public string? FileName { get; private set; }

        public uint? SequenceNumber { get; private set; }

        public string? Content { get; private set; }

        public async Task ProcessFileAsync(CcrOpenedFileInfo fileInfo, CancellationToken cancellationToken)
        {
            ProcessCallCount++;
            FileName = fileInfo.Name;
            SequenceNumber = fileInfo.SequenceNumber;

            await using var stream = fileInfo.Reader.AsStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Content = await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private sealed class ThrowingCcrFileProcessor
        : IFileProcessor<CcrOpenedFileInfo>
    {
        public Task ProcessFileAsync(CcrOpenedFileInfo fileInfo, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Processor failed.");
        }
    }
}
