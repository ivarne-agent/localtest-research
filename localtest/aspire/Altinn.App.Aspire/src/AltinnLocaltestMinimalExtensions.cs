using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire;

/// <summary>Adds the Altinn localtest platform in minimal mode to an Aspire AppHost.</summary>
public static class AltinnLocaltestMinimalExtensions
{
    /// <summary>
    /// The parent resource's default name in minimal mode: the dashboard names the mode. The package's volume prefix
    /// does not follow it (minimal mode has no volumes), and the containers are named after the manifest as in the
    /// full stack.
    /// </summary>
    public const string DefaultName = AltinnLocaltestExtensions.DefaultName + "-minimal";

    /// <summary>
    /// Adds the localtest platform in minimal mode, as <c>studioctl env up -r localtest-v2 --minimal</c> runs it: the
    /// manifest's <c>minimal</c> group (Postgres, Storage, the workflow engine and the localtest container) in place of
    /// the <c>default</c> one, the localtest container emulating the rest of the default group (Register, Profile,
    /// Authentication, Authorization permitting everything, Access Management, Events, the Resource Registry,
    /// Notifications and the PDF service), its port 8080 published as <c>http://local.altinn.cloud:8000</c>. Nothing
    /// persists: the containers live for the AppHost's run (session lifetime), each manifest volume is a tmpfs, and the
    /// front door keeps its app routes in memory, so every start is a clean slate. There are no optional groups, and no
    /// method enables another service on the returned builder; use <c>AddAltinnLocaltest()</c> for the real
    /// components.
    /// </summary>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="name">The parent resource's name.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The parent resource, which is running once every container is healthy.</returns>
    public static IResourceBuilder<AltinnLocaltestMinimalResource> AddAltinnLocaltestMinimal(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = DefaultName,
        Action<AltinnLocaltestMinimalOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AltinnLocaltestMinimalOptions();
        configure?.Invoke(options);

        // No reset command: there is nothing to reset. The containers and their tmpfs go when the AppHost stops, and
        // the full stack's altinn-localtest-* volumes are never mounted, so they are intact for the next full run.
        var stack = new LocaltestStack(builder, LocaltestManifest.Default, options);
        return LocaltestPlatform.Add(
            builder,
            new AltinnLocaltestMinimalResource(name, stack),
            "AltinnLocaltestMinimal",
            []
        );
    }
}
