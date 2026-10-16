using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents an external source for roles.
/// </summary>
/// <remarks>
/// This enum is explicitly made such that <c>default(ExternalRoleSource)</c> is not a valid value.
/// </remarks>
[StringEnumConverter]
public enum ExternalRoleSource
{
    /// <summary>
    /// The Norwegian Central Coordinating Register for Legal Entities.
    /// </summary>
    [JsonStringEnumMemberName("ccr")]
    CentralCoordinatingRegister = 1,

    /// <summary>
    /// The Norwegian National Population Register.
    /// </summary>
    [JsonStringEnumMemberName("npr")]
    NationalPopulationRegister,

    /// <summary>
    /// The Norwegian register of employers and employees.
    /// </summary>
    [JsonStringEnumMemberName("aar")]
    EmployersEmployeeRegister,

    /// <summary>
    /// The Norwegian Civil Rights Authority (Sivilrettsforvaltningen).
    /// </summary>
    [JsonStringEnumMemberName("cra")]
    CivilRightsAuthority,
}

/// <summary>
/// Extension methods for <see cref="ExternalRoleSource"/>.
/// </summary>
[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "This *must* be updated when the enum is.")]
internal static class ExternalRoleSourceExtensions
{
    /// <summary>
    /// Converts the specified external role source to its corresponding URN fragment string.
    /// </summary>
    /// <param name="source">The external role source to convert. Must be a defined value of <see cref="ExternalRoleSource"/>.</param>
    /// <returns>A string representing the URN fragment for the specified external role source.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="source"/> is not a valid value of <see cref="ExternalRoleSource"/>.</exception>
    public static string ToUrnFragment(this ExternalRoleSource source) => source switch
    {
        ExternalRoleSource.CentralCoordinatingRegister => "ccr",
        ExternalRoleSource.NationalPopulationRegister => "npr",
        ExternalRoleSource.EmployersEmployeeRegister => "aar",
        ExternalRoleSource.CivilRightsAuthority => "cra",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    /// <summary>
    /// Converts the specified external role source to its corresponding URN fragment string.
    /// </summary>
    /// <param name="source">The external role source to convert. Must be a defined value of <see cref="ExternalRoleSource"/>.</param>
    /// <returns>A string representing the URN fragment for the specified external role source.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="source"/> is not a valid value of <see cref="ExternalRoleSource"/>.</exception>
    public static string ToUrnFragment(this NonExhaustiveEnum<ExternalRoleSource> source)
        => source.IsWellKnown
        ? source.Value.ToUrnFragment()
        : source.UnknownValue;
}
