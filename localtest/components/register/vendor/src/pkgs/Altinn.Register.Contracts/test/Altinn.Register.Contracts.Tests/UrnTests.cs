using Altinn.Urn;
using Xunit.Sdk;

namespace Altinn.Register.Contracts.Tests;

public class UrnTests
{
    [Theory]
    [MemberData(nameof(PartyExternalRefUrns))]
    public void All_PartyExternalRefUrns_CanBeParsed_As_PartyUrns(PartyExternalRefUrnWrapper source)
    {
        PartyUrn.TryParse(source.Urn.Urn, out _).ShouldBeTrue();
    }

    public static TheoryData<PartyExternalRefUrnWrapper> PartyExternalRefUrns()
    {
        return new TheoryData<PartyExternalRefUrnWrapper>([
            new TheoryDataRow<PartyExternalRefUrnWrapper>(PartyExternalRefUrn.PersonId.Create(PersonIdentifier.Parse("16876198847"))),
            new TheoryDataRow<PartyExternalRefUrnWrapper>(PartyExternalRefUrn.OrganizationId.Create(OrganizationIdentifier.Parse("910462474"))),
            new TheoryDataRow<PartyExternalRefUrnWrapper>(PartyExternalRefUrn.LegacySelfIdentifiedUsername.Create(UrnEncoded.Create("legacy_user_123"))),
            new TheoryDataRow<PartyExternalRefUrnWrapper>(PartyExternalRefUrn.SystemUserUuid.Create(Guid.Parse("3c2f50ed-0961-41c7-b2f3-53d78056b5dc"))),
            new TheoryDataRow<PartyExternalRefUrnWrapper>(PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create("test@example.com"))),
        ]);
    }

    public sealed class PartyExternalRefUrnWrapper
        : IXunitSerializable
    {
        private PartyExternalRefUrn _urn = null!;

        public required PartyExternalRefUrn Urn
        {
            get => _urn;
            init => _urn = value;
        }

        public override string ToString()
            => _urn.ToString();

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            var urn = info.GetValue<string>(nameof(Urn));
            _urn = PartyExternalRefUrn.Parse(urn);
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Urn), _urn.Urn, typeof(string));
        }

        public static implicit operator PartyExternalRefUrnWrapper(PartyExternalRefUrn urn)
            => new() { Urn = urn };
    }
}
