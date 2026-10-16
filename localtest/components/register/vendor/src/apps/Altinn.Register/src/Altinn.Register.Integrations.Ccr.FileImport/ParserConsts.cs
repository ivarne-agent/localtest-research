namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Provides configuration values for the CCR flat file parser, such as field lengths, offsets, and attribute names.
/// </summary>
internal static class ParserConsts
{
    /// <summary>
    /// Gets the Unit Length Established Attribute
    /// </summary>
    public const int EnhLengthEstablished = 8;

    /// <summary>
    /// Gets the Unit Length First transfer Attribute
    /// </summary>
    public const int EnhLengthFoersteOverf = 1;

    /// <summary>
    /// Gets the Unit Length Last Changed Attribute
    /// </summary>
    public const int EnhLengthLastChanged = 8;

    /// <summary>
    /// Gets the Unit Length Unit number Attribute
    /// </summary>
    public const int EnhLengthOrgnr = 9;

    /// <summary>
    /// Gets the ENH_LENGTHSTATUS Attribute
    /// </summary>
    public const int EnhLengthstatus = 1;

    /// <summary>
    /// Gets the ENH_LENGTHSUBSTATUS Attribute
    /// </summary>
    public const int EnhLengthsubstatus = 4;

    /// <summary>
    /// Gets the ENH_LENGTHUNITTYPE Attribute
    /// </summary>
    public const int EnhLengthunittype = 4;

    /// <summary>
    /// Gets the ENH_OFFSETESTABLISHED Attribute
    /// </summary>
    public const int EnhOffsetestablished = 22;

    /// <summary>
    /// Gets the ENH_OFFSETFOERSTEOVERF Attribute
    /// </summary>
    public const int EnhOffsetfoersteoverf = 38;

    /// <summary>
    /// Gets the ENH_OFFSETLASTCHANGED Attribute
    /// </summary>
    public const int EnhOffsetlastchanged = 30;

    /// <summary>
    /// Gets the ENH_OFFSETORGNR Attribute
    /// </summary>
    public const int EnhOffsetorgnr = 4;

    /// <summary>
    /// Gets the ENH_OFFSETSTATUS Attribute
    /// </summary>
    public const int EnhOffsetstatus = 17;

    /// <summary>
    /// Gets the ENH_OFFSETSUBSTATUS Attribute
    /// </summary>
    public const int EnhOffsetsubstatus = 18;

    /// <summary>
    /// Gets the ENH_OFFSETUNITTYPE Attribute
    /// </summary>
    public const int EnhOffsetunittype = 13;

    /// <summary>
    /// Gets the EPOS_LENGTHEPOSTADRESSE Attribute
    /// </summary>
    public const int EposLengthepostadresse = 150;

    /// <summary>
    /// Gets the EPOS_LENGTHSTATUS Attribute
    /// </summary>
    public const int EposLengthstatus = 1;

    /// <summary>
    /// Gets the EPOS_OFFSETEPOSTADRESSE Attribute
    /// </summary>
    public const int EposOffsetEpostAdresse = 8;

    /// <summary>
    /// Gets the Email Offset status Attribute
    /// </summary>
    public const int EposOffsetStatus = 4;

    /// <summary>
    /// Gets the Business address Length Address1 Attribute
    /// </summary>
    public const int FadrLengthAdresse1 = 35;

    /// <summary>
    /// Gets the Business address Length Address2 Attribute
    /// </summary>
    public const int FadrLengthAdresse2 = 35;

    /// <summary>
    /// Gets the Business address Length Address3 Attribute
    /// </summary>
    public const int FadrLengthAdresse3 = 35;

    /// <summary>
    /// Gets the Business address Length municipal number Attribute
    /// </summary>
    public const int FadrLengthKommuneNr = 9;

    /// <summary>
    /// Gets the Business address Length Country code Attribute
    /// </summary>
    public const int FadrLengthLandkode = 3;

    /// <summary>
    /// Gets the Business address Length address Attribute
    /// </summary>
    public const int FadrLengthPostadresse = 9;

    /// <summary>
    /// Gets the Business adress post number and name foreign addree Length Attribute
    /// </summary>
    public const int FadrLengthPoststed = 35;

    /// <summary>
    /// Gets the Business address Length Status Attribute
    /// </summary>
    public const int FadrLengthStatus = 1;

    /// <summary>
    /// Gets the Business address Offset address1 Attribute
    /// </summary>
    public const int FadrOffsetAdresse1 = 64;

    /// <summary>
    /// Gets the Business address Offset address2 Attribute
    /// </summary>
    public const int FadrOffsetAdresse2 = 99;

    /// <summary>
    /// Gets the Business address Offset address3 Attribute
    /// </summary>
    public const int FadrOffsetadresse3 = 134;

