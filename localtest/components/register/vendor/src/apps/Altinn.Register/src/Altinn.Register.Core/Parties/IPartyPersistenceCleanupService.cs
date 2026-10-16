namespace Altinn.Register.Core.Parties;

/// <summary>
/// A service for cleaning up party persistence storage.
/// </summary>
public interface IPartyPersistenceCleanupService
{
    /// <summary>
    /// Runs periodic cleanup of party storage.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task RunPeriodicPartyCleanup(CancellationToken cancellationToken = default);
}
