using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Models;

namespace Altinn.Register.Tests.UnitTests;

public class PartyTypesSetTests
{
    [Fact]
    public void Contains_AllTypes()
    {
        PartyTypes allBitsSet = ~PartyTypes.None;
        var partyTypesSet = new PartyTypesSet(allBitsSet);

        var allPartyTypes = Enum.GetValues<PartyRecordType>();
        partyTypesSet.Count.ShouldBe(allPartyTypes.Length);

        foreach (var partyType in allPartyTypes)
        {
            partyTypesSet.Contains(partyType).ShouldBeTrue($"Expected PartyTypesSet to contain party type {partyType}");
        }

        var enumerated = partyTypesSet.ToList();
        enumerated.Count.ShouldBe(allPartyTypes.Length, "Expected enumeration to yield all party types");

        foreach (var partyType in allPartyTypes)
        {
            enumerated.ShouldContain(partyType, $"Expected enumeration to contain party type {partyType}");
        }
    }
}
