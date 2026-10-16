using System.IO.Pipelines;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.ImportJobs.FileProcessing;

/// <summary>
/// Information about an opened file, including the name and a <see cref="PipeReader"/> to read the file content from.
/// </summary>
/// <remarks>
/// This class is intended to be derived from, to allow for additional information about the opened file to be included.
/// </remarks>
public class OpenedFileInfo
{
    private readonly string _name;
    private readonly PipeReader _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenedFileInfo"/> class.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="reader">The file reader.</param>
    public OpenedFileInfo(string name, PipeReader reader)
    {
        Guard.IsNotNull(reader);
        Guard.IsNotNullOrWhiteSpace(name);

        _name = name;
        _reader = reader;
    }

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string Name
        => _name;

    /// <summary>
    /// Gets the <see cref="PipeReader"/> to read the file content from.
    /// </summary>
    public PipeReader Reader
        => _reader;
}
