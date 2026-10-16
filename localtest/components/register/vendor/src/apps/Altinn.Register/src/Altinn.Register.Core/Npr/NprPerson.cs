using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Npr;

/// <summary>
/// Represents a person as returned by the NPR API, including their name, address, date of birth/death, and guardianship information.
/// </summary>
public sealed record NprPerson
{
    /// <summary>
    /// Gets the identifier of the person.
    /// </summary>
    public required PersonIdentifier PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the display name of the person, which is typically a concatenation of the first name, middle name (if any), and last name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the first name.
    /// </summary>
    public required string FirstName { get; init; }

    /// <summary>
    /// Gets the (optional) middle name.
    /// </summary>
    public required string? MiddleName { get; init; }

    /// <summary>
    /// Gets the last name.
    /// </summary>
    public required string LastName { get; init; }

    /// <summary>
    /// Gets the short name.
    /// </summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// Gets the (optional) <see cref="StreetAddressRecord"/> of the person.
    /// </summary>
    public required StreetAddressRecord? Address { get; init; }

    /// <summary>
    /// Gets the (optional) <see cref="MailingAddressRecord"/> of the person.
    /// </summary>
    public required MailingAddressRecord? MailingAddress { get; init; }

    /// <summary>
    /// Gets the date of birth of the person.
    /// </summary>
    public required DateOnly? DateOfBirth { get; init; }

    /// <summary>
    /// Gets the (optional) date of death of the person.
    /// </summary>
    public required DateOnly? DateOfDeath { get; init; }

    /// <summary>
    /// Gets the list of guardianships of the person.
    /// </summary>
    /// <remarks>
    /// This <strong>MUST</strong> be sorted by <see cref="NprGuardianship.Guardian"/>.
    /// </remarks>
    public required ImmutableValueArray<NprGuardianship> Guardians { get; init; }
}
