#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Profile.Integrations.ContactRegister;
using Microsoft.Extensions.Options;

namespace Altinn.Profile.Localtest.Register;

/// <summary>
/// Reads the test data the stack runs with from the Register component's anonymous localtest API, the single source of
/// it (<c>localtest/testdata/README.md</c>): <c>test-users</c> (users and their preferences), <c>organizations</c>
/// (official notification addresses) and the KRR stand-in <c>krr/rest/v2/personer</c> (e-mail, mobile number,
/// reservation). Profile never reads the seed file.
/// </summary>
public sealed class LocaltestRegisterTestData(HttpClient httpClient, IOptions<LocaltestProfileOptions> options)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private string BaseUrl => options.Value.PlatformEndpoint.TrimEnd('/') + "/register/api/localtest/";

    /// <summary>The KRR stand-in's URL, which the configuration names as the KRR change log endpoint (for the logs).</summary>
    public static string KrrPersonsUrl(string platformEndpoint) => platformEndpoint.TrimEnd('/') + "/register/api/localtest/krr/rest/v2/personer";

    public async Task<IReadOnlyList<TestUser>> GetUsers(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<TestUser>>(BaseUrl + "test-users", Web, cancellationToken) ?? [];

    public async Task<IReadOnlyList<TestOrganization>> GetOrganizations(CancellationToken cancellationToken)
        => await httpClient.GetFromJsonAsync<List<TestOrganization>>(BaseUrl + "organizations", Web, cancellationToken) ?? [];

    /// <summary>
    /// The KRR stand-in's answer for these persons, in KRR's own shape, which is the shape of an entry in KRR's change
    /// log as well (<see cref="PersonContactPreferencesSnapshot"/>).
    /// </summary>
    public async Task<IReadOnlyList<PersonContactPreferencesSnapshot>> GetKrrPersons(IEnumerable<string> nationalIdentityNumbers, CancellationToken cancellationToken)
    {
        var ssns = nationalIdentityNumbers.Distinct(StringComparer.Ordinal).ToList();
        if (ssns.Count == 0)
        {
            return [];
        }

        using var response = await httpClient.PostAsJsonAsync(BaseUrl + "krr/rest/v2/personer", new { personidentifikatorer = ssns }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<KrrPersons>(cancellationToken);
        return answer?.Personer ?? [];
    }

    public async Task<bool> IsAvailable(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(BaseUrl + "test-users", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public sealed record TestUser(int UserId, int PartyId, Guid PartyUuid, string? Ssn, TestUserProfile? Profile);

    public sealed record TestUserProfile(string? Language, int PreSelectedPartyId, bool DoNotPromptForParty);

    public sealed record TestOrganization(int PartyId, Guid PartyUuid, string OrgNumber, int? ParentPartyId, IReadOnlyList<TestNotificationAddress>? NotificationAddresses);

    public sealed record TestNotificationAddress(string? Email, string? MobileNumber);

    private sealed record KrrPersons([property: JsonPropertyName("personer")] List<PersonContactPreferencesSnapshot>? Personer);
}
