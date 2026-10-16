namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// The status of a unit of work.
/// </summary>
public enum UnitOfWorkStatus
{
    /// <summary>
    /// The unit of work is active.
    /// </summary>
    Active,

    /// <summary>
    /// The unit of work is committed.
    /// </summary>
    Committed,

    /// <summary>
    /// The unit of work is rolled back.
    /// </summary>
    RolledBack,

    /// <summary>
    /// The unit of work is disposed.
    /// </summary>
    Disposed,
}
