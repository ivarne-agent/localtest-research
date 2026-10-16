using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire.Organization;

namespace Altinn.Register.Integrations.Sire;

/// <summary>
/// Implementation of <see cref="ISireClient"/> that calls the SIRE lookup API.
/// </summary>
public sealed class SireClient
    : ISireClient
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly ILocationLookupProvider _lookupProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SireClient"/> class.
    /// </summary>
    public SireClient(HttpClient client, ILocationLookupProvider lookupProvider, TimeProvider timeProvider)
    {
        _client = client;
        _lookupProvider = lookupProvider;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<Result<SireOrganization>> GetOrganization(OrganizationIdentifier organizationIdentifier, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(
            $"v1/digdir/{organizationIdentifier}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Problems.OrganizationNotFound.Create(
                [
                    new("organization.source", "sire"),
                    new("organization.identifier", organizationIdentifier.ToString())
                ]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Problems.PartyFetchFailed.Create(
                detail: $"SIRE API responded with status code {response.StatusCode}",
                [
                    new("organization.source", "sire"),
                    new("http.status_code", ((int)response.StatusCode).ToString()),
                    new("organization.identifier", organizationIdentifier.ToString())
                ]);
        }

        OrganizationDocument? document;
        try
        {
            document = await response.Content.ReadFromJsonAsync<OrganizationDocument>(_options, cancellationToken);
        }
        catch (JsonException)
        {
            return Problems.PartyFetchFailed.Create(
                detail: "Response deserialization failed",
                [
                    new("organization.source", "sire"),
                    new("organization.identifier", organizationIdentifier.ToString())
                ]);
        }

        if (document is null)
        {
            return Problems.PartyFetchFailed.Create(
                detail: "Response deserialization resulted in null",
                [
                    new("organization.source", "sire"),
                    new("organization.identifier", organizationIdentifier.ToString())
                ]);
        }

        var lookup = await _lookupProvider.GetLocationLookup(cancellationToken);

        // Snapshot "now" once so every validity check inside the document compares
        // against the same instant — independent of how long the validator's loop takes.
        var now = _timeProvider.GetUtcNow();

        ValidationProblemBuilder builder = default;
        builder.TryValidate(path: "/", document, new OrganizationDocumentValidator(lookup, now), out SireOrganization? validated);

        if (builder.TryBuild(out var error))
        {
            return error;
        }

        Debug.Assert(validated is not null);
        return validated;
    }
}
