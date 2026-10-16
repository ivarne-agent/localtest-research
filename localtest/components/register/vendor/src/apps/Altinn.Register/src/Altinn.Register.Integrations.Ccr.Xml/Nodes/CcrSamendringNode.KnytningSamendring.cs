using System.Xml;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Integrations.Ccr.Xml;
using CommunityToolkit.Diagnostics;

internal abstract partial record CcrSamendringNode
{
    private abstract record KnytningSamendring
        : CcrSamendringNode
    {
        public static KnytningSamendring ParseNode(XmlReader reader, string? felttype, string? endringstype)
        {
            reader.AssertStartElement("samendringer");
            reader.AssertNotEmptyElement();
            reader.Read(); // Consume the <samendringer> start element

            string? knytningsOrgnr = null,
                knytningFratraadt = null;

            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "knytningOrganisasjonsnummer":
                        knytningsOrgnr = reader.ReadElementContentAsString();
                        break;

                    case "knytningFratraadt":
                        knytningFratraadt = reader.ReadElementContentAsString();
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

            if (string.IsNullOrEmpty(knytningsOrgnr))
            {
                ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required field 'knytningOrganisasjonsnummer' for role assignment in <samendringer> element.");
            }

            if (!OrganizationIdentifier.TryParse(knytningsOrgnr, null, out var organizationIdentifier))
            {
                ThrowHelper.ThrowInvalidDataException("XmlReader: Invalid format for required field 'knytningOrganisasjonsnummer' for role assignment in <samendringer> element.");
            }

            return (endringstype, knytningFratraadt) switch
            {
                (_, FratrådtValue.Ja) or (EndringstypeAttr.Utgått, _) => new ConnectionRemoval
                {
                    RoleCode = felttype,
                    OrganizationIdentifier = organizationIdentifier,
                },

                (EndringstypeAttr.Ny, _) => new ConnectionAddition
                {
                    RoleCode = felttype,
                    OrganizationIdentifier = organizationIdentifier,
                },

                _ => ThrowHelper.ThrowInvalidDataException<KnytningSamendring>($"XmlReader: Invalid combination of 'endringstype' and 'knytningFratraadt' values for personal role assignment in <samendringer> element. endringstype: '{endringstype}', knytningFratraadt: '{knytningFratraadt}'"),
            };
        }

        private sealed record ConnectionRemoval
            : KnytningSamendring
        {
            public required string RoleCode { get; init; }

            public required OrganizationIdentifier OrganizationIdentifier { get; init; }

            internal override void Apply(
                string orgform,
                IList<CcrRoleAssignment> additions,
                IList<CcrRoleAssignment> removals,
                IExternalRoleDefinitionLookup roleLookup,
                ILocationLookup locationLookup)
            {
                foreach (var roleDef in GetRoleDefinitionsForRemoval(RoleCode, roleLookup))
                {
                    removals.Add(CcrRoleAssignment.CreateConnection(
                        roleDef.Identifier,
                        OrganizationIdentifier));
                }
            }
        }

        private sealed record ConnectionAddition
            : KnytningSamendring
        {
            public required string RoleCode { get; init; }

            public required OrganizationIdentifier OrganizationIdentifier { get; init; }

            internal override void Apply(
                string orgform,
                IList<CcrRoleAssignment> additions,
                IList<CcrRoleAssignment> removals,
                IExternalRoleDefinitionLookup roleLookup,
                ILocationLookup locationLookup)
            {
                foreach (var roleDef in GetRoleDefinitionsForAddition(RoleCode, orgform, roleLookup))
                {
                    additions.Add(CcrRoleAssignment.CreateConnection(
                        roleDef.Identifier,
                        OrganizationIdentifier));
                }
            }
        }
    }
}
