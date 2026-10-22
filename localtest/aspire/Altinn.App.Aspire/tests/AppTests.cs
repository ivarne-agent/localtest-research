using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.App.Aspire.Tests;

/// <summary>The app side: <c>AddAltinnApp</c> in the sample's model, the app section, the secrets, the guards.</summary>
public sealed class AppTests
{
    private static readonly string[] _runnerKeys =
    [
        "Kestrel__EndPoints__Http__Url",
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "AppSettings__AppFrontendAssetBaseUrl",
        "ALTINN_KEYS_DIRECTORY",
    ];

    private readonly SampleModel _model;
    private readonly ProjectResource _app;

    public AppTests()
    {
        _model = SampleModel.Build();
        _app = _model.App!.Resource;
    }

    [Fact]
    public async Task TheAppGetsEveryVariableOfTheManifestsAppSection()
    {
        var environment = await _model.Environment(_app);
        var app = ManifestLoader.Default.App;
        string[] keys =
        [
            .. app.Environment.Keys,
            .. app.FixedEnvironment.Keys,
            .. app.AccessTokenSigning.Keys,
            app.Secrets.DirectoryEnv,
            .. app.Secrets.Files.Select(f => f.Env),
        ];

        Assert.All(keys, key => Assert.True(environment.ContainsKey(key), key));
        Assert.All(keys, key => Assert.DoesNotContain("{{", environment[key], StringComparison.Ordinal));
        Assert.Equal("1", environment["STUDIOCTL_APP_RUN"]);
        Assert.Equal("Development", environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("true", environment["AppSettings__UseOpenTelemetry"]);
        Assert.Equal(
            "http://local.altinn.cloud:8000/authentication/api/v1/openid/",
            environment["AppSettings__OpenIdWellKnownEndpoint"]
        );
        Assert.Equal("http://local.altinn.cloud:8000/{org}/{app}/", environment["GeneralSettings__ExternalAppBaseUrl"]);
        Assert.Equal("local.altinn.cloud", environment["GeneralSettings__HostName"]);
        Assert.Equal(
            "http://local.altinn.cloud:8000/storage/api/v1/",
            environment["PlatformSettings__ApiStorageEndpoint"]
        );
        Assert.Equal("http://pdf.local.altinn.cloud:8000/pdf", environment["PlatformSettings__ApiPdf2Endpoint"]);
        Assert.Equal(
            "http://workflow-engine.local.altinn.cloud:8000/api/v1/",
            environment["PlatformSettings__ApiWorkflowEngineEndpoint"]
        );
        Assert.Equal(
            "/data-directory/certs/localtest-accesstoken.pfx",
            environment["AccessTokenSettings__AccessTokenSigningCertificateFullPath"]
        );
        Assert.Equal(
            "localtest-accesstoken.pfx",
            environment["AccessTokenSettings__AccessTokenSigningCertificateFileName"]
        );
        var appRoot = Path.GetDirectoryName(Path.GetDirectoryName(new SampleAppProject().ProjectPath))!;
        var keysFolder = environment["AccessTokenSettings__AccessTokenSigningKeysFolder"];
        Assert.StartsWith("/", keysFolder, StringComparison.Ordinal);
        Assert.EndsWith("/", keysFolder, StringComparison.Ordinal);
        Assert.Equal("/data-directory/certs", Path.GetFullPath(Path.Combine(appRoot, keysFolder.Trim('/'))));
        Assert.Equal("/data-directory/apps/ttd/testapp/secrets", environment["RUNTIME_APP_SECRETS_DIR"]);
        Assert.Equal("app-codes.json", environment["RUNTIME_APP_SECRETS_APPCODES_FILENAME"]);
        Assert.Equal("maskinporten-settings.json", environment["RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME"]);
    }

    [Fact]
    public async Task TheAppListensOnEveryInterfaceOnAnAspireAssignedPort()
    {
        var endpoint = Assert.Single(_app.Annotations.OfType<EndpointAnnotation>());

        Assert.Equal(AltinnAppExtensions.HttpEndpoint, endpoint.Name);
        Assert.Equal("http", endpoint.UriScheme);
        Assert.False(endpoint.IsProxied);
        Assert.Equal("0.0.0.0", endpoint.TargetHost);
        Assert.Null(endpoint.Port);
        Assert.Equal(
            "http://0.0.0.0:{app.bindings.http.targetPort}",
            await _model.EnvironmentExpression(_app, "Kestrel__EndPoints__Http__Url")
        );
    }

    [Fact]
    public async Task TheOpenTelemetryOptionTurnsTheExportOff()
    {
        var model = SampleModel.Build(configureApp: o => o.OpenTelemetry = false);

        var environment = await model.Environment(model.App!.Resource);

        Assert.DoesNotContain("AppSettings__UseOpenTelemetry", environment.Keys);
    }

    [Fact]
    public void TheAppWaitsForThePlatformAndIsHealthyOnItsHealthPathAndItsProvisioning()
    {
        Assert.Contains(
            _app.Annotations.OfType<WaitAnnotation>(),
            w => w.Resource == _model.Localtest.Resource && w.WaitType == WaitType.WaitUntilHealthy
        );
        var checks = _app.Annotations.OfType<HealthCheckAnnotation>().Select(h => h.Key).ToList();
        Assert.Equal(2, checks.Count);
        Assert.Contains("app_provisioning", checks);
        Assert.Single(_app.Annotations.OfType<AppProvisioningAnnotation>());
    }

    [Fact]
    public async Task TheFrontDoorRoutesTheAppIdToTheAppThroughTheHostGateway()
    {
        var route = _model.YarpRoutes.Single(r => r.ClusterId == "cluster_app");

        Assert.Equal("/ttd/testapp/{**catch-all}", route.Match.Path);
        Assert.Equal(["local.altinn.cloud", "localhost", "127.0.0.1"], route.Match.Hosts);
        Assert.Equal(FrontDoorRoutes.AppOrder, route.Order);
        Assert.True(
            _model
                .YarpRoutes.Where(r => r.ClusterId != "cluster_app" && r.Order != FrontDoorRoutes.HostOnlyOrder)
                .All(r => r.Order < route.Order)
        );
        Assert.True(route.Order < FrontDoorRoutes.HostOnlyOrder);
        Assert.Equal(
            [new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "True" }],
            route.Transforms!.Select(t => t.ToDictionary())
        );
        // The front door's configuration is generated from the built application's services.
        using var built = _model.Builder.Build();
        Assert.Equal(
            "http://host.docker.internal:{app.bindings.http.port}",
            await _model.EnvironmentExpression(
                _model.FrontDoor,
                "REVERSEPROXY__CLUSTERS__cluster_app__DESTINATIONS__destination1__ADDRESS"
            )
        );
        var args = await SampleModel.RuntimeArgs(_model.FrontDoor);
        Assert.Equal(["--add-host", "host.docker.internal:host-gateway"], args);
    }

