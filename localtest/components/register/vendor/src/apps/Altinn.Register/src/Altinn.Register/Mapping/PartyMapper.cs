using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Mapping;

/// <summary>
/// Mappers for converting from <see cref="PartyRecord"/>
/// to <see cref="Party"/>.
/// </summary>
internal static partial class PartyMapper
{
    /// <summary>
    /// Maps a <see cref="PartyRecord"/> to a <see cref="Party"/>.
    /// </summary>
    /// <param name="source">The source party.</param>
    /// <returns>A <see cref="Party"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static Party? ToPlatformModel(this PartyRecord? source)
        => source switch
        {
            PersonRecord pers => pers.ToPlatformModel(),
            OrganizationRecord org => org.ToPlatformModel(),
            SelfIdentifiedUserRecord siUser => siUser.ToPlatformModel(),
            EnterpriseUserRecord entUser => entUser.ToPlatformModel(),
            SystemUserRecord sysUser => sysUser.ToPlatformModel(),
            _ => ThrowHelper.ThrowInvalidOperationException<Party>("Unsupported party type"),
        };

    /// <summary>
    /// Maps a <see cref="PartyUserRecord"/> to a <see cref="PartyUser"/>.
    /// </summary>
    /// <param name="source">The source person.</param>
    /// <returns>A <see cref="Person"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static Person? ToPlatformModel(this PersonRecord? source)
        => MapPerson(source);

    /// <summary>
    /// Maps a <see cref="OrganizationRecord"/> to a <see cref="Organization"/>.
    /// </summary>
    /// <param name="source">The source organization.</param>
    /// <returns>A <see cref="Organization"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static Organization? ToPlatformModel(this OrganizationRecord? source)
        => MapOrganization(source);

    /// <summary>
    /// Maps a <see cref="SelfIdentifiedUserRecord"/> to a <see cref="SelfIdentifiedUser"/>.
    /// </summary>
    /// <param name="source">The source self-identified user.</param>
    /// <returns>A <see cref="SelfIdentifiedUser"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static SelfIdentifiedUser? ToPlatformModel(this SelfIdentifiedUserRecord? source)
        => MapSelfIdentifiedUser(source);

    /// <summary>
    /// Maps a <see cref="EnterpriseUserRecord"/> to a <see cref="EnterpriseUser"/>.
    /// </summary>
    /// <param name="source">The source enterprise user.</param>
    /// <returns>A <see cref="EnterpriseUser"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static EnterpriseUser? ToPlatformModel(this EnterpriseUserRecord? source)
        => MapEnterpriseUser(source);

    /// <summary>
    /// Maps a <see cref="SystemUserRecord"/> to a <see cref="SystemUser"/>.
    /// </summary>
    /// <param name="source">The source self-identified user.</param>
    /// <returns>A <see cref="SystemUser"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static SystemUser? ToPlatformModel(this SystemUserRecord? source)
        => MapSystemUser(source);

    /// <summary>
    /// Maps a <see cref="StreetAddressRecord"/> to a <see cref="StreetAddress"/>.
    /// </summary>
    /// <param name="source">The source street-address.</param>
    /// <returns>A <see cref="StreetAddress"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static StreetAddress? ToPlatformModel(this StreetAddressRecord? source)
        => MapStreetAddress(source);

    /// <summary>
    /// Maps a <see cref="MailingAddressRecord"/> to a <see cref="MailingAddress"/>.
    /// </summary>
    /// <param name="source">The source mailing-address.</param>
    /// <returns>A <see cref="MailingAddress"/>.</returns>
    [return: NotNullIfNotNull(nameof(source))]
    public static MailingAddress? ToPlatformModel(this MailingAddressRecord? source)
        => MapMailingAddress(source);
}
