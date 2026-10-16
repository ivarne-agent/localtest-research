using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents an identifier that can be either a <see cref="PersonIdentifier"/> or an <see cref="OrganizationIdentifier"/>.
/// </summary>
public readonly struct PersonOrOrganizationIdentifier
{
    private readonly object _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonOrOrganizationIdentifier"/> struct with a <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier.</param>
    public PersonOrOrganizationIdentifier(PersonIdentifier personIdentifier)
    {
        _value = personIdentifier;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonOrOrganizationIdentifier"/> struct with a <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier.</param>
    public PersonOrOrganizationIdentifier(OrganizationIdentifier organizationIdentifier)
    {
        _value = organizationIdentifier;
    }

    /// <summary>
    /// Gets the value of the identifier, which can be either a <see cref="PersonIdentifier"/> or an <see cref="OrganizationIdentifier"/>.
    /// </summary>
    public object? Value
        => _value;

    /// <summary>
    /// Gets a value indicating whether the identifier has a value (i.e., is not null).
    /// </summary>
    public bool HasValue
        => _value != null;

    /// <summary>
    /// Tries to get the person identifier if the value is of type <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier if the value is of type <see cref="PersonIdentifier"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value is of type <see cref="PersonIdentifier"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPersonIdentifier([NotNullWhen(true)] out PersonIdentifier? personIdentifier)
    {
        if (_value is PersonIdentifier pid)
        {
            personIdentifier = pid;
            return true;
        }

        personIdentifier = default;
        return false;
    }

    /// <summary>
    /// Tries to get the organization identifier if the value is of type <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier if the value is of type <see cref="OrganizationIdentifier"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the value is of type <see cref="OrganizationIdentifier"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetOrganizationIdentifier([NotNullWhen(true)] out OrganizationIdentifier? organizationIdentifier)
    {
        if (_value is OrganizationIdentifier oid)
        {
            organizationIdentifier = oid;
            return true;
        }

        organizationIdentifier = default;
        return false;
    }

    /// <summary>
    /// Defines an implicit conversion from <see cref="PersonIdentifier"/> to <see cref="PersonOrOrganizationIdentifier"/>.
    /// </summary>
    /// <param name="personIdentifier">The person identifier to convert.</param>
    public static implicit operator PersonOrOrganizationIdentifier(PersonIdentifier personIdentifier)
        => new(personIdentifier);

    /// <summary>
    /// Defines an implicit conversion from <see cref="OrganizationIdentifier"/> to <see cref="PersonOrOrganizationIdentifier"/>.
    /// </summary>
    /// <param name="organizationIdentifier">The organization identifier to convert.</param>
    public static implicit operator PersonOrOrganizationIdentifier(OrganizationIdentifier organizationIdentifier)
        => new(organizationIdentifier);

    /// <summary>
    /// Defines an explicit conversion from <see cref="PersonOrOrganizationIdentifier"/> to <see cref="PersonIdentifier"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="PersonOrOrganizationIdentifier"/> to convert.</param>
    public static explicit operator PersonIdentifier(PersonOrOrganizationIdentifier identifier)
        => (PersonIdentifier)identifier._value;

    /// <summary>
    /// Defines an explicit conversion from <see cref="PersonOrOrganizationIdentifier"/> to <see cref="OrganizationIdentifier"/>.
    /// </summary>
    /// <param name="identifier">The <see cref="PersonOrOrganizationIdentifier"/> to convert.</param>
    public static explicit operator OrganizationIdentifier(PersonOrOrganizationIdentifier identifier)
        => (OrganizationIdentifier)identifier._value;
}
