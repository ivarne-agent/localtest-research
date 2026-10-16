using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.Errors;

/// <summary>
/// Validation errors for Register.
/// </summary>
public static class ValidationErrors
{
    private static readonly ValidationErrorDescriptorFactory _factory
        = ValidationErrorDescriptorFactory.New("REG");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor PartyFields_SubUnits_Forbidden { get; }
        = _factory.Create(0, "Cannot request party subunits");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor TooManyItems { get; }
        = _factory.Create(1, "Too many items requested");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor PartyUrn_Invalid { get; }
        = _factory.Create(2, "Invalid party URN");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor Null { get; }
        = _factory.Create(3, "Value cannot be null");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor Empty { get; }
        = _factory.Create(4, "List cannot be empty");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor NotNull { get; }
        = _factory.Create(5, "Value must be null");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor InvalidOrganizationNumber { get; }
        = _factory.Create(6, "Invalid organization number");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor InvalidPersonNumber { get; }
        = _factory.Create(7, "Invalid person number");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor MutuallyExclusive { get; }
        = _factory.Create(8, "Values are mutually exclusive");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor InvalidPartyId { get; }
        = _factory.Create(9, "Invalid party id");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor InvalidPartyUuid { get; }
        = _factory.Create(10, "Invalid party uuid");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor MultipleActiveElements { get; }
        = _factory.Create(11, "Multiple elements are marked as active, but only one is allowed.");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor InvalidDate { get; }
        = _factory.Create(12, "Invalid date");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor UnknownEnumValue { get; }
        = _factory.Create(13, "Unknown enum value");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    /// <remarks>Prefer using more specific error descriptors when possible, such as <see cref="InvalidOrganizationNumber"/> or <see cref="InvalidPersonNumber"/>.</remarks>
    public static ValidationErrorDescriptor InvalidValue { get; }
        = _factory.Create(14, "Invalid value");

    /// <summary>Gets a <see cref="ValidationErrorDescriptor"/>.</summary>
    public static ValidationErrorDescriptor UnknownGuardianshipRole { get; }
        = _factory.Create(15, "Unknown guardianship role");
}
