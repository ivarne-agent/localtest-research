using System.Text;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Core.Sire;
using Altinn.Register.Integrations.Sire;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SireEventClient"/> covering each branch of
/// <see cref="ISireEventClient.GetUpdates"/>: empty page, populated page, multi-page
/// termination, malformed JSON, validation failure, and the antall page-size option.
/// </summary>
public class SireEventClientTests
    : HostTestBase
{
    private const string FeedPath = "/v1/hendelser";
    private const int DefaultPageSize = 100;

    private static SireEventClient CreateClient(FakeHttpMessageHandler handler, int pageSize = DefaultPageSize)
        => new(handler.CreateClient(), Options.Create(new SireEventClientOptions { PageSize = pageSize }));

    /// <summary>
    /// SIRE responds with HTTP 404 when no events exist at or beyond the resume cursor.
    /// That's an end-of-feed signal, not a fault — the loop should terminate cleanly
    /// without yielding any pages and without throwing.
    /// </summary>
    [Fact]
    public async Task GetUpdates_NotFoundResponse_YieldsNothing()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

        var client = CreateClient(handler);

        var pages = new List<SireUpdatePage>();
        await foreach (var page in client.GetUpdates(1, CancellationToken))
        {
            pages.Add(page);
        }

        Assert.Empty(pages);
    }

    /// <summary>
    /// An empty feed (no <c>hendelser</c>) terminates the async-enumerable immediately
    /// without yielding any pages.
    /// </summary>
    [Fact]
    public async Task GetUpdates_EmptyFeed_YieldsNothing()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent("""{"hendelser":[]}""", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var pages = new List<SireUpdatePage>();
        await foreach (var page in client.GetUpdates(1, CancellationToken))
        {
            pages.Add(page);
        }

        Assert.Empty(pages);
    }

    /// <summary>
    /// A populated feed page yields one <see cref="SireUpdatePage"/> whose entries
    /// mirror the wire <c>hendelser</c>. A second call with the advanced cursor returns
    /// empty and terminates the loop. All three wire <c>hendelsetype</c> values that
    /// Skatt emits (NY, ENDRET, SLETTET) are exercised here so the enum mapping is
    /// covered end-to-end.
    /// </summary>
    [Fact]
    public async Task GetUpdates_SinglePage_YieldsOnePageThenTerminates()
    {
        const string firstPageBody = """
            {
              "hendelser": [
                {
                  "sekvensnummer": 1,
                  "identifikator": "090090003",
                  "registreringstidspunkt": "2024-09-11T14:10:20.514Z",
                  "hendelsetype": "Ny"
                },
                {
                  "sekvensnummer": 2,
                  "identifikator": "090090011",
                  "registreringstidspunkt": "2024-09-17T10:01:31.696Z",
                  "hendelsetype": "Endret"
                },
                {
                  "sekvensnummer": 3,
                  "identifikator": "090090054",
                  "registreringstidspunkt": "2024-09-20T08:00:00.000Z",
                  "hendelsetype": "Slettet"
                }
              ]
            }
            """;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent(firstPageBody, Encoding.UTF8, "application/json"));
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "4")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent("""{"hendelser":[]}""", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var pages = new List<SireUpdatePage>();
        await foreach (var singlePage in client.GetUpdates(1, CancellationToken))
        {
            pages.Add(singlePage);
        }

        var page = Assert.Single(pages);
        Assert.Equal(3, page.Count);
        Assert.Equal(1u, page[0].SequenceNumber);
        Assert.Equal("090090003", page[0].OrganizationIdentifier.ToString());
        Assert.Equal(SireUpdateType.New, page[0].UpdateType.Value);
        Assert.Equal(2u, page[1].SequenceNumber);
        Assert.Equal("090090011", page[1].OrganizationIdentifier.ToString());
        Assert.Equal(SireUpdateType.Changed, page[1].UpdateType.Value);
        Assert.Equal(3u, page[2].SequenceNumber);
        Assert.Equal("090090054", page[2].OrganizationIdentifier.ToString());
        Assert.Equal(SireUpdateType.Deleted, page[2].UpdateType.Value);
    }

    /// <summary>
    /// Cursor advances to <c>seqMax + 1</c> after each non-empty page. Iteration
    /// continues until SIRE returns an empty page.
    /// </summary>
    [Fact]
    public async Task GetUpdates_MultiplePages_AdvancesCursorAndTerminates()
    {
        const string firstPageBody = """
            {
              "hendelser": [
                {
                  "sekvensnummer": 5,
                  "identifikator": "090090003",
                  "registreringstidspunkt": "2024-09-11T14:10:20.514Z",
                  "hendelsetype": "Ny"
                }
              ]
            }
            """;

        const string secondPageBody = """
            {
              "hendelser": [
                {
                  "sekvensnummer": 7,
                  "identifikator": "090090011",
                  "registreringstidspunkt": "2024-09-17T10:01:31.696Z",
                  "hendelsetype": "Endret"
                }
              ]
            }
            """;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent(firstPageBody, Encoding.UTF8, "application/json"));
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "6")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent(secondPageBody, Encoding.UTF8, "application/json"));
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "8")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent("""{"hendelser":[]}""", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        var pages = new List<SireUpdatePage>();
        await foreach (var page in client.GetUpdates(1, CancellationToken))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.Equal(5u, pages[0][0].SequenceNumber);
        Assert.Equal(7u, pages[1][0].SequenceNumber);
    }

    /// <summary>
    /// Malformed JSON in the response triggers a Problem response, surfaced via
    /// <c>result.EnsureSuccess()</c> as a thrown <c>ProblemInstanceException</c>.
    /// </summary>
    [Fact]
    public async Task GetUpdates_MalformedJson_Throws()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent("{ this is not valid json", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in client.GetUpdates(1, CancellationToken))
            {
                // consume
            }
        });
    }

    /// <summary>
    /// A page item with a missing/invalid identifikator fails validation; the entire
    /// page is rejected as a Problem and surfaces via <c>EnsureSuccess()</c>.
    /// </summary>
    [Fact]
    public async Task GetUpdates_ItemFailsValidation_Throws()
    {
        const string badPageBody = """
            {
              "hendelser": [
                {
                  "sekvensnummer": 1,
                  "identifikator": null,
                  "registreringstidspunkt": "2024-09-11T14:10:20.514Z",
                  "hendelsetype": "Ny"
                }
              ]
            }
            """;

        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "100")
            .Respond(() => new StringContent(badPageBody, Encoding.UTF8, "application/json"));

        var client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in client.GetUpdates(1, CancellationToken))
            {
                // consume
            }
        });
    }

    /// <summary>
    /// When <see cref="SireEventClientOptions.PageSize"/> is overridden, the request URL
    /// reflects the configured value in the <c>antall</c> query-string parameter.
    /// </summary>
    [Fact]
    public async Task GetUpdates_PageSizeOption_AppendsAntallToUrl()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.Expect(HttpMethod.Get, FeedPath)
            .WithQuery("fraSekvensnummer", "1")
            .WithQuery("antall", "500")
            .Respond(() => new StringContent("""{"hendelser":[]}""", Encoding.UTF8, "application/json"));

        var client = CreateClient(handler, pageSize: 500);

        await foreach (var _ in client.GetUpdates(1, CancellationToken))
        {
            // consume — expecting no pages
        }

        // If the URL didn't include antall=500, the FakeHttpMessageHandler would throw
        // an ExpectationNotMetException on dispose.
    }
}
