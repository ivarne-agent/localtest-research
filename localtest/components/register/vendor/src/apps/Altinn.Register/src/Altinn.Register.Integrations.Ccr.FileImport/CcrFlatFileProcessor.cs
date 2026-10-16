using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Altinn.Register.Contracts;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Processor for CCR flat files, produces XML documents for each organization in the file.
/// </summary>
internal sealed partial class CcrFlatFileProcessor
{
    private static readonly Encoding _encoding = LegacyEncodings.Windows1252;

    /// <summary>
    /// Processes a CCR flat file from the given PipeReader, writing XML documents for each organization to the provided ChannelWriter.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to use for logging.</param>
    /// <param name="reader">The <see cref="PipeReader"/> to read the CCR flat file from.</param>
    /// <param name="sink">The <see cref="ChannelWriter{T}"/> to write the XML documents to.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    internal static async Task ProcessAsync(
        ILogger<CcrFlatFileProcessor> logger,
        PipeReader reader,
        ChannelWriter<OrganizationUpdateDocument> sink,
        CancellationToken cancellationToken = default)
    {
        await using var lineReader = new LineReader(reader);
        var processor = new CcrFlatFileProcessor(lineReader, logger);

        await processor.ParseAll(sink, cancellationToken);
    }

    /// <summary>
    /// Processes a CCR flat file from the given PipeReader, yielding XML documents for each organization as byte sequences.
    /// </summary>
    /// <param name="logger">The <see cref="ILogger"/> to use for logging.</param>
    /// <param name="reader">The <see cref="PipeReader"/> to read the CCR flat file from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="Sequence{T}"/> representing the XML documents.</returns>
    internal static async IAsyncEnumerable<OrganizationUpdateDocument> ProcessAsync(
        ILogger<CcrFlatFileProcessor> logger,
        PipeReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<OrganizationUpdateDocument>(2);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingTask = ProcessAndCloneAsync(logger, reader, channel.Writer, cts.Token);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cts.Token))
            {
                yield return item;
            }
        }
        finally
        {
            await cts.CancelAsync();
            await processingTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        static async Task ProcessAndCloneAsync(
            ILogger<CcrFlatFileProcessor> logger,
            PipeReader reader,
            ChannelWriter<OrganizationUpdateDocument> sink,
            CancellationToken cancellationToken)
        {
            try
            {
                await ProcessAsync(logger, reader, sink, cancellationToken);
            }
            catch (Exception ex)
            {
                // we complete the channel with the exception, so that the consumer can observe it. We then rethrow to ensure the processing task also observes the exception and doesn't fault silently.
                sink.TryComplete(ex);
                throw;
            }
            finally
            {
                sink.TryComplete();
            }
        }
    }

    private readonly ILogger<CcrFlatFileProcessor> _logger;
    private readonly LineReader _reader;

    // mutable structs - do not make readonly
    private LineBuffer _currentLine;
    private LineBuffer _nextLine;

    private ReadOnlySpan<char> Line
        => _currentLine;

    private ReadOnlySpan<char> NextLine
        => _nextLine;

    private CcrFlatFileProcessor(LineReader reader, ILogger<CcrFlatFileProcessor> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    // ParseNonDls
    private async ValueTask ParseAll(
        ChannelWriter<OrganizationUpdateDocument> sink,
        CancellationToken cancellationToken)
    {
        // assuming we only need to support non-dls here...
        var header = await ParseHeader(cancellationToken);

        while (await Peek(cancellationToken))
        {
            var recordType = GetRecordType(NextLine);

            switch (recordType)
            {
                case "HEAD": // HEADER
                    // we ignore any header records after the first one
                    await Read(cancellationToken); // consume header line
                    break;

                case "TRAI": // TRAILER
                    // This *should* be the end of the file, but to keep legacy behavior, we will continue parsing until the end of the input
                    await Read(cancellationToken); // consume trailer line
                    break;

                case "ENH ": // ENHET
                    // Start a new organization
                    // Note: this output must not be disposed if it's written to the channel, it's up to the channel reader to dispose it
                    var output = new Sequence<byte>(ArrayPool<byte>.Shared);
                    OrganizationRecord org;

                    {
                        using var writer = new CcrXmlWriter(output.AsStream());
                        writer.WriteHead(header);

                        org = await ParseOrganization(writer, cancellationToken);

                        writer.WriteFooter(header, 1);
                    }

                    var doc = new OrganizationUpdateDocument(org.OrgNr, output);
                    await sink.WriteAsync(doc, cancellationToken);
                    break;

                default:
                    // This is an error. ParseOrganization should consume all other record types until it reaches the next header, trailer, or organization record.
                    ThrowHelper.ThrowInvalidDataException($"Unexpected record type: {recordType.ToString()}");
                    break;
            }
        }

        _currentLine.Dispose();
        _nextLine.Dispose();
    }

    private async ValueTask<OrganizationRecord> ParseOrganization(
        CcrXmlWriter writer,
        CancellationToken cancellationToken)
    {
        var org = await ParseOrganizationRecord(cancellationToken);
        writer.WriteOrganizationStart(org);

        while (await Peek(cancellationToken))
        {
            var nextRecordType = GetRecordType(NextLine);
            if (nextRecordType is "HEAD" or "TRAI" or "ENH ")
            {
                // end of org, handled by parent
                break;
            }

            // we know the line is owned by the organization, so we consume it
            await Read(cancellationToken);
            var recordType = GetRecordType(Line);
            var dataOrTextRecord = Line.Slice(ParserConsts.KnytRollOffsetData, ParserConsts.KnytRollLengthData).Trim();

            Debug.Assert(recordType is not ("HEAD" or "TRAI" or "ENH "));
            switch (recordType)
            {
                /////////////////////////////////////////////////////////////////////////////
                // noder med navn infotype, dvs. all informasjon knyttet direkte til enheten
                case "EPOS": // EPOSTADRESSE
                    {
                        var status = Line.Slice(ParserConsts.EposOffsetStatus, ParserConsts.EposLengthstatus).Trim();
                        var epostAdresse = Line.Slice(ParserConsts.EposOffsetEpostAdresse, ParserConsts.EposLengthepostadresse).Trim();
                        writer.WriteEpostAddresse(status, epostAdresse);
                        break;
                    }

                case "FADR": // FORRETNINGSADRESSE
                case "PADR": // POSTADRESSE
                    {
                        var status = Line.Slice(ParserConsts.FadrOffsetStatus, ParserConsts.FadrLengthStatus).Trim();
                        var postNummer = Line.Slice(ParserConsts.FadrOffsetPostadresse, ParserConsts.FadrLengthPostadresse).Trim();
                        var poststedIUtland = Line.Slice(ParserConsts.FadrOffsetPoststed, ParserConsts.FadrLengthPoststed).Trim();
                        var kommuneNummer = Line.Slice(ParserConsts.FadrOffsetKommuneNr, ParserConsts.FadrLengthKommuneNr).Trim();
                        var landKode = Line.Slice(ParserConsts.FadrOffsetLandkode, ParserConsts.FadrLengthLandkode).Trim();
                        var adresse1 = Line.Slice(ParserConsts.FadrOffsetAdresse1, ParserConsts.FadrLengthAdresse1).Trim();
                        var adresse2 = Line.Slice(ParserConsts.FadrOffsetAdresse2, ParserConsts.FadrLengthAdresse2).Trim();
                        var adresse3 = Line.Slice(ParserConsts.FadrOffsetadresse3, ParserConsts.FadrLengthAdresse3).Trim();

                        writer.WriteAddress(
                            type: recordType.Trim(),
                            status: status,
                            postNummer: postNummer,
                            poststedIUtland: poststedIUtland,
                            kommuneNummer: kommuneNummer,
                            landKode: landKode,
                            adresse1: adresse1,
                            adresse2: adresse2,
                            adresse3: adresse3);
                        break;
                    }

                case "IADR": // INTERNETTADRESSE
                    {
                        var status = Line.Slice(ParserConsts.IadrOffsetStatus, ParserConsts.IadrLengthStatus).Trim();
                        var internetAddress = Line.Slice(ParserConsts.IadrOffsetInternettAdresse, ParserConsts.IadrLengthInternettAdresse).Trim();
                        writer.WriteInternettAdresse(status, internetAddress);
                        break;
                    }

                case "MTLF": // MOBILTELEFON
                    {
                        var status = Line.Slice(ParserConsts.MtlfOffsetStatus, ParserConsts.MtlfLengthStatus).Trim();
                        var mobil = Line.Slice(ParserConsts.MtlfOffsetMobiltelefon, ParserConsts.MtlfLengthMobiltelefon).Trim();
                        writer.WriteMobiltelefon(status, mobil);
                        break;
                    }

                case "NAVN":
                    {
                        var status = Line.Slice(ParserConsts.NavnOffsetStatus, ParserConsts.NavnLengthStatus).Trim();
                        var navn1 = Line.Slice(ParserConsts.NavnOffsetNavn1, ParserConsts.NavnLengthNavn1).Trim();
                        var navn2 = Line.Slice(ParserConsts.NavnOffsetNavn2, ParserConsts.NavnLengthNavn2).Trim();
                        var navn3 = Line.Slice(ParserConsts.NavnOffsetNavn3, ParserConsts.NavnLengthNavn3).Trim();
                        var navn4 = Line.Slice(ParserConsts.NavnOffsetNavn4, ParserConsts.NavnLengthNavn4).Trim();
                        var navn5 = Line.Slice(ParserConsts.NavnOffsetNavn5, ParserConsts.NavnLengthNavn5).Trim();

                        var redigertNavn = Line.Slice(ParserConsts.NavnOffsetRedigertNavn, ParserConsts.NavnLengthRedigertNavn).Trim();

                        writer.WriteName(status, navn1, navn2, navn3, navn4, navn5, redigertNavn);
                        break;
                    }

                case "TFAX": // TELEFAX
                    {
                        var status = Line.Slice(ParserConsts.TfaxOffsetStatus, ParserConsts.TfaxLengthStatus).Trim();
                        var telefax = Line.Slice(ParserConsts.TfaxOffsetTelefax, ParserConsts.TfaxLengthTelefax).Trim();
                        writer.WriteTelefax(status, telefax);
                        break;
                    }

                case "TFON": // TELEFON
                    {
                        var status = Line.Slice(ParserConsts.TfonOffsetStatus, ParserConsts.TfonLengthStatus).Trim();
                        var telefon = Line.Slice(ParserConsts.TfonOffsetTelefon, ParserConsts.TfonLengthTelefon).Trim();
                        writer.WriteTelefon(status, telefon);
                        break;
                    }

                case "SN25": // NAERINGSKODE25
                case "NACE": // NAERINGSKODE
                    {
                        var status = Line.Slice(ParserConsts.TfonOffsetStatus, ParserConsts.TfonLengthStatus).Trim();

                        ReadOnlySpan<char> naeringskode = default;
                        ReadOnlySpan<char> gyldighetsdato = default;
                        ReadOnlySpan<char> hjelpeenhet = default;
                        if (IsNewOrUpdateChange(status))
                        {
                            naeringskode = Line.Slice(ParserConsts.NACENaeringsKodeOffset, ParserConsts.NACENaeringsKodeLength).Trim();
                            gyldighetsdato = Line.Slice(ParserConsts.NACEDatoOffset, ParserConsts.NACEDatoLength).Trim();
                            hjelpeenhet = Line.Slice(ParserConsts.NACEHjelpeEnhetOffset, ParserConsts.NACEHjelpeEnhetLength).Trim();
                        }

                        writer.WriteNaeringskode(
                            status: status,
                            naeringskode: naeringskode,
                            gyldighetsdato: gyldighetsdato,
                            hjelpeenhet: hjelpeenhet);
                        break;
                    }

                case "PAAT": // PAATEGNING
                    {
                        var status = Line.Slice(ParserConsts.TfonOffsetStatus, ParserConsts.TfonLengthStatus).Trim();

                        ReadOnlySpan<char> infotype = default;
                        ReadOnlySpan<char> register = default;
                        ReadOnlySpan<char> text1 = default;
                        ReadOnlySpan<char> text2 = default;
                        ReadOnlySpan<char> text3 = default;
                        if (IsNewOrUpdateChange(status))
                        {
                            infotype = Line.Slice(ParserConsts.PAAT_INFOTYPE_OFFSET, ParserConsts.PAAT_INFOTYPE_LEN).Trim();
                            register = Line.Slice(ParserConsts.PAAT_REGISTER_OFFSET, ParserConsts.PAAT_REGISTER_LEN).Trim();
                            text1 = Line.Slice(ParserConsts.PAAT_TEXT1_OFFSET, ParserConsts.PAAT_TEXT_LEN).Trim();
                            text2 = Line.Slice(ParserConsts.PAAT_TEXT2_OFFSET, ParserConsts.PAAT_TEXT_LEN).Trim();
                            text3 = Line.Slice(ParserConsts.PAAT_TEXT3_OFFSET, ParserConsts.PAAT_TEXT_LEN).Trim();
                        }

                        writer.WritePaategning(
                            status: status,
                            infotype: infotype,
                            register: register,
                            text1: text1,
                            text2: text2,
                            text3: text3);
                        break;
                    }

                case "ULOV":
                    {
                        var status = Line.Slice(ParserConsts.TfonOffsetStatus, ParserConsts.TfonLengthStatus).Trim();

                        ReadOnlySpan<char> landKode = default;
                        ReadOnlySpan<char> orgForm = default;
                        ReadOnlySpan<char> descFo = default;
                        ReadOnlySpan<char> descNo = default;
                        if (IsNewOrUpdateChange(status))
                        {
                            landKode = Line.Slice(ParserConsts.ULOV_LANDKODE_OFFSET, ParserConsts.ULOV_LANDKODE_LEN).Trim();
                            orgForm = Line.Slice(ParserConsts.ULOV_ORGFORM_OFFSET, ParserConsts.ULOV_ORGFORM_LEN).Trim();
                            descFo = Line.Slice(ParserConsts.ULOV_DESCFO_OFFSET, ParserConsts.ULOV_DESCFO_LEN).Trim();
                            descNo = Line.Slice(ParserConsts.ULOV_DESCNO_OFFSET, ParserConsts.ULOV_DESCNO_LEN).Trim();
                        }

                        writer.WriteUlov(
                            status: status,
                            landKode: landKode,
                            orgForm: orgForm,
                            descFo: descFo,
                            descNo: descNo);
                        break;
                    }

                case "UREG":
                    {
                        var status = Line.Slice(ParserConsts.TfonOffsetStatus, ParserConsts.TfonLengthStatus).Trim();

                        ReadOnlySpan<char> regNr = default;
                        ReadOnlySpan<char> regName1 = default;
                        ReadOnlySpan<char> regName2 = default;
                        ReadOnlySpan<char> regName3 = default;
                        ReadOnlySpan<char> landKode = default;
                        ReadOnlySpan<char> postalArea = default;
                        ReadOnlySpan<char> mailAdr1 = default;
                        ReadOnlySpan<char> mailAdr2 = default;
                        ReadOnlySpan<char> mailAdr3 = default;
                        if (IsNewOrUpdateChange(status))
                        {
                            regNr = Line.Slice(ParserConsts.UREG_REGNR_OFFSET, ParserConsts.UREG_REGNR_LEN).Trim();
                            regName1 = Line.Slice(ParserConsts.UREG_NAVN1_OFFSET, ParserConsts.UREG_NAVN1_LEN).Trim();
                            regName2 = Line.Slice(ParserConsts.UREG_NAVN2_OFFSET, ParserConsts.UREG_NAVN2_LEN).Trim();
                            regName3 = Line.Slice(ParserConsts.UREG_NAVN3_OFFSET, ParserConsts.UREG_NAVN3_LEN).Trim();
                            landKode = Line.Slice(ParserConsts.UREG_LANDKODE_OFFSET, ParserConsts.UREG_LANDKODE_LEN).Trim();
                            postalArea = Line.Slice(ParserConsts.UREG_POSTSTED_OFFSET, ParserConsts.UREG_POSTSTED_LEN).Trim();
                            mailAdr1 = Line.Slice(ParserConsts.UREG_POSTADRESSE1_OFFSET, ParserConsts.UREG_POSTADRESSE1_LEN).Trim();
                            mailAdr2 = Line.Slice(ParserConsts.UREG_POSTADRESSE2_OFFSET, ParserConsts.UREG_POSTADRESSE2_LEN).Trim();
                            mailAdr3 = Line.Slice(ParserConsts.UREG_POSTADRESSE3_OFFSET, ParserConsts.UREG_POSTADRESSE3_LEN).Trim();
                        }

                        writer.WriteUreg(
                            status: status,
                            regNr: regNr,
                            regName1: regName1,
                            regName2: regName2,
                            regName3: regName3,
                            landKode: landKode,
                            postalArea: postalArea,
                            mailAdr1: mailAdr1,
                            mailAdr2: mailAdr2,
                            mailAdr3: mailAdr3);
                        break;
                    }

                case "KAPI":
                    // altinn doesn't use these, so we ignore them
                    break;

                case "FMVA":
                    {
                        var status = Line.Slice(ParserConsts.FMVA_STATUS_OFFSET, ParserConsts.FMVA_STATUS_LENGTH).Trim();
                        var fmva_type = Line.Slice(ParserConsts.FMVA_TYPE_OFFSET, ParserConsts.FMVA_TYPE_LENGTH).Trim();
                        writer.WriteFmva(status, fmva_type);
                        break;
                    }

                case "KATG":
                case "TKN ":
                    // no longer in use, so we ignore it
                    break;

                /////////////////////////////////////////////////////////////////////////////
                // nodene med navn samendringer, altså rolle og knytninger og samendringer
                case "AAFY": // SK_AAFY
                case "ADOS": // SK_ADOS
                case "AVKL": // SR_AVKL
                case "BEDR": // SK_BEDR
                case "BEST": // SRK_BEST
                case "BOBE": // SRK_BOBE
                case "DAGL": // SRK_DAGL
                case "DELT": // S_DELT
                case "DTPR": // RK_DTPR
                case "DTSO": // RK_DTSO
                case "EIKM": // SK_EIKM
                case "ESGR": // SRK_ESGR
                case "ETDL": // RK_ETDL
                case "FFØR": // SRK_FFØR
                case "FGRP": // SRK_FGRP
                case "FISJ": // K_FISJ
                case "FUSJ": // K_FUSJ
                case "HFOR": // SK_HFOR
                case "HLED": // R_HLED
                case "HLSE": // SK_HLSE
                case "HMDL": // R_HMDL
                case "HNST": // R_HNST
                case "HOST": // S_HOST
                case "HVAR": // R_HVAR
                case "INNH": // SR_INNH
                case "KDAT": // SK_KDAT
                case "KDEB": // SK_KDEB
                case "KENK": // SK_KENK
                case "KGRL": // SK_KGRL
                case "KIRK": // SK_KIRK
                case "KMOR": // SK_KMOR
                case "KOMP": // SRK_KOMP
                case "KONT": // SRK_KONT
                case "KTRF": // SK_KTRF
                case "LEDE": // RK_LEDE
                case "MEDL": // RK_MEDL
                case "NEST": // RK_NEST
                case "OBS": // R_OBS
                case "OPMV": // SK_OPMV
                case "ORGL": // SK_ORGL
                case "POFE": // SR_POFE
                case "POHV": // SR_POHV
                case "PROK": // SR_PROK
                case "READ": // K_READ
                case "REGN": // SRK_REGN
                case "REPR": // SRK_REPR
                case "REVI": // SRK_REVI
                case "RFAD": // K_RFAD
                case "SAM": // RK_SAM
                case "SIFE": // SRK_SIFE
                case "SIGN": // SRK_SIGN
                case "SIHV": // SRK_SIHV
                case "STFT": // SR_STFT
                case "STYR": // S_STYR
                case "UTBG": // SK_UTBG
                case "VARA": // R_VARA
                case "VIFE": // SK_VIFE
                    if (dataOrTextRecord is "D")
                    {
                        // Create SAMENDRINGER node childs (rolle knytning)
                        CreateSamendringerNodeData(writer, Line);
                    }
                    else
                    {
                        // Create SAMENDRINGER node childs (samendring)
                        CreateSamendringerNodeText(writer, Line);
                    }

                    break;

                /////////////////////////////////////////////////////////////////////////////
                // noder med enkle infotyper.
                case "MÅL ": // MAL
                case "KTO ": // KTO
                case "BFOR":
                case "ARBG":
                case "VEDT":
                case "ISEK":
                case "PLFR":
                case "STID":
                case "SLFR":
                case "NYFR":
                case "EVDT":
                case "FVRP":
                case "R-FV": // REGFV
                case "R-FR": // REGFR
                case "R-SR": // REGSR
                case "FVRR":
                case "GRDT":
                case "GRUN":
                case "KJRP":
                case "MPVT":
                case "UVNO":
                case "UENO":
                case "RVFG":
                case "VFOR":
                case "FORM":
                case "RSKP":
                    {
                        var endringsType = Line.Slice(ParserConsts.INFOTYPE_CHANGETYPE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_CHANGETYPE_LENGTH_DEFAULT).Trim();
                        var value = Line.SafeSlice(ParserConsts.INFOTYPE_VALUE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_VALUE_LENGTH_DEFAULT).Trim();
                        writer.WriteSimpleInfoType(recordType.Trim(), endringsType, value);
                        break;
                    }

                case "R-MV": // REGMV
                    {
                        var endringsType = Line.Slice(ParserConsts.INFOTYPE_CHANGETYPE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_CHANGETYPE_LENGTH_DEFAULT).Trim();
                        var value = Line.Slice(ParserConsts.INFOTYPE_VALUE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_VALUE_LENGTH_BOOL).Trim();
                        writer.WriteSimpleInfoType(recordType.Trim(), endringsType, value);
                        break;
                    }

                case "EDAT":
                case "BDAT":
                case "NDAT":
                    {
                        var endringsType = Line.Slice(ParserConsts.INFOTYPE_CHANGETYPE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_CHANGETYPE_LENGTH_DEFAULT).Trim();
                        var value = Line.SafeSlice(ParserConsts.INFOTYPE_VALUE_OFFSET_DEFAULT, ParserConsts.INFOTYPE_VALUE_LENGTH_DEFAULT).Trim();

                        if (!value.IsEmpty)
                        {
                            writer.WriteSimpleInfoType(recordType.Trim(), endringsType, value);
                        }

                        break;
                    }

                /////////////////////////////////////////////////////////////////////////////
                // noder med navn samendringutgaar, dvs alle SAMU.
                case "SAMU": // SAMENDRINGUTGAAR
                    {
                        var samendringsType = Line.Slice(ParserConsts.SamuOffsetType, ParserConsts.SamuLengthType).Trim();
                        writer.WriteSamendringutgaar(samendringsType);
                        break;
                    }

                /////////////////////////////////////////////////////////////////////////////
                // statuser for en organisasjon
                // en organisasjon kan ha flere statuser.
                case "KONK": // Status bankrupt.
                    {
                        var endringsType = Line.Slice(ParserConsts.StatusFieldStatusOffset, ParserConsts.StatusFieldStatusLength).Trim();
                        var kjennelsesDato = Line.Slice(8, 8);
                        writer.WriteStatus(status: recordType.Trim(), endringsType: endringsType, kjennelsesDato: kjennelsesDato);
                        break;
                    }

                case "AKKO": // Opened accord.
                case "BRSL": // Main enterprise (UTLA) has been deleted is bankrupt in its home country.
                case "BRKO": // Main enterprise (UTLA) is under bankruptcy-proceedings or forced liquidation in its home country.
                case "BROP": // Main enterprise (UTLA) is undergoing liquidation in its home country.
                case "FIFO": // Is a finance enterprise.
                case "FIPL": // Status demerger plan.
                case "FITA": // Acquiring company is in demerging.
                case "FLYT": // Decided move across state border.
                case "FUTA": // Acquiring company is in merging.
                case "FUPL": // Status merge plan.
                case "IPF ": // Enterprise is an IPF.
                case "OMPL": // Received transformation plan.
                case "OPFI": // Transferring company in demerging. - Role of type FISJ defines connected unit for demerger.
                case "OPFU": // Disbanded for merge. - Connection of type FUSJ defines connected unit for merger.
                case "OPPL": // Status disbanded.
                case "OSDL": // Probate court due to CEO.
                case "OSED": // Probate court due to EØFG participants.
                case "OSBA": // Probate court due to cooperatives not registered.
                case "OSEF": // Probate court due to EØFG business manager.
                case "OSEV": // Probate court due to EØFG duration.
                case "OSKA": // Probate court from FR due to capital below 100000.
                case "OSKP": // Probate court due to capital.
                case "OSRE": // Probate court due to accountant.
                case "OSST": // Probate court due to board.
                case "SKRR": // Probate court from RR.
                case "TVBA": // Forcibly disbanded due to cooperative not registered.
                case "TVDL": // Forcibly disbanded due to CEO.
                case "TVKA": // Forcibly disbanded due to capital below 100000.
                case "TVOV": // Forcibly disbanded – Taken over by probate court.
                case "TVRE": // Forcibly disbanded due to accountant.
                case "TVRR": // Forcibly disbanded due to yearly Financial Statements.
                case "TVST": // Forcibly disbanded due to board.
                case "USL ": // Status - being deleted.
                case "USYS": // Status - unmanned organization.
                    {
                        var endringsType = Line.Slice(ParserConsts.StatusFieldStatusOffset, ParserConsts.StatusFieldStatusLength).Trim();
                        writer.WriteStatus(status: recordType.Trim(), endringsType: endringsType, kjennelsesDato: default);
                        break;
                    }

                /////////////////////////////////////////////////////////////////////////////
                // fullmaktsnoder
                case "FMKA":
                case "FMAK":
                case "FMAP":
                case "FMKL":
                case "FMUU":
                case "FSTR":
                case "TRAK":
                case "KLAN":
                    {
                        // not in use, ignored
                        break;
                    }

                /////////////////////////////////////////////////////////////////////////////
                // ignorerte noder
                case "MANR": // Matrikkeladresse
                case "R-FS": // Utgått infotype
                case "SYSS": // Antall ansatte
                case "ÅRSO": // Sist innsendte årsoppgjør
                    {
                        // not in use, ignored
                        break;
                    }

                default:
                    {
                        Log.UnknownOrganizationRecordType(_logger, recordType.Trim().ToString());
                        break;
                    }
            }
        }

        writer.WriteOrganizationEnd();
        return org;
    }

    private void CreateSamendringerNodeData(
        CcrXmlWriter writer,
        scoped ReadOnlySpan<char> line)
    {
        var recordType = GetRecordType(line);
        var type = line.Slice(ParserConsts.KnytRollOffsetType, ParserConsts.KnytRollLengthType).Trim();
        var dataOrTextRecord = line.Slice(ParserConsts.KnytRollOffsetData, ParserConsts.KnytRollLengthData).Trim();

        switch (type)
        {
            case "R": // TypeRolle
                {
                    var status = line.Slice(ParserConsts.RollOffsetStatus, ParserConsts.RollLengthStatus).Trim();
                    var rolleFnr = line.Slice(ParserConsts.RollOffsetRolleFnr, ParserConsts.RollLengthRolleFnr).Trim();
                    var rolleFratraadt = line.Slice(ParserConsts.RollOffsetAltStatus, ParserConsts.RollLengthAltStatus).Trim();

                    var rolleAnsvarsandel = line.Slice(ParserConsts.RollOffsetAnsvarsandel, ParserConsts.RollLengthAnsvarsandel).Trim();
                    var rolleValgtav = line.Slice(ParserConsts.RollOffsetValgtav, ParserConsts.RollLengthValgtav).Trim();
                    var rolleRekkefoelge = line.Slice(ParserConsts.RollOffseteRekkefoelge, ParserConsts.RollLengtheRekkefoelge).Trim().TrimStart('0');
                    var fornavn = line.Slice(ParserConsts.RollOffsetFornavn, ParserConsts.RollLengthFornavn).Trim();
                    var mellomnavn = line.Slice(ParserConsts.RollOffsetMellomnavn, ParserConsts.RollLengthMellomnavn).Trim();
                    var slektsnavn = line.Slice(ParserConsts.RollOffsetSlektsnavn, ParserConsts.RollLengthSlektsnavn).Trim();
                    var postnr = line.Slice(ParserConsts.RollOffsetPostnr, ParserConsts.RollLengthPostnr).Trim();
                    var adresse1 = line.Slice(ParserConsts.RollOffsetAdresse1, ParserConsts.RollLengthAdresse1).Trim();
                    var adresse2 = line.Slice(ParserConsts.RollOffsetAdresse2, ParserConsts.RollLengthAdresse2).Trim();
                    var adresse3 = line.Slice(ParserConsts.RollOffsetAdresse3, ParserConsts.RollLengthAdresse3).Trim();
                    var adresseLandkode = line.Slice(ParserConsts.RollOffsetAdresseLandkode, ParserConsts.RollLengthAdresseLandkode).Trim();
                    var personstatus = line.Slice(ParserConsts.RollOffsetPersonstatus, ParserConsts.RollLengthPersonstatus).Trim();

                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);

                    writer.WriteOptionalTextElementNode("rolleAnsvarsandel", rolleAnsvarsandel);
                    writer.WriteOptionalTextElementNode("rolleFratraadt", rolleFratraadt);
                    writer.WriteOptionalTextElementNode("rolleValgtav", rolleValgtav);
                    writer.WriteOptionalTextElementNode("rolleRekkefoelge", rolleRekkefoelge);
                    writer.WriteOptionalTextElementNode("rolleFoedselsnr", rolleFnr);
                    writer.WriteOptionalTextElementNode("fornavn", fornavn);
                    writer.WriteOptionalTextElementNode("mellomnavn", mellomnavn);
                    writer.WriteOptionalTextElementNode("slektsnavn", slektsnavn);
                    writer.WriteOptionalTextElementNode("postnr", postnr);
                    writer.WriteOptionalTextElementNode("adresse1", adresse1);
                    writer.WriteOptionalTextElementNode("adresse2", adresse2);
                    writer.WriteOptionalTextElementNode("adresse3", adresse3);
                    writer.WriteOptionalTextElementNode("adresseLandkode", adresseLandkode);
                    writer.WriteOptionalTextElementNode("personstatus", personstatus);

                    writer.WriteSamendringEnd();
                    break;
                }

            case "K": // TypeKnytning
                {
                    var status = line.Slice(ParserConsts.KnytOffsetStatus, ParserConsts.KnytLengthStatus).Trim();
                    var knytningOrgnr = line.Slice(ParserConsts.KnytOffsetKnyttetTilOrgnr, ParserConsts.KnytLengthKnyttetTilOrgnr).Trim();
                    var knytningFratraadt = line.Slice(ParserConsts.RollOffsetAltStatus, ParserConsts.RollLengthAltStatus).Trim();

                    var knytningAnsvarsandel = line.Slice(ParserConsts.RollOffsetAnsvarsandel, ParserConsts.RollLengthAnsvarsandel).Trim();
                    var knytningValgtav = line.Slice(ParserConsts.KnytOffsetValgtav, ParserConsts.KnytLengthValgtav).Trim();
                    var knytningRekkefoelge = line.Slice(ParserConsts.KnytOffsetRekkefoelge, ParserConsts.KnytLengthRekkefoelge).Trim().TrimStart('0');
                    var korrektOrganisasjonsnummer = line.Slice(ParserConsts.KnytOffsetKorrektOrganisasjonsnummer, ParserConsts.KnytLengthKorrektOrganisasjonsnummer).Trim();

                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);

                    writer.WriteOptionalTextElementNode("knytningAnsvarsandel", knytningAnsvarsandel);
                    writer.WriteOptionalTextElementNode("knytningFratraadt", knytningFratraadt);
                    writer.WriteOptionalTextElementNode("knytningOrganisasjonsnummer", knytningOrgnr);
                    writer.WriteOptionalTextElementNode("knytningValgtav", knytningValgtav);
                    writer.WriteOptionalTextElementNode("knytningRekkefoelge", knytningRekkefoelge);
                    writer.WriteOptionalTextElementNode("korrektOrganisasjonsnummer", korrektOrganisasjonsnummer);

                    writer.WriteSamendringEnd();
                    break;
                }

            default:
                {
                    Log.UnknownSamendringType(_logger, type.Trim().ToString());
                    break;
                }
        }
    }

    private void CreateSamendringerNodeText(
        CcrXmlWriter writer,
        scoped ReadOnlySpan<char> line)
    {
        var recordType = GetRecordType(line);
        var status = line.Slice(ParserConsts.SamOffsetStatus, ParserConsts.SamLengthStatus).Trim();
        var type = line.Slice(ParserConsts.KnytRollOffsetType, ParserConsts.KnytRollLengthType).Trim();
        var dataOrTextRecord = line.Slice(ParserConsts.KnytRollOffsetData, ParserConsts.KnytRollLengthData).Trim();

        switch (type)
        {
            case "S": // TypeSamendring
                {
                    var samendringText = line.Slice(ParserConsts.SamOffsetText, ParserConsts.SamLengthText).Trim();
                    var position = line.Slice(ParserConsts.FreeTextPositionOffset, ParserConsts.FreeTextPositionLength).Trim();

                    if (samendringText.IsEmpty)
                    {
                        return;
                    }

                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);

                    writer.WriteOptionalTextElementNode("plassering", position);
                    writer.WriteOptionalTextElementNode("samendringfritTekstlinje", samendringText);

                    writer.WriteSamendringEnd();
                    break;
                }

            case "R": // TypeRolle
                {
                    var roleSsn = line.Slice(ParserConsts.RoleFreetextOffsetSsn, ParserConsts.RoleFreetextLengthSsn).Trim();
                    var roleText = line.Slice(ParserConsts.RoleFreetextOffsetText, ParserConsts.RoleFreetextLengthText).Trim();

                    if (roleText.IsEmpty)
                    {
                        return;
                    }

                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);

                    writer.WriteOptionalTextElementNode("rollefritFoedselsnr", roleSsn);
                    writer.WriteOptionalTextElementNode("rollefritTekstlinje", roleText);

                    writer.WriteSamendringEnd();
                    break;
                }

            case "K": // TypeKnytning
                {
                    var knytningOrgnr = line.Slice(ParserConsts.ConnectionFreetextOffsetOrgnumber, ParserConsts.ConnectionFreetextLengthOrgnumber).Trim();
                    var knytningText = line.Slice(ParserConsts.ConnectionFreetextOffsetText, ParserConsts.ConnectionFreetextLengthText).Trim();

                    if (knytningText.IsEmpty)
                    {
                        return;
                    }

                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);

                    writer.WriteOptionalTextElementNode("knytningfritOrganisasjonsnummer", knytningOrgnr);
                    writer.WriteOptionalTextElementNode("knytningfritTekstlinje", knytningText);

                    writer.WriteSamendringEnd();
                    break;
                }

            default:
                {
                    writer.WriteSamendringStart(dataOrTextRecord, recordType.Trim(), status, type);
                    writer.WriteSamendringEnd();
                    break;
                }
        }
    }

    private async ValueTask<HeaderRecord> ParseHeader(CancellationToken cancellationToken)
    {
        if (!await Peek("HEAD", cancellationToken))
        {
            ThrowHelper.ThrowInvalidDataException("Expected header record at the beginning of the file.");
        }

        // consume header line
        await Read(cancellationToken);
        var avsender = Line.Slice(ParserConsts.HeadAttrAvsenderOffset, ParserConsts.HeadAttrAvsenderLength).Trim();
        var dato = Line.Slice(ParserConsts.HeadAttrDatoOffset, ParserConsts.HeadAttrDatoLength).Trim();
        var kjoereNr = Line.Slice(ParserConsts.HeadAttrKjoerenrOffset, ParserConsts.HeadAttrKjoerenrLength).Trim();
        var mottaker = Line.Slice(ParserConsts.HeadAttrMottakerOffset, ParserConsts.HeadAttrMottakerLength).Trim();
        var type = Line.Slice(ParserConsts.HeadAttrTypeOffset, ParserConsts.HeadAttrTypeLength).Trim();

        return new()
        {
            Avsender = avsender.ToString(),
            Dato = dato.ToString(),
            KjoereNr = kjoereNr.ToString(),
            Mottaker = mottaker.ToString(),
            Type = type.ToString(),
        };
    }

    private async ValueTask<OrganizationRecord> ParseOrganizationRecord(CancellationToken cancellationToken)
    {
        if (!await Peek("ENH ", cancellationToken))
        {
            ThrowHelper.ThrowInvalidDataException("Expected organization record.");
        }

        // consume header line
        await Read(cancellationToken);
        var orgNr = Line.Slice(ParserConsts.EnhOffsetorgnr, ParserConsts.EnhLengthOrgnr).Trim();
        var orgForm = Line.Slice(ParserConsts.EnhOffsetunittype, ParserConsts.EnhLengthunittype).Trim();
        var hovedsaksType = Line.Slice(ParserConsts.EnhOffsetstatus, ParserConsts.EnhLengthstatus).Trim();
        var undersaksType = Line.Slice(ParserConsts.EnhOffsetsubstatus, ParserConsts.EnhLengthsubstatus).Trim();
        var datoFoedt = Line.Slice(ParserConsts.EnhOffsetestablished, ParserConsts.EnhLengthEstablished).Trim();
        var datoSistEndret = Line.Slice(ParserConsts.EnhOffsetlastchanged, ParserConsts.EnhLengthLastChanged).Trim();
        var foersteOverfoering = Line.Slice(ParserConsts.EnhOffsetfoersteoverf, ParserConsts.EnhLengthFoersteOverf).Trim();

        if (!OrganizationIdentifier.TryParse(orgNr, CultureInfo.InvariantCulture, out var orgNrValue))
        {
            ThrowHelper.ThrowInvalidDataException($"Invalid organization number in organization record: {orgNr}");
        }

        return new()
        {
            OrgNr = orgNrValue,
            OrgForm = orgForm.ToString(),
            HovedsaksType = hovedsaksType.ToString(),
            UndersaksType = undersaksType.ToString(),
            DatoFoedt = datoFoedt.ToString(),
            DatoSistEndret = datoSistEndret.ToString(),
            FoersteOverfoering = foersteOverfoering.ToString(),
        };
    }

    private async ValueTask<bool> Peek(string expectedRecordType, CancellationToken cancellationToken)
    {
        if (!await Peek(cancellationToken))
        {
            return false;
        }

        var recordType = GetRecordType(NextLine);
        return recordType.SequenceEqual(expectedRecordType);
    }

    private ValueTask<bool> Peek(CancellationToken cancellationToken)
    {
        if (_nextLine.HasValue)
        {
            return ValueTask.FromResult(true);
        }

        return PeekCore(this, cancellationToken);

        static async ValueTask<bool> PeekCore(CcrFlatFileProcessor processor, CancellationToken cancellationToken)
        {
            var nextLine = await processor.FetchLineCore(cancellationToken);
            processor._nextLine = nextLine;

            return nextLine.HasValue;
        }
    }

    private ValueTask<bool> Read(CancellationToken cancellationToken)
    {
        if (_currentLine.HasValue)
        {
            _currentLine.Dispose();
        }

        if (_nextLine.HasValue)
        {
            _currentLine = _nextLine;
            _nextLine = default;

            return ValueTask.FromResult(true);
        }

        return ReadCore(this, cancellationToken);

        static async ValueTask<bool> ReadCore(CcrFlatFileProcessor processor, CancellationToken cancellationToken)
        {
            var nextLine = await processor.FetchLineCore(cancellationToken);
            processor._currentLine = nextLine;

            return nextLine.HasValue;
        }
    }

    private async ValueTask<LineBuffer> FetchLineCore(CancellationToken cancellationToken)
    {
        // we pad all lines to this length such that subslicing later is safe
        const int MIN_LINE_LENGTH = 500;

        while (await _reader.ReadNext(cancellationToken))
        {
            if (_reader.Line.IsEmpty)
            {
                // we ignore empty lines
                continue;
            }

            // max line length is 4KB for now
            var buffer = ArrayPool<char>.Shared.Rent(4 * 1024);
            if (!_encoding.TryGetChars(_reader.Line, buffer, out var written))
            {
                ThrowHelper.ThrowInvalidDataException("Line is too long to process.");
            }

            if (written < MIN_LINE_LENGTH)
            {
                buffer.AsSpan(written, MIN_LINE_LENGTH - written).Fill(' ');
                written = MIN_LINE_LENGTH;
            }

            return new LineBuffer(buffer, written);
        }

        // end of input
        return default;
    }

    private static ReadOnlySpan<char> GetRecordType(in ReadOnlySpan<char> line)
    {
        return line.Slice(ParserConsts.OffsetRecord, ParserConsts.LengthRecord);
    }

    private static bool IsNewOrUpdateChange(in ReadOnlySpan<char> status)
        => status.SequenceEqual("N") || status.SequenceEqual("K");

    private record HeaderRecord
    {
        public required string Avsender { get; init; }

        public required string Dato { get; init; }

        public required string KjoereNr { get; init; }

        public required string Mottaker { get; init; }

        public required string Type { get; init; }
    }

    private record OrganizationRecord
    {
        public required OrganizationIdentifier OrgNr { get; init; }

        public required string OrgForm { get; init; }

        public required string HovedsaksType { get; init; }

        public required string UndersaksType { get; init; }

        public required string DatoFoedt { get; init; }

        public required string DatoSistEndret { get; init; }

        public required string FoersteOverfoering { get; init; }
    }

    private struct LineBuffer
        : IDisposable
    {
        private char[]? _buffer;
        private int _length;

        public LineBuffer(char[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
        }

        public readonly bool HasValue
            => _length > 0;

        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<char>.Shared.Return(_buffer, clearArray: true);
            }

            _buffer = null;
            _length = 0;
        }

        public static implicit operator ReadOnlySpan<char>(LineBuffer line)
            => line._buffer.AsSpan(0, line._length);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Unknown record type '{RecordType}' encountered in CCR file.")]
        public static partial void UnknownOrganizationRecordType(ILogger logger, string recordType);

        [LoggerMessage(1, LogLevel.Warning, "Unknown samendring type '{SamendringType}' encountered in CCR file.")]
        public static partial void UnknownSamendringType(ILogger logger, string samendringType);
    }
}