    /// <summary>
    /// Gets the Business address Offset municipal Number Attribute
    /// </summary>
    public const int FadrOffsetKommuneNr = 20;

    /// <summary>
    /// Gets the Business address Offset Country code Attribute
    /// </summary>
    public const int FadrOffsetLandkode = 17;

    /// <summary>
    /// Gets the Business address Offset address Attribute
    /// </summary>
    public const int FadrOffsetPostadresse = 8;

    /// <summary>
    /// Gets the Business Adress Poststed for foreign adresses Offset Attribute
    /// </summary>
    public const int FadrOffsetPoststed = 29;

    /// <summary>
    /// Gets the Business address Offset Status Attribute
    /// </summary>
    public const int FadrOffsetStatus = 4;

    /// <summary>
    /// Gets the Head Attribute Sender Length Attribute
    /// </summary>
    public const int HeadAttrAvsenderLength = 3;

    /// <summary>
    /// Gets the Head Attribute Sender Offset Attribute
    /// </summary>
    public const int HeadAttrAvsenderOffset = 4;

    /// <summary>
    /// Gets the Head Attribute Date Length Attribute
    /// </summary>
    public const int HeadAttrDatoLength = 8;

    /// <summary>
    /// Gets the Head Attribute Date Offset Attribute
    /// </summary>
    public const int HeadAttrDatoOffset = 7;

    /// <summary>
    /// Gets the Head Attribute sequence number Length Attribute
    /// </summary>
    public const int HeadAttrKjoerenrLength = 5;

    /// <summary>
    /// Gets the Head Attribute Sequence number Offset Attribute
    /// </summary>
    public const int HeadAttrKjoerenrOffset = 15;

    /// <summary>
    /// Gets the Head Attribute Receiver Length Attribute
    /// </summary>
    public const int HeadAttrMottakerLength = 3;

    /// <summary>
    /// Gets the Head Attribute Receiver Offset Attribute
    /// </summary>
    public const int HeadAttrMottakerOffset = 20;

    /// <summary>
    /// Gets the Head Attribute Type Length Attribute
    /// </summary>
    public const int HeadAttrTypeLength = 1;

    /// <summary>
    /// Gets the Head Attribute Type Offset Attribute
    /// </summary>
    public const int HeadAttrTypeOffset = 23;

    /// <summary>
    /// Gets the Internet Address Length URL Attribute
    /// </summary>
    public const int IadrLengthInternettAdresse = 150;

    /// <summary>
    /// Gets the Internet AddressLength Status Attribute
    /// </summary>
    public const int IadrLengthStatus = 1;

    /// <summary>
    /// Gets the Internet Address Offset URL Attribute
    /// </summary>
    public const int IadrOffsetInternettAdresse = 8;

    /// <summary>
    /// Gets the Internet Address Offset Status Attribute
    /// </summary>
    public const int IadrOffsetStatus = 4;

    /// <summary>
    /// Gets the Connection Role Length Data Attribute
    /// </summary>
    public const int KnytRollLengthData = 1;

    /// <summary>
    /// Gets the Connection Role Length Type Attribute
    /// </summary>
    public const int KnytRollLengthType = 1;

    /// <summary>
    /// Gets the Connection Role Offset Data Attribute
    /// </summary>
    public const int KnytRollOffsetData = 9;

    /// <summary>
    /// Gets the Connection Role Offset Type Attribute
    /// </summary>
    public const int KnytRollOffsetType = 8;

    /// <summary>
    /// Gets the Connection Length connected to unit Attribute
    /// </summary>
    public const int KnytLengthKnyttetTilOrgnr = 9;

    /// <summary>
    /// Gets the Connection Length Status Attribute
    /// </summary>
    public const int KnytLengthStatus = 1;

    /// <summary>
    /// Gets the Connection Offset connected to unit Attribute
    /// </summary>
    public const int KnytOffsetKnyttetTilOrgnr = 41;

    /// <summary>
    /// Gets the Connection Offset Status Attribute
    /// </summary>
    public const int KnytOffsetStatus = 4;

    /// <summary>
    /// Gets the Multiple change Offset Status Attribute
    /// </summary>
    public const int SamOffsetStatus = 4;

    /// <summary>
    /// Gets the Multiple change Length Status Attribute
    /// </summary>
    public const int SamLengthStatus = 1;

    /// <summary>
    /// Gets the Multiple change Offset Status Attribute
    /// </summary>
    public const int SamOffsetText = 11;

    /// <summary>
    /// Gets the Multiple change Length Status Attribute
    /// </summary>
    public const int SamLengthText = 70;

