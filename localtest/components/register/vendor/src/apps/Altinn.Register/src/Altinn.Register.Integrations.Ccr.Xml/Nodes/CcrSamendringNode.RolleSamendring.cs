using System.Xml;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Integrations.Ccr.Xml;
using CommunityToolkit.Diagnostics;

internal abstract partial record CcrSamendringNode
{
    private abstract record RolleSamendring
        : CcrSamendringNode
    {
        public static RolleSamendring ParseNode(XmlReader reader, string? felttype, string? endringstype)
        {
            reader.AssertStartElement("samendringer");
            reader.AssertNotEmptyElement();
            reader.Read(); // Consume the <samendringer> start element

            string? rolleFoedselsnr = null,
                rolleFratraadt = null,
                fornavn = null,
                mellomnavn = null,
                etternavn = null,
                postnr = null,
                adr1 = null,
                adr2 = null,
                adr3 = null,
                landkode = null,
                kommunenr = null,
                poststed = null;

            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "rolleFoedselsnr":
                        rolleFoedselsnr = reader.ReadElementContentAsString();
                        break;

                    case "rolleFratraadt":
                        rolleFratraadt = reader.ReadElementContentAsString();
                        break;

                    case "fornavn":
                        fornavn = reader.ReadElementContentAsString();
                        break;

                    case "mellomnavn":
                        mellomnavn = reader.ReadElementContentAsString();
                        break;

                    case "slektsnavn":
                        etternavn = reader.ReadElementContentAsString();
                        break;

                    case "postnr":
                        postnr = reader.ReadElementContentAsString();
                        break;

                    case "adresse1":
                        adr1 = reader.ReadElementContentAsString();
                        break;

                    case "adresse2":
                        adr2 = reader.ReadElementContentAsString();
                        break;

                    case "adresse3":
                        adr3 = reader.ReadElementContentAsString();
                        break;

                    case "adresseLandkode":
                        landkode = reader.ReadElementContentAsString();
                        break;

                    case "kommunenr":
                        kommunenr = reader.ReadElementContentAsString();
                        break;

                    case "poststed":
                        poststed = reader.ReadElementContentAsString();
                        break;

                    default:
                        // we ignore elements we don't expect
                        // todo: log warning about unexpected element
                        reader.Skip();
                        break;
                }
            }

            reader.ReadEndElement("samendringer"); // Consume the </samendringer> end element

            if (string.IsNullOrEmpty(felttype))
            {
                ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required field 'felttype' for role assignment in <samendringer> element.");
            }

            if (string.IsNullOrEmpty(rolleFoedselsnr))
            {
                ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required field 'rolleFoedselsnr' for role assignment in <samendringer> element.");
            }

            if (!PersonIdentifier.TryParse(rolleFoedselsnr, null, out var personIdentifier))
            {
                ThrowHelper.ThrowInvalidDataException("XmlReader: Invalid format for required field 'rolleFoedselsnr' for role assignment in <samendringer> element.");
            }

