using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;
using Nerdbank.Streams;

namespace Altinn.Register.Integrations.Ccr.Xml;

/// <summary>
/// XML processor for CCR (Customer Contact Register) data. This class provides functionality to read and process CCR XML data,
/// yielding updates as CcrPartyUpdate instances. The processing is done in an asynchronous manner, allowing for efficient handling of large XML data streams.
/// </summary>
internal sealed class CcrXmlProcessor
    : ICcrXmlProcessor
{
    /// <inheritdoc/>
    public IEnumerable<CcrOrganizationUpdate> ProcessCcrXml(
        ReadOnlySequence<byte> xmlData,
        IExternalRoleDefinitionLookup roleDefs,
        ILocationLookup locations,
        CancellationToken cancellationToken = default)
    {
        using var reader = XmlReader.Create(xmlData.AsStream(), new XmlReaderSettings { IgnoreWhitespace = true });
        int enhet = 0;

        // 1. Read to root element <batchAjourholdXML>
        reader.ReadStartElement("batchAjourholdXML");

        // 2. Read header <head ... />
        _ = reader.ParseElement<CcrBatchHeader>();

        // 3. Read <enhet> nodes
        reader.MoveToContent();
        while (reader.NodeType == XmlNodeType.Element && reader.LocalName == "enhet")
        {
            enhet++;
            cancellationToken.ThrowIfCancellationRequested();

            {
                using var enhetReader = reader.ReadSubtree("enhet");
                yield return ReadEnhet(enhetReader, roleDefs, locations);
            }

            reader.Read();
            reader.MoveToContent();
        }

        // 4. Read trailer <trai ... />
        var trailer = reader.ParseElement<CcrBatchTrailer>();

        if (enhet == 0)
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: No <enhet> elements found in the document.");
        }

        if (enhet != trailer.AntallEnheter)
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: The number of <enhet> elements read ({enhet}) does not match the 'antallEnheter' attribute in the trailer ({trailer?.AntallEnheter}).");
        }
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static DateOnly? ParseDate(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            ThrowHelper.ThrowArgumentException(nameof(value), "Invalid date format. Expected a date in the format 'yyyyMMdd'.");
        }

        return parsed;
    }

    private static CcrOrganizationUpdate ReadEnhet(
        XmlReader reader,
        IExternalRoleDefinitionLookup roleDef,
        ILocationLookup locationLookup)
    {
        OrgBuilder org;

        reader.MoveToContent();
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "enhet")
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Expected <enhet> element at the beginning of the document.");
        }

        var organisasjonsnummer = reader.GetAttribute("organisasjonsnummer");
        if (string.IsNullOrEmpty(organisasjonsnummer))
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required attribute 'organisasjonsnummer' in <enhet> element.");
        }

        var organisasjonsform = reader.GetAttribute("organisasjonsform");
        var hovedsakstype = reader.GetAttribute("hovedsakstype");
        _ = reader.GetAttribute("undersakstype");
        var foersteOverfoering = reader.GetAttribute("foersteOverfoering");
        var datoSistEndret = ParseDate(reader.GetAttribute("datoSistEndret"));
        var isDeleted = hovedsakstype == "S";

        if (string.IsNullOrWhiteSpace(organisasjonsform))
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required attribute 'organisasjonsform' in <enhet> element.");
        }

        if (datoSistEndret is null)
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Missing or invalid required attribute 'datoSistEndret' in <enhet> element. Expected format is 'yyyyMMdd'.");
        }

        // If a first transfer, fields are explicitly set to null if missing from xml
        if (!string.IsNullOrEmpty(foersteOverfoering) && foersteOverfoering == "J")
        {
            org = new()
            {
                IsFirstRegistration = true,
                OrganizationIdentifier = OrganizationIdentifier.Parse(organisasjonsnummer),
                UnitType = organisasjonsform,
                UnitStatus = hovedsakstype,
                DatoSistEndret = datoSistEndret.Value,
                DisplayName = FieldValue.Null,
                BusinessAddress = FieldValue.Null,
                MailingAddress = FieldValue.Null,
                FaxNumber = FieldValue.Null,
                InternetAddress = FieldValue.Null,
                MobileNumber = FieldValue.Null,
                TelephoneNumber = FieldValue.Null,
                EmailAddress = FieldValue.Null,

                DeletedAt = isDeleted ? datoSistEndret : null,
                IsDeleted = isDeleted,
            };
        }

        // On updates we set fields to Unset, and leave them as is in the Register db
        else
        {
            org = new()
            {
                IsFirstRegistration = false,
                OrganizationIdentifier = OrganizationIdentifier.Parse(organisasjonsnummer),
                UnitType = organisasjonsform,
                UnitStatus = hovedsakstype,

                DatoSistEndret = datoSistEndret.Value,
                DisplayName = FieldValue.Unset,
                BusinessAddress = FieldValue.Unset,
                MailingAddress = FieldValue.Unset,
                FaxNumber = FieldValue.Unset,
                InternetAddress = FieldValue.Unset,
                MobileNumber = FieldValue.Unset,
                TelephoneNumber = FieldValue.Unset,
                EmailAddress = FieldValue.Unset,

                DeletedAt = isDeleted ? datoSistEndret : null,
                IsDeleted = isDeleted,
            };
        }

        reader.MoveToContent();
        var isEmpty = reader.IsEmptyElement;
        reader.ReadStartElement("enhet");
        if (!isEmpty)
        {
            reader.MoveToContent();

            while (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "infotype")
                {
                    using var subReader = reader.ReadSubtree("infotype");
                    ReadInfoType(subReader, org, locationLookup);
                }
                else if (reader.LocalName == "samendringer")
                {
                    org.RoleUpdates ??= new();
                    reader.ParseElement<CcrSamendringNode>()
                        .Apply(
                            orgform: organisasjonsform,
                            additions: org.RoleUpdates.RoleAssignments,
                            removals: org.RoleUpdates.RemoveRoleAssignments,
                            roleLookup: roleDef,
                            locationLookup: locationLookup);

                    continue; // we need to not call the reader.Read() below, as ParseElement already does this correctly
                }
                else if (reader.LocalName == "status")
                {
                    //// ReadStatus(reader, org);
                    reader.Skip();
                    reader.MoveToContent();
                    continue;
                }
                else if (reader.LocalName == "samendringUtgaar")
                {
                    org.RoleUpdates ??= new();
                    using var subReader = reader.ReadSubtree("samendringUtgaar");
                    ReadSamu(
                        subReader,
                        org.RoleUpdates.BulkRemoveRoleAssignments,
                        roleDef);
                }
                else
                {
                    ThrowHelper.ThrowInvalidDataException("XmlReader: unknown element <" + reader.LocalName + "> in <enhet> element.");
                }

                reader.Read(); // move the outer reader past the subreader
                reader.MoveToContent();
            }

            reader.ReadEndElement(); // </enhet>
        }

        return org.Build();
    }

    private static void ReadInfoType(XmlReader reader, OrgBuilder org, ILocationLookup locationLookup)
    {
        reader.MoveToContent(); // Move to the <infotype> element

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "infotype")
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Expected <infotype> element at the beginning of the document.");
        }

        var felttype = reader.GetAttribute("felttype") ?? string.Empty;
        var endringstype = reader.GetAttribute("endringstype") ?? string.Empty;

        switch (felttype, endringstype)
        {
            case ("EPOS", "N"):
                {
                    reader.ReadStartElement("infotype");
                    if (TryReadOpplysning(reader, out string? epost))
                    {
                        org.EmailAddress = !string.IsNullOrEmpty(epost) ? epost : FieldValue.Null;
                    }

                    reader.ReadEndElement();

                    break;
                }

            case ("EPOS", "U"):
                {
                    org.EmailAddress = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("FADR", "N"):
                {
                    reader.ReadStartElement("infotype");
                    var fadrFields = ReadChildFields(reader);
                    org.BusinessAddress = ReadMailingAddress(fadrFields, locationLookup);
                    reader.ReadEndElement();
                    break;
                }

            case ("FADR", "U"):
                {
                    org.BusinessAddress = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("PADR", "N"):
                {
                    reader.ReadStartElement("infotype");
                    var fadrFields = ReadChildFields(reader);
                    org.MailingAddress = ReadMailingAddress(fadrFields, locationLookup);
                    reader.ReadEndElement();
                    break;
                }

            case ("PADR", "U"):
                {
                    org.MailingAddress = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("IADR", "N"):
                {
                    reader.ReadStartElement("infotype");
                    if (TryReadOpplysning(reader, out string? iadr))
                    {
                        org.InternetAddress = !string.IsNullOrEmpty(iadr) ? iadr : FieldValue.Null;
                    }

                    reader.ReadEndElement();
                    break;
                }

            case ("IADR", "U"):
                {
                    org.InternetAddress = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("TFON", "N"):
                {
                    reader.ReadStartElement("infotype");
                    if (TryReadOpplysning(reader, out string? tlf))
                    {
                        org.TelephoneNumber = !string.IsNullOrEmpty(tlf) ? tlf : FieldValue.Null;
                    }

                    reader.ReadEndElement();
                    break;
                }

            case ("TFON", "U"):
                {
                    org.TelephoneNumber = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("MTLF", "N"):
                {
                    reader.ReadStartElement("infotype");
                    if (TryReadOpplysning(reader, out string? mtlf))
                    {
                        org.MobileNumber = !string.IsNullOrEmpty(mtlf) ? mtlf : FieldValue.Null;
                    }

                    reader.ReadEndElement();
                    break;
                }

            case ("MTLF", "U"):
                {
                    org.MobileNumber = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("TFAX", "N"):
                {
                    reader.ReadStartElement("infotype");
                    if (TryReadOpplysning(reader, out string? fax))
                    {
                        org.FaxNumber = !string.IsNullOrEmpty(fax) ? fax : FieldValue.Null;
                    }

                    reader.ReadEndElement();
                    break;
                }

            case ("TFAX", "U"):
                {
                    org.FaxNumber = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            case ("NAVN", "N"):
                {
                    // We assume we dont get a redigertNavn for a full insert
                    reader.ReadStartElement("infotype");
                    var navnFields = ReadChildFields(reader);
                    org.DisplayName = ReadAndConcatName(navnFields);
                    reader.ReadEndElement();
                    break;
                }

            case ("NAVN", "U"):
                {
                    org.DisplayName = FieldValue.Null;
                    reader.Skip();
                    break;
                }

            // The following felttyper are currently not mapped to any fields in the organization record,
            // but we want to allow them without throwing an exception, as they may be present in the XML data and we want to be able to process it without errors.
            // If we later decide to map any of these felttyper to fields in the organization record, we can simply add the necessary code to do so.
            case ("ARBG", _):
            case ("BDAT", _):
            case ("BFOR", _):
            case ("EDAT", _):
            case ("EVDT", _):
            case ("FMAK", _):
            case ("FMAP", _):
            case ("FMKA", _):
            case ("FMKL", _):
            case ("FMUU", _):
            case ("FMVA", _):
            case ("FSTR", _):
            case ("FVRP", _):
            case ("FVRR", _):
            case ("GRDT", _):
            case ("GRUN", _):
            case ("ISEK", _):
            case ("KAPI", _):
            case ("KATG", _):
            case ("KJRP", _):
            case ("KLAN", _):
            case ("KTO", _):
            case ("MÅL", _):
            case ("MPVT", _):
            case ("NACE", _):
            case ("NDAT", _):
            case ("NYFR", _):
            case ("PAAT", _):
            case ("PLFR", _):
            case ("R-FR", _):
            case ("R-FV", _):
            case ("R-MV", _):
            case ("R-SR", _):
            case ("R-FS", _):
            case ("RSKP", _):
            case ("RVFG", _):
            case ("SLFR", _):
            case ("SN25", _):
            case ("STID", _):
            case ("TKN", _):
            case ("TRAK", _):
            case ("UENO", _):
            case ("ULOV", _):
            case ("UREG", _):
            case ("UVNO", _):
            case ("VEDT", _):
            case ("VFOR", _):
            case ("FORM", _):
            case ("naeringskode", _):
            case ("registrertHjemlandetsRegister", _):
            case ("underlagtHjemlandetsLovgivning", _):
            case ("paategning", _):
                reader.Skip();
                break;

            default:
                {
                    ThrowHelper.ThrowInvalidDataException("XmlReader: unknown felttype '" + felttype + "' in <infotype> element.");
                    break;
                }
        }
    }

    private static FieldValue<string> ReadAndConcatName(Dictionary<string, string> navnFields)
    {
        StringBuilder sb = new();
        string? value;

        if (navnFields.TryGetValue("navn1", out value) && !string.IsNullOrWhiteSpace(value))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value.AsSpan().Trim());
        }

        if (navnFields.TryGetValue("navn2", out value) && !string.IsNullOrWhiteSpace(value))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value.AsSpan().Trim());
        }

        if (navnFields.TryGetValue("navn3", out value) && !string.IsNullOrWhiteSpace(value))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value.AsSpan().Trim());
        }

        if (navnFields.TryGetValue("navn4", out value) && !string.IsNullOrWhiteSpace(value))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value.AsSpan().Trim());
        }

        if (navnFields.TryGetValue("navn5", out value) && !string.IsNullOrWhiteSpace(value))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(value.AsSpan().Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Used for organisations
    /// </summary>
    /// <param name="addressFields">The fields from the xml</param>
    /// <param name="locationLookup">The location lookup service</param>
    /// <returns>A <see cref="MailingAddressRecord"/>, if it would contain values.</returns>
    private static MailingAddressRecord? ReadMailingAddress(Dictionary<string, string> addressFields, ILocationLookup locationLookup)
    {
        string? line1 = addressFields.TryGetValue("adresse1", out var l1) ? l1 : null;
        string? line2 = addressFields.TryGetValue("adresse2", out var l2) ? l2 : null;
        string? line3 = addressFields.TryGetValue("adresse3", out var l3) ? l3 : null;
        string? landkode = addressFields.TryGetValue("landkode", out var lk) ? lk : null;
        string? postkode = addressFields.TryGetValue("postnr", out var pk) ? pk : null;
        string? poststedIUtland = addressFields.TryGetValue("poststed", out var ps) ? ps : null;
        string? kommunenr = addressFields.TryGetValue("kommunenr", out var kn) ? kn : null;

        return MapAddress(
            adr1: line1,
            adr2: line2,
            adr3: line3,
            land: landkode,
            postnr: postkode,
            poststedIUtland: poststedIUtland,
            kommuneNummer: kommunenr,
            locationLookup: locationLookup);
    }

    private static void ReadSamu(
        XmlReader reader,
        ImmutableArray<CcrRoleAssignment>.Builder bulkRemovals,
        IExternalRoleDefinitionLookup roleDef)
    {
        reader.MoveToContent(); // Move to the <samendringUtgaar> element

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "samendringUtgaar")
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Expected <samendringUtgaar> element at the beginning of the document.");
        }

        reader.ReadStartElement("samendringUtgaar");
        var samuFields = ReadChildFields(reader);
        samuFields.TryGetValue("samendringstype", out var samuType);

        if (string.IsNullOrEmpty(samuType))
        {
            ThrowHelper.ThrowInvalidDataException("XmlReader: Missing required field 'samendringstype' in <samendringUtgaar> element.");
        }

        switch (samuType)
        {
            case "STYR":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("LEDE", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("NEST", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("MEDL", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("VARA", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("OBS", roleDef)));
                    break;
                }

            case "DELT":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("DTSO", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("DTPR", roleDef)));
                    break;
                }

            case "SIGN":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("SIGN", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("SIFE", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("SIHV", roleDef)));
                    break;
                }

            case "PROK":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("PROK", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("POHV", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("POFE", roleDef)));
                    break;
                }

            case "KONT":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("SREVA", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("KOMK", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("KNUF", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("KEMN", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("KONT", roleDef)));
                    break;
                }

            case "REVI":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("REVI", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("READ", roleDef)));
                    break;
                }

            case "REGN":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("RFAD", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("REGN", roleDef)));
                    break;
                }

            case "HOST":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("HLED", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("HMDL", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("HNST", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("HVAR", roleDef)));
                    break;
                }

            case "ESGR":
                {
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("ESGR", roleDef)));
                    bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode("ETDL", roleDef)));
                    break;
                }

            default:
                {
                    if (VerifyRoleCode(samuType))
                    {
                        bulkRemovals.Add(CcrRoleAssignment.CreateBulkRoleAssignmentRemoval(ConvertToAltinnRoleCode(samuType, roleDef)));
                    }
                    else
                    {
                        ThrowHelper.ThrowInvalidDataException($"XmlReader: unknown samendringstype '{samuType}' in <samendringUtgaar> element.");
                    }

                    break;
                }
        }

        reader.ReadEndElement();
    }

    private static bool VerifyRoleCode(string samuCode)
    {
        if (samuCode == "BEDR" || samuCode == "AAFY" || samuCode == "BEST" || samuCode == "BOBE" || samuCode == "DAGL"
                || samuCode == "KOMP" || samuCode == "REPR" || samuCode == "FFØR" || samuCode == "INNH" || samuCode == "ADOS"
                || samuCode == "AVKL" || samuCode == "EIKM" || samuCode == "FGRP" || samuCode == "HFOR" || samuCode == "HLSE"
                || samuCode == "KDAT" || samuCode == "KDEB" || samuCode == "KENK" || samuCode == "KGRL" || samuCode == "KIRK"
                || samuCode == "KMOR" || samuCode == "KOMP" || samuCode == "KTRF" || samuCode == "OPMV" || samuCode == "ORGL"
                || samuCode == "STFT" || samuCode == "UTBG" || samuCode == "VIFE")
        {
            return true;
        }

        return false;
    }

    // This method can be used to convert CCR role codes to Altinn role codes if they differ.
    // For example, if CCR uses "LEDER" and Altinn uses "LEDE", you could implement the mapping here.
    // For now, we assume they are the same.
    // In this case the feltype corresponds to a CCR role code, and we want to validate to the corresponding Altinn Role Code.
    private static string ConvertToAltinnRoleCode(
        string ccrRoleCode,
        IExternalRoleDefinitionLookup roleDef)
    {
        if (!roleDef.TryGetRoleDefinitionByRoleCode(ccrRoleCode, out var roleDefinition))
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Unknown role code '{ccrRoleCode}' in <samendringer> element. No corresponding role definition found.");
        }

        if (roleDefinition.Source != ExternalRoleSource.CentralCoordinatingRegister)
        {
            ThrowHelper.ThrowInvalidDataException($"XmlReader: Role code '{ccrRoleCode}' in <samendringer> element does not correspond to a role definition with source '{ExternalRoleSource.CentralCoordinatingRegister}'. Found role definition {roleDefinition.Identifier} with source '{roleDefinition.Source}'.");
        }

        return roleDefinition.Identifier;
    }

    private static MailingAddressRecord? MapAddress(
        string? adr1,
        string? adr2,
        string? adr3,
        string? land,
        string? postnr,
        string? poststedIUtland,
        string? kommuneNummer,
        ILocationLookup locationLookup)
    {
        List<string> addressLines = [];
        if (!string.IsNullOrEmpty(adr1))
        {
            addressLines.Add(adr1);
        }

        if (!string.IsNullOrEmpty(adr2))
        {
            addressLines.Add(adr2);
        }

        if (!string.IsNullOrEmpty(adr3))
        {
            addressLines.Add(adr3);
        }

        string? city = null;
        if (!string.IsNullOrEmpty(poststedIUtland))
        {
            city = poststedIUtland;
        }
        else if (!string.IsNullOrEmpty(kommuneNummer) && locationLookup.TryGetMunicipality(kommuneNummer, out Municipality? municipality))
        {
            city = municipality.Name;
        }

        if (addressLines.Count > 0 && postnr is not null && !addressLines[^1].StartsWith(postnr, StringComparison.Ordinal))
        {
            addressLines.Add($"{postnr} {city}".Trim());
        }

        if (!string.IsNullOrEmpty(land)
            && !string.Equals(land, "NO", StringComparison.OrdinalIgnoreCase)
            && locationLookup.TryGetCountry(land, out Country? countryCode))
        {
            addressLines.Add(countryCode.Name);
        }

        string? concatAddress = addressLines.Count == 0 ? null : string.Join(" ", addressLines);
        if (concatAddress is null && postnr is null && city is null)
        {
            return null;
        }

        return new MailingAddressRecord
        {
            Address = concatAddress,
            PostalCode = postnr,
            City = city,
        };
    }

    private static bool TryReadOpplysning(XmlReader reader, out string? value)
    {
        reader.MoveToContent();

        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "opplysning")
        {
            value = default;
            return false;
        }

        value = Normalize(reader.ReadElementContentAsString());
        return true;
    }

    private static Dictionary<string, string> ReadChildFields(XmlReader reader)
    {
        var fields = new Dictionary<string, string>();
        reader.MoveToContent();

        while (reader.NodeType == XmlNodeType.Element)
        {
            var name = reader.LocalName;
            var value = reader.ReadElementContentAsString();
            fields[name] = value;
            reader.MoveToContent();
        }

        return fields;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class OrgBuilder
    {
        public required OrganizationIdentifier OrganizationIdentifier { get; init; }

        public required bool IsFirstRegistration { get; init; }

        public required FieldValue<string> DisplayName { get; set; }

        public required bool IsDeleted { get; init; }

        public required DateOnly? DeletedAt { get; init; }

        public required string UnitType { get; init; }

        public required FieldValue<string> UnitStatus { get; init; }

        public required DateOnly DatoSistEndret { get; init; }

        public required FieldValue<string> EmailAddress { get; set; }

        public required FieldValue<string> InternetAddress { get; set; }

        public required FieldValue<string> TelephoneNumber { get; set; }

        public required FieldValue<string> MobileNumber { get; set; }

        public required FieldValue<string> FaxNumber { get; set; }

        public required FieldValue<MailingAddressRecord> MailingAddress { get; set; }

        public required FieldValue<MailingAddressRecord> BusinessAddress { get; set; }

        public RoleUpdatesBuilder? RoleUpdates { get; set; }

        public CcrOrganizationUpdate Build() => new()
        {
            OrganizationIdentifier = OrganizationIdentifier,
            IsFirstRegistration = IsFirstRegistration,
            DisplayName = DisplayName,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt,
            UnitType = UnitType,
            UnitStatus = UnitStatus,
            DatoSistEndret = DatoSistEndret,
            EmailAddress = EmailAddress,
            InternetAddress = InternetAddress,
            TelephoneNumber = TelephoneNumber,
            MobileNumber = MobileNumber,
            FaxNumber = FaxNumber,
            MailingAddress = MailingAddress,
            BusinessAddress = BusinessAddress,
            RoleUpdates = RoleUpdates?.Build(),
        };
    }

    private sealed class RoleUpdatesBuilder
    {
        public ImmutableArray<CcrRoleAssignment>.Builder RoleAssignments { get; }
            = ImmutableArray.CreateBuilder<CcrRoleAssignment>();

        public ImmutableArray<CcrRoleAssignment>.Builder RemoveRoleAssignments { get; }
            = ImmutableArray.CreateBuilder<CcrRoleAssignment>();

        public ImmutableArray<CcrRoleAssignment>.Builder BulkRemoveRoleAssignments { get; }
            = ImmutableArray.CreateBuilder<CcrRoleAssignment>();

        public CcrRoleAssignmentsUpdate Build() => new()
        {
            RoleAssignments = RoleAssignments.DrainToImmutableValueArray(),
            RemoveRoleAssignments = RemoveRoleAssignments.DrainToImmutableValueArray(),
            BulkRemoveRoleAssignments = BulkRemoveRoleAssignments.DrainToImmutableValueArray(),
        };
    }
}
