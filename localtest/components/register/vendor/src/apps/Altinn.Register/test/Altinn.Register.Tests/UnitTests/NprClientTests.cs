using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Npr;
using Altinn.Register.Tests.Mocks;
using Altinn.Register.TestUtils;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Tests.UnitTests;

public class NprClientTests
    : HostTestBase
{
    private INprClient Client
        => GetRequiredService<INprClient>();

    [Fact]
    public async Task GetUpdates_NoPages_YieldsNothing()
    {
        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "1")
            .Respond("application/json", "[]");

        var pages = await Client.GetUpdates(cancellationToken: CancellationToken).ToListAsync(CancellationToken);

        pages.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetUpdates_OnePage_YieldsPageAndStopsAfterEmptyResponse()
    {
        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "10")
            .Respond(
                "application/json",
                """
                [
                    {
                        "sekvensnummer": 10,
                        "hendelse": {
                            "folkeregisteridentifikator": "25871999336",
                            "ajourholdstidspunkt": "2024-01-01T08:00:00+00:00"
                        }
                    },
                    {
                        "sekvensnummer": 11,
                        "hendelse": {
                            "folkeregisteridentifikator": "20821098387",
                            "ajourholdstidspunkt": "2024-01-01T09:00:00+00:00"
                        }
                    }
                ]
                """);

        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "12")
            .Respond("application/json", "[]");

        var pages = await Client.GetUpdates(10, CancellationToken).ToListAsync(CancellationToken);

        pages.Count.ShouldBe(1);
        pages[0].Count.ShouldBe(2);
        pages[0][0].SequenceNumber.ShouldBe(10U);
        pages[0][0].PersonIdentifier.ShouldBe(PersonIdentifier.Parse("25871999336"));
        pages[0][1].SequenceNumber.ShouldBe(11U);
        pages[0][1].PersonIdentifier.ShouldBe(PersonIdentifier.Parse("20821098387"));
    }

    [Fact]
    public async Task GetUpdates_MultiplePages_RequestsNextPageFromLastSequenceNumber()
    {
        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "100")
            .Respond(
                "application/json",
                """
                [
                    {
                        "sekvensnummer": 100,
                        "hendelse": {
                            "folkeregisteridentifikator": "25871999336",
                            "ajourholdstidspunkt": "2024-01-01T08:00:00+00:00"
                        }
                    },
                    {
                        "sekvensnummer": 102,
                        "hendelse": {
                            "folkeregisteridentifikator": "20821098387",
                            "ajourholdstidspunkt": "2024-01-01T09:00:00+00:00"
                        }
                    }
                ]
                """);

        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "103")
            .Respond(
                "application/json",
                """
                [
                    {
                        "sekvensnummer": 103,
                        "hendelse": {
                            "folkeregisteridentifikator": "69914600685",
                            "ajourholdstidspunkt": "2024-01-01T10:00:00+00:00"
                        }
                    }
                ]
                """);

        HttpHandlers.For<INprClient>()
            .Expect(HttpMethod.Get, "/folkeregisteret/offentlig-med-hjemmel/api/v1/hendelser/feed")
            .WithQuery("seq", "104")
            .Respond("application/json", "[]");

        var pages = await Client.GetUpdates(100, CancellationToken).ToListAsync(CancellationToken);

        pages.Count.ShouldBe(2);
        pages[0].Select(update => update.SequenceNumber).ShouldBe([100U, 102U]);
        pages[1].Select(update => update.SequenceNumber).ShouldBe([103U]);
        pages[1][0].PersonIdentifier.ShouldBe(PersonIdentifier.Parse("69914600685"));
    }

    protected override ValueTask ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILocationLookupProvider, MockLocationLookupProvider>();
        services.AddNprClient().ConfigureBaseAddress(FakeHttpEndpoint.HttpsUri);

        return ValueTask.CompletedTask;
    }
}
