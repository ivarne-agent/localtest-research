using Altinn.Register.Contracts.ExternalRoles;

namespace Altinn.Register.Integrations.Npr;

// TODO: make internal once NprClient does not use it anymore

/// <summary>Mappings of guardianship values from npr to Altinn Register.</summary>
public partial class GuardianshipRoleMapper
{
    /// <summary>Tries to find a guardianship role by NPR values.</summary>
    /// <param name="vergeTjenestevirksomhet">The NPR value for the guardianship area.</param>
    /// <param name="vergeTjenesteoppgave">The NPR value for the guardianship task.</param>
    /// <param name="role">The found role, if any.</param>
    /// <remarks>If the role is expired, <see langword="null"/> is returned.</remarks>
    /// <returns><see langword="true"/> if a role was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryFindRoleByNprValues(
        string vergeTjenestevirksomhet,
        string vergeTjenesteoppgave,
        out ExternalRoleReference? role)
        => TryFindRoleByNprValues(vergeTjenestevirksomhet.AsSpan(), vergeTjenesteoppgave.AsSpan(), out role);
}
