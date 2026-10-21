using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>Adds the Altinn localtest platform to an Aspire AppHost.</summary>
public static class AltinnLocaltestExtensions
{
    /// <summary>
    /// Adds the localtest platform: the always-on components of the localtest-v2 manifest as persistent containers
    /// (the images, environment, ports, volumes and certificate studioctl runs), and a YARP front door on
    /// <c>http://local.altinn.cloud:8000</c> with the manifest's routes. Optional groups come from
    /// <see cref="AltinnLocaltestOptions.Groups" /> or <see cref="WithGroup" />.
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
            certificateDirectory
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
        stack.Initialize(parent, options.Groups);

        // The certificate is written when the AppHost starts, not when the model is built.
        builder.Eventing.Subscribe<BeforeStartEvent>(
            (_, _) =>
            {
                LocaltestCertificate.Ensure(certificateDirectory, manifest);
                return Task.CompletedTask;
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

    private static async Task ReportWhenHealthyAsync(InitializeResourceEvent e, CancellationToken cancellationToken)
    {
        var resource = (AltinnLocaltestResource)e.Resource;
        await e
            .Notifications.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.Starting })
            .ConfigureAwait(false);
        try
        {
            await Task.WhenAll(
                    resource.Stack.Children.Select(child =>
                        e.Notifications.WaitForResourceHealthyAsync(child.Name, cancellationToken)
                    )
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            e.Logger.LogLocaltestFailed(exception);
            await e
                .Notifications.PublishUpdateAsync(resource, s => s with { State = KnownResourceStates.FailedToStart })
                .ConfigureAwait(false);
            return;
        }
        await e
            .Notifications.PublishUpdateAsync(
                resource,
                s => s with { State = KnownResourceStates.Running, StartTimeStamp = DateTime.UtcNow }
            )
            .ConfigureAwait(false);
    }
}
