using Altinn.Register.Contracts.V1;

namespace Altinn.Register.Tests.Utils
{
    public static class AssertionUtil
    {
        public static void AssertEqual(List<Party> expected, List<Party> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                AssertEqual(expected[i], actual[i]);
            }
        }

        private static void AssertEqual(Party expected, Party actual)
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.PartyId, actual.PartyId);
            Assert.Equal(expected.PartyTypeName, actual.PartyTypeName);
            if (expected.PartyTypeName == PartyType.Organisation)
            {
                Assert.Equal(expected.OrgNumber, actual.OrgNumber);
                Assert.Equal(expected.Organization?.Name, actual.Organization?.Name);
                Assert.Equal(expected.Organization?.UnitType, actual.Organization?.UnitType);
                Assert.Equal(expected.Organization?.UnitStatus, actual.Organization?.UnitStatus);
                Assert.Equal(expected.Organization?.MailingPostalCode, actual.Organization?.MailingPostalCode);
                Assert.Equal(expected.Organization?.MailingAddress, actual.Organization?.MailingAddress);
                Assert.Equal(expected.Organization?.BusinessPostalCode, actual.Organization?.BusinessPostalCode);
                Assert.Equal(expected.Organization?.BusinessAddress, actual.Organization?.BusinessAddress);
                Assert.Equal(expected.Organization?.BusinessPostalCity, actual.Organization?.BusinessPostalCity);
                Assert.Equal(expected.Organization?.EMailAddress, actual.Organization?.EMailAddress);
                Assert.Equal(expected.Organization?.FaxNumber, actual.Organization?.FaxNumber);
                Assert.Equal(expected.Organization?.MobileNumber, actual.Organization?.MobileNumber);
                Assert.Equal(expected.Organization?.OrgNumber, actual.Organization?.OrgNumber);
                Assert.Equal(expected.Organization?.TelephoneNumber, actual.Organization?.TelephoneNumber);
            }
            else if (expected.PartyTypeName == PartyType.Person)
            {
                Assert.Equal(expected.SSN, actual.SSN);
                Assert.Equal(expected.Person?.SSN, actual.Person?.SSN);
                Assert.Equal(expected.Person?.Name, actual.Person?.Name);
                Assert.Equal(expected.Person?.AddressMunicipalNumber, actual.Person?.AddressMunicipalNumber);
                Assert.Equal(expected.Person?.AddressMunicipalName, actual.Person?.AddressMunicipalName);
                Assert.Equal(expected.Person?.AddressHouseNumber, actual.Person?.AddressHouseNumber);
                Assert.Equal(expected.Person?.AddressHouseLetter, actual.Person?.AddressHouseLetter);
                Assert.Equal(expected.Person?.AddressCity, actual.Person?.AddressCity);
                Assert.Equal(expected.Person?.AddressPostalCode, actual.Person?.AddressPostalCode);
                Assert.Equal(expected.Person?.AddressStreetName, actual.Person?.AddressStreetName);
                Assert.Equal(expected.Person?.MiddleName, actual.Person?.MiddleName);
                Assert.Equal(expected.Person?.LastName, actual.Person?.LastName);
                Assert.Equal(expected.Person?.FirstName, actual.Person?.FirstName);
                Assert.Equal(expected.Person?.MailingAddress, actual.Person?.MailingAddress);
                Assert.Equal(expected.Person?.MailingPostalCity, actual.Person?.MailingPostalCity);
                Assert.Equal(expected.Person?.MailingPostalCode, actual.Person?.MailingPostalCode);
            }
        }
    }
}