    /// <summary>
    /// Gets the Multiple change Offset Connection Freetext Attribute
    /// </summary>
    public const int ConnectionFreetextOffsetText = 19;

    /// <summary>
    /// Gets the Multiple change Length Connection Freetext Attribute
    /// </summary>
    public const int ConnectionFreetextLengthText = 70;

    /// <summary>
    /// Gets the Multiple change Offset Connection Organization number Attribute
    /// </summary>
    public const int ConnectionFreetextOffsetOrgnumber = 10;

    /// <summary>
    /// Gets the Multiple change Length Connection Organization number Attribute
    /// </summary>
    public const int ConnectionFreetextLengthOrgnumber = 9;

    /// <summary>
    /// Gets the Multiple change Offset Role free text Attribute
    /// </summary>
    public const int RoleFreetextOffsetText = 21;

    /// <summary>
    /// Gets the Multiple change Length Role free text Attribute
    /// </summary>
    public const int RoleFreetextLengthText = 70;

    /// <summary>
    /// Gets the Multiple change Offset Role ssn Attribute
    /// </summary>
    public const int RoleFreetextOffsetSsn = 10;

    /// <summary>
    /// Gets the Multiple change Length Role ssn Attribute
    /// </summary>
    public const int RoleFreetextLengthSsn = 11;

    /// <summary>
    /// Gets the Length Record Attribute
    /// </summary>
    public const int LengthRecord = 4;

    /// <summary>
    /// Gets the Mobile Length number Attribute
    /// </summary>
    public const int MtlfLengthMobiltelefon = 13;

    /// <summary>
    /// Gets the Mobile Length Status Attribute
    /// </summary>
    public const int MtlfLengthStatus = 1;

    /// <summary>
    /// Gets the Mobile Offset number Attribute
    /// </summary>
    public const int MtlfOffsetMobiltelefon = 8;

    /// <summary>
    /// Gets the Mobile Offset Status Attribute
    /// </summary>
    public const int MtlfOffsetStatus = 4;

    /// <summary>
    /// Gets the Name Length name1 Attribute
    /// </summary>
    public const int NavnLengthNavn1 = 35;

    /// <summary>
    /// Gets the Name Length name2 Attribute
    /// </summary>
    public const int NavnLengthNavn2 = 35;

    /// <summary>
    /// Gets the Name Length name3 Attribute
    /// </summary>
    public const int NavnLengthNavn3 = 35;

    /// <summary>
    /// Gets the Name Length name4 Attribute
    /// </summary>
    public const int NavnLengthNavn4 = 35;

    /// <summary>
    /// Gets the Name Length name5 Attribute
    /// </summary>
    public const int NavnLengthNavn5 = 35;

    /// <summary>
    /// Gets the Name Length Shortened name Attribute
    /// </summary>
    public const int NavnLengthRedigertNavn = 36;

    /// <summary>
    /// Gets the Name Length Status Attribute
    /// </summary>
    public const int NavnLengthStatus = 1;

    /// <summary>
    /// Gets the Name Offset name1 Attribute
    /// </summary>
    public const int NavnOffsetNavn1 = 8;

    /// <summary>
    /// Gets the Name Offset name2 Attribute
    /// </summary>
    public const int NavnOffsetNavn2 = 43;

    /// <summary>
    /// Gets the Name Offset name3 Attribute
    /// </summary>
    public const int NavnOffsetNavn3 = 78;

    /// <summary>
    /// Gets the Name Offset name4 Attribute
    /// </summary>
    public const int NavnOffsetNavn4 = 113;

    /// <summary>
    /// Gets the Name Offset name5 Attribute
    /// </summary>
    public const int NavnOffsetNavn5 = 148;

    /// <summary>
    /// Gets the Name Offset shortened name Attribute
    /// </summary>
    public const int NavnOffsetRedigertNavn = 183;

    /// <summary>
    /// Gets the Name Offset Status Attribute
    /// </summary>
    public const int NavnOffsetStatus = 4;

    /// <summary>
    /// Gets the Offset Record Attribute
    /// </summary>
    public const int OffsetRecord = 0;

    /// <summary>
    /// Gets the Role Length Alternative Status Attribute
    /// </summary>
    public const int RollLengthAltStatus = 1;

    /// <summary>
    /// Gets the Role Length SSN Attribute
    /// </summary>
    public const int RollLengthRolleFnr = 11;

    /// <summary>
    /// Gets the Role Length Status Attribute
    /// </summary>
    public const int RollLengthStatus = 1;

    /// <summary>
    /// Gets the Role Offset alternative Status Attribute
    /// </summary>
    public const int RollOffsetAltStatus = 40;

    /// <summary>
    /// Gets the Role Offset SSN Attribute
    /// </summary>
    public const int RollOffsetRolleFnr = 48;

