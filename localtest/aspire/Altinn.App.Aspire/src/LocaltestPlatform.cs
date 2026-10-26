using System.Diagnostics;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

/// <summary>
/// What <c>AddAltinnLocaltest</c> and <c>AddAltinnLocaltestMinimal</c> do alike: the parent resource, the containers,
/// the port check, the certificate and the manifest's files before the start, and the parent's state from its containers' health.
/// </summary>
internal static class LocaltestPlatform
{
    /// <summary>Adds <paramref name="resource" /> with its stack's containers and the groups.</summary>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="resource">The parent resource, which holds the stack.</param>
    /// <param name="resourceType">The resource type the dashboard shows.</param>
    /// <param name="groups">The optional groups to enable (none in minimal mode).</param>
    public static IResourceBuilder<T> Add<T>(
        IDistributedApplicationBuilder builder,
        T resource,
        string resourceType,
        IEnumerable<string> groups
    )
        where T : AltinnLocaltestResourceBase
    {
        var stack = resource.Stack;
        var parent = builder
            .AddResource(resource)
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = resourceType,
                    State = KnownResourceStates.NotStarted,
                    Properties =
                    [
                        new ResourcePropertySnapshot("localtest.platformUri", stack.Origin.Url.Then("/").ToString()),
                        new ResourcePropertySnapshot("localtest.mode", stack.Minimal ? "minimal" : "default"),
                    ],
                }
            );
        stack.Initialize(parent, groups);
        // The containers send their telemetry to the dashboard (LocaltestStack), which they must reach.
        builder.Services.TryAddEventingSubscriber<DashboardTelemetry.HostWideOtlp>();

        // The port check, the certificate and the manifest's files run when the AppHost starts, not when the model is built.
        builder.Eventing.Subscribe<BeforeStartEvent>(
            async (e, ct) =>
            {
                // An allocated port (PublicPort = 0) is free by construction.
                if (builder.ExecutionContext.IsRunMode && stack.Origin.Port is { } port)
                {
                    var own = await OwnFrontDoorAsync(builder.Configuration, ct).ConfigureAwait(false);
                    PortGuard.EnsureFree(
                        port,
                        stack.Origin.Host,
                        persistentFrontDoor: !stack.Minimal,
                        own,
                        ContainerRuntime.Cli(builder.Configuration)
                    );
                }
                // Before Aspire's dashboard reads the key (its subscriber runs after this handler).
                if (builder.ExecutionContext.IsRunMode && !stack.Minimal)
                {
                    DashboardTelemetry.KeepOtlpApiKey(builder.Configuration, stack.DataDirectory);
                }
                LocaltestCertificate.Ensure(stack.CertificateDirectory);
                stack.Manifest.Files.Ensure(stack.FilesDirectory);
            }
        );
        builder.Eventing.Subscribe<InitializeResourceEvent>(parent.Resource, ReportWhenHealthyAsync);
        return parent;
    }

    /// <summary>
    /// This AppHost's persistent localtest container when it runs, from a previous run of the full stack: then it holds
    /// the public port. Aspire names a persistent container after the resource and the first eight characters of the
    /// AppHost's hash.
    /// </summary>
    private static async Task<string?> OwnFrontDoorAsync(IConfiguration configuration, CancellationToken ct)
    {
        if (configuration["AppHost:Sha256"] is not { Length: >= 8 } hash)
        {
            return null;
        }
#pragma warning disable CA1308 // Aspire lower-cases the suffix.
        var name = $"{Topology.FrontDoor}-{hash[..8].ToLowerInvariant()}";
#pragma warning restore CA1308
        try
        {
            var (exitCode, output) = await ContainerRuntime
                .RunAsync(
                    ContainerRuntime.Cli(configuration),
                    ["container", "inspect", "--format", "{{.State.Running}}", name],
                    ct
                )
                .ConfigureAwait(false);
            return exitCode == 0 && output.Trim() == "true" ? name : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task ReportWhenHealthyAsync(InitializeResourceEvent e, CancellationToken cancellationToken)
    {
        var resource = (AltinnLocaltestResourceBase)e.Resource;
        // The AppHost's console, besides the resource's own log in the dashboard.
        var console = e.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Altinn.App.Aspire");
        var started = Stopwatch.StartNew();
        await e
            .Notifications.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Starting })
            .ConfigureAwait(false);
        try
        {
            await Task.WhenAll(
                    resource.Stack.Containers.Values.Select(async container =>
                    {
                        await e
                            .Notifications.WaitForResourceHealthyAsync(container.Resource.Name, cancellationToken)
                            .ConfigureAwait(false);
                        console.LogContainerHealthy(container.Resource.Name, started.Elapsed.TotalSeconds);
                    })
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            e.Logger.LogLocaltestFailed(exception);
            console.LogLocaltestFailed(exception);
            await e
                .Notifications.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.FailedToStart })
                .ConfigureAwait(false);
            return;
        }
        console.LogLocaltestHealthy(resource.Name, started.Elapsed.TotalSeconds);
        // The origin with its port, which Aspire has allocated by now when PublicPort = 0.
        var platformUri = resource.PlatformUri.ToString();
        await e
            .Notifications.PublishUpdateAsync(
                resource,
                s =>
                    s with
                    {
                        State = KnownResourceStates.Running,
                        StartTimeStamp = DateTime.UtcNow,
                        Properties =
                        [
                            .. s.Properties.Where(p => p.Name != "localtest.platformUri"),
                            new ResourcePropertySnapshot("localtest.platformUri", platformUri),
                        ],
                    }
            )
            .ConfigureAwait(false);
    }
}
