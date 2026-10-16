using System.IO.Pipelines;
using Altinn.Register.Core.ImportJobs.FileProcessing;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Information about an opened CCR flat file, including the name, a <see cref="PipeReader"/> to read the file content from, and the sequence number of the CCR file.
/// </summary>
public sealed class CcrOpenedFileInfo
    : OpenedFileInfo
{
    private readonly uint _sequenceNumber;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrOpenedFileInfo"/> class.
    /// </summary>
    /// <param name="name">The name of the CCR file.</param>
    /// <param name="reader">The <see cref="PipeReader"/> to read the file content from.</param>
    /// <param name="sequenceNumber">The sequence number of the CCR file.</param>
    public CcrOpenedFileInfo(string name, PipeReader reader, uint sequenceNumber)
        : base(name, reader)
    {
        _sequenceNumber = sequenceNumber;
    }

    /// <summary>
    /// Gets the sequence number of the CCR file.
    /// </summary>
    public uint SequenceNumber
        => _sequenceNumber;
}
