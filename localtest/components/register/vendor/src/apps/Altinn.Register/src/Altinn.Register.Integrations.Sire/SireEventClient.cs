using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire.Feed;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Integrations.Sire;

/// <summary>
/// Implementation of <see cref="ISireEventClient"/> that calls the SIRE
/// hendelser (event-feed) API.
/// </summary>
public sealed class SireEventClient
    : ISireEventClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private const string FeedPath = "v1/hendelser";
    private const string FromQueryParam = "fraSekvensnummer";
    private const string AntallQueryParam = "antall";

    private readonly HttpClient _client;
    private readonly IOptions<SireEventClientOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SireEventClient"/> class.
    /// </summary>
    public SireEventClient(HttpClient client, IOptions<SireEventClientOptions> options)
    {
        _client = client;
        _options = options;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SireUpdatePage> GetUpdates(
        uint fromInclusive = 1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await GetUpdatePage(fromInclusive, cancellationToken);
            result.EnsureSuccess();

            var page = result.Value;
            if (page.Count == 0)
            {
                yield break;
            }

            yield return page;

            var seqMax = page[^1].SequenceNumber;
            Assert(seqMax >= fromInclusive);
            Assert(seqMax < uint.MaxValue);
            fromInclusive = seqMax + 1;
        }
    }

    private async Task<Result<SireUpdatePage>> GetUpdatePage(uint fromInclusive, CancellationToken cancellationToken)
    {
        var url = $"{FeedPath}?{FromQueryParam}={fromInclusive}&{AntallQueryParam}={_options.Value.PageSize}";

        using var response = await _client.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new SireUpdatePage([]);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Problems.PartyFetchFailed.Create(
                detail: $"SIRE feed responded with status code {response.StatusCode}",
                [
                    new("feed.source", "sire"),
                    new("http.status_code", ((int)response.StatusCode).ToString()),
                    new("feed.fromInclusive", fromInclusive.ToString()),
                ]);
        }

        UpdateFeed? feed;
        try
        {
            feed = await response.Content.ReadFromJsonAsync<UpdateFeed>(_jsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Problems.PartyFetchFailed.Create(
                detail: "SIRE feed deserialization failed",
                [
                    new("feed.source", "sire"),
                    new("feed.fromInclusive", fromInclusive.ToString()),
                ]);
        }

        var items = feed?.Updates;
        if (items is null or { Count: 0 })
        {
            return new SireUpdatePage([]);
        }

        var builder = ImmutableArray.CreateBuilder<SireUpdate>(items.Count);
        var errors = default(ValidationProblemBuilder);
        for (var index = 0; index < items.Count; index++)
        {
            if (errors.TryValidate(path: $"/hendelser/{index}", items[index], default(UpdateItemValidator), out SireUpdate? update))
            {
                builder.Add(update);
            }
        }

        if (errors.TryBuild(out var error))
        {
            return error;
        }

        return new SireUpdatePage(builder.DrainToImmutableValueArray());
    }

    private static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        [CallerArgumentExpression(nameof(condition))] string? conditionString = null)
    {
        if (!condition)
        {
            ThrowHelper.ThrowInvalidOperationException($"Assertion failed: {conditionString}");
        }
    }
}