            return (endringstype, rolleFratraadt) switch
            {
                (_, FratrådtValue.Ja) or (EndringstypeAttr.Utgått, _) => new RoleRemoval
                {
                    RoleCode = felttype,
                    PersonIdentifier = personIdentifier,
                },

                (EndringstypeAttr.Ny, _) => new RoleAddition
                {
                    RoleCode = felttype,
                    PersonIdentifier = personIdentifier,
                    FirstName = fornavn,
                    MiddleName = mellomnavn,
                    LastName = etternavn,
                    AddressLine1 = adr1,
                    AddressLine2 = adr2,
                    AddressLine3 = adr3,
                    PostalCode = postnr,
                    MunicipalityCode = kommunenr,
                    City = poststed,
                    CountryCode = landkode,
                },

                _ => ThrowHelper.ThrowInvalidDataException<RolleSamendring>($"XmlReader: Invalid combination of 'endringstype' and 'rolleFratraadt' values for personal role assignment in <samendringer> element. endringstype: '{endringstype}', rolleFratraadt: '{rolleFratraadt}'"),
            };
        }

        private sealed record RoleRemoval
            : RolleSamendring
        {
            public required string RoleCode { get; init; }

            public required PersonIdentifier PersonIdentifier { get; init; }

            internal override void Apply(
                string orgform,
                IList<CcrRoleAssignment> additions,
                IList<CcrRoleAssignment> removals,
                IExternalRoleDefinitionLookup roleLookup,
                ILocationLookup locationLookup)
            {
                foreach (var roleDef in GetRoleDefinitionsForRemoval(RoleCode, roleLookup))
                {
                    removals.Add(CcrRoleAssignment.CreatePersonalRoleAssignment(
                        roleDef.Identifier,
                        PersonIdentifier,
                        personName: null,
                        mailingAddress: null));
                }
            }
        }

        private sealed record RoleAddition
            : RolleSamendring
        {
            public required string RoleCode { get; init; }

            public required PersonIdentifier PersonIdentifier { get; init; }

            public string? FirstName { get; init; }

            public string? MiddleName { get; init; }

            public string? LastName { get; init; }

            public string? AddressLine1 { get; init; }

            public string? AddressLine2 { get; init; }

            public string? AddressLine3 { get; init; }

            public string? PostalCode { get; init; }

            public string? MunicipalityCode { get; init; }

            public string? City { get; init; }

            public string? CountryCode { get; init; }

            internal override void Apply(
                string orgform,
                IList<CcrRoleAssignment> additions,
                IList<CcrRoleAssignment> removals,
                IExternalRoleDefinitionLookup roleLookup,
                ILocationLookup locationLookup)
            {
                var personName = PersonName.Create(FirstName, MiddleName, LastName);
                var mailingAddress = MapAddress(locationLookup);

                foreach (var roleDef in GetRoleDefinitionsForAddition(RoleCode, orgform, roleLookup))
                {
                    additions.Add(CcrRoleAssignment.CreatePersonalRoleAssignment(
                        roleDef.Identifier,
                        PersonIdentifier,
                        personName,
                        mailingAddress));
                }
            }

            private MailingAddressRecord? MapAddress(ILocationLookup locationLookup)
            {
                List<string> addressLines = [];
                if (!string.IsNullOrEmpty(AddressLine1))
                {
                    addressLines.Add(AddressLine1);
                }

                if (!string.IsNullOrEmpty(AddressLine2))
                {
                    addressLines.Add(AddressLine2);
                }

                if (!string.IsNullOrEmpty(AddressLine3))
                {
                    addressLines.Add(AddressLine3);
                }

                string? city = null;
                if (!string.IsNullOrEmpty(City))
                {
                    city = City;
                }
                else if (!string.IsNullOrEmpty(MunicipalityCode) && locationLookup.TryGetMunicipality(MunicipalityCode, out Municipality? municipality))
                {
                    city = municipality.Name;
                }

                if (addressLines.Count > 0 && PostalCode is not null && !addressLines[^1].StartsWith(PostalCode, StringComparison.Ordinal))
                {
                    addressLines.Add($"{PostalCode} {city}".Trim());
                }

                if (!string.IsNullOrEmpty(CountryCode)
                    && !string.Equals(CountryCode, "NO", StringComparison.OrdinalIgnoreCase)
                    && locationLookup.TryGetCountry(CountryCode, out Country? countryCode))
                {
                    addressLines.Add(countryCode.Name);
                }

                string? concatAddress = addressLines.Count == 0 ? null : string.Join(" ", addressLines);
                if (concatAddress is null && PostalCode is null && city is null)
                {
                    return null;
                }

                return new MailingAddressRecord
                {
                    Address = concatAddress,
                    PostalCode = PostalCode,
                    City = city,
                };
            }
        }
    }
}
