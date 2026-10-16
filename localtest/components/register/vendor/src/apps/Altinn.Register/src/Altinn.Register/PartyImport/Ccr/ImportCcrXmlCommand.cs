using System.Text.Json.Serialization;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;

namespace Altinn.Register.PartyImport.Ccr;

/// <summary>
/// A command carrying a single organization update parsed from a CCR flat file, to be applied by
/// the <see cref="Altinn.Register.Core.Ccr.CcrService"/>.
/// </summary>
public sealed record ImportCcrXmlCommand
    : CommandBase
{
    /// <summary>
    /// Gets the unique identifier of the batch, if available.
    /// Is null for sync SOAP requests, but will be set for messages produced by the <see cref="CcrImportJob"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? BatchId { get; init; }

    /// <summary>
    /// Gets the identifier of the organization this command pertains to.
    /// Is null for sync SOAP requests, but will be set for messages produced by the <see cref="CcrImportJob"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrganizationIdentifier? OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the CCR XML document as raw bytes.
    /// </summary>
    public required byte[] Document { get; init; }

    /// <summary>
    /// Gets whether the update should be federated to other Altinn instances.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, the default behavior is to federate the update.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Federate { get; init; }

    /// <summary>
    /// Gets the name of the CCR client that sent this command.
    /// </summary>
    /// <remarks>
    /// This is only used for logging and auditing purposes, and does not affect the processing of the command.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; init; }
}
