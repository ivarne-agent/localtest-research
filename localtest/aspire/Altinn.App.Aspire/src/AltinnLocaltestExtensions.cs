using System.Diagnostics;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

/// <summary>Adds the Altinn localtest platform to an Aspire AppHost.</summary>
public static class AltinnLocaltestExtensions
{
    /// <summary>The parent resource's default name; the localtest container is <c>localtest</c>.</summary>
    public const string DefaultName = "altinn-localtest";

    /// <summary>
    /// Adds the localtest platform: the containers of the localtest-v2 manifest's <c>default</c> group as persistent
    /// containers (the images, environment, mounts and certificate studioctl runs, with the package's own volumes,
    /// <c>altinn-localtest-&lt;id&gt;</c>), the localtest container's port 8080 published as
    /// <c>http://local.altinn.cloud:8000</c>, the front door. Optional groups come from
    /// <see cref="AltinnLocaltestOptions.Groups" />, <see cref="WithGroup" /> or a typed alias such as
    /// <see cref="WithDialogporten" />.
    /// </summary>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="name">The parent resource's name.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The parent resource, which is running once every container is healthy.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> AddAltinnLocaltest(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = DefaultName,
        Action<AltinnLocaltestOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AltinnLocaltestOptions();
        configure?.Invoke(options);

        var stack = new LocaltestStack(builder, LocaltestManifest.Default, options);
        var parent = builder
            .AddResource(new AltinnLocaltestResource(name, stack))
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "AltinnLocaltest",
                    State = KnownResourceStates.NotStarted,
                    Properties =
                    [
                        new ResourcePropertySnapshot("localtest.platformUri", Topology.PlatformUri.ToString()),
                    ],
                }
            );
        stack.Initialize(parent, options.Groups);

        // The port check and the certificate run when the AppHost starts, not when the model is built.
        builder.Eventing.Subscribe<BeforeStartEvent>(
            async (e, ct) =>
            {
                if (builder.ExecutionContext.IsRunMode)
                {
                    var own = await OwnFrontDoorRunningAsync(builder.Configuration, ct).ConfigureAwait(false);
                    PortGuard.EnsureFree(Topology.IngressPort, () => own);
                }
                LocaltestCertificate.Ensure(stack.CertificateDirectory);
            }
        );
        parent.WithCommand(
            LocaltestReset.CommandName,
            "Reset",
            context => LocaltestReset.ExecuteAsync(context, parent.Resource),
            new CommandOptions
            {
                Description =
                    "Remove the localtest containers and their volumes (as studioctl env reset), then restart the AppHost",
                ConfirmationMessage = LocaltestReset.Confirmation,
                IconName = "ArrowReset",
            }
        );
        builder.Eventing.Subscribe<InitializeResourceEvent>(parent.Resource, ReportWhenHealthyAsync);
        return parent;
    }

    /// <summary>
    /// Routes <c>app-frontend.local.altinn.cloud:8000</c> to a frontend dev server (Vite) on the host, as
    /// <c>studioctl run --dev-frontend</c> does: the route is registered with the front door once the localtest
    /// container is healthy (<c>PUT localtest/api/routes/frontend-dev-server</c>), and every app added with
    /// <c>AddAltinnApp</c> loads app-frontend from it (<c>AppSettings__AppFrontendAssetBaseUrl</c>).
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <param name="port">The dev server's port on the host.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithFrontendDevServer(
        this IResourceBuilder<AltinnLocaltestResource> builder,
        int port = 8080
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        var stack = builder.Resource.Stack;
        if (stack.FrontendDevServerPort is null)
        {
            builder.ApplicationBuilder.Eventing.Subscribe<ResourceReadyEvent>(
                stack.FrontDoor.Resource,
                async (e, ct) =>
                {
                    var console = e.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Altinn.App.Aspire");
                    try
                    {
                        var answer = await FrontDoorClient
                            .PutAsync(
                                Topology.PlatformUri,
                                FrontDoorClient.DevServerPath,
                                FrontDoorClient.HostAddress(stack.FrontendDevServerPort!.Value),
                                ct
                            )
                            .ConfigureAwait(false);
                        console.LogDevServerRoute(answer.ToString());
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        console.LogDevServerRouteFailed(
                            exception,
                            new Uri(Topology.PlatformUri, FrontDoorClient.DevServerPath).ToString()
                        );
                    }
                }
            );
        }
        stack.FrontendDevServerPort = port;
        return builder;
    }

    /// <summary>
    /// Enables an optional group of the manifest: its root containers and what they depend on (<c>correspondence</c>
    /// starts Dialogporten's WebApi, its dependency; <c>everything</c> enables every group). A group that is off has
    /// no containers and no <c>services__</c> variables, so the front door answers 503 "not running" for it.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <param name="group">The group's name.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithGroup(
        this IResourceBuilder<AltinnLocaltestResource> builder,
        string group
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        builder.Resource.Stack.AddGroups([group]);
        return builder;
    }

    // One typed alias per optional group of the manifest, and everything; GroupMethodsTests keeps the set equal to the
    // manifest's groups.

    /// <summary>
    /// Enables the <c>dialogporten</c> group: Dialogporten's WebApi, GraphQL and Service hosts, its Redis, and the
    /// adapter that turns Storage's instances into dialogs.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithDialogporten(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("dialogporten");

    /// <summary>
    /// Enables the <c>receipt</c> group: the platform receipt for archived instances.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithReceipt(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("receipt");

    /// <summary>
    /// Enables the <c>correspondence</c> group: Altinn Correspondence, with what it depends on: Redis and Dialogporten's
    /// WebApi among them, which its publish needs.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithCorrespondence(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("correspondence");

    /// <summary>
    /// Enables the <c>arbeidsflate</c> group: Arbeidsflate, the Altinn inbox (the SPA and its BFF) on
    /// <c>af.local.altinn.cloud</c>. With what it depends on: Dialogporten's WebApi and GraphQL hosts among them.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithArbeidsflate(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("arbeidsflate");

    /// <summary>Enables every optional group.</summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithEverything(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("everything");

    /// <summary>
    /// Whether this AppHost's own localtest container runs, from a previous run (it is persistent): then it holds port
    /// 8000, and the next run reuses it. Aspire names a persistent container after the resource and the first eight
    /// characters of the AppHost's hash.
    /// </summary>
    private static async Task<bool> OwnFrontDoorRunningAsync(IConfiguration configuration, CancellationToken ct)
    {
        if (configuration["AppHost:Sha256"] is not { Length: >= 8 } hash)
        {
            return false;
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
            return exitCode == 0 && output.Trim() == "true";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task ReportWhenHealthyAsync(InitializeResourceEvent e, CancellationToken cancellationToken)
    {
        var resource = (AltinnLocaltestResource)e.Resource;
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
        await e
            .Notifications.PublishUpdateAsync(
                resource,
                s => s with { State = KnownResourceStates.Running, StartTimeStamp = DateTime.UtcNow }
            )
            .ConfigureAwait(false);
    }
}
