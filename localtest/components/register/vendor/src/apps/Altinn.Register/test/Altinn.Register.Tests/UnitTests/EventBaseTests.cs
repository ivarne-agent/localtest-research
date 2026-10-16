using System.Text.Json;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit;

namespace Altinn.Register.Tests.UnitTests;

public class EventBaseTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TestCommand_RoundTrips_WithCorrelationId()
    {
        var evt = new TestEvent
        {
            Foo = "foo",
            Bar = "bar",
        };

        var correlationId = ((CorrelatedBy<Guid>)evt).CorrelationId;
        correlationId.ShouldNotBe(Guid.Empty);

        var json = JsonSerializer.Serialize(evt, Options);
        var roundTripped = JsonSerializer.Deserialize<TestEvent>(json, Options)!;

        roundTripped.ShouldBeEquivalentTo(evt);
        ((CorrelatedBy<Guid>)roundTripped).CorrelationId.ShouldBe(correlationId);
    }

    private sealed record TestEvent
        : EventBase
    {
        public required string Foo { get; init; }

        public required string Bar { get; init; }
    }
}
