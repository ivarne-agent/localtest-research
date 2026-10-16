using System.Diagnostics;
using Altinn.Register.Core;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Implementation of <see cref="INetworkFileSystemClient"/> that uses an <see cref="ISftpClient"/> to interact with an SFTP server.
/// </summary>
internal sealed class SftpNetworkFileSystemClient
    : INetworkFileSystemClient
{
    private readonly ISftpClient _sftpClient;
    private readonly string _basePath;

    // only used for telemetry
    private readonly SftpClientSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SftpNetworkFileSystemClient"/> class.
    /// </summary>
    /// <param name="sftpClient">The sftp client.</param>
    /// <param name="basePath">
    /// The remote base directory to resolve relative paths against. May be empty or "/" to leave
    /// relative paths unresolved (they will then be interpreted by the server against its own
    /// session cwd, typically the user's home).
    /// </param>
    /// <param name="settings">The SFTP client settings, used for telemetry purposes.</param>
    public SftpNetworkFileSystemClient(ISftpClient sftpClient, string basePath, SftpClientSettings settings)
    {
        _sftpClient = sftpClient;
        _basePath = basePath;
        _settings = settings;
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // SftpClient.OpenAsync sends the path to the server verbatim - unlike most other methods
        // on SftpClient, it doesn't prepend the working directory for relative paths. Resolve it
        // here so callers can use relative paths against the base path supplied at construction.
        var fullPath = Resolve(path);
        using var activity = RegisterTelemetry.StartActivity(name: $"sftp open read {fullPath}", kind: ActivityKind.Client, tags: [
            new("server.address", _settings.Host),
            new("server.port", _settings.Port),
            new("network.protocol.name", "sftp"),
            new("microsoft.dependency.type", "SFTP"),
            new("sftp.path", fullPath),
        ]);

        try
        {
            return await _sftpClient.OpenAsync(fullPath, FileMode.Open, FileAccess.Read, cancellationToken);
        }
        catch (SftpPathNotFoundException ex)
        {
            activity?.SetTag("sftp.file.status", "not found"); // expected outcome, don't log as error
            throw new FileNotFoundException("The specified file does not exist on the sftp server.", path, ex);
        }
    }

    /// <inheritdoc/>
    public async Task RenameFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Resolve(sourcePath);
        var fullDestinationPath = Resolve(destinationPath);
        using var activity = RegisterTelemetry.StartActivity(name: $"sftp rename {fullSourcePath}", kind: ActivityKind.Client, tags: [
            new("server.address", _settings.Host),
            new("server.port", _settings.Port),
            new("network.protocol.name", "sftp"),
            new("microsoft.dependency.type", "SFTP"),
            new("sftp.source.path", fullSourcePath),
            new("sftp.destination.path", fullDestinationPath),
        ]);

        await _sftpClient.RenameFileAsync(fullSourcePath, fullDestinationPath, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _sftpClient.Dispose(); // does not support async disposal currently
        return ValueTask.CompletedTask;
    }

    private string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/')
        {
            return path;
        }

        if (string.IsNullOrEmpty(_basePath))
        {
            return path;
        }

        return $"{_basePath}/{path}";
    }
}
