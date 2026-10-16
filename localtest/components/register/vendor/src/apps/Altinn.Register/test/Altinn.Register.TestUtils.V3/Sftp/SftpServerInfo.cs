using Microsoft.Extensions.Configuration;
using Renci.SshNet;

namespace Altinn.Register.TestUtils.Sftp;

public record class SftpServerInfo(
    string Host,
    int Port,
    string Username,
    string Password,
    string UploadDirectory)
{
    /// <summary>
    /// Writes this server's connection details into <paramref name="configuration"/> under
    /// <paramref name="sectionKey"/>, using property names that match the shape of
    /// <c>SftpClientSettings</c> (<c>Host</c>, <c>Port</c>, <c>User</c>, <c>Password</c>,
    /// <c>RemotePath</c>).
    /// </summary>
    /// <param name="configuration">The <see cref="IConfigurationBuilder"/> to write into.</param>
    /// <param name="sectionKey">The configuration section to write under (no trailing colon).</param>
    public void Configure(IConfigurationBuilder configuration, string sectionKey)
    {
        configuration.AddInMemoryCollection([
            new($"{sectionKey}:Host", Host),
            new($"{sectionKey}:Port", Port.ToString()),
            new($"{sectionKey}:User", Username),
            new($"{sectionKey}:Password", Password),
            new($"{sectionKey}:RemotePath", UploadDirectory),
        ]);
    }

    /// <summary>
    /// Uploads the content of <paramref name="content"/> to a file named <paramref name="fileName"/>
    /// inside this server's <see cref="UploadDirectory"/>.
    /// </summary>
    /// <param name="fileName">The file name (without any path component).</param>
    /// <param name="content">A stream positioned at the start of the bytes to upload.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    internal async Task UploadFileAsync(string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        using var client = new SftpClient(Host, Port, Username, Password);
        await client.ConnectAsync(cancellationToken);
        try
        {
            client.UploadFile(content, $"{UploadDirectory}/{fileName}");
        }
        finally
        {
            client.Disconnect();
        }
    }
}
