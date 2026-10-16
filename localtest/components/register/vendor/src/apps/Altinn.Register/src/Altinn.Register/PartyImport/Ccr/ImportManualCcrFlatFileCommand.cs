using Altinn.Authorization.ServiceDefaults.MassTransit;

namespace Altinn.Register.PartyImport.Ccr;

/// <summary>
/// A command that triggers the import of a manually specified CCR flat file.
/// </summary>
public sealed record ImportManualCcrFlatFileCommand
    : CommandBase
{
    /// <summary>
    /// Gets the name of the CCR flat file to import.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the run ID associated with the CCR flat file.
    /// </summary>
    public required uint RunId { get; init; }
}