    [Fact]
    public void TheCommandsAreThereWithTheirConfirmations()
    {
        var appCommands = _app.Annotations.OfType<ResourceCommandAnnotation>().ToList();
        Assert.Contains(appCommands, c => c.Name == AltinnAppExtensions.ProvisionCommand);
        var deprovision = Assert.Single(appCommands, c => c.Name == AltinnAppExtensions.DeprovisionCommand);
        Assert.Contains("ttd/testapp", deprovision.ConfirmationMessage, StringComparison.Ordinal);

        var reset = Assert.Single(
            _model.Localtest.Resource.Annotations.OfType<ResourceCommandAnnotation>(),
            c => c.Name == LocaltestReset.CommandName
        );
        Assert.Equal(LocaltestReset.Confirmation, reset.ConfirmationMessage);
    }

    [Fact]
    public void AnAppWithoutReadableMetadataIsRefusedWithTheFileItNeeds()
    {
        var folder = Directory.CreateTempSubdirectory("altinn-app-aspire-");
        try
        {
            var project = Path.Combine(folder.FullName, "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            var model = SampleModel.Build(withApp: false);

            var error = Assert.Throws<InvalidOperationException>(() =>
                model.Builder.AddAltinnApp(model.Localtest, project)
            );

            Assert.Contains(
                Path.Combine(folder.FullName, "App", "config", "applicationmetadata.json"),
                error.Message,
                StringComparison.Ordinal
            );
            Assert.Contains("org/app", error.Message, StringComparison.Ordinal);

            Directory.CreateDirectory(Path.Combine(folder.FullName, "App", "config"));
            File.WriteAllText(
                Path.Combine(folder.FullName, "App", "config", "applicationmetadata.json"),
                """{ "id": "no-slash" }"""
            );
            Assert.Throws<InvalidOperationException>(() => model.Builder.AddAltinnApp(model.Localtest, project));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The package expands the app section as studioctl does: the Go snapshot of a process run against localtest-v2
    /// (<c>internal/cmd/app/testdata/app-environment.golden.json</c>), less the runner's own variables.
    /// </summary>
    [Fact]
    public void TheAppSectionExpandsAsStudioctlExpandsIt()
    {
        using var golden = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "app-environment.golden.json"))
        );
        var run = golden
            .RootElement.GetProperty("cases")
            .EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "native-localtest-v2");
        var signing = run.GetProperty("signing");
        var want = run.GetProperty("environment")
            .EnumerateObject()
            .Where(p => !_runnerKeys.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var resolver = new ManifestResolver(ManifestLoader.Default, "/unused");

        var got = resolver
            .AppEnvironment(
                run.GetProperty("secretsDir").GetString()!,
                new AppCertificate(
                    signing.GetProperty("FullPath").GetString()!,
                    signing.GetProperty("KeysFolder").GetString()!
                )
            )
            .AllVariables();

        Assert.Equal(want.OrderBy(kv => kv.Key, StringComparer.Ordinal), got);
        Assert.Equal("localtest-accesstoken.pfx", signing.GetProperty("FileName").GetString());
    }

