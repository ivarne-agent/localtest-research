using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// The kind of SIRE feed event. Carried on each <see cref="SireUpdate"/> for
/// observability, but the consumer does not branch on it — every event triggers the
/// same action (re-fetch the full organization via <c>ISireClient.GetOrganization</c>
/// and upsert).
/// </summary>
[StringEnumConverter]
public enum SireUpdateType
{
    /// <summary>
    /// A new organization has been registered in SIRE.
    /// </summary>
    [JsonStringEnumMemberName("Ny")]
    New = 1,

    /// <summary>
    /// An existing organization has been changed in SIRE.
    /// </summary>
    [JsonStringEnumMemberName("Endret")]
    Changed,

    /// <summary>
    /// An organization has been deleted in SIRE. The full lookup record still resolves
    /// (with <c>slettetdato</c> populated), so the standard re-fetch + upsert flow
    /// propagates the deletion via <see cref="SireOrganization.DeletedAt"/>.
    /// </summary>
    [JsonStringEnumMemberName("Slettet")]
    Deleted,
}
