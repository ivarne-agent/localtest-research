using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Extension helpers for working with addresses.
/// </summary>
internal static class AddressExtensions
{
    extension(MailingAddressRecord address)
    {
        public bool IsEmpty
            => address.Address is null
            && address.PostalCode is null
            && address.City is null;
    }

    extension(StreetAddressRecord address)
    {
        public bool IsEmpty
            => address.MunicipalNumber is null
            && address.MunicipalName is null
            && address.StreetName is null
            && address.HouseNumber is null
            && address.HouseLetter is null
            && address.PostalCode is null
            && address.City is null;
    }
}