    /// <summary>
    /// Gets the Role Offset Status Attribute
    /// </summary>
    public const int RollOffsetStatus = 4;

    /// <summary>
    /// Gets the Position Length Status Attribute
    /// </summary>
    public const int FreeTextPositionLength = 1;

    /// <summary>
    /// Gets the Position Offset Status Attribute
    /// </summary>
    public const int FreeTextPositionOffset = 10;

    /// <summary>
    /// Gets Offset of NaeringsKode field in flatfile.
    /// </summary>
    public const int NACENaeringsKodeOffset = 8;

    /// <summary>
    /// Gets Length of NaeringsKode field in flatfile.
    /// </summary>
    public const int NACENaeringsKodeLength = 6;

    /// <summary>
    /// Gets Offset of Dato field in flatfile.
    /// </summary>
    public const int NACEDatoOffset = 14;

    /// <summary>
    /// Gets Length of HjelpeEnhet field in flatfile.
    /// </summary>
    public const int NACEDatoLength = 8;

    /// <summary>
    /// Gets Offset of HjelpeEnhet field in flatfile.
    /// </summary>
    public const int NACEHjelpeEnhetOffset = 22;

    /// <summary>
    /// Gets Length of HjelpeEnhet field in flatfile.
    /// </summary>
    public const int NACEHjelpeEnhetLength = 1;

    /// <summary>
    /// Gets the Multiple change Length Type Attribute
    /// </summary>
    public const int SamuLengthType = 4;

    /// <summary>
    /// Gets the Multiple change Offset Type Attribute
    /// </summary>
    public const int SamuOffsetType = 4;

    /// <summary>
    /// Gets the Telefax Length Status Attribute
    /// </summary>
    public const int TfaxLengthStatus = 1;

    /// <summary>
    /// Gets the Telefax Length Number Attribute
    /// </summary>
    public const int TfaxLengthTelefax = 13;

    /// <summary>
    /// Gets the Telefax Offset Status Attribute
    /// </summary>
    public const int TfaxOffsetStatus = 4;

    /// <summary>
    /// Gets the Telefax Offset number Attribute
    /// </summary>
    public const int TfaxOffsetTelefax = 8;

    /// <summary>
    /// Gets the Phone Length Status Attribute
    /// </summary>
    public const int TfonLengthStatus = 1;

    /// <summary>
    /// Gets the Phone Length Number Attribute
    /// </summary>
    public const int TfonLengthTelefon = 13;

    /// <summary>
    /// Gets the Phone Offset Status Attribute
    /// </summary>
    public const int TfonOffsetStatus = 4;

    /// <summary>
    /// Gets the Phone offset number Attribute
    /// </summary>
    public const int TfonOffsetTelefon = 8;

    /// <summary>
    /// Gets the Trailer Attribute Number of units Length Attribute
    /// </summary>
    public const int TraiAttrAntallEnehterLength = 7;

    /// <summary>
    /// Gets the Trailer Attribute Number Units Offset Attribute
    /// </summary>
    public const int TraiAttrAntallEnheterOffset = 7;

    /// <summary>
    /// Gets the Trailer Attribute Sender Length Attribute
    /// </summary>
    public const int TraiAttrAvsenderLength = 3;

    /// <summary>
    /// Gets the Trailer Attribute Sender Offset Attribute
    /// </summary>
    public const int TraiAttrAvsenderOffset = 4;

    /// <summary>
    /// Gets the StatusFieldStatusOffset Attribute
    /// </summary>
    public const int StatusFieldStatusOffset = 4;

    /// <summary>
    /// Gets the StatusFieldStatusLength Attribute
    /// </summary>
    public const int StatusFieldStatusLength = 1;

    /// <summary>
    /// Gets the offset for the påtegning infotype
    /// </summary>
    public const int PAAT_INFOTYPE_OFFSET = 8;

    /// <summary>
    /// Gets the length for the påtegning infotype
    /// </summary>
    public const int PAAT_INFOTYPE_LEN = 4;

    /// <summary>
    /// Gets the offset for the påtegning registrar
    /// </summary>
    public const int PAAT_REGISTER_OFFSET = 12;

    /// <summary>
    /// Gets the length for the påtegning registrar
    /// </summary>
    public const int PAAT_REGISTER_LEN = 2;

    /// <summary>
    /// Gets the offset for the påtegning text field
    /// </summary>
    public const int PAAT_TEXT1_OFFSET = 14;

    /// <summary>
    /// Gets the offset for the påtegning text field
    /// </summary>
    public const int PAAT_TEXT2_OFFSET = 84;

    /// <summary>
    /// Gets the offset for the påtegning text field
    /// </summary>
    public const int PAAT_TEXT3_OFFSET = 154;

