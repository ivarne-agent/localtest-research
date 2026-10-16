using System.Text.Json.Serialization;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.TempWorkarounds;

/// <summary>
/// Access management client.
/// </summary>
public sealed class AccessManagementClient
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessManagementClient"/> class.
    /// </summary>
    public AccessManagementClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Encourages access-management to create a self-identified user.
    /// </summary>
    /// <param name="partyUuid">The party uuid.</param>
    /// <param name="partyId">The party id.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="type">The self-identified user type.</param>
    /// <param name="displayName">The display name of the user.</param>
    /// <param name="emailIdentifier">The email identifier of the user, if applicable.</param>
    /// <param name="externalUrn">The external URN of the user, if applicable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<Result> CreateSelfIdentifiedUser(
        Guid partyUuid,
        uint partyId,
        uint userId,
        SelfIdentifiedUserType type,
        string displayName,
        string? emailIdentifier,
        PartyExternalRefUrn? externalUrn,
        CancellationToken cancellationToken)
    {
        using var createResponse = await _client.PostAsJsonAsync(
            requestUri: "api/v1/internal/party",
            value: new CreateSelfIdentifiedUserRequest(
                PartyUuid: partyUuid,
                PartyId: partyId,
                UserId: userId,
                EntityType: "Selvidentifisert",
                EntityVariantType: type switch
                {
                    SelfIdentifiedUserType.IdPortenEmail => "SI_EMAIL",
                    SelfIdentifiedUserType.Educational => "SI_EDU",
                    _ => ThrowHelper.ThrowNotSupportedException<string>($"Unsupported self-identified user type: {type}"),
                },
                DisplayName: displayName,
                EmailIdentifier: emailIdentifier,
                ExternalUrn: externalUrn),
            cancellationToken: cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            return Problems.AccessManagementSelfIdentifiedUserPushFailed.Create([
                new("party.uuid", partyUuid.ToString()),
                new("http.status_code", ((int)createResponse.StatusCode).ToString()),
                new("request.name", "create self-identified user"),
            ]);
        }

        using var selnResponse = await _client.PostAsync($"api/v1/internal/connections/selfidentifiedusers?from={partyUuid}&to={partyUuid}", content: null, cancellationToken: cancellationToken);

        if (!selnResponse.IsSuccessStatusCode)
        {
            return Problems.AccessManagementSelfIdentifiedUserPushFailed.Create([
                new("party.uuid", partyUuid.ToString()),
                new("http.status_code", ((int)selnResponse.StatusCode).ToString()),
                new("request.name", "add SELN to self-identified user"),
            ]);
        }

        return Result.Success;
    }

    private record CreateSelfIdentifiedUserRequest(
        [property: JsonPropertyName("PartyUuid")] Guid PartyUuid,
        [property: JsonPropertyName("PartyId")] uint PartyId,
        [property: JsonPropertyName("UserId")] uint UserId,
        [property: JsonPropertyName("EntityType")] string EntityType,
        [property: JsonPropertyName("EntityVariantType")] string EntityVariantType,
        [property: JsonPropertyName("DisplayName")] string DisplayName,
        [property: JsonPropertyName("EmailIdentifier")] string? EmailIdentifier,
        [property: JsonPropertyName("ExternalUrn")] PartyExternalRefUrn? ExternalUrn);
}
