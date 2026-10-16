using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Configuration;
using Altinn.Register.Contracts;
using Altinn.Register.Conventions;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Sire;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.PartyImport.Sire;
using Asp.Versioning;
using CommunityToolkit.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Nerdbank.Streams;

namespace Altinn.Register.Controllers;

/// <summary>
/// Proxy to SBL bridge for test environments.
/// </summary>
[ApiController]
[ApiVersion(0.0)]
[DevTestCondition]
[Authorize(Policy = "Debug")]
[Route("register/api/v{version:apiVersion}/debug")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DebugController
    : ControllerBase
{
    private readonly IOptions<GeneralSettings> _generalSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugController"/> class.
    /// </summary>
    public DebugController(
        IOptions<GeneralSettings> generalSettings)
    {
        _generalSettings = generalSettings;
    }

    /// <summary>
    /// Temp test
    /// </summary>
    [HttpGet("npr/raw/{fnr}")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> Test(
        [FromServices] IHttpClientFactory httpClientFactory,
        string fnr,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;
        PersonIdentifier? personIdentifier = null;
        if (string.IsNullOrWhiteSpace(fnr))
        {
            builder.Add(StdValidationErrors.Required, "/$PATH/fnr");
        }
        else if (!PersonIdentifier.TryParse(fnr, provider: null, out personIdentifier))
        {
            builder.Add(ValidationErrors.InvalidPersonNumber, "/$PATH/fnr");
        }

        if (builder.TryBuild(out var error))
        {
            return error.ToActionResult();
        }

        Debug.Assert(personIdentifier is not null);
        using var client = httpClientFactory.CreateClient(nameof(INprClient));
        using var response = await client.GetAsync($"folkeregisteret/offentlig-med-hjemmel/api/v1/personer/{personIdentifier!}?part=navn&part=foedsel&part=bostedsadresse&part=doedsfall&part=status&part=oppholdsadresse&part=familierelasjon&part=postadresse&part=postadresseIUtlandet&part=vergemaalEllerFremtidsfullmakt&part=sivilstand&part=statsborgerskap&part=historikk&part=identifikasjonsnummer&part=deltBosted&part=adressebeskyttelse&part=utenlandskPersonidentifikasjon&part=foreldreansvar&part=innflytting&part=utflytting&part=foedselINorge&part=opphold&part=RettsligHandleevne&part=bibehold&part=brukAvSamiskSpraak", cancellationToken);

        Response.StatusCode = (int)response.StatusCode;
        HttpProxyResult.CopyHeaders(response.Headers, Response);
        HttpProxyResult.CopyHeaders(response.Content.Headers, Response);
        await response.Content.CopyToAsync(Response.Body, cancellationToken);

        return new EmptyResult();
    }

    /// <summary>
    /// Temp test
    /// </summary>
    [HttpGet("npr/person/{fnr}")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> Test2(
        string fnr,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;
        PersonIdentifier? personIdentifier = null;
        if (string.IsNullOrWhiteSpace(fnr))
        {
            builder.Add(StdValidationErrors.Required, "/$PATH/fnr");
        }
        else if (!PersonIdentifier.TryParse(fnr, provider: null, out personIdentifier))
        {
            builder.Add(ValidationErrors.InvalidPersonNumber, "/$PATH/fnr");
        }

        if (builder.TryBuild(out var error))
        {
            return error.ToActionResult();
        }

        Debug.Assert(personIdentifier is not null);
        var client = HttpContext.RequestServices.GetRequiredService<INprClient>();
        var response = await client.GetPerson(personIdentifier!, cancellationToken);
        if (response.IsProblem)
        {
            return response.Problem.ToActionResult();
        }

        return Ok(response.Value);
    }

    /// <summary>
    /// Temp test
    /// </summary>
    [HttpGet("sire/raw/{orgNo}")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> GetSireRaw(
        [FromServices] IHttpClientFactory httpClientFactory,
        string orgNo,
        CancellationToken cancellationToken = default)
    {
        ValidationProblemBuilder builder = default;
        OrganizationIdentifier? organizationIdentifier = null;
        if (string.IsNullOrWhiteSpace(orgNo))
        {
            builder.Add(StdValidationErrors.Required, "/$PATH/orgNo");
        }
        else if (!OrganizationIdentifier.TryParse(orgNo, provider: null, out organizationIdentifier))
        {
            builder.Add(ValidationErrors.InvalidOrganizationNumber, "/$PATH/orgNo");
        }

        if (builder.TryBuild(out var error))
        {
            return error.ToActionResult();
        }

        Debug.Assert(organizationIdentifier is not null);
        using var client = httpClientFactory.CreateClient(nameof(ISireClient));
        using var response = await client.GetAsync(
            $"v1/digdir/{organizationIdentifier}",
            cancellationToken);

        Response.StatusCode = (int)response.StatusCode;
        HttpProxyResult.CopyHeaders(response.Headers, Response);
        HttpProxyResult.CopyHeaders(response.Content.Headers, Response);
        await response.Content.CopyToAsync(Response.Body, cancellationToken);

        return new EmptyResult();
    }

    /// <summary>
    /// Temp test
    /// </summary>
    [HttpGet("sire/raw/events")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> GetSireEvents(
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery(Name = "from")] uint sequenceNumber = 0,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(nameof(ISireClient));
        if (sequenceNumber == 0)
        {
            var startResponse = await client.GetFromJsonAsync<SireStartModel>("https://sire-events/v1/hendelser/start", cancellationToken);
            sequenceNumber = startResponse!.SequenceNumber;
        }

        using var response = await client.GetAsync($"https://sire-events/v1/hendelser?fraSekvensnummer={sequenceNumber}&antall=10", cancellationToken);

        Response.StatusCode = (int)response.StatusCode;
        HttpProxyResult.CopyHeaders(response.Headers, Response);
        HttpProxyResult.CopyHeaders(response.Content.Headers, Response);
        await response.Content.CopyToAsync(Response.Body, cancellationToken);

        return new EmptyResult();
    }

    /// <summary>
    /// Manually trigger SIRE enrichment for an organization.
    /// </summary>
    [HttpPost("sire/org/{orgNo}")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> EnrichSireOrganization(
        string orgNo,
        [FromServices] ISireClient sireClient,
        CancellationToken cancellationToken = default)
    {
        if (!OrganizationIdentifier.TryParse(orgNo, provider: null, out var orgId))
        {
            return BadRequest("Invalid organization number");
        }

        var result = await sireClient.GetOrganization(orgId, cancellationToken);

        if (result.IsProblem)
        {
            return result.Problem.ToActionResult();
        }

        return Ok(result.Value);
    }

    private sealed record SireStartModel
    {
        [JsonPropertyName("sekvensnummer")]
        public required uint SequenceNumber { get; init; }
    }

    /// <summary>
    /// Manually trigger one run of the SIRE import job (poll feed + enqueue commands).
    /// Useful for local testing without waiting for the 10-minute scheduled interval.
    /// </summary>
    [HttpPost("sire/import/trigger")]
    [Authorize(Policy = "Debug")]
    public async Task<IActionResult> TriggerSireImport(
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var job = ActivatorUtilities.CreateInstance<SireImportJob>(scope.ServiceProvider);

        try
        {
            await job.RunOnceAsync(cancellationToken);
            return Ok(new { status = "completed", message = "SireImportJob ran one cycle." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    /// <summary>
    /// Gets the party changes.
    /// </summary>
    /// <param name="changeId">The change id to start from.</param>
    [HttpGet("parties/partychanges/{changeId:int}")]
    public IActionResult PartyChanges(int changeId)
        => ForwardTo($"parties/partychanges/{changeId}");

    /// <summary>
    /// Gets the roles provided by a party.
    /// </summary>
    /// <param name="partyId">The party id.</param>
    [HttpGet("parties/partyroles/{partyId:int}")]
    public IActionResult PartyRoles(int partyId)
        => ForwardTo($"parties/partyroles/{partyId}");

    /// <summary>
    /// Gets the party.
    /// </summary>
    /// <param name="partyId">The party id.</param>
    [HttpGet("parties/{partyId:int}")]
    public IActionResult Party(int partyId)
        => ForwardTo($"parties/{partyId}");

    /// <summary>
    /// Gets trace info.
    /// </summary>
    [HttpGet("trace")]
    [AllowAnonymous]
    public ActionResult<ActivityInfo> Trace()
    {
        var activity = GetRootmost(Activity.Current);
        var requestInfo = GetRequestInfo(Request);
        return new ActivityInfo(activity, Request.Headers.TraceParent, requestInfo);

        static Activity? GetRootmost(Activity? activity)
        {
            if (activity is null)
            {
                return null;
            }

            while (activity.Parent is not null)
            {
                activity = activity.Parent;
            }

            return activity;
        }

        static RequestInfo GetRequestInfo(HttpRequest request)
        {
            var method = request.Method;
            var host = request.Host;
            var path = request.Path;
            var remoteIp = request.HttpContext.Connection.RemoteIpAddress;

            return new RequestInfo(method, host, path, remoteIp);
        }
    }

    /// <summary>
    /// Manually trigger import of a party from A2.
    /// </summary>
    /// <param name="command">The import command.</param>
    /// <param name="sender">The command sender.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    [HttpPost("import/a2")]
    public async Task<IActionResult> ImportA2Party(
        [FromBody] ImportA2PartyCommand command,
        [FromServices] ICommandSender sender,
        CancellationToken cancellationToken = default)
    {
        await sender.Send(command, cancellationToken);

        return Ok(command);
    }

    [NonAction]
    private HttpProxyResult ForwardTo(string path)
    {
        HttpRequestMessage? request = null;

        try
        {
            var settings = _generalSettings.Value;
            var endpointUrlBuilder = new UriBuilder($"{settings.BridgeApiEndpoint}{path}");

            if (Request.QueryString.HasValue)
            {
                endpointUrlBuilder.Query = MergeQueryString(endpointUrlBuilder.Query, Request.QueryString);
            }

            var method = HttpMethod.Parse(Request.Method);
            request = new HttpRequestMessage(method, endpointUrlBuilder.Uri);

            var result = new HttpProxyResult(request);
            request = null;
            return result;
        }
        finally
        {
            request?.Dispose();
        }
    }

    [NonAction]
    private static string MergeQueryString(string query, QueryString toAppend)
    {
        Debug.Assert(toAppend.HasValue);

        if (string.IsNullOrWhiteSpace(query))
        {
            return toAppend.Value;
        }

        return string.Create(query.Length + toAppend.Value.Length, (query, toAppend), (span, args) =>
        {
            var (query, toAppend) = args;
            query.AsSpan().CopyTo(span);
            span[query.Length] = '&';
            toAppend.Value.AsSpan().CopyTo(span.Slice(query.Length + 1));
        });
    }

    /// <summary>
    /// Activity info for debugging purposes.
    /// </summary>
    public sealed class ActivityInfo(Activity? activity, StringValues traceParent, RequestInfo requestInfo)
    {
        /// <summary>Gets the trace id.</summary>
        public string TraceId => (activity?.Context ?? default).TraceId.ToString();

        /// <summary>Gets the span id.</summary>
        public string SpanId => (activity?.Context ?? default).SpanId.ToString();

        /// <summary>Gets whether the trace is remote.</summary>
        public bool IsRemote => (activity?.Context ?? default).IsRemote;

        /// <summary>Gets the trace flags.</summary>
        public int TraceFlags => (int)(activity?.Context ?? default).TraceFlags;

        /// <summary>Gets whether the activity has a remote parent.</summary>
        public bool HasRemoteParent => activity?.HasRemoteParent ?? false;

        /// <summary>Gets the parent span id.</summary>
        public string ParentSpanId => (activity?.ParentSpanId ?? default).ToString();

        /// <summary>Gets the trace header(s).</summary>
        public IEnumerable<string> TraceParent => traceParent;

        /// <summary>Gets the request info.</summary>
        public RequestInfo RequestInfo => requestInfo;
    }

    /// <summary>
    /// Request info for debugging purposes.
    /// </summary>
    public sealed class RequestInfo(string method, HostString host, PathString path, IPAddress? remoteIp)
    {
        /// <summary>
        /// Gets the request method.
        /// </summary>
        public string Method => method;

        /// <summary>
        /// Gets the request path.
        /// </summary>
        public string Path => path;

        /// <summary>
        /// Gets the hostname of the request.
        /// </summary>
        public string? Host => host.Value;

        /// <summary>
        /// Gets the remote IP address of the request.
        /// </summary>
        public IPAddress? RemoteIp => remoteIp;
    }

    private sealed class HttpProxyResult
        : IActionResult
        , IDisposable
    {
        private readonly HttpRequestMessage _request;

        public HttpProxyResult(HttpRequestMessage request)
        {
            _request = request;
        }

        public void Dispose()
        {
            _request.Dispose();
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            using var client = context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DebugController));
            using var request = _request;
            using var data = new Sequence<byte>(ArrayPool<byte>.Shared);
            await CopyBody(context.HttpContext.Request.BodyReader, data, context.HttpContext.RequestAborted);
            using var content = new StreamContent(data.AsReadOnlySequence.AsStream());
            request.Content = content;

            using var response = await client.SendAsync(request, context.HttpContext.RequestAborted);

            context.HttpContext.Response.StatusCode = (int)response.StatusCode;
            CopyHeaders(response.Headers, context.HttpContext.Response);
            CopyHeaders(response.Content.Headers, context.HttpContext.Response);
            await response.Content.CopyToAsync(context.HttpContext.Response.Body, context.HttpContext.RequestAborted);
        }

        public static void CopyHeaders(HttpHeaders headers, HttpResponse response)
        {
            foreach (var (name, values) in headers)
            {
                if (IsIgnoredHeader(name))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    response.Headers.Append(name, value);
                }
            }
        }

        private static async Task CopyBody(PipeReader reader, Sequence<byte> writer, CancellationToken cancellationToken)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThrowHelper.ThrowOperationCanceledException(CancellationToken.None);
                    }

                    writer.Write(buffer);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    // Advance even if WriteAsync throws so the PipeReader is not left in the
                    // currently reading state
                    reader.AdvanceTo(buffer.End);
                }
            }
        }

        private static readonly SearchValues<string> UnproxiedHeaders
            = SearchValues.Create(
                [
                    "connection",
                    "keep-alive",
                    "proxy-authenticate",
                    "proxy-authorization",
                    "te",
                    "trailer",
                    "transfer-encoding",
                    "upgrade",
                    "host", // re-calculated by HttpClient
                    "content-length", // re-calculated by HttpClient
                    "x-forwarded-for",
                    "x-forwarded-host",
                    "x-forwarded-proto",
                    "platformaccesstoken",
                    "authorization",
                ],
                StringComparison.OrdinalIgnoreCase);

        private static bool IsIgnoredHeader(string name)
            => UnproxiedHeaders.Contains(name);
    }
}
