using System.Diagnostics;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents the result of an operation to process a CCR flat file, indicating whether a file was processed or if there was no file to process.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct CcrFlatFileOperationResult
{
    /// <summary>
    /// Indicates that there was no CCR flat file to process.
    /// </summary>
    public static readonly CcrFlatFileOperationResult NoFileToProcess
        = new(Outcome.NoFileToProcess);

    /// <summary>
    /// Indicates that a CCR flat file was successfully processed.
    /// </summary>
    public static readonly CcrFlatFileOperationResult FileProcessed
        = new(Outcome.FileProcessed);

    /// <inheritdoc/>
    public override string ToString() => _outcome.ToString();

    private readonly Outcome _outcome;

    private CcrFlatFileOperationResult(Outcome outcome)
    {
        _outcome = outcome;
    }

    private enum Outcome
    {
        NoFileToProcess = default,
        FileProcessed,
    }
}
