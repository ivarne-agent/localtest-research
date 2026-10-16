using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.PartyImport;

/// <summary>
/// Tracking information for a party import.
/// </summary>
public readonly record struct UpsertPartyTracking
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpsertPartyTracking"/> record.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <param name="progress">The progress.</param>
    [SetsRequiredMembers]
    public UpsertPartyTracking(string jobName, ulong progress)
    {
        ArgumentNullException.ThrowIfNull(jobName);
        ArgumentOutOfRangeException.ThrowIfZero(progress);

        JobName = jobName;
        Progress = progress;
    }

    /// <summary>
    /// The name of the job that imported the party.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// The change ID of the party.
    /// </summary>
    public required ulong Progress { get; init; }

    /// <summary>
    /// Gets a value indicating whether this instance has a value.
    /// </summary>
    public bool HasValue => JobName is not null;
}
