namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents the type of a user profile in Altinn 2.
/// </summary>
public enum A2UserProfileType
{
    /// <summary>
    /// A person (SSN identified) user.
    /// </summary>
    Person,

    /// <summary>
    /// An enterprise (org number identified) user.
    /// </summary>
    EnterpriseUser,

    /// <summary>
    /// A self-identified (username identified) user.
    /// </summary>
    SelfIdentifiedUser,
}