    [Fact]
    public void TheCertificateKeysFolderIsATraversalFromTheAppRootAsStudioctlComputesIt()
    {
        var certificate = AppCertificate.ForProcess(
            "/home/dev/apps/testapp",
            "/home/dev/.altinn-studio/localtest-v2/certs",
            "localtest-accesstoken.pfx"
        );

        Assert.Equal("/home/dev/.altinn-studio/localtest-v2/certs/localtest-accesstoken.pfx", certificate.Path);
        Assert.Equal("/../../.altinn-studio/localtest-v2/certs/", certificate.KeysFolder);
    }

    [Fact]
    public void TheAppCodesAreTheBytesStudioctlWrites()
    {
        var folder = Directory.CreateTempSubdirectory("altinn-app-aspire-");
        try
        {
            var directory = Path.Combine(folder.FullName, "apps", "ttd", "testapp", "secrets");
            var path = AppSecrets.WriteDevelopmentAppCodes(directory, "app-codes.json");

            Assert.Equal(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "app-codes.golden.json")),
                File.ReadAllBytes(path)
            );
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(directory)
                );
            }
            var written = File.GetLastWriteTimeUtc(path);
            AppSecrets.WriteDevelopmentAppCodes(directory, "app-codes.json");
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void AHeldPortIsRefusedAndItsHolderNamed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var error = Assert.Throws<InvalidOperationException>(() => PortGuard.EnsureFree(port));

        Assert.Contains($"Port {port} is in use", error.Message, StringComparison.Ordinal);
        Assert.Contains("another localtest stack or AppHost", error.Message, StringComparison.Ordinal);
        if (OperatingSystem.IsLinux())
        {
            Assert.Contains($"(pid {Environment.ProcessId})", error.Message, StringComparison.Ordinal);
        }
        listener.Stop();
        PortGuard.EnsureFree(port);
    }

    [Fact]
    public void TheResetRemovesTheContainersThenTheVolumesThatExist()
    {
        var volumes = ManifestLoader.Default.Volumes.Select(v => v.Name).ToList();

        var commands = LocaltestReset.Commands(
            ["c1", "c2"],
            volumes,
            new HashSet<string>(["localtest-v2-postgres-data", "localtest-v2-policy-store", "unrelated"])
        );

        Assert.Equal(
            [
                ["rm", "--force", "c1", "c2"],
                ["volume", "rm", "--force", "localtest-v2-postgres-data", "localtest-v2-policy-store"],
            ],
            commands
        );
        Assert.Equal(5, volumes.Count);
        Assert.Empty(LocaltestReset.Commands([], volumes, new HashSet<string>()));
    }

    [Theory]
    [InlineData(null, "reading the app's files")]
    [InlineData("http://local.altinn.cloud:8000/Home/GetTestStudioToken", "the Studio token (the localtest app)")]
    [InlineData(
        "http://local.altinn.cloud:8000/register/api/localtest/apps/ttd/testapp",
        "step 1, the Register's test users"
    )]
    [InlineData(
        "http://local.altinn.cloud:8000/storage/api/v1/applications/ttd/testapp",
        "step 2, Storage's application metadata and texts"
    )]
    [InlineData(
        "http://local.altinn.cloud:8000/authorization/api/v1/policies?org=ttd&app=testapp",
        "step 3, the PDP's policy"
    )]
    [InlineData(
        "http://local.altinn.cloud:8000/resourceregistry/api/v1/resource/app_ttd_testapp/policy/subjects",
        "step 3b, the Resource Registry's policy subjects"
    )]
    [InlineData(
        "http://local.altinn.cloud:8000/accessmanagement/api/v1/localtest/refresh",
        "step 4, the Access Management refresh"
    )]
    public void AFailureNamesItsStep(string? request, string step) =>
        Assert.Equal(step, ProvisioningSteps.Of(request is null ? null : new Uri(request)));

    /// <summary>
    /// A provisioning that cannot reach the platform is a degraded health check with the step and URL, not an
    /// exception: the app keeps running.
    /// </summary>
    [Fact]
    public async Task AFailedProvisioningIsADegradedHealthCheckWithItsStepAndUrl()
    {
        using var unused = new TcpListener(IPAddress.Loopback, 0);
        unused.Start();
        var port = ((IPEndPoint)unused.LocalEndpoint).Port;
        unused.Stop();
        var provisioning = _app.Annotations.OfType<AppProvisioningAnnotation>().Single().Provisioning;
        var refused = new AppProvisioning(
            provisioning.AppId,
            provisioning.Source,
            new Uri($"http://127.0.0.1:{port}/"),
            () => false
        );
        using var built = _model.Builder.Build();

        var status = await refused.ProvisionAsync(built.Services, _app, TestContext.Current.CancellationToken);

        Assert.Equal(ProvisioningState.Failed, status.State);
        Assert.Contains("the Studio token", status.Message, StringComparison.Ordinal);
        Assert.Contains($"http://127.0.0.1:{port}/Home/GetTestStudioToken", status.Message, StringComparison.Ordinal);
        var health = await new ProvisioningHealthCheck(refused).CheckHealthAsync(
            new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded, health.Status);
        Assert.Equal(status.Message, health.Description);
        Assert.NotNull(built.Services.GetService<ResourceNotificationService>());
    }
}
