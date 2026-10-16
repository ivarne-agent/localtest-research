namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Represents a client for interacting with a network file system, such as an SFTP server.
/// </summary>
internal interface INetworkFileSystemClient
    : IAsyncDisposable
{
    /// <summary>
    /// Asynchronously opens a file for reading from the network file system.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Stream"/> for reading the file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist on the network file system.</exception>
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously renames a file on the network file system from <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// </summary>
    /// <param name="sourcePath">The current file path.</param>
    /// <param name="destinationPath">The new file path.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task RenameFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
