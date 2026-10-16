namespace Altinn.Register.Core.Sire;

/// <summary>
/// Options for <see cref="ISireEventClient"/>.
/// </summary>
public sealed class SireEventClientOptions
{
    /// <summary>
    /// Maximum number of events to request per call to the SIRE feed, sent as the
    /// <c>antall</c> query-string parameter. Defaults to <c>100</c> when not overridden via configuration.
    /// </summary>
    /// <remarks> 
    /// Bound from configuration key <c>Altinn:register:PartyImport:Sire:Client:PageSize</c>.
    /// </remarks>
    public int PageSize { get; init; } = 100;
}