    /// <summary>
    /// Gets the offset for the påtegning textfield length. This is the same for all 3 fields.
    /// </summary>
    public const int PAAT_TEXT_LEN = 70;

    /// <summary>
    /// Gets the offset of the landkode field.
    /// </summary>
    public const int ULOV_LANDKODE_OFFSET = 8;

    /// <summary>
    /// Gets the length of the landkode field.
    /// </summary>
    public const int ULOV_LANDKODE_LEN = 3;

    /// <summary>
    /// Gets the offset of the organization form field.
    /// </summary>
    public const int ULOV_ORGFORM_OFFSET = 11;

    /// <summary>
    /// Gets the length of the organization form field.
    /// </summary>
    public const int ULOV_ORGFORM_LEN = 8;

    /// <summary>
    /// Gets the offset of the norwegian description field.
    /// </summary>
    public const int ULOV_DESCNO_OFFSET = 89;

    /// <summary>
    /// Gets the length of the norwegian description field.
    /// </summary>
    public const int ULOV_DESCNO_LEN = 70;

    /// <summary>
    /// Gets the offset of the foreign description field.
    /// </summary>
    public const int ULOV_DESCFO_OFFSET = 19;

    /// <summary>
    /// Gets the length of the foreign description field.
    /// </summary>
    public const int ULOV_DESCFO_LEN = 70;

    /// <summary>
    /// Gets the offset of the Registernr field.
    /// </summary>
    public const int UREG_REGNR_OFFSET = 8;

    /// <summary>
    /// Gets the offset of the Registernr field.
    /// </summary>
    public const int UREG_REGNR_LEN = 35;

    /// <summary>
    /// Gets the offset of the NAVN1 field.
    /// </summary>
    public const int UREG_NAVN1_OFFSET = 43;

    /// <summary>
    /// Gets the offset of the NAVN1 field.
    /// </summary>
    public const int UREG_NAVN1_LEN = 35;

    /// <summary>
    /// Gets the offset of the NAVN2 field.
    /// </summary>
    public const int UREG_NAVN2_OFFSET = 78;

    /// <summary>
    /// Gets the offset of the NAVN2 field.
    /// </summary>
    public const int UREG_NAVN2_LEN = 35;

    /// <summary>
    /// Gets the offset of the NAVN3 field.
    /// </summary>
    public const int UREG_NAVN3_OFFSET = 113;

    /// <summary>
    /// Gets the offset of the NAVN3 field.
    /// </summary>
    public const int UREG_NAVN3_LEN = 35;

    /// <summary>
    /// Gets the offset of the LANDKODE field.
    /// </summary>
    public const int UREG_LANDKODE_OFFSET = 148;

    /// <summary>
    /// Gets the offset of the LANDKODE field.
    /// </summary>
    public const int UREG_LANDKODE_LEN = 3;

    /// <summary>
    /// Gets the offset of the POSTSTED field.
    /// </summary>
    public const int UREG_POSTSTED_OFFSET = 151;

    /// <summary>
    /// Gets the offset of the POSTSTED field.
    /// </summary>
    public const int UREG_POSTSTED_LEN = 35;

    /// <summary>
    /// Gets the offset of the POSTADRESSE1 field.
    /// </summary>
    public const int UREG_POSTADRESSE1_OFFSET = 186;

    /// <summary>
    /// Gets the offset of the POSTADRESSE1 field.
    /// </summary>
    public const int UREG_POSTADRESSE1_LEN = 35;

    /// <summary>
    /// Gets the offset of the POSTADRESSE2 field.
    /// </summary>
    public const int UREG_POSTADRESSE2_OFFSET = 221;

    /// <summary>
    /// Gets the offset of the POSTADRESSE2 field.
    /// </summary>
    public const int UREG_POSTADRESSE2_LEN = 35;

    /// <summary>
    /// Gets the offset of the POSTADRESSE3 field.
    /// </summary>
    public const int UREG_POSTADRESSE3_OFFSET = 256;

    /// <summary>
    /// Gets the offset of the POSTADRESSE3 field.
    /// </summary>
    public const int UREG_POSTADRESSE3_LEN = 35;

    /// <summary>
    /// Gets the most common changetype offset used in the BRG ER flatfile.
    /// </summary>
    public const int INFOTYPE_CHANGETYPE_OFFSET_DEFAULT = 4;

    /// <summary>
    /// Gets the most common changetype length used in the BRG ER flatfile.
    /// </summary>
    public const int INFOTYPE_CHANGETYPE_LENGTH_DEFAULT = 1;

    /// <summary>
    /// Gets the most common value offset used in the BRG ER flatfile.
    /// </summary>
    public const int INFOTYPE_VALUE_OFFSET_DEFAULT = 8;

