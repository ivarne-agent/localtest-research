using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Localtest.Telemetry;
using Localtest.Tests.Emulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Localtest.Tests;

/// <summary>
/// The front door's OpenTelemetry (<see cref="FrontDoorTelemetry"/>): nothing without <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>;
/// with it, OTLP to the endpoint, a server span and a client span per forwarded request with <c>traceparent</c> passed on,
/// no span for <c>/otlp</c>, <c>/grafana</c> and <c>/health</c>, and a span per emulated call in minimal mode. The spans
/// are collected by a processor next to the OTLP exporter. An activity listener sees every host in the process (the other
/// tests' too), so each test sends its own <c>traceparent</c> and looks only at its own trace.
/// </summary>
public sealed class TelemetryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutTheVariableNothingIsRegistered()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();
        using var response = await client.GetAsync("/health", Ct);

        Assert.Null(frontDoor.Services.GetService<TracerProvider>());
        Assert.Null(frontDoor.Services.GetService<MeterProvider>());
        Assert.Null(frontDoor.Services.GetService<LoggerProvider>());
        Assert.DoesNotContain(frontDoor.Services.GetServices<ILoggerProvider>(), p => p is OpenTelemetryLoggerProvider);
    }

    [Theory]
    [InlineData(null, "localtest")]
    [InlineData("named-by-the-runner", "named-by-the-runner")]
    public async Task WithTheVariableTracesGoOverOtlpNamedLocaltestUnlessTheRunnerNamesIt(string? serviceName, string expected)
    {
        await using var collector = await FakeServer.Start(_ => Task.CompletedTask);
        await using var frontDoor = new FrontDoor();
        var spans = new Spans();
        await using var traced = WithTelemetry(frontDoor, collector, spans, serviceName);
        using var client = Client(traced);

        using var response = await client.SendAsync(Request(HttpMethod.Get, "/", out _), Ct);
        var provider = traced.Services.GetRequiredService<TracerProvider>();
        Assert.True(provider.ForceFlush());

        var resource = provider.GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal(expected, resource["service.name"]);
        // http/protobuf: the SDK appends /v1/traces to the endpoint from the variable.
        var exported = await Until(() => collector.Requests.FirstOrDefault(r => r.Call == "POST /otlp/v1/traces"));
        Assert.Contains(expected, exported.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AForwardedRequestIsAServerSpanAndAClientSpanWhoseTraceparentReachesTheDestination()
    {
        await using var collector = await FakeServer.Start(_ => Task.CompletedTask);
        await using var storage = await FakeServer.Start(context => context.Response.WriteAsync("storage"));
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?> { ["storage"] = storage.Origin });
        var spans = new Spans();
        await using var traced = WithTelemetry(frontDoor, collector, spans);
        using var client = Client(traced);

        using var response = await client.SendAsync(Request(HttpMethod.Get, "/storage/api/v1/applications", out var caller), Ct);
        Assert.Equal("storage", await response.Content.ReadAsStringAsync(Ct));

        var server = await Until(() => spans.Of(caller.TraceId).FirstOrDefault(s => s.Kind == ActivityKind.Server && s.ParentSpanId == caller.SpanId));
        var forwarded = await Until(() => spans.Of(caller.TraceId).FirstOrDefault(s => s.Kind == ActivityKind.Client && s.ParentSpanId == server.SpanId));
        Assert.Equal("Microsoft.AspNetCore", server.Source.Name);
        Assert.Equal("GET storage", server.DisplayName);
        Assert.Equal("storage", server.GetTagItem(FrontDoorTelemetry.RouteTag));
        Assert.Equal("storage", forwarded.GetTagItem(FrontDoorTelemetry.PeerServiceTag));
        Assert.Equal($"00-{caller.TraceId}-{forwarded.SpanId}-01", Assert.Single(storage.Requests).Traceparent);
    }

    [Theory]
    [InlineData("/otlp/v1/traces")]
    [InlineData("/grafana/api/health")]
    [InlineData("/health")]
    public async Task OtlpGrafanaAndHealthAreNotTraced(string path)
    {
        await using var collector = await FakeServer.Start(_ => Task.CompletedTask);
        await using var otelCollector = await FakeServer.Start(_ => Task.CompletedTask);
        await using var grafana = await FakeServer.Start(_ => Task.CompletedTask);
        await using var storage = await FakeServer.Start(_ => Task.CompletedTask);
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?>
        {
            ["otel-collector"] = otelCollector.Origin,
            ["grafana"] = grafana.Origin,
            ["storage"] = storage.Origin,
        });
        var spans = new Spans();
        await using var traced = WithTelemetry(frontDoor, collector, spans);
        using var client = Client(traced);

        using var untraced = Request(HttpMethod.Post, path, out var skipped);
        untraced.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
        using var response = await client.SendAsync(untraced, Ct);
        // A traced request after it: once its spans are in, the first request's would be too.
        using var after = await client.SendAsync(Request(HttpMethod.Get, "/storage/x", out var marker), Ct);
        await Until(() => spans.Of(marker.TraceId).FirstOrDefault(s => s.Kind == ActivityKind.Server && s.ParentSpanId == marker.SpanId));

        Assert.Empty(spans.Of(skipped.TraceId));
        Assert.DoesNotContain(spans.All, s => s.GetTagItem("url.path") is string p && p.StartsWith(path, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/register/api/v1/parties/500000", "register", "Register GET /register/api/v1/parties/{partyId:int}")]
    [InlineData("/authorization/api/v1/parties?userId=1337", "authorization", "Authorization GET /authorization/api/v1/parties")]
    public async Task AnEmulatedCallIsASpanNamingItsComponent(string path, string service, string name)
    {
        await using var collector = await FakeServer.Start(_ => Task.CompletedTask);
        await using var frontDoor = new MinimalFrontDoor();
        var spans = new Spans();
        await using var traced = WithTelemetry(frontDoor, collector, spans);
        using var client = Client(traced, MinimalFrontDoor.Origin);

        using var response = await client.SendAsync(Request(HttpMethod.Get, path, out var caller), Ct);
        Assert.Equal(service, Assert.Single(response.Headers.GetValues(Localtest.Emulation.Header)));

        var server = await Until(() => spans.Of(caller.TraceId).FirstOrDefault(s => s.Kind == ActivityKind.Server && s.ParentSpanId == caller.SpanId));
        var emulated = await Until(() => spans.Of(caller.TraceId).FirstOrDefault(s => s.Source.Name == FrontDoorTelemetry.EmulationSourceName));
        Assert.Equal(server.SpanId, emulated.ParentSpanId);
        Assert.Equal(name, emulated.DisplayName);
        Assert.Equal(service, emulated.GetTagItem(FrontDoorTelemetry.PeerServiceTag));
        Assert.Equal(200, emulated.GetTagItem("http.response.status_code"));
    }

    /// <summary>
    /// The front door with the variable set as host settings (the builder's configuration sees them, as it sees the
    /// environment) and <paramref name="spans"/> next to the exporter. The endpoint is under <c>/otlp</c> on the fake
    /// collector, so the collector's own server spans (it runs in this process) are filtered like the front door's.
    /// </summary>
    private static WebApplicationFactory<Program> WithTelemetry(WebApplicationFactory<Program> frontDoor, FakeServer collector, Spans spans, string? serviceName = null)
        => frontDoor.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", collector.Origin + "/otlp");
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
            if (serviceName is not null)
            {
                builder.UseSetting("OTEL_SERVICE_NAME", serviceName);
            }

            builder.ConfigureTestServices(services => services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(spans)));
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory, string origin = "http://local.altinn.cloud:8000")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri(origin) });
        client.DefaultRequestHeaders.Host = "local.altinn.cloud:8000";
        return client;
    }

    /// <summary>A request as a traced caller (an app) sends it: with a sampled <c>traceparent</c> of its own.</summary>
    private static HttpRequestMessage Request(HttpMethod method, string path, out ActivityContext caller)
    {
        caller = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", $"00-{caller.TraceId}-{caller.SpanId}-01");
        return request;
    }

    private static async Task<T> Until<T>(Func<T?> find)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (find() is { } found)
            {
                return found;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("Timed out waiting for telemetry.");
        return null!;
    }

    /// <summary>Every span the provider ends, kept in memory.</summary>
    private sealed class Spans : BaseProcessor<Activity>
    {
        private readonly ConcurrentQueue<Activity> _ended = new();

        public IEnumerable<Activity> All => _ended;

        public IEnumerable<Activity> Of(ActivityTraceId traceId) => _ended.Where(s => s.TraceId == traceId);

        public override void OnEnd(Activity data) => _ended.Enqueue(data);
    }
}
