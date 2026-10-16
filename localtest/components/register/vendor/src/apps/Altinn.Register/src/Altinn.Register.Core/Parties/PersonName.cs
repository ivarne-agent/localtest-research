using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents a person's name, including first name, middle name (if any), last name, display name and short name.
/// </summary>
public sealed record class PersonName
{
    /// <summary>
    /// Gets the default name used when a person name is missing.
    /// </summary>
    public static PersonName Missing { get; } = new PersonName(
        firstName: "Mangler",
        middleName: null,
        lastName: "Navn",
        displayName: "Mangler Navn",
        shortName: "Mangler Navn");

    /// <summary>
    /// Creates a new instance of <see cref="PersonName"/>.
    /// </summary>
    /// <param name="firstName">The first name of the person.</param>
    /// <param name="lastName">The last name of the person.</param>
    /// <returns>A new instance of <see cref="PersonName"/>.</returns>
    public static PersonName Create(string? firstName, string? lastName)
        => Create(firstName, middleName: null, lastName);

    /// <summary>
    /// Creates a new instance of <see cref="PersonName"/>.
    /// </summary>
    /// <param name="firstName">The first name of the person.</param>
    /// <param name="middleName">The middle name of the person (if any).</param>
    /// <param name="lastName">The last name of the person.</param>
    /// <returns>A new instance of <see cref="PersonName"/>.</returns>
    public static PersonName Create(string? firstName, string? middleName, string? lastName)
        => Create(firstName, middleName, lastName, shortName: null);

    /// <summary>
    /// Creates a new instance of <see cref="PersonName"/>.
    /// </summary>
    /// <param name="firstName">The first name of the person.</param>
    /// <param name="middleName">The middle name of the person (if any).</param>
    /// <param name="lastName">The last name of the person.</param>
    /// <param name="shortName">The short name of the person (if any).</param>
    /// <returns>A new instance of <see cref="PersonName"/>.</returns>
    public static PersonName Create(string? firstName, string? middleName, string? lastName, string? shortName)
    {
        firstName = OrDefault(firstName, Missing.FirstName);
        middleName = OrDefault(middleName, null);
        lastName = OrDefault(lastName, Missing.LastName);
        shortName = shortName?.Trim();

        if (string.IsNullOrEmpty(shortName))
        {
            var sb = new StringBuilder(lastName);

            sb.Append(' ').Append(firstName);

            if (middleName is not null)
            {
                sb.Append(' ').Append(middleName);
            }

            shortName = sb.ToString();
        }

        var dn = new StringBuilder(firstName);

        if (middleName is not null)
        {
            dn.Append(' ').Append(middleName);
        }

        dn.Append(' ').Append(lastName);

        var displayName = dn.ToString();

        return new PersonName(firstName, middleName, lastName, shortName, displayName);

        [return: NotNullIfNotNull(nameof(value))]
        [return: NotNullIfNotNull(nameof(defaultValue))]
        static string? OrDefault(string? value, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return value.Trim();
        }
    }

    [JsonConstructor]
    private PersonName(
        string firstName,
        string? middleName,
        string lastName,
        string shortName,
        string displayName)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(firstName));
        Debug.Assert(!string.IsNullOrWhiteSpace(lastName));
        Debug.Assert(!string.IsNullOrWhiteSpace(displayName));
        Debug.Assert(!string.IsNullOrWhiteSpace(shortName));

        FirstName = firstName;
        MiddleName = middleName;
        LastName = lastName;
        ShortName = shortName;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the first name.
    /// </summary>
    public string FirstName { get; }

    /// <summary>
    /// Gets the middle name (if any).
    /// </summary>
    public string? MiddleName { get; }

    /// <summary>
    /// Gets the last name.
    /// </summary>
    public string LastName { get; }

    /// <summary>
    /// Gets the short name.
    /// </summary>
    public string ShortName { get; }
}
