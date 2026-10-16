using Altinn.Register.Configuration;
using Altinn.Register.Contracts;
using Altinn.Register.Core.A2;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Clients;

/// <summary>
/// Implementation of <see cref="IOrganizationClient"/> using SBL Bridge Register API as data source
/// </summary>
public class OrganizationClient
    : IOrganizationClient
{
    private readonly GeneralSettings _generalSettings;
    private readonly ILogger<OrganizationClient> _logger;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizationClient"/> class
    /// </summary>
    /// <param name="httpClient">HttpClient from default <see cref="IHttpClientFactory"/></param>
    /// <param name="generalSettings">the general settings</param>
    /// <param name="logger">the logger</param>
    public OrganizationClient(HttpClient httpClient, IOptions<GeneralSettings> generalSettings, ILogger<OrganizationClient> logger)
    {
        _generalSettings = generalSettings.Value;
        _logger = logger;
        _client = httpClient;
    }

    /// <inheritdoc />
    public async Task<Contracts.V1.Organization?> GetOrganization(OrganizationIdentifier orgNr, CancellationToken cancellationToken = default)
    {
        Uri endpointUrl = new($"{_generalSettings.BridgeApiEndpoint}organizations/{orgNr}");

        HttpResponseMessage response = await _client.GetAsync(endpointUrl, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<Contracts.V1.Organization>(cancellationToken: cancellationToken);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        _logger.LogError("Getting org with org nr '{OrgNr}' failed with statuscode {StatusCode}", orgNr, response.StatusCode);
        response.EnsureSuccessStatusCode(); // should throw
        return null;
    }
}
