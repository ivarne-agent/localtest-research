using System.Diagnostics.CodeAnalysis;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Mapping;

/// <summary>
/// Mappers for converting from <see cref="PartyRecord"/>
/// to <see cref="Party"/>.
/// </summary>
internal static partial class PartyMapper
{
    [return: NotNullIfNotNull(nameof(source))]
    private static StreetAddress? MapStreetAddress(StreetAddressRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        return new StreetAddress
        {
            MunicipalNumber = source.MunicipalNumber,
            MunicipalName = source.MunicipalName,
            StreetName = source.StreetName,
            HouseNumber = source.HouseNumber,
            HouseLetter = source.HouseLetter,
            PostalCode = source.PostalCode,
            City = source.City,
        };
    }

    [return: NotNullIfNotNull(nameof(source))]
    private static MailingAddress? MapMailingAddress(MailingAddressRecord? source)
    {
        if (source is null)
        {
            return null;
        }

        return new MailingAddress
        {
            Address = source.Address,
            PostalCode = source.PostalCode,
            City = source.City,
        };
    }
}
