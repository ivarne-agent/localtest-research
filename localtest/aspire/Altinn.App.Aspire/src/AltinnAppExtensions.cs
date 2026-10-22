using Altinn.App.Aspire.Manifest;
using Altinn.Studio.Localtest.Provisioning;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>Adds an Altinn app project to an Aspire AppHost, run against the localtest platform.</summary>
public static class AltinnAppExtensions
{
    /// <summary>The name of the app's HTTP endpoint.</summary>
    public const string HttpEndpoint = "http";

    /// <summary>The app's health path (app-lib's <c>/health</c>).</summary>
    public const string HealthPath = "/health";

    /// <summary>The app command that provisions the app again.</summary>
    public const string ProvisionCommand = "provision";

    /// <summary>The app command that removes the app's test users from the Register.</summary>
    public const string DeprovisionCommand = "deprovision";

    /// <summary>
    /// Adds an Altinn app project (<c>Projects.App</c> from the AppHost's <c>#:project</c> or project reference) that
    /// runs against <paramref name="localtest" />: the environment of the manifest's app section (the platform
    /// endpoints, host name and base URL, the OpenID well-known endpoint, the test certificate, the secrets directory
    /// with the development app codes), one HTTP endpoint on an Aspire-assigned port on every interface, the route
    /// <c>/{org}/{app}</c> on the front door, provisioning on the platform once the app is ready, and the
    /// <c>provision</c> and <c>deprovision</c> commands. The app id is read from the project's
    /// <c>config/applicationmetadata.json</c>.
    /// </summary>
    /// <typeparam name="TProject">The project's metadata type.</typeparam>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="localtest">The platform, from <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />.</param>
    /// <param name="name">The app resource's name.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The app's project resource.</returns>
    /// <exception cref="InvalidOperationException">The project has no readable application metadata with an id.</exception>
    public static IResourceBuilder<ProjectResource> AddAltinnApp<TProject>(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AltinnLocaltestResource> localtest,
        [ResourceName] string name = "app",
        Action<AltinnAppOptions>? configure = null
    )
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        return AddAltinnApp(
            builder,
            localtest,
            name,
            new TProject().ProjectPath,
            configure,
            () => builder.AddProject<TProject>(name, ProjectOptions)
        );
    }

    /// <summary>
    /// Adds an Altinn app project by the path of its <c>.csproj</c> (absolute, or relative to the AppHost's
    /// directory), as <see cref="AddAltinnApp{TProject}" /> does: for an AppHost without the generated
    /// <c>Projects.*</c> metadata.
    /// </summary>
    /// <param name="builder">The AppHost's builder.</param>
    /// <param name="localtest">The platform, from <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />.</param>
    /// <param name="projectPath">The app's project file.</param>
    /// <param name="name">The app resource's name.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The app's project resource.</returns>
    /// <exception cref="InvalidOperationException">The project has no readable application metadata with an id.</exception>
    public static IResourceBuilder<ProjectResource> AddAltinnApp(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AltinnLocaltestResource> localtest,
        string projectPath,
        [ResourceName] string name = "app",
        Action<AltinnAppOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullPath = Path.GetFullPath(projectPath, builder.AppHostDirectory);
        return AddAltinnApp(
            builder,
            localtest,
            name,
            fullPath,
            configure,
            () => builder.AddProject(name, fullPath, ProjectOptions)
        );
    }

    private static void ProjectOptions(ProjectResourceOptions options)
    {
        // The template's launch profile (local.altinn.cloud:8000) and Kestrel endpoint (port 5005) are studioctl's; the
        // package gives the app its own endpoint.
        options.ExcludeLaunchProfile = true;
        options.ExcludeKestrelEndpoints = true;
    }

    private static IResourceBuilder<ProjectResource> AddAltinnApp(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<AltinnLocaltestResource> localtest,
        string name,
        string projectPath,
        Action<AltinnAppOptions>? configure,
        Func<IResourceBuilder<ProjectResource>> addProject
    )
    {
        ArgumentNullException.ThrowIfNull(localtest);
        var options = new AltinnAppOptions();
        configure?.Invoke(options);
        var stack = localtest.Resource.Stack;
        var manifest = stack.Resolver.Manifest;

        var projectDirectory =
            Path.GetDirectoryName(Path.GetFullPath(projectPath))
            ?? throw new ArgumentException($"{projectPath} has no folder.", nameof(projectPath));
        var source = new DirectoryAppSource(projectDirectory);
        var appId = ReadAppId(source, name);

        var app = addProject();
        // One endpoint without a proxy: Aspire assigns the port (the same for Port and TargetPort), and the app listens
        // on every interface (ASPNETCORE_URLS uses the target host), so the front door reaches it through the host
        // gateway. The template's appsettings.json names a Kestrel endpoint, which wins over ASPNETCORE_URLS, so the
        // Kestrel URL is set too, as studioctl sets it.
        app.WithHttpEndpoint(name: HttpEndpoint, isProxied: false)
            .WithEndpoint(HttpEndpoint, endpoint => endpoint.TargetHost = "0.0.0.0");
        var endpoint = app.GetEndpoint(HttpEndpoint);
        app.WithEnvironment(
            "Kestrel__EndPoints__Http__Url",
            ReferenceExpression.Create(
                $"{Uri.UriSchemeHttp}://0.0.0.0:{endpoint.Property(EndpointProperty.TargetPort)}"
            )
        );

        // The manifest's app section, as studioctl's run sets it, with the package's certificate and secrets directory.
        var appRoot = Path.GetDirectoryName(source.AppDirectory) ?? source.AppDirectory;
        var secretsDirectory = AppSecrets.Directory(stack.DataDirectory, appId.Org, appId.App);
        var certificate = AppCertificate.ForProcess(appRoot, stack.CertificateDirectory, manifest.Certificate.FileName);
        foreach (var (key, value) in stack.Resolver.AppEnvironment(secretsDirectory, certificate).AllVariables())
        {
            app.WithEnvironment(key, value);
        }
        if (options.OpenTelemetry)
        {
            app.WithEnvironment("AppSettings__UseOpenTelemetry", "true");
        }
        var appCodes = manifest.App.Secrets.File(ManifestAppSecretFile.AppCodes);
        builder.Eventing.Subscribe<BeforeStartEvent>(
            (_, _) =>
            {
                AppSecrets.WriteDevelopmentAppCodes(secretsDirectory, appCodes.FileName);
                return Task.CompletedTask;
            }
        );

        app.WithHttpHealthCheck(HealthPath, endpointName: HttpEndpoint).WaitFor(localtest);
        stack.AddAppRoute(name, appId.Org, appId.App, endpoint);
        app.WithUrlForEndpoint(
            HttpEndpoint,
            url =>
            {
                url.DisplayText = "Direct (no front door)";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            }
        );
        app.WithUrl(new Uri(stack.PlatformUri, $"{appId.Org}/{appId.App}/").ToString(), "App (front door)");

        AddProvisioning(builder, localtest, app, appId, source, options);
        return app;
    }

    private static AppId ReadAppId(DirectoryAppSource source, string name)
    {
        try
        {
            return source.ReadAppId(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException(
                $"AddAltinnApp(\"{name}\"): the app id is read from {Path.Combine(source.AppDirectory, "config", "applicationmetadata.json")}, "
                    + $"which the project folder must hold with an \"id\" of the form org/app: {e.Message}",
                e
            );
        }
    }

    private static void AddProvisioning(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<AltinnLocaltestResource> localtest,
        IResourceBuilder<ProjectResource> app,
        AppId appId,
        DirectoryAppSource source,
        AltinnAppOptions options
    )
    {
        var provisioning = new AppProvisioning(
            appId,
            source,
            localtest.Resource.PlatformUri,
            () => localtest.Resource.Groups.Contains("dialogporten")
        );
        var key = $"{app.Resource.Name}_provisioning";
        builder
            .Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(key, _ => new ProvisioningHealthCheck(provisioning), null, null));
        app.WithHealthCheck(key);
        app.WithAnnotation(new AppProvisioningAnnotation(provisioning));

        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(
            app.Resource,
            (_, _) =>
            {
                provisioning.Reset();
                return Task.CompletedTask;
            }
        );
        if (options.ProvisionWhenReady)
        {
            builder.Eventing.Subscribe<ResourceReadyEvent>(
                app.Resource,
                (e, ct) => provisioning.ProvisionAsync(e.Services, e.Resource, ct)
            );
        }
        app.WithCommand(
            ProvisionCommand,
            "Provision",
            async context =>
                Result(
                    await provisioning
                        .ProvisionAsync(context.Services, app.Resource, context.CancellationToken)
                        .ConfigureAwait(false)
                ),
            new CommandOptions
            {
                Description =
                    "Push the app's test users, metadata, texts and policy to the platform again (from its files on disk)",
                IconName = "CloudArrowUp",
            }
        );
        app.WithCommand(
            DeprovisionCommand,
            "Deprovision",
            async context =>
                Result(
                    await provisioning
                        .DeprovisionAsync(context.Services, app.Resource, context.CancellationToken)
                        .ConfigureAwait(false)
                ),
            new CommandOptions
            {
                Description = "Remove the app's test users from the Register (Storage and the PDP keep the app)",
                IconName = "CloudDismiss",
                ConfirmationMessage = $"Remove {appId}'s test users from the Register?",
            }
        );
    }

    private static ExecuteCommandResult Result(ProvisioningStatus status) =>
        status.State == ProvisioningState.Failed
            ? CommandResults.Failure(status.Message)
            : CommandResults.Success(status.Message);
}

/// <summary>Holds an app's provisioning on its resource, for the AppHost's own code and the tests.</summary>
/// <param name="Provisioning">The provisioning.</param>
internal sealed record AppProvisioningAnnotation(AppProvisioning Provisioning) : IResourceAnnotation;
