using System.Text;
using System.Text.Json;
using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Configuration;
using Altinn.Register.Contracts.V1;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Clients;

/// <summary>
/// Implementation of <see cref="IPersonClient"/> using SBL Bridge Register API as data source
/// </summary>
public class PersonClient : IPersonClient
{
    private readonly GeneralSettings _generalSettings;
    private readonly ILogger _logger;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonClient"/> class
    /// </summary>
    public PersonClient(HttpClient httpClient, IOptions<GeneralSettings> generalSettings, ILogger<PersonClient> logger)
    {
        _generalSettings = generalSettings.Value;
        _logger = logger;
        _client = httpClient;
    }

    /// <inheritdoc />
    public async Task<Person?> GetPerson(string nationalIdentityNumber, CancellationToken cancellationToken = default)
    {
        Uri endpointUrl = new($"{_generalSettings.BridgeApiEndpoint}persons");

        StringContent requestBody = new(JsonSerializer.Serialize(nationalIdentityNumber), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync(endpointUrl, requestBody, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            return await response.Content.ReadFromJsonAsync<Person>(cancellationToken: cancellationToken);
        }
        else
        {
            _logger.LogError("Getting person failed with statuscode {StatusCode}", response.StatusCode);
        }

        return null;
    }
}
