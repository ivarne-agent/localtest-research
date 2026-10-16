using System.Diagnostics;
using Altinn.Register.Core;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Default implementation of <see cref="INetworkFileSystemClientFactory"/> that creates and connects an <see cref="SftpClient"/> using the provided settings.
/// </summary>
internal sealed class DefaultSftpClientFactory
    : INetworkFileSystemClientFactory
{
    private readonly IOptionsMonitor<SftpClientSettings> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSftpClientFactory"/> class with the specified settings.
    /// The settings are provided through an <see cref="IOptionsMonitor{T}"/>, allowing for dynamic configuration changes if needed.
    /// </summary>
    /// <param name="options">Options monitor for <see cref="SftpClientSettings"/>.</param>
    public DefaultSftpClientFactory(IOptionsMonitor<SftpClientSettings> options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<INetworkFileSystemClient> Connect(string name, CancellationToken cancellationToken = default)
    {
        var settings = _options.Get(name);

        Debug.Assert(settings.Host is not null);
        Debug.Assert(settings.User is not null);
        Debug.Assert(settings.Password is not null);
        Debug.Assert(settings.RemotePath is not null);

        var client = new SftpClient(settings.Host, settings.Port, settings.User, settings.Password);
        using var activity = RegisterTelemetry.StartActivity(name: "sftp connect", kind: ActivityKind.Client, tags: [
            new("server.address", settings.Host),
            new("server.port", settings.Port),
            new("network.protocol.name", "sftp"),
            new("microsoft.dependency.type", "SFTP"),
            new("sftp.path", settings.RemotePath),
        ]);

        try
        {
            await client.ConnectAsync(cancellationToken);

            var ret = new SftpNetworkFileSystemClient(client, settings.RemotePath, settings);
            client = null;
            return ret;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            client?.Dispose();
        }
    }
}
