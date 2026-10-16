using System.Globalization;
using System.Xml;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Processor for CCR flat files, produces XML documents for each organization in the file.
/// </summary>
internal sealed partial class CcrFlatFileProcessor
{
    private sealed class CcrXmlWriter
        : IDisposable
    {
        private readonly XmlWriter _xmlWriter;

        public CcrXmlWriter(Stream output)
        {
            _xmlWriter = XmlWriter.Create(output);
            _xmlWriter.WriteStartDocument();

            _xmlWriter.WriteStartElement("batchAjourholdXML");

            // xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            _xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

            // xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd"
            _xmlWriter.WriteAttributeString("xsi", "noNamespaceSchemaLocation", null, "batchAjourholdXML_versjon2_1.xsd");
        }

        public void Dispose()
        {
            _xmlWriter.WriteEndElement();
            _xmlWriter.WriteEndDocument();
            _xmlWriter.Flush();
            _xmlWriter.Dispose();
        }

        public void WriteHead(HeaderRecord header)
        {
            _xmlWriter.WriteStartElement("head");
            _xmlWriter.WriteAttributeString("avsender", header.Avsender);
            _xmlWriter.WriteAttributeString("dato", header.Dato);
            _xmlWriter.WriteAttributeString("kjoerenr", header.KjoereNr);
            _xmlWriter.WriteAttributeString("mottaker", header.Mottaker);
            _xmlWriter.WriteAttributeString("type", header.Type);
            _xmlWriter.WriteEndElement();
        }

        public void WriteFooter(HeaderRecord header, ushort numberOfOrganizations)
        {
            _xmlWriter.WriteStartElement("trai");
            _xmlWriter.WriteAttributeString("antallEnheter", numberOfOrganizations.ToString(CultureInfo.InvariantCulture));
            _xmlWriter.WriteAttributeString("avsender", header.Avsender);
            _xmlWriter.WriteEndElement();
        }

        public void WriteOrganizationStart(OrganizationRecord org)
        {
            _xmlWriter.WriteStartElement("enhet");
            _xmlWriter.WriteAttributeString("organisasjonsnummer", org.OrgNr.ToString());
            _xmlWriter.WriteAttributeString("organisasjonsform", org.OrgForm);
            _xmlWriter.WriteAttributeString("hovedsakstype", org.HovedsaksType);
            _xmlWriter.WriteAttributeString("undersakstype", org.UndersaksType);
            _xmlWriter.WriteAttributeString("foersteOverfoering", org.FoersteOverfoering ?? "N"); // TODO: this default of "N" here is probably not needed
            _xmlWriter.WriteAttributeString("datoFoedt", org.DatoFoedt);
            _xmlWriter.WriteAttributeString("datoSistEndret", org.DatoSistEndret);
        }

        public void WriteOrganizationEnd()
        {
            _xmlWriter.WriteFullEndElement();
        }

        public void WriteOptionalTextElementNode(string name, ReadOnlySpan<char> value)
        {
            if (!value.IsEmpty)
            {
                _xmlWriter.WriteStartElement(name);
                _xmlWriter.WriteString(value.ToString());
                _xmlWriter.WriteEndElement();
            }
        }

        private void WriteInfoElementStart(ReadOnlySpan<char> type, ReadOnlySpan<char> mode)
        {
            _xmlWriter.WriteStartElement("infotype");
            _xmlWriter.WriteAttributeString("felttype", type.ToString());
            _xmlWriter.WriteAttributeString("endringstype", mode.ToString());
        }

        private void WriteInfoElementValue(string name, ReadOnlySpan<char> value)
        {
            WriteOptionalTextElementNode(name, value);
        }

        private void WriteInfoElementEnd()
        {
            _xmlWriter.WriteEndElement();
        }

        public void WriteSimpleInfoType(ReadOnlySpan<char> type, ReadOnlySpan<char> mode, ReadOnlySpan<char> value)
        {
            WriteInfoElementStart(type, mode);
            WriteInfoElementValue("opplysning", value);
            WriteInfoElementEnd();
        }

        public void WriteEpostAddresse(ReadOnlySpan<char> status, ReadOnlySpan<char> epostAdresse)
        {
            WriteSimpleInfoType("EPOS", status, epostAdresse);
        }

        public void WriteAddress(
            ReadOnlySpan<char> type,
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> postNummer,
            ReadOnlySpan<char> poststedIUtland,
            ReadOnlySpan<char> kommuneNummer,
            ReadOnlySpan<char> landKode,
            ReadOnlySpan<char> adresse1,
            ReadOnlySpan<char> adresse2,
            ReadOnlySpan<char> adresse3)
        {
            WriteInfoElementStart(type, status);
            WriteInfoElementValue("postnr", postNummer);
            WriteInfoElementValue("landkode", landKode);
            WriteInfoElementValue("kommunenr", kommuneNummer);
            WriteInfoElementValue("poststed", poststedIUtland);
            WriteInfoElementValue("adresse1", adresse1);
            WriteInfoElementValue("adresse2", adresse2);
            WriteInfoElementValue("adresse3", adresse3);
            WriteInfoElementEnd();
        }

        public void WriteInternettAdresse(ReadOnlySpan<char> status, ReadOnlySpan<char> internetAddress)
        {
            WriteSimpleInfoType("IADR", status, internetAddress);
        }

        public void WriteMobiltelefon(ReadOnlySpan<char> status, ReadOnlySpan<char> mobil)
        {
            WriteSimpleInfoType("MTLF", status, mobil);
        }

        public void WriteName(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> navn1,
            ReadOnlySpan<char> navn2,
            ReadOnlySpan<char> navn3,
            ReadOnlySpan<char> navn4,
            ReadOnlySpan<char> navn5,
            ReadOnlySpan<char> redigertNavn)
        {
            WriteInfoElementStart("NAVN", status);
            WriteInfoElementValue("navn1", navn1);
            WriteInfoElementValue("navn2", navn2);
            WriteInfoElementValue("navn3", navn3);
            WriteInfoElementValue("navn4", navn4);
            WriteInfoElementValue("navn5", navn5);
            WriteInfoElementValue("rednavn", redigertNavn);
            WriteInfoElementEnd();
        }

        public void WriteTelefax(ReadOnlySpan<char> status, ReadOnlySpan<char> telefax)
        {
            WriteSimpleInfoType("TFAX", status, telefax);
        }

        public void WriteTelefon(ReadOnlySpan<char> status, ReadOnlySpan<char> telefon)
        {
            WriteSimpleInfoType("TFON", status, telefon);
        }

        public void WriteNaeringskode(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> naeringskode,
            ReadOnlySpan<char> gyldighetsdato,
            ReadOnlySpan<char> hjelpeenhet)
        {
            WriteInfoElementStart("naeringskode", status);
            WriteInfoElementValue("naeringskode", naeringskode);
            WriteInfoElementValue("gyldighetsdato", gyldighetsdato);
            WriteInfoElementValue("hjelpeenhet", hjelpeenhet);
            WriteInfoElementEnd();
        }

        public void WritePaategning(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> infotype,
            ReadOnlySpan<char> register,
            ReadOnlySpan<char> text1,
            ReadOnlySpan<char> text2,
            ReadOnlySpan<char> text3)
        {
            WriteInfoElementStart("paategning", status);
            WriteInfoElementValue("infotype", infotype);
            WriteInfoElementValue("register", register);
            WriteInfoElementValue("tekstlinje", text1);
            WriteInfoElementValue("tekstlinje", text2);
            WriteInfoElementValue("tekstlinje", text3);
            WriteInfoElementEnd();
        }

        public void WriteUlov(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> landKode,
            ReadOnlySpan<char> orgForm,
            ReadOnlySpan<char> descFo,
            ReadOnlySpan<char> descNo)
        {
            WriteInfoElementStart("underlagtHjemlandetsLovgivning", status);
            WriteInfoElementValue("foretaksform", orgForm);
            WriteInfoElementValue("beskrivelseForetaksformHjemland", descFo);
            WriteInfoElementValue("beskrivelseForetaksformNorsk", descNo);
            WriteInfoElementValue("landkode", landKode);
            WriteInfoElementEnd();
        }

        public void WriteUreg(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> regNr,
            ReadOnlySpan<char> regName1,
            ReadOnlySpan<char> regName2,
            ReadOnlySpan<char> regName3,
            ReadOnlySpan<char> landKode,
            ReadOnlySpan<char> postalArea,
            ReadOnlySpan<char> mailAdr1,
            ReadOnlySpan<char> mailAdr2,
            ReadOnlySpan<char> mailAdr3)
        {
            WriteInfoElementStart("registrertHjemlandetsRegister", status);
            WriteInfoElementValue("registernr", regNr);
            WriteInfoElementValue("registerNavn1", regName1);
            WriteInfoElementValue("registerNavn2", regName2);
            WriteInfoElementValue("registerNavn3", regName3);
            WriteInfoElementValue("landkode", landKode);
            WriteInfoElementValue("utenlandskPoststed", postalArea);
            WriteInfoElementValue("postadresse1", mailAdr1);
            WriteInfoElementValue("postadresse2", mailAdr2);
            WriteInfoElementValue("postadresse3", mailAdr3);
            WriteInfoElementEnd();
        }

        public void WriteFmva(ReadOnlySpan<char> status, ReadOnlySpan<char> fmva_type)
        {
            WriteInfoElementStart("FMVA", status);
            WriteInfoElementValue("opplysning", fmva_type);
            WriteInfoElementEnd();
        }

        public void WriteKatg(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> katgType,
            ReadOnlySpan<char> katgRanking)
        {
            WriteInfoElementStart("KATG", status);

            if (IsNewOrUpdateChange(status))
            {
                _xmlWriter.WriteStartElement("kategoriFV");
                WriteInfoElementValue("kategorikode", katgType);
                WriteInfoElementValue("rangering", katgRanking);
                _xmlWriter.WriteEndElement();
            }

            WriteInfoElementEnd();
        }

        public void WriteTkn(
            ReadOnlySpan<char> status,
            ReadOnlySpan<char> regionalEnhetOrgnNr,
            ReadOnlySpan<char> sentralEnhetOrgnNr)
        {
            WriteInfoElementStart("TKN", status);
            WriteInfoElementValue("organisasjonsnummerRegionalEnhet", regionalEnhetOrgnNr);
            WriteInfoElementValue("organisasjonsnummerSentralEnhet", sentralEnhetOrgnNr);
            WriteInfoElementEnd();
        }

        public void WriteSamendringStart(
            ReadOnlySpan<char> data,
            ReadOnlySpan<char> felttype,
            ReadOnlySpan<char> endringsType,
            ReadOnlySpan<char> samendringsType)
        {
            _xmlWriter.WriteStartElement("samendringer");
            _xmlWriter.WriteAttributeString("data", data.ToString());
            _xmlWriter.WriteAttributeString("felttype", felttype.ToString());
            _xmlWriter.WriteAttributeString("endringstype", endringsType.ToString());
            _xmlWriter.WriteAttributeString("type", samendringsType.ToString());
        }

        public void WriteSamendringEnd()
        {
            _xmlWriter.WriteEndElement();
        }

        public void WriteSamendringutgaar(ReadOnlySpan<char> samendringsType)
        {
            _xmlWriter.WriteStartElement("samendringUtgaar");
            _xmlWriter.WriteAttributeString("felttype", "SAMU");
            WriteOptionalTextElementNode("samendringstype", samendringsType);
            _xmlWriter.WriteEndElement();
        }

        public void WriteStatus(ReadOnlySpan<char> status, ReadOnlySpan<char> endringsType, ReadOnlySpan<char> kjennelsesDato)
        {
            _xmlWriter.WriteStartElement("status");
            _xmlWriter.WriteAttributeString("felttype", status.ToString());
            _xmlWriter.WriteAttributeString("endringstype", endringsType.ToString());

            if (endringsType is "N" && !kjennelsesDato.IsWhiteSpace())
            {
                WriteOptionalTextElementNode("kjennelsesdato", kjennelsesDato);
            }

            _xmlWriter.WriteEndElement();
        }
    }
}
