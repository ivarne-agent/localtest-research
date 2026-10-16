using System.Xml;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Integrations.Ccr.Xml;
using CommunityToolkit.Diagnostics;

internal abstract partial record CcrSamendringNode
    : IXmlParsable<CcrSamendringNode>
{
    /// <inheritdoc/>
    public static CcrSamendringNode ParseNode(XmlReader reader)
    {
        reader.AssertStartElement("samendringer");

        string? felttype = null, endringstype = null, type = null, data = null;
        while (reader.MoveToNextAttribute())
        {
            switch (reader.LocalName)
            {
                case "felttype":
                    felttype = reader.Value;
                    break;

                case "endringstype":
                    endringstype = reader.Value;
                    break;

                case "type":
                    type = reader.Value;
                    break;

                case "data":
                    data = reader.Value;
                    break;

                default:
                    // we ignore attributes we don't expect
                    // todo: log warning about unexpected attribute
                    break;
            }
        }

        reader.MoveToElement(); // move back to the <samendringer> element
        switch ((type, data))
        {
            case (TypeAttr.Rolle, DataAttr.Data):
                return RolleSamendring.ParseNode(reader, felttype, endringstype);

            case (TypeAttr.Knytning, DataAttr.Data):
                return KnytningSamendring.ParseNode(reader, felttype, endringstype);

            case (TypeAttr.FritekstRolle, _) when endringstype is EndringstypeAttr.Ny or EndringstypeAttr.Utgått:
            case (TypeAttr.Rolle, DataAttr.Fritekst) when endringstype is EndringstypeAttr.Ny or EndringstypeAttr.Utgått:
            case (TypeAttr.Knytning, DataAttr.Fritekst) when endringstype is EndringstypeAttr.Ny or EndringstypeAttr.Utgått:
                // Free-text samendring/role/connection entries are supplementary descriptive
                // text only - the structured ("R", "D") / ("K", "D") siblings (when present)
                // carry the actual role-assignment data. We currently have no mapping for any
                // samendringsfritekst variant, whether new, update or delete.
                return IgnoredSamendring.ParseNode(reader);
        }

        return ThrowHelper.ThrowArgumentException<CcrSamendringNode>($"XmlReader: unknown samendring '{felttype}' type '{type}' (data = '{data}') in <samendringer> element.");
    }

    /// <summary>
    /// Applies the samendring node to the org state that's being built.
    /// </summary>
    /// <param name="orgform">The organization form.</param>
    /// <param name="additions">The list of role assignments to be added.</param>
    /// <param name="removals">The list of role assignments to be removed.</param>
    /// <param name="roleLookup">The role definition lookup.</param>
    /// <param name="locationLookup">The location lookup.</param>
    internal abstract void Apply(
        string orgform,
        IList<CcrRoleAssignment> additions,
        IList<CcrRoleAssignment> removals,
        IExternalRoleDefinitionLookup roleLookup,
        ILocationLookup locationLookup);

    private static string ConvertToAltinnRoleCode(
        string ccrRoleCode,
        IExternalRoleDefinitionLookup roleLookup)
    {
        if (!roleLookup.TryGetRoleDefinitionByRoleCode(ccrRoleCode, out var roleDefinition))
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Unknown role code '{ccrRoleCode}' in <samendringer> element. No corresponding role definition found.");
        }

        if (roleDefinition.Source != ExternalRoleSource.CentralCoordinatingRegister)
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Role code '{ccrRoleCode}' in <samendringer> element does not correspond to a role definition with source '{ExternalRoleSource.CentralCoordinatingRegister}'. Found role definition {roleDefinition.Identifier} with source '{roleDefinition.Source}'.");
        }

        return roleDefinition.Identifier;
    }

    private static IEnumerable<ExternalRoleDefinition> GetRoleDefinitionsForRemoval(
        string ccrRoleCode,
        IExternalRoleDefinitionLookup roleLookup)
    {
        if (!roleLookup.TryGetRoleDefinitionByRoleCode(ccrRoleCode, out var roleDefinition))
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Unknown role code '{ccrRoleCode}' in <samendringer> element. No corresponding role definition found.");
        }

        if (roleDefinition.Source != ExternalRoleSource.CentralCoordinatingRegister)
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Role code '{ccrRoleCode}' in <samendringer> element does not correspond to a role definition with source '{ExternalRoleSource.CentralCoordinatingRegister}'. Found role definition {roleDefinition.Identifier} with source '{roleDefinition.Source}'.");
        }

        yield return roleDefinition;

        if (ccrRoleCode == "KONT")
        {
            // For the "KONT" (kontaktperson) role, we also need to remove the derivative roles
            yield return GetWellKnownRoleDefinition("SREVA", roleLookup);
            yield return GetWellKnownRoleDefinition("KOMK", roleLookup);
            yield return GetWellKnownRoleDefinition("KNUF", roleLookup);
            yield return GetWellKnownRoleDefinition("KEMN", roleLookup);
        }
    }

    private static IEnumerable<ExternalRoleDefinition> GetRoleDefinitionsForAddition(
        string ccrRoleCode,
        string orgform,
        IExternalRoleDefinitionLookup roleLookup)
    {
        if (!roleLookup.TryGetRoleDefinitionByRoleCode(ccrRoleCode, out var roleDefinition))
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Unknown role code '{ccrRoleCode}' in <samendringer> element. No corresponding role definition found.");
        }

        if (roleDefinition.Source != ExternalRoleSource.CentralCoordinatingRegister)
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Role code '{ccrRoleCode}' in <samendringer> element does not correspond to a role definition with source '{ExternalRoleSource.CentralCoordinatingRegister}'. Found role definition {roleDefinition.Identifier} with source '{roleDefinition.Source}'.");
        }

        yield return roleDefinition;

        if (ccrRoleCode == "KONT")
        {
            // For the "KONT" (kontaktperson) role, add derived role based on the organization form
            switch (orgform)
            {
                case "KOMM":
                case "FYLK":
                    yield return GetWellKnownRoleDefinition("KOMK", roleLookup);
                    break;

                case "REV":
                    yield return GetWellKnownRoleDefinition("SREVA", roleLookup);
                    break;

                case "NUF":
                    yield return GetWellKnownRoleDefinition("KNUF", roleLookup);
                    break;

                case "ADOS":
                    yield return GetWellKnownRoleDefinition("KEMN", roleLookup);
                    break;
            }
        }
    }

    private static ExternalRoleDefinition GetWellKnownRoleDefinition(
        string ccrRoleCode,
        IExternalRoleDefinitionLookup roleLookup)
    {
        if (!roleLookup.TryGetRoleDefinitionByRoleCode(ccrRoleCode, out var roleDefinition))
        {
            ThrowHelper.ThrowInvalidOperationException($"Well known role-code '{ccrRoleCode}' not found.");
        }

        return roleDefinition;
    }

    private static class TypeAttr
    {
        public const string Rolle = "R";
        public const string Knytning = "K";
        public const string FritekstRolle = "S";
    }

    private static class DataAttr
    {
        public const string Data = "D";
        public const string Fritekst = "T";
    }

    private static class EndringstypeAttr
    {
        public const string Ny = "N";
        public const string Utgått = "U";
        public const string Oppdatert = "O";
    }

    private static class FratrådtValue
    {
        public const string Ja = "F";
    }
}
