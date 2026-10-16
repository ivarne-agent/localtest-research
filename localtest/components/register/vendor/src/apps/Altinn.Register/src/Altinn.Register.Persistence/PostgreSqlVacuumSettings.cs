using System.Diagnostics;

namespace Altinn.Register.Persistence;

/// <summary>
/// Settings for the PostgreSQL VACUUM command.
/// </summary>
internal readonly record struct PostgreSqlVacuumSettings
{
    /// <summary>
    /// Selects “full” vacuum, which can reclaim more space, but takes much longer and exclusively locks the table.
    /// This method also requires extra disk space, since it writes a new copy of the table and doesn't release the
    /// old copy until the operation is complete. Usually this should only be used when a significant amount of space
    /// needs to be reclaimed from within the table.
    /// </summary>
    public bool? Full { get; init; }

    /// <summary>
    /// Selects aggressive “freezing” of tuples.
    /// </summary>
    public bool? Freeze { get; init; }

    /// <summary>
    /// Updates statistics used by the planner to determine the most efficient way to execute a query.
    /// </summary>
    public bool? Analyze { get; init; }

    /// <summary>
    /// Normally, VACUUM will skip pages based on the visibility map. Pages where all tuples are known to be frozen can
    /// always be skipped, and those where all tuples are known to be visible to all transactions may be skipped except
    /// when performing an aggressive vacuum. Furthermore, except when performing an aggressive vacuum, some pages may
    /// be skipped in order to avoid waiting for other sessions to finish using them. This option disables all
    /// page-skipping behavior, and is intended to be used only when the contents of the visibility map are suspect,
    /// which should happen only if there is a hardware or software issue causing database corruption.
    /// </summary>
    public bool? DisablePageSkipping { get; init; }

    /// <summary>
    /// Specifies that VACUUM should not wait for any conflicting locks to be released when beginning work on a relation:
    /// if a relation cannot be locked immediately without waiting, the relation is skipped. Note that even with this
    /// option, VACUUM may still block when opening the relation's indexes. Additionally, VACUUM ANALYZE may still block
    /// when acquiring sample rows from partitions, table inheritance children, and some types of foreign tables. Also,
    /// while VACUUM ordinarily processes all partitions of specified partitioned tables, this option will cause VACUUM
    /// to skip all partitions if there is a conflicting lock on the partitioned table.
    /// </summary>
    public bool? SkipLocked { get; init; }

    /// <summary>
    /// Normally, VACUUM will skip index vacuuming when there are very few dead tuples in the table. The cost of
    /// processing all of the table's indexes is expected to greatly exceed the benefit of removing dead index tuples
    /// when this happens. This option can be used to force VACUUM to process indexes when there are more than zero dead tuples.
    /// The default is AUTO, which allows VACUUM to skip index vacuuming when appropriate. If INDEX_CLEANUP is set to ON,
    /// VACUUM will conservatively remove all dead tuples from indexes. This may be useful for backwards compatibility with
    /// earlier releases of PostgreSQL where this was the standard behavior.
    ///
    /// This option has no effect for tables that have no index and is ignored if the FULL option is used. It also has no effect
    /// on the transaction ID wraparound failsafe mechanism. When triggered it will skip index vacuuming, even when INDEX_CLEANUP
    /// is set to ON.
    /// </summary>
    public PostgreSqlVacuumIndexCleanup? IndexCleanup { get; init; }

    /// <summary>
    /// Specifies that VACUUM should attempt to truncate off any empty pages at the end of the table and allow the disk space for
    /// the truncated pages to be returned to the operating system. This is normally the desired behavior and is the default unless
    /// the vacuum_truncate option has been set to false for the table to be vacuumed. Setting this option to false may be useful
    /// to avoid ACCESS EXCLUSIVE lock on the table that the truncation requires. This option is ignored if the FULL option is used.
    /// </summary>
    public bool? Truncate { get; init; }

    /// <summary>
    /// Gets a value indicating whether any of the settings have been set.
    /// </summary>
    /// <returns><see langword="true"/>, if any settings have been set.</returns>
    internal bool Any()
        => Full is not null
        || Freeze is not null
        || Analyze is not null
        || DisablePageSkipping is not null
        || SkipLocked is not null
        || IndexCleanup is not null
        || Truncate is not null;

    /// <summary>
    /// Gets tags for telemetry.
    /// </summary>
    internal TagList Tags
    {
        get
        {
            return [
                new("vacuum.full", BoolTag(Full)),
                new("vacuum.freeze", BoolTag(Freeze)),
                new("vacuum.analyze", BoolTag(Analyze)),
                new("vacuum.disable_page_skipping", BoolTag(DisablePageSkipping)),
                new("vacuum.skip_locked", BoolTag(SkipLocked)),
                new("vacuum.index_cleanup", IndexCleanupTag(IndexCleanup)),
                new("vacuum.truncate", BoolTag(Truncate)),
            ];

            static string? BoolTag(bool? value) => value switch
            {
                true => "true",
                false => "false",
                _ => null,
            };

            static string? IndexCleanupTag(PostgreSqlVacuumIndexCleanup? value) => value switch
            {
                PostgreSqlVacuumIndexCleanup.Off => "off",
                PostgreSqlVacuumIndexCleanup.Auto => "auto",
                PostgreSqlVacuumIndexCleanup.On => "on",
                _ => null,
            };
        }
    }
}
