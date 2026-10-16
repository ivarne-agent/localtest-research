using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Configuration;

/// <summary>
/// Settings for importing CCR flat files from the BRG SFTP server.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class CcrFileImportSettings
{
    /// <summary>
    /// Gets or sets the host name of the SFTP server to connect to.
    /// </summary>
    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port of the SFTP server to connect to.
    /// </summary>
    [Range(1, 65535)]
    public ushort Port { get; set; } = 22;

    /// <summary>
    /// Gets or sets the username used to authenticate against the SFTP server.
    /// </summary>
    [Required]
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password used to authenticate against the SFTP server.
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the remote directory to read CCR flat files from.
    /// </summary>
    [Required]
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection timeout, in seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan Timeout { get; set; }
}