    /// <summary>
    /// Gets the length of Bool ValueType J/N.
    /// </summary>
    public const int INFOTYPE_VALUE_LENGTH_BOOL = 1;

    /// <summary>
    /// Gets the most common value length used in the BRG ER flatfile.
    /// </summary>
    public const int INFOTYPE_VALUE_LENGTH_DEFAULT = 150;

    /// <summary>
    /// Gets the length for the status infotype childnode used in the ER flatfile.
    /// </summary>
    public const int KAPI_LENGTH_STATUS = 1;

    /// <summary>
    /// Gets the length for the valutakode infotype childnode used in the ER flatfile.
    /// </summary>
    public const int KAPI_LENGTH_VALUTAKODE = 3;

    /// <summary>
    /// Gets the length for the kapital infotype childnode used in the ER flatfile.
    /// </summary>
    public const int KAPI_LENGTH_KAPITAL = 18;

    /// <summary>
    /// Gets the length for the kapital innbetalt infotype childnode used in the ER flatfile.
    /// </summary>
    public const int KAPI_LENGTH_KAPITAL_INNBETALT = 18;

    /// <summary>
    /// Gets the length for the kapital bundet ks infotype childnode used in the ER flatfile
    /// </summary>
    public const int KAPI_LENGTH_KAPITAL_BUNDET_KS = 70;

    /// <summary>
    /// Gets the length for the fritekst infotype childnode used in the ER flatfile.
    /// </summary>
    public const int KAPI_LENGTH_FRITEKST = 70;

    /// <summary>
    /// Gets the status offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_STATUS = 4;

    /// <summary>
    /// Gets the valutakode offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_VALUTAKODE = 8;

    /// <summary>
    /// Gets the kapital offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_KAPITAL = 11;

    /// <summary>
    /// Gets the kapital innbetalt offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_KAPITAL_INNBETALT = 29;

    /// <summary>
    /// Gets the kaptial bundet ks offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_KAPITAL_BUNDET_KS = 47;

    /// <summary>
    /// Gets the fritekst offset used in the ER flatfile.
    /// </summary>
    public const int KAPI_OFFSET_FRITEKST = 117;

    /// <summary>
    /// Gets the length of the status field.
    /// </summary>
    public const int FMVA_STATUS_LENGTH = 1;

    /// <summary>
    /// Gets the offset of the status field.
    /// </summary>
    public const int FMVA_STATUS_OFFSET = 4;

    /// <summary>
    /// Gets the length of the status field.
    /// </summary>
    public const int FMVA_TYPE_LENGTH = 4;

    /// <summary>
    /// Gets the offset of the status field.
    /// </summary>
    public const int FMVA_TYPE_OFFSET = 8;

    /// <summary>
    /// Gets the length of the type field.
    /// </summary>
    public const int KATG_CODE_LENGTH = 5;

    /// <summary>
    /// Gets the offset of the type field.
    /// </summary>
    public const int KATG_CODE_OFFSET = 8;

    /// <summary>
    /// Gets the length of the type field.
    /// </summary>
    public const int KATG_RANKING_LENGTH = 1;

    /// <summary>
    /// Gets the offset of the type field.
    /// </summary>
    public const int KATG_RANKING_OFFSET = 13;

    /// <summary>
    /// Gets the Offset of Ansvarsandel
    /// </summary>
    public const int RollOffsetAnsvarsandel = 10;

    /// <summary>
    /// Gets the length of Ansvarsandel
    /// </summary>
    public const int RollLengthAnsvarsandel = 30;

    /// <summary>
    /// Gets the Offset of Valgtav
    /// </summary>
    public const int RollOffsetValgtav = 41;

    /// <summary>
    /// Gets the length of Valgt av
    /// </summary>
    public const int RollLengthValgtav = 4;

    /// <summary>
    /// Gets the Offset of Rekkefølge
    /// </summary>
    public const int RollOffseteRekkefoelge = 45;

    /// <summary>
    /// Gets the length of rolle rekkefølge
    /// </summary>
    public const int RollLengtheRekkefoelge = 3;

    /// <summary>
    /// Gets the Offset of Fornavn
    /// </summary>
    public const int RollOffsetFornavn = 59;

    /// <summary>
    /// Gets the length of Fornavn
    /// </summary>
    public const int RollLengthFornavn = 50;

    /// <summary>
    /// Gets the Offset of Mellomnavn
    /// </summary>
    public const int RollOffsetMellomnavn = 109;

    /// <summary>
    /// Gets the length of Mellomnavn
    /// </summary>
    public const int RollLengthMellomnavn = 50;

