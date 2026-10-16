namespace Altinn.Register.Persistence;

/// <summary>
/// Vacuum index cleanup options.
/// </summary>
internal enum PostgreSqlVacuumIndexCleanup
    : byte
{
    /// <summary>
    /// Vacuum index is disabled.
    /// </summary>
    Off = default,

    /// <summary>
    /// Vacuum index is enabled based on internal heuristics. This is the default.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Vacuum index is always enabled.
    /// </summary>
    On = 2,
}
