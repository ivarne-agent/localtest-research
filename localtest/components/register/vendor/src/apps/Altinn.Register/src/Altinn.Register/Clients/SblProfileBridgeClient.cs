using System.Net;
using System.Text;
using System.Text.Json;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Core.A2.SblProfile;
using Altinn.Register.Core.Errors;

namespace Altinn.Register.Clients;

/// <summary>
/// Proxies calls to the SBL Bridge profile API for the iteration-1 implementation
/// of the register users endpoint.
/// </summary>
internal sealed partial class SblProfileBridgeClient : ISblProfileBridgeClient
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly ILogger<SblProfileBridgeClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SblProfileBridgeClient"/> class.
    /// </summary>
    /// <param name="httpClient">An <see cref="HttpClient"/> configured for the SBL Bridge profile API.</param>
    /// <param name="logger">A logger.</param>
    public SblProfileBridgeClient(HttpClient httpClient, ILogger<SblProfileBridgeClient> logger)
    {
        _client = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<SblUserLookup>> LookupUser(string externalIdentity, CancellationToken cancellationToken = default)
    {
        using var requestBody = new StringContent(JsonSerializer.Serialize(externalIdentity, _options), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("profile/api/users/", requestBody, cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var user = await response.Content.ReadFromJsonAsync<SblUserProfile>(_options, cancellationToken);
            return new SblUserLookup(user);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return SblUserLookup.NotFound;
        }

        Log.LookupFailed(_logger, response.StatusCode);
        return Problems.PartyFetchFailed.Create([
            new("operation", "profile/api/users/"),
            new("http.status", ((int)response.StatusCode).ToString()),
        ]);
    }

    /// <inheritdoc/>
    public async Task<Result<SblUserProfile>> CreateUser(SblUserProfile user, CancellationToken cancellationToken = default)
    {
        using var requestBody = JsonContent.Create(user, options: _options);
        using var response = await _client.PostAsync("profile/api/users/create/", requestBody, cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var created = await response.Content.ReadFromJsonAsync<SblUserProfile>(_options, cancellationToken);
            if (created is null)
            {
                Log.CreateReturnedEmptyBody(_logger);
                return Problems.PartyFetchFailed.Create([
                    new("operation", "profile/api/users/create/"),
                    new("reason", "bridge returned empty body"),
                ]);
            }

            return created;
        }

        Log.CreateFailed(_logger, response.StatusCode);
        return Problems.PartyFetchFailed.Create([
            new("operation", "profile/api/users/create/"),
            new("http.status", ((int)response.StatusCode).ToString()),
        ]);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "SBL Bridge user lookup returned non-success status {StatusCode}")]
        public static partial void LookupFailed(ILogger logger, HttpStatusCode statusCode);

        [LoggerMessage(1, LogLevel.Error, "SBL Bridge user create returned non-success status {StatusCode}")]
        public static partial void CreateFailed(ILogger logger, HttpStatusCode statusCode);

        [LoggerMessage(2, LogLevel.Error, "SBL Bridge user create returned 200 OK with empty body")]
        public static partial void CreateReturnedEmptyBody(ILogger logger);
    }
}