    /// <summary>
    /// Gets the Offset of Slektsnavn
    /// </summary>
    public const int RollOffsetSlektsnavn = 159;

    /// <summary>
    /// Gets the length of Slektsnavn
    /// </summary>
    public const int RollLengthSlektsnavn = 50;

    /// <summary>
    /// Gets the Offset of Postnr
    /// </summary>
    public const int RollOffsetPostnr = 209;

    /// <summary>
    /// Gets the length of Postnr
    /// </summary>
    public const int RollLengthPostnr = 9;

    /// <summary>
    /// Gets the Offset of Addresse1
    /// </summary>
    public const int RollOffsetAdresse1 = 218;

    /// <summary>
    /// Gets the length of Addresse1
    /// </summary>
    public const int RollLengthAdresse1 = 35;

    /// <summary>
    /// Gets the Offset of Adresse2
    /// </summary>
    public const int RollOffsetAdresse2 = 253;

    /// <summary>
    /// Gets the length of Addresse2
    /// </summary>
    public const int RollLengthAdresse2 = 35;

    /// <summary>
    /// Gets the Offset of Adresse3
    /// </summary>
    public const int RollOffsetAdresse3 = 288;

    /// <summary>
    /// Gets the length of Addresse3
    /// </summary>
    public const int RollLengthAdresse3 = 35;

    /// <summary>
    /// Gets the Offset of Landkode
    /// </summary>
    public const int RollOffsetAdresseLandkode = 323;

    /// <summary>
    /// Gets the length of Landkode
    /// </summary>
    public const int RollLengthAdresseLandkode = 3;

    /// <summary>
    /// Gets the Offset of Personstatus
    /// </summary>
    public const int RollOffsetPersonstatus = 326;

    /// <summary>
    /// Gets the length of person status
    /// </summary>
    public const int RollLengthPersonstatus = 1;

    /// <summary>
    /// Gets the Offset of ValgtAv for Knytting
    /// </summary>
    public const int KnytOffsetValgtav = 50;

    /// <summary>
    /// Gets the Length of Chosen by for knyttning
    /// </summary>
    public const int KnytLengthValgtav = 4;

    /// <summary>
    /// Gets the Offset of Rekkefølge
    /// </summary>
    public const int KnytOffsetRekkefoelge = 54;

    /// <summary>
    /// Gets the Length of order for knyttning
    /// </summary>
    public const int KnytLengthRekkefoelge = 3;

    /// <summary>
    /// Gets the Offset of KorrektOrganisasjonsnummer
    /// </summary>
    public const int KnytOffsetKorrektOrganisasjonsnummer = 57;

    /// <summary>
    /// Gets the Length of KorrektOrganisasjonsnummer for knyttning
    /// </summary>
    public const int KnytLengthKorrektOrganisasjonsnummer = 9;

    /// <summary>
    /// Gets the length of the element
    /// </summary>
    public const int FFI_FULLMAKT_INSTRUMENT_NR_LENGTH = 3;

    /// <summary>
    /// Gets the length of the element
    /// </summary>
    public const int FFI_DATOGITT_LENGTH = 8;

    /// <summary>
    /// Gets the length of the element
    /// </summary>
    public const int FFI_DATOUTGAAR_LENGTH = 8;

    /// <summary>
    /// Gets the length of the element
    /// </summary>
    public const int FFI_VALUTAKODE_LENGTH = 3;

    /// <summary>
    /// Gets the length of the kapitalforhoyelse element
    /// </summary>
    public const int FFI_KAPITALFORHOYELSE_LENGTH = 18;

    /// <summary>
    /// Gets the length of the max brukt belop element
    /// </summary>
    public const int FFI_MAX_BRUKT_BELOP_LENGTH = 18;

    /// <summary>
    /// Gets the length of the omfatter fusjon element
    /// </summary>
    public const int FFI_OMFATTER_FUSJON_LENGTH = 1;

    /// <summary>
    /// Gets the length of the  etter fullmakt element
    /// </summary>
    public const int FFI_ETTERFULLMAKT_LENGTH = 1;

    /// <summary>
    /// Gets the length of the utgatt element
    /// </summary>
    public const int FFI_UTGAATT_LENGTH = 1;

    /// <summary>
    /// Gets the length of the utgatt tekst element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_LENGTH = 140;

    /// <summary>
    /// Gets the offset of the fullmakt nr element
    /// </summary>
    public const int FFI_FULLMAKT_INSTRUMENT_NR_OFFSET = 8;

    /// <summary>
    /// Gets the offset of the dato gitt element
    /// </summary>
    public const int FFI_DATOGITT_OFFSET = 11;

    /// <summary>
    /// Gets the offset of the dato utgaar element
    /// </summary>
    public const int FFI_DATOUTGAAR_OFFSET = 19;

