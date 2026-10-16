namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Factory for creating instances of <see cref="INetworkFileSystemClient"/>. This interface abstracts the creation of network file system clients,
/// allowing for different implementations to be used without changing the code that depends on it. The factory method takes
/// a string parameter, which can be used to specify the name of the client which is used to lookup configuration settings by
/// the default implementation.
/// </summary>
internal interface INetworkFileSystemClientFactory
{
    /// <summary>
    /// Connects to a remote SFTP server and returns an instance of <see cref="INetworkFileSystemClient"/> that can be used to interact with the server.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>An instance of <see cref="INetworkFileSystemClient"/> connected to the specified SFTP server.</returns>
    public Task<INetworkFileSystemClient> Connect(string name, CancellationToken cancellationToken = default);
}
