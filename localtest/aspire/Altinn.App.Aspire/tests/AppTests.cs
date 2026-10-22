using System.Net;
using System.Net.Sockets;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.App.Aspire.Tests;

/// <summary>The app side: <c>AddAltinnApp</c> in the sample's model, the app section, the secrets, the guards.</summary>
public sealed class AppTests
{
    private readonly SampleModel _model;
    private readonly ProjectResource _app;

    public AppTests()
    {
        _model = SampleModel.Build();
        _app = _model.App!.Resource;
    }

    /// <summary>
    /// The app runs with studioctl run's environment (<see cref="AppEnvironment" />, held to studioctl's golden file
    /// by <see cref="GoldenComparisonTests" />) with the package's certificate and secrets directory; Aspire sets the
    /// OpenTelemetry endpoint, the package the Kestrel URL from the allocated port.
    /// </summary>
    [Fact]
    public async Task TheAppGetsStudioctlRunsEnvironment()
    {
        var environment = await _model.Environment(_app);
        var appRoot = Path.GetDirectoryName(Path.GetDirectoryName(new SampleAppProject().ProjectPath))!;
        var expected = AppEnvironment.Build(
            [],
            "unused",
            8000,
            "",
            "/data-directory/apps/ttd/testapp/secrets",
            "",
            AppCertificate.ForProcess(appRoot, "/data-directory/certs")
        );

        foreach (
            var (key, value) in expected.Where(kv =>
                kv.Key is not "Kestrel__EndPoints__Http__Url" and not "OTEL_EXPORTER_OTLP_ENDPOINT"
            )
        )
        {
            Assert.Equal((key, value), (key, environment[key]));
        }
        Assert.DoesNotContain("AppSettings__AppFrontendAssetBaseUrl", environment.Keys);
        Assert.Equal("true", environment["AppSettings__UseOpenTelemetry"]);
        Assert.Equal(
            "/data-directory/certs/localtest-accesstoken.pfx",
            environment["AccessTokenSettings__AccessTokenSigningCertificateFullPath"]
        );
        var keysFolder = environment["AccessTokenSettings__AccessTokenSigningKeysFolder"];
        Assert.Equal("/data-directory/certs", Path.GetFullPath(Path.Combine(appRoot, keysFolder.Trim('/'))));
        Assert.Equal("/data-directory/apps/ttd/testapp/secrets", environment["RUNTIME_APP_SECRETS_DIR"]);
    }

    [Fact]
    public async Task TheFrontendDevServerRoutesAppFrontendToTheHost()
    {
        var model = SampleModel.Build(withApp: true);
        model.Localtest.WithFrontendDevServer(5173);

        var environment = await model.Environment(model.App!.Resource);

        Assert.Equal(
            "http://app-frontend.local.altinn.cloud:8000",
            environment["AppSettings__AppFrontendAssetBaseUrl"]
        );
        Assert.Equal(5173, model.Localtest.Resource.Stack.FrontendDevServerPort);
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

    [Fact]
    public void TheCertificateKeysFolderIsATraversalFromTheAppRootAsStudioctlComputesIt()
    {
        var certificate = AppCertificate.ForProcess(
            "/home/dev/apps/testapp",
            "/home/dev/.altinn-studio/localtest-v2/certs"
        );

        Assert.Equal("/home/dev/.altinn-studio/localtest-v2/certs/localtest-accesstoken.pfx", certificate.FullPath);
        Assert.Equal("localtest-accesstoken.pfx", certificate.FileName);
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
        var volumes = LocaltestManifest.Default.VolumeNames;

        var commands = LocaltestReset.Commands(
            ["c1", "c2"],
            volumes,
            new HashSet<string>(["localtest-v2-postgres-data", "localtest-v2-policy-store", "unrelated"])
        );

        Assert.Equal(
            [
                ["rm", "--force", "c1", "c2"],
                ["volume", "rm", "--force", "localtest-v2-policy-store", "localtest-v2-postgres-data"],
            ],
            commands
        );
        Assert.Equal(5, volumes.Count);
        Assert.Empty(LocaltestReset.Commands([], volumes, new HashSet<string>()));
    }
}
