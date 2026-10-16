using System.Collections.Immutable;
using Altinn.Register.Core.Parties;

namespace Altinn.Register.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="IPartyPersistenceCleanupService"/>.
/// </summary>
internal class PartyPostgreSqlPersistenceCleanupService
    : IPartyPersistenceCleanupService
{
    private static readonly ImmutableArray<string> TablesToVacuum = [
        "party",
        "person",
        "organization",
        "import_job_party_state",
        "external_role_assignment",
    ];

    private readonly PostgreSqlVacuumService _vacuumService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyPostgreSqlPersistenceCleanupService"/> class.
    /// </summary>
    public PartyPostgreSqlPersistenceCleanupService(PostgreSqlVacuumService vacuumService)
    {
        _vacuumService = vacuumService;
    }

    /// <inheritdoc/>
    public async Task RunPeriodicPartyCleanup(CancellationToken cancellationToken = default)
    {
        var settings = new PostgreSqlVacuumSettings
        {
            Freeze = true,
            Analyze = true,
            Truncate = false,
        };

        foreach (var table in TablesToVacuum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _vacuumService.VacuumTable("register", table, settings, cancellationToken);
        }
    }
}
