using System.Diagnostics;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

/// <summary>Adds the Altinn localtest platform to an Aspire AppHost.</summary>
public static class AltinnLocaltestExtensions
{
    /// <summary>
    /// Adds the localtest platform: the always-on components of the localtest-v2 manifest as persistent containers
    /// (the images, environment, ports, volumes and certificate studioctl runs), and a YARP front door on
    /// <c>http://local.altinn.cloud:8000</c> with the manifest's routes. Optional groups come from
    /// <see cref="AltinnLocaltestOptions.Groups" />, <see cref="WithGroup" /> or a typed alias such as
    /// <see cref="WithDialogporten" />.
    /// </summary>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="name">The parent resource's name.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The parent resource, which is running once every component and the front door is healthy.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> AddAltinnLocaltest(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "localtest",
        Action<AltinnLocaltestOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AltinnLocaltestOptions();
        configure?.Invoke(options);

        var manifest = ManifestLoader.Default;
        var certificateDirectory = Path.Combine(options.DataDirectory, "certs");
        var stack = new LocaltestStack(
            builder,
            new ManifestResolver(manifest, certificateDirectory, options.Tag),
            certificateDirectory,
            options
        );
        var parent = builder
            .AddResource(new AltinnLocaltestResource(name, stack))
            .WithInitialState(
                new CustomResourceSnapshot
                {
                    ResourceType = "AltinnLocaltest",
                    State = KnownResourceStates.NotStarted,
                    Properties =
                    [
                        new ResourcePropertySnapshot("localtest.manifestTag", manifest.Images.Tag),
                        new ResourcePropertySnapshot("localtest.platformUri", stack.PlatformUri.ToString()),
                    ],
                }
            );
        stack.Initialize(parent, options.Groups, options.OnlyComponents);

        // The port check and the certificate run when the AppHost starts, not when the model is built.
        builder.Eventing.Subscribe<BeforeStartEvent>(
            (_, _) =>
            {
                if (builder.ExecutionContext.IsRunMode)
                {
                    PortGuard.EnsureFree(stack.Resolver.Topology.IngressPort);
                }
                LocaltestCertificate.Ensure(certificateDirectory, manifest);
                return Task.CompletedTask;
            }
        );
        parent.WithCommand(
            LocaltestReset.CommandName,
            "Reset",
            context => LocaltestReset.ExecuteAsync(context, parent.Resource),
            new CommandOptions
            {
                Description =
                    "Remove the localtest containers and the named volumes (studioctl env reset), then restart the AppHost",
                ConfirmationMessage = LocaltestReset.Confirmation,
                IconName = "ArrowReset",
            }
        );
        builder.Eventing.Subscribe<InitializeResourceEvent>(parent.Resource, ReportWhenHealthyAsync);
        return parent;
    }

    /// <summary>
    /// Enables an optional component group and the groups it implies (<c>correspondence</c> brings
    /// <c>dialogporten</c>; <c>everything</c> brings all). Components of groups that are not enabled are not added, so a
    /// request to their prefix falls through to the localtest app.
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

    // One typed alias per manifest group; GroupMethodsTests keeps the set equal to the manifest's groups.

    /// <summary>
    /// Enables the <c>dialogporten</c> group: Dialogporten's WebApi, GraphQL and Service hosts, its Redis, and the
    /// adapter that turns Storage's instances into dialogs. Implies no other group.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithDialogporten(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("dialogporten");

    /// <summary>
    /// Enables the <c>receipt</c> group: the platform receipt for archived instances. Implies no other group.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithReceipt(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("receipt");

    /// <summary>
    /// Enables the <c>correspondence</c> group: Altinn Correspondence. Also brings Dialogporten (the
    /// <c>dialogporten</c> group), which its publish needs.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithCorrespondence(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("correspondence");

    /// <summary>
    /// Enables the <c>arbeidsflate</c> group: Arbeidsflate, the Altinn inbox (the SPA and its BFF) on
    /// <c>af.local.altinn.cloud</c>. Also brings Dialogporten (the <c>dialogporten</c> group), whose dialogs it shows.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithArbeidsflate(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("arbeidsflate");

    /// <summary>
    /// Enables the <c>everything</c> group, which owns no components and brings every other group.
    /// </summary>
    /// <param name="builder">The localtest resource.</param>
    /// <returns>The same builder.</returns>
    public static IResourceBuilder<AltinnLocaltestResource> WithEverything(
        this IResourceBuilder<AltinnLocaltestResource> builder
    ) => builder.WithGroup("everything");

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
                    resource.Stack.Children.Select(async child =>
                    {
                        await e
                            .Notifications.WaitForResourceHealthyAsync(child.Name, cancellationToken)
                            .ConfigureAwait(false);
                        console.LogComponentHealthy(child.Name, started.Elapsed.TotalSeconds);
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
