using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Xml;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Core;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.Utils;
using Altinn.Register.Integrations.Ccr.Xml;
using Altinn.Register.PartyImport.Ccr;
using Asp.Versioning;
using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Nerdbank.Streams;

namespace Altinn.Register.Controllers;

/// <summary>
/// Controller for handling CCR update requests.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("enhets-registeret/api/v{version:apiVersion}")]
public partial class CcrController
    : ControllerBase
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CcrController> _logger;
    private readonly CcrControllerMeters _meters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrController"/> class.
    /// </summary>
    public CcrController(
        TimeProvider timeProvider,
        ILogger<CcrController> logger,
        IMetricsProvider metricsProvider)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _meters = metricsProvider.Get<CcrControllerMeters>();
    }

    /// <summary>
    /// Updates the CCR data for a party.
    /// </summary>
    /// <remarks>
    /// Enhetsregisteret hurtig-oppdattering.
    /// </remarks>
    [HttpPost("update.svc")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [RequestSizeLimit(50_000_000 /* 50 MB */)]
    public async Task Update(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receivedAt = _timeProvider.GetUtcNow();
            await UpdateFromCcr(cancellationToken);

            await WriteAltinnSoapSuccess(receivedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.UnauthorizedUpdateError(_logger, ex);
            await WriteAltinnSoapFault("Client authentication failed", isClientFault: true);
        }
        catch (XmlException ex)
        {
            Log.InternalUpdateError(_logger, ex);
            await WriteAltinnSoapFault("Malformed XML in request", isClientFault: true);
        }
        catch (Exception ex)
        {
            Log.InternalUpdateError(_logger, ex);
            await WriteAltinnSoapFault("Internal server error", isClientFault: false);
        }
    }

    private async Task UpdateFromCcr(CancellationToken cancellationToken)
    {
        using var activity = RegisterTelemetry.StartActivity(name: "handle ccr online update");

        using var seq = new Sequence<byte>(ArrayPool<byte>.Shared);
        await Request.BodyReader.CopyToAsync(seq, cancellationToken);

        var result = CcrUpdateEnvelopeReader.ReadEnvelope(seq.AsReadOnlySequence);
        activity?.SetTag("ccr.client.username", result.UserName);
        seq.Reset();

        {
            using var writer = new BufferTextWriter(seq, Encoding.UTF8);
            writer.Write("""<?xml version="1.0" encoding="utf-8"?>""");
            writer.Write(SkipOptionalXmlDeclaration(result.Payload.AsSpan()));
            writer.Flush();
        }

        var sender = HttpContext.RequestServices.GetRequiredService<ICommandSender>();
        var ccrService = HttpContext.RequestServices.GetRequiredService<CcrService>();
        CcrClientIdentitySettings? clientSettings = null;
        if (HttpContext.Connection.RemoteIpAddress is null
            || string.IsNullOrWhiteSpace(result.UserName)
            || string.IsNullOrWhiteSpace(result.Password)
            || !ccrService.AuthorizeCcrClient(result.UserName, result.Password, HttpContext.Connection.RemoteIpAddress, out clientSettings))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "unauthorized client");
            ThrowHelper.ThrowUnauthorizedAccessException("Invalid CCR client credentials or missing source IP address");
        }

        try
        {
            var federate = clientSettings.Federate ?? true;
            activity?.SetTag("ccr.federate", federate);
            var command = new ImportCcrXmlCommand
            {
                BatchId = null,
                OrganizationIdentifier = null,
                Document = seq.AsReadOnlySequence.ToArray(),
                Federate = federate,
                ClientName = result.UserName,
            };

            await sender.Send(command, cancellationToken);
            Response.Headers.Append("X-Altinn-Register-Ccr-CommandId", command.CommandId.ToString("D"));
            _meters.OnlineUpdates.Add(1, [new("ccr.client.username", result.UserName)]);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "error processing CCR update");
            throw;
        }

        static ReadOnlySpan<char> SkipOptionalXmlDeclaration(ReadOnlySpan<char> xml)
        {
            if (xml.StartsWith('\uFEFF'))
            {
                xml = xml[1..];
            }

            if (!xml.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                return xml;
            }

            var end = xml.IndexOf("?>", StringComparison.Ordinal);
            return end >= 0 ? xml[(end + 2)..] : xml;
        }
    }

    private async Task WriteAltinnSoapSuccess(DateTimeOffset timeReceived)
    {
        const string nsSoap = "http://schemas.xmlsoap.org/soap/envelope/";
        const string nsRegister = "http://www.altinn.no/services/Register/ER/2013/06";
        const string nsXsi = "http://www.w3.org/2001/XMLSchema-instance";

        Response.ContentType = "text/xml; charset=utf-8";

        var receiptXml = $"""
            <?xml version="1.0" encoding="UTF-8"?><ERReceipt schemaVersion="1.0" xmlns:xsi="{nsXsi}" xsi:noNamespaceSchemaLocation="GovAgencyReceipt.xsd"><DataUnitInReceipt receiptType="ER" status="OK_ER_DATA_PROCESSED" timeReceived="{timeReceived.UtcDateTime:yyyy-MM-ddTHH:mm:ss}"><Message><MessageEntry>ER data processed ok</MessageEntry></Message></DataUnitInReceipt></ERReceipt>
            """;

        await using var writer = XmlWriter.Create(Response.Body, new XmlWriterSettings { Async = true, CloseOutput = false });
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(prefix: "s", localName: "Envelope", ns: nsSoap);
        await writer.WriteStartElementAsync(prefix: "s", localName: "Body", ns: nsSoap);
        await writer.WriteStartElementAsync(prefix: null, localName: "SubmitERDataBasicResponse", ns: nsRegister);
        await writer.WriteElementStringAsync(prefix: null, localName: "SubmitERDataBasicResult", ns: nsRegister, value: receiptXml);
        await writer.WriteEndElementAsync(); // SubmitERDataBasicResponse
        await writer.WriteEndElementAsync(); // Body
        await writer.WriteEndElementAsync(); // Envelope
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    private async Task WriteAltinnSoapFault(string faultString, bool isClientFault)
    {
        const string nsSoap = "http://schemas.xmlsoap.org/soap/envelope/";
        const string nsAltinnFault = "http://www.altinn.no/services/common/fault/2009/10";
        const string nsXsi = "http://www.w3.org/2001/XMLSchema-instance";

        Response.StatusCode = (int)(isClientFault ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError);
        Response.ContentType = "text/xml; charset=utf-8";

        await using var writer = XmlWriter.Create(Response.Body, new XmlWriterSettings { Async = true, CloseOutput = false });
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(prefix: "s", localName: "Envelope", ns: nsSoap);
        await writer.WriteStartElementAsync(prefix: "s", localName: "Body", ns: nsSoap);
        await writer.WriteStartElementAsync(prefix: "s", localName: "Fault", ns: nsSoap);

        await writer.WriteElementStringAsync(prefix: null, localName: "faultcode", ns: null, value: isClientFault ? "s:Client" : "s:Server");

        await writer.WriteStartElementAsync(prefix: null, localName: "faultstring", ns: null);
        await writer.WriteAttributeStringAsync(prefix: "xml", localName: "lang", ns: null, value: "nb-NO");
        await writer.WriteStringAsync("An error occurred");
        await writer.WriteEndElementAsync(); // faultstring

        await writer.WriteStartElementAsync(prefix: null, localName: "detail", ns: null);
        await writer.WriteStartElementAsync(prefix: null, localName: "AltinnFault", ns: nsAltinnFault);
        await writer.WriteAttributeStringAsync(prefix: "xmlns", localName: "i", ns: null, value: nsXsi);
        await writer.WriteElementStringAsync(prefix: null, localName: "AltinnErrorMessage", ns: nsAltinnFault, value: faultString);
        await writer.WriteElementStringAsync(prefix: null, localName: "AltinnExtendedErrorMessage", ns: nsAltinnFault, value: "No further information available");
        await writer.WriteElementStringAsync(prefix: null, localName: "AltinnLocalizedErrorMessage", ns: nsAltinnFault, value: "No further information available");
        await writer.WriteElementStringAsync(prefix: null, localName: "ErrorGuid", ns: nsAltinnFault, value: Guid.CreateVersion7().ToString());
        await writer.WriteElementStringAsync(prefix: null, localName: "ErrorID", ns: nsAltinnFault, value: "0");
        await writer.WriteElementStringAsync(prefix: null, localName: "UserGuid", ns: nsAltinnFault, value: "-no value-");
        await writer.WriteElementStringAsync(prefix: null, localName: "UserId", ns: nsAltinnFault, value: "0");
        await writer.WriteEndElementAsync(); // AltinnFault
        await writer.WriteEndElementAsync(); // detail

        await writer.WriteEndElementAsync(); // Fault
        await writer.WriteEndElementAsync(); // Body
        await writer.WriteEndElementAsync(); // Envelope
        await writer.WriteEndDocumentAsync();
        await writer.FlushAsync();
    }

    private static partial class Log
    {
        [LoggerMessage(5, LogLevel.Error, "Error during internal update from CCR")]
        public static partial void InternalUpdateError(ILogger logger, Exception ex);

        [LoggerMessage(6, LogLevel.Error, "Unauthorized update attempt from CCR")]
        public static partial void UnauthorizedUpdateError(ILogger logger, Exception ex);
    }

    /// <summary>
    /// Meters for <see cref="CcrController"/>.
    /// </summary>
    private sealed class CcrControllerMeters(Meter meter)
        : IMetrics<CcrControllerMeters>
    {
        /// <summary>
        /// Gets a counter for the number of online updates from CCR.
        /// </summary>
        public Counter<int> OnlineUpdates { get; }
            = meter.CreateCounter<int>("altinn.register.ccr.online-updates", "The number of online updates from CCR.");

        /// <inheritdoc/>
        public static CcrControllerMeters Create(Meter meter)
            => new CcrControllerMeters(meter);
    }
}
