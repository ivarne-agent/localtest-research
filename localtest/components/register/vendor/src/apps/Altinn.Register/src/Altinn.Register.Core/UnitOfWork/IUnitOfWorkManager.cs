using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// A manager for creating units of work.
/// </summary>
public interface IUnitOfWorkManager
{
    /// <summary>
    /// Creates a new unit of work.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <param name="activityName">The name of the activity, used in telemetry.</param>
    /// <returns>A <see cref="IUnitOfWork"/>.</returns>
    public ValueTask<IUnitOfWork> CreateAsync(
        CancellationToken cancellationToken,
        [CallerMemberName] string activityName = "")
        => CreateAsync(tags: default, links: default, cancellationToken, activityName);

    /// <summary>
    /// Creates a new unit of work.
    /// </summary>
    /// <param name="tags">A set of tags to be added to the activity.</param>
    /// <param name="links">A set of links to be added to the activity.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <param name="activityName">The name of the activity, used in telemetry.</param>
    /// <returns>A <see cref="IUnitOfWork"/>.</returns>
    public ValueTask<IUnitOfWork> CreateAsync(
        ReadOnlySpan<KeyValuePair<string, object?>> tags = default,
        ReadOnlySpan<ActivityLink> links = default,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string activityName = "");
}
