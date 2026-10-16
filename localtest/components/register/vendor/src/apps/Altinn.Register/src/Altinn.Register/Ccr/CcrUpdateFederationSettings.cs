using System.Collections.Immutable;

namespace Altinn.Register.Ccr;

/// <summary>
/// Settings for CCR update federation, including whether federation is enabled and the list of target queues to send updates to.
/// </summary>
internal sealed class CcrUpdateFederationSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether CCR update federation is enabled. If false, no updates will be federated to other systems.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// Gets the names of the targets to federate CCR updates to. Each target corresponds to a storage queue configuration name.
    /// </summary>
    public ImmutableArray<string> Targets { get; set; }
        = ImmutableArray<string>.Empty;
}
