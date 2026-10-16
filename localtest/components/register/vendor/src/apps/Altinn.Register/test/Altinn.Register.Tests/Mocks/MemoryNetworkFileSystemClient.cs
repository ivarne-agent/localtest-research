using Altinn.Register.Integrations.Ccr.FileImport;

namespace Altinn.Register.Tests.Mocks;

internal sealed class MemoryNetworkFileSystemClient
    : INetworkFileSystemClient
{
    private readonly Dictionary<string, byte[]> _files;

    public MemoryNetworkFileSystemClient(Dictionary<string, byte[]> files)
    {
        _files = files;
    }

    public IReadOnlyDictionary<string, byte[]> Files
        => _files;

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    public int DisposeCount { get; private set; }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_files.TryGetValue(path, out var content))
        {
            throw new FileNotFoundException($"File '{path}' was not found.", path);
        }

        return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
    }

    public Task RenameFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_files.Remove(sourcePath, out var content))
        {
            throw new FileNotFoundException($"File '{sourcePath}' was not found.", sourcePath);
        }

        _files[destinationPath] = content;
        return Task.CompletedTask;
    }
}
