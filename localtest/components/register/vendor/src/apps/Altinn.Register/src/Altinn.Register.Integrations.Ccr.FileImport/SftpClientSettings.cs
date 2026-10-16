using System.ComponentModel.DataAnnotations;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Settings for transferring CCR flat files, including the host, user, password, remote path, port, and timeout for the file transfer.
/// </summary>
internal sealed class SftpClientSettings
{
    /// <summary>
    /// Gets or sets the host to connect to for transferring CCR flat files.
    /// </summary>
    [Required]
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the username for authentication when transferring CCR flat files.
    /// </summary>
    [Required]
    public string? User { get; set; }

    /// <summary>
    /// Gets or sets the password for authentication when transferring CCR flat files.
    /// </summary>
    [Required]
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the remote path to access for transferring CCR flat files.
    /// This is the path on the SFTP server where the CCR flat files are located.
    /// The service will look for files in this path and process them accordingly.
    /// </summary>
    [Required]
    public string? RemotePath { get; set; }

    /// <summary>
    /// Gets or sets the port to connect to for transferring CCR flat files. The default value is 22, which is the standard port for SFTP connections.
    /// </summary>
    public ushort Port { get; set; } = 22;

    /// <summary>
    /// Gets or sets the timeout for connection attempts when transferring CCR flat files. The default value is 30 seconds.
    /// This setting determines how long the service will wait for a connection to be established before timing out and throwing an error.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