    /// <summary>
    /// Gets the offset of the valutakode element
    /// </summary>
    public const int FFI_VALUTAKODE_OFFSET = 27;

    /// <summary>
    /// Gets the offset of the kapitalforhoyelse element
    /// </summary>
    public const int FFI_KAPITALFORHOYELSE_OFFSET = 30;

    /// <summary>
    /// Gets the offset of the max brukt belop element
    /// </summary>
    public const int FFI_MAX_BRUKT_BELOP_OFFSET = 48;

    /// <summary>
    /// Gets the offset of the max brukt belop FMKL element
    /// </summary>
    public const int FFI_BRUKT_BELOP_FMKL_OFFSET = 66;

    /// <summary>
    /// Gets the offset of the omfatter fusjon FMKA element
    /// </summary>
    public const int FFI_OMFATTER_FUSJON_FMKA_OFFSET = 66;

    /// <summary>
    /// Gets the offset of the omfatter fusjon FMAK FMAP element
    /// </summary>
    public const int FFI_OMFATTER_FUSJON_FMAK_FMAP_OFFSET = 48;

    /// <summary>
    /// Gets the offset of the omfatter fusjon FMKL element
    /// </summary>
    public const int FFI_OMFATTER_FUSJON_FMKL_OFFSET = 84;

    /// <summary>
    /// Gets the offset of the omfatter fusjon FMUU element
    /// </summary>
    public const int FFI_OMFATTER_FUSJON_FMUU_OFFSET = 27;

    /// <summary>
    /// Gets the offset of the etter fullmakt element
    /// </summary>
    public const int FFI_ETTERFULLMAKT_OFFSET = 66;

    /// <summary>
    /// Gets the offset of the fullmakt nr KLAN element
    /// </summary>
    public const int FFI_FULLMAKTNR_KLAN_OFFSET = 67;

    /// <summary>
    /// Gets the offset of the utgaattFMKA element
    /// </summary>
    public const int FFI_UTGAATT_FMKA_OFFSET = 67;

    /// <summary>
    /// Gets the offset of the utgaattFMAK element
    /// </summary>
    public const int FFI_UTGAATT_FMAK_FMAP_OFFSET = 49;

    /// <summary>
    /// Gets the offset of the utgaattFMKL element
    /// </summary>
    public const int FFI_UTGAATT_FMKL_OFFSET = 85;

    /// <summary>
    /// Gets the offset of the utgaatt FMUU element
    /// </summary>
    public const int FFI_UTGAATT_FMUU_OFFSET = 28;

    /// <summary>
    /// Gets the offset of the utgaatt FSTR_TRAK element
    /// </summary>
    public const int FFI_UTGAATT_FSTR_TRAK_OFFSET = 66;

    /// <summary>
    /// Gets the offset of the utgaatt KLAN element
    /// </summary>
    public const int FFI_UTGAATT_KLAN_OFFSET = 70;

    /// <summary>
    /// Gets the offset of the utgaatt tekst FMKA element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_FMKA_OFFSET = 68;

    /// <summary>
    /// Gets the offset of the utgaatt tekst FMAK FMAP element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_FMAK_FMAP_OFFSET = 50;

    /// <summary>
    /// Gets the offset of the utgaatt tekst FMKL element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_FMKL_OFFSET = 86;

    /// <summary>
    /// Gets the offset of the utgaatttekst FMUU element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_FMUU_OFFSET = 29;

    /// <summary>
    /// Gets the offset of the utgaatttekst FSTR_TRAK element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_FSTR_TRAK_OFFSET = 67;

    /// <summary>
    /// Gets the offset of the utgaatttekst KLAN element
    /// </summary>
    public const int FFI_UTGAATT_TEKST_KLAN_OFFSET = 71;

    /// <summary>
    /// Gets the length of the status field.
    /// </summary>
    public const int TKN_STATUS_LENGTH = 1;

    /// <summary>
    /// Gets the offset of the status field.
    /// </summary>
    public const int TKN_STATUS_OFFSET = 4;

    /// <summary>
    /// Gets the length of the regional unit organization number field.
    /// </summary>
    public const int TKN_REGIONALENH_LENGTH = 9;

    /// <summary>
    /// Gets the offset of the regional unit organization number field.
    /// </summary>
    public const int TKN_REGIONALENH_OFFSET = 8;

    /// <summary>
    /// Gets the length of the central unit organization number field.
    /// </summary>
    public const int TKN_SENTRALENH_LENGTH = 9;

    /// <summary>
    /// Gets the offset of the central unit organization number field.
    /// </summary>
    public const int TKN_SENTRALENH_OFFSET = 17;
}
