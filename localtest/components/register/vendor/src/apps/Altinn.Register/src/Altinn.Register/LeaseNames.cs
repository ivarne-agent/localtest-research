using Altinn.Register.PartyImport.A2;
using Altinn.Register.PartyImport.SystemUser;

namespace Altinn.Register;

/// <summary>
/// Lease names for register.
/// </summary>
internal static class LeaseNames
{
    /// <summary>
    /// Lease name for <see cref="A2PartyImportJob"/>.
    /// </summary>
    internal const string A2PartyImport = "a2-party-import";

    /// <summary>
    /// Lease name for <see cref="A2ProfileImportJob"/>.
    /// </summary>
    internal const string A2ProfileImport = "a2-profile-import";

    /// <summary>
    /// Lease name for <see cref="SystemUserImportJob"/>.
    /// </summary>
    internal const string SystemUserImport = "systemuser-import";

    /// <summary>
    /// Lease name for party cleanup job.
    /// </summary>
    internal const string PartyCleanup = "db:party-cleanup";

    /// <summary>
    /// Lease name for NPR import job.
    /// </summary>
    internal const string NprImport = "npr-import";

    /// <summary>
    /// Lease name for the CCR import job.
    /// </summary>
    internal const string CcrImport = "ccr-file-import";

    /// <summary>
    /// Lease name for SIRE import job.
    /// </summary>
    internal const string SireImport = "sire-import";
}
