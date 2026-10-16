using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Models;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.SystemUser;

/// <summary>
/// Service for getting system users.
/// </summary>
internal sealed class SystemUserImportService
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemUserImportService"/> class.
    /// </summary>
    public SystemUserImportService(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets an async stream of system users, starting from the given URL or from the beginning if no URL is provided.
    /// </summary>
    /// <param name="startUrl">The url to continue the stream from.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A stream of pages containing <see cref="SystemUserRecord"/>s.</returns>
    public async IAsyncEnumerable<SystemUserPage> GetStream(
        string? startUrl = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        const string INITIAL_URL = "api/v1/systemuser/internal/systemusers/stream";

        string? nextUrl = startUrl ?? INITIAL_URL;

        do
        {
            var response = await GetPage(nextUrl, cancellationToken);

            yield return response;
            nextUrl = response.NextUrl;
        }
        while (!cancellationToken.IsCancellationRequested && nextUrl is not null);
    }

    private async Task<SystemUserPage> GetPage(string url, CancellationToken cancellationToken)
    {
        var source = await _client.GetFromJsonAsync<ItemStream<SystemUserStreamItem>>(url, cancellationToken);

        if (source is null)
        {
            ThrowHelper.ThrowInvalidOperationException("Failed to get system user stream page.");
        }

        var items = source.Items.Select(item => new SystemUserItem
        {
            Id = item.Id,
            SequenceNumber = item.SequenceNumber,
            Name = item.IntegrationTitle,
            IsDeleted = item.IsDeleted,
            CreatedAt = item.CreatedAt,
            LastChangedAt = item.LastChangedAt,
            OwnerPartyId = item.PartyId,
            Type = item.Type.Select(static t => t switch
            {
                SystemUserType.Standard => SystemUserRecordType.Standard,
                SystemUserType.Agent => SystemUserRecordType.Agent,
                _ => Unreachable<SystemUserRecordType>($"Unknown system user type: {t}"),
            }),
        }).ToImmutableArray();

        return new SystemUserPage(items, source.Links.Next, source.Stats.SequenceMax);

        static T Unreachable<T>(string message)
        {
            throw new UnreachableException(message);
        }
    }

    private sealed record SystemUserStreamItem
    {
        [JsonPropertyName("id")]
        public required Guid Id { get; init; }

        [JsonPropertyName("integrationTitle")]
        public required string IntegrationTitle { get; init; }

        [JsonPropertyName("isDeleted")]
        public required bool IsDeleted { get; init; }

        [JsonPropertyName("created")]
        public required DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("lastChanged")]
        public required DateTimeOffset LastChangedAt { get; init; }

        [JsonPropertyName("systemUserType")]
        public required NonExhaustiveEnum<SystemUserType> Type { get; init; }

        [JsonPropertyName("partyId")]
        public required SystemUserItem.GuidOrUint PartyId { get; init; }

        [JsonPropertyName("sequenceNo")]
        public required ulong SequenceNumber { get; init; }
    }

    private enum SystemUserType
    {
        [JsonStringEnumMemberName("standard")]
        Standard,

        [JsonStringEnumMemberName("agent")]
        Agent,
    }
}
