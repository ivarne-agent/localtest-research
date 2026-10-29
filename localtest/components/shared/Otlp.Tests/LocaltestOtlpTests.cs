using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Localtest.Shared.Otlp.Tests;

public sealed class LocaltestOtlpTests
{
    private const string ComponentName = "component-hard-coded-name";
    private const string SourceName = "Localtest.Otlp.Tests";

    [Fact]
    public async Task HttpProtobuf_SendsAllThreeSignals_NamedByOtelServiceName()
    {
        await using var collector = await FakeCollector.Start(HttpProtocols.Http1);

        await RunComponent(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_SERVICE_NAME"] = "named-by-the-runner",
        });

        var received = await collector.WaitFor("/v1/traces", "/v1/metrics", "/v1/logs");
        foreach (var path in new[] { "/v1/traces", "/v1/metrics", "/v1/logs" })
        {
            Assert.Contains("named-by-the-runner", received[path]);
            Assert.DoesNotContain(ComponentName, received[path]);
        }

        Assert.Contains("the-span", received["/v1/traces"]);
        Assert.Contains("the-counter", received["/v1/metrics"]);
        Assert.Contains("the log line", received["/v1/logs"]);
    }

    [Fact]
    public async Task WithoutOtelServiceName_TheComponentsOwnNameStays()
    {
        await using var collector = await FakeCollector.Start(HttpProtocols.Http1);

        await RunComponent(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });

        var received = await collector.WaitFor("/v1/traces");
        Assert.Contains(ComponentName, received["/v1/traces"]);
    }

    [Fact]
    public async Task HttpProtobuf_TheExportersOwnHttpCallsAreNotLogged()
    {
        // A host with IHttpClientFactory (nearly every component) gives the exporters factory clients, whose handlers log
        // every export at Information under System.Net.Http.HttpClient.Otlp*Exporter; on the console that is noise.
        await using var collector = await FakeCollector.Start(HttpProtocols.Http1);
        var logged = new ConcurrentQueue<(string Category, LogLevel Level)>();

        await RunComponent(
            new()
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            },
            builder =>
            {
                builder.Services.AddHttpClient();
                builder.Logging.SetMinimumLevel(LogLevel.Information);
                builder.Logging.AddProvider(new CapturingLoggerProvider(logged));
            });

        await collector.WaitFor("/v1/traces", "/v1/metrics", "/v1/logs");
        Assert.Contains(logged, l => l.Category == typeof(LocaltestOtlpTests).FullName);
        Assert.DoesNotContain(logged, l => l.Category.StartsWith("System.Net.Http.HttpClient.Otlp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Grpc_SendsOverGrpc()
    {
        await using var collector = await FakeCollector.Start(HttpProtocols.Http2);

        await RunComponent(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
            ["OTEL_SERVICE_NAME"] = "named-by-the-runner",
        });

        var received = await collector.WaitFor("/opentelemetry.proto.collector.trace.v1.TraceService/Export");
        Assert.Contains("named-by-the-runner", received["/opentelemetry.proto.collector.trace.v1.TraceService/Export"]);
    }

    [Fact]
    public async Task APerSignalEndpointOverridesTheGeneralOne()
    {
        await using var collector = await FakeCollector.Start(HttpProtocols.Http1);

        // The per-signal endpoint is used as it is (no /v1/traces appended), as the specification says; the other signals
        // keep the general endpoint, so nothing goes to the SDK's default localhost:4317.
        await RunComponent(new()
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = collector.Endpoint + "/custom/traces",
            ["OTEL_SERVICE_NAME"] = "named-by-the-runner",
        });

        var received = await collector.WaitFor("/custom/traces", "/v1/metrics", "/v1/logs");
        Assert.Contains("named-by-the-runner", received["/custom/traces"]);
        Assert.DoesNotContain("/v1/traces", received.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutTheVariable_RegistersNothing(string? endpoint)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
                // A per-signal endpoint alone does not turn the helper on: UseOtlpExporter() turns on all three signals,
                // and the ones without an endpoint would go to the SDK's default, localhost:4317. Port 9 (discard) on
                // loopback, in case it ever did.
                ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = "http://127.0.0.1:9/traces",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_SERVICE_NAME"] = "named-by-the-runner",
            })
            .Build();
        var services = new ServiceCollection();
        var builder = services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(SourceName));
        var before = services.Select(d => d.ServiceType).ToList();

        builder.AddLocaltestOtlp(configuration);
        builder.AddLocaltestOtlpResource(configuration);

        Assert.False(LocaltestOtlp.IsEnabled(configuration));
        Assert.Equal(before, services.Select(d => d.ServiceType).ToList());
    }

    [Fact]
    public void WithoutTheVariable_ASignalSpecificExporterStillWorks()
    {
        // UseOtlpExporter and a signal-specific AddOtlpExporter cannot be combined; without the variable the helper must
        // not have called it, so a host's own signal-specific exporter is unaffected.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddOpenTelemetry()
            .AddLocaltestOtlp(configuration)
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter());

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<TracerProvider>());
    }

    [Fact]
    public async Task ResourceOnly_NamesAHostThatExportsByItself()
    {
        // A host that exports by itself (the Altinn.Authorization.ServiceDefaults shape until task 61 removed this call
        // there; no consumer now): signal-specific AddOtlpExporter when the variable is set, and a resource detector with a
        // hard-coded service.name.
        await using var collector = await FakeCollector.Start(HttpProtocols.Http1);
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_SERVICE_NAME"] = "named-by-the-runner",
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ComponentName))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddOtlpExporter())
            .AddLocaltestOtlpResource(builder.Configuration);

        using (var host = builder.Build())
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            using var source = new ActivitySource(SourceName);
            source.StartActivity("the-span")?.Dispose();
            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush());
            await host.StopAsync(TestContext.Current.CancellationToken);
        }

        var received = await collector.WaitFor("/v1/traces");
        Assert.Contains("named-by-the-runner", received["/v1/traces"]);
        Assert.DoesNotContain(ComponentName, received["/v1/traces"]);
    }

    /// <summary>A component as most of ours look: a hard-coded service name, a source, a meter, logs, then the helper.</summary>
    private static async Task RunComponent(
        Dictionary<string, string?> settings, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        configure?.Invoke(builder);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ComponentName))
            .WithTracing(tracing => tracing.AddSource(SourceName))
            .WithMetrics(metrics => metrics.AddMeter(SourceName))
            .AddLocaltestOtlp(builder.Configuration);

        using (var host = builder.Build())
        {
            await host.StartAsync();

            using var source = new ActivitySource(SourceName);
            using (source.StartActivity("the-span"))
            {
                host.Services.GetRequiredService<ILogger<LocaltestOtlpTests>>().LogWarning("the log line");
            }

            using var meter = new Meter(SourceName);
            meter.CreateCounter<long>("the-counter").Add(1);

            // Flushed before the host stops: on disposal the exporters' HttpClient can be gone before the log provider's
            // last batch (seen as an ObjectDisposedException in the exporter's event source).
            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush());
            Assert.True(host.Services.GetRequiredService<MeterProvider>().ForceFlush());
            Assert.True(host.Services.GetRequiredService<LoggerProvider>().ForceFlush());
            await host.StopAsync();
        }
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<(string Category, LogLevel Level)> logged) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, logged);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<(string Category, LogLevel Level)> logged) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logged.Enqueue((category, logLevel));
        }
    }

    private sealed class FakeCollector : IAsyncDisposable
    {
        private readonly WebApplication app;
        private readonly ConcurrentQueue<(string Path, string Body)> requests = new();

        private FakeCollector(WebApplication app) => this.app = app;

        public string Endpoint { get; private set; } = "";

        public static async Task<FakeCollector> Start(HttpProtocols protocols)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel =>
                kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = protocols));
            var app = builder.Build();
            var collector = new FakeCollector(app);
            app.Run(async context =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                // Protobuf carries strings as UTF-8 bytes, so the names are found in the uncompressed body as they are.
                collector.requests.Enqueue((context.Request.Path.Value ?? "", Encoding.UTF8.GetString(body.ToArray())));
                if (context.Request.Protocol == "HTTP/2")
                {
                    context.Response.ContentType = "application/grpc";
                    context.Response.AppendTrailer("grpc-status", "0");
                    await context.Response.Body.WriteAsync(new byte[5]);
                }
                else
                {
                    context.Response.ContentType = "application/x-protobuf";
                }
            });
            await app.StartAsync();
            collector.Endpoint = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            return collector;
        }

        /// <summary>The bodies received on each path, once every path has had a request.</summary>
        public async Task<Dictionary<string, string>> WaitFor(params string[] paths)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                var received = requests
                    .GroupBy(r => r.Path)
                    .ToDictionary(g => g.Key, g => string.Concat(g.Select(r => r.Body)));
                if (paths.All(received.ContainsKey))
                {
                    return received;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Expected requests on {string.Join(", ", paths)}; got {string.Join(", ", received.Keys)}");
                }

                await Task.Delay(100);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
