using System.Net;
using System.Net.Sockets;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// <c>AddAltinnLocaltestMinimal()</c> in the minimal sample's model: four session containers on tmpfs, the emulated set,
/// both apps, no reset command. The containers' environment and mounts against studioctl's <c>minimal</c> snapshot are
/// in <see cref="GoldenComparisonTests" />.
/// </summary>
public sealed class MinimalModeTests
{
    private readonly SampleModel _model = SampleModel.BuildMinimal(withApps: true);

    [Fact]
    public void TheMinimalSampleIsTheParentTheFourContainersAndBothApps()
    {
        // AddProject adds the hidden <name>-rebuilder beside each app.
        Assert.Equal(
            [
                "altinn-localtest-minimal",
                "postgres",
                "storage",
                "localtest",
                "workflow-engine",
                "app",
                "app-rebuilder",
                "app2",
                "app2-rebuilder",
            ],
            _model.Resources.Select(r => r.Name)
        );
        Assert.Equal(new Uri("http://local.altinn.cloud:8000/"), _model.Minimal.Resource.PlatformUri);
        Assert.True(_model.Minimal.Resource.Stack.Minimal);
    }

    [Fact]
    public void TheEmulatedServicesAreTheDefaultGroupsHttpContainersMinimalDoesNotStart()
    {
        Assert.Equal(
            [
                "register",
                "profile",
                "authentication",
                "authorization",
                "events",
                "resource-registry",
                "notifications",
                "pdf3",
                "access-management",
            ],
            _model.Minimal.Resource.Emulated
        );
    }

    [Fact]
    public void EveryContainerLivesForTheSessionOnTmpfsWithoutAVolume()
    {
        foreach (var container in _model.Containers)
        {
            Assert.Equal(
                ContainerLifetime.Session,
                container.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime
            );
            Assert.DoesNotContain(
                container.Annotations.OfType<ContainerMountAnnotation>(),
                m => m.Type == ContainerMountType.Volume
            );
        }
    }

    [Fact]
    public async Task TheTmpfsMountsAreStudioctlsWithTheBlobsBounded()
    {
        Assert.Equal(
            ["--tmpfs", "/var/lib/postgresql:mode=1777"],
            (await SampleModel.RuntimeArgs(_model.Container("postgres"))).Take(2)
        );
        Assert.Equal(
            ["--tmpfs", "/data/blobs:size=1g,mode=1777"],
            (await SampleModel.RuntimeArgs(_model.Container("storage"))).Take(2)
        );
        Assert.Equal(
            ["--tmpfs", "/data:mode=1777"],
            (await SampleModel.RuntimeArgs(_model.Container("localtest"))).Take(2)
        );
        Assert.DoesNotContain("--tmpfs", await SampleModel.RuntimeArgs(_model.Container("workflow-engine")));
    }

    [Fact]
    public async Task TheFrontDoorKeepsItsAppRoutesInMemory()
    {
        var environment = await _model.Environment(_model.Container("localtest"));

        Assert.Equal("", environment[StackSpec.AppRoutesFileVariable]);
        Assert.Equal("http://localtest:8080", environment["services__register__http__0"]);
        Assert.DoesNotContain(
            StackSpec.AppRoutesFileVariable,
            (await _model.Environment(_model.Container("storage"))).Keys
        );
    }

    [Fact]
    public void TheAppsWaitForTheMinimalPlatformAndHaveTheirCommands()
    {
        foreach (var name in new[] { "app", "app2" })
        {
            var app = _model.Builder.Resources.OfType<ProjectResource>().Single(r => r.Name == name);
            Assert.Contains(
                app.Annotations.OfType<WaitAnnotation>(),
                w => w.Resource == _model.Minimal.Resource && w.WaitType == WaitType.WaitUntilHealthy
            );
            Assert.Single(app.Annotations.OfType<AppProvisioningAnnotation>());
            var commands = app.Annotations.OfType<ResourceCommandAnnotation>().Select(c => c.Name).ToList();
            Assert.Contains(AltinnAppExtensions.ProvisionCommand, commands);
            Assert.Contains(AltinnAppExtensions.DeprovisionCommand, commands);
        }
    }

    [Fact]
    public void ThereIsNoResetCommandSinceNothingPersists()
    {
        Assert.Empty(_model.Minimal.Resource.Annotations.OfType<ResourceCommandAnnotation>());
    }

    [Fact]
    public void TheOptionsTagAndPostgresPortApply()
    {
        var model = SampleModel.BuildMinimal(configure: o =>
        {
            o.Tag = "latest";
            o.PostgresHostPort = 15432;
        });

        var storage = model.Container("storage").Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("latest", storage.Tag);
        var postgres = Assert.Single(model.Container("postgres").Annotations.OfType<EndpointAnnotation>());
        Assert.Equal((15432, 5432), (postgres.Port, postgres.TargetPort));
    }

    /// <summary>
    /// Minimal mode's front door is a session container: this AppHost's persistent front door from a run of the full
    /// stack holding the port is named, not accepted as it is by the full stack, which reuses it.
    /// </summary>
    [Fact]
    public void ThePortGuardNamesTheFullStacksFrontDoorInMinimalMode()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        PortGuard.EnsureFree(port, persistentFrontDoor: true, "localtest-0123abcd", "podman");
        var error = Assert.Throws<InvalidOperationException>(() =>
            PortGuard.EnsureFree(port, persistentFrontDoor: false, "localtest-0123abcd", "podman")
        );
        Assert.Contains("`podman rm --force localtest-0123abcd`", error.Message, StringComparison.Ordinal);
        var other = Assert.Throws<InvalidOperationException>(() =>
            PortGuard.EnsureFree(port, persistentFrontDoor: false, null, "podman")
        );
        Assert.Contains("another localtest stack or AppHost", other.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            PortGuard.EnsureFree(port, persistentFrontDoor: true, null, "podman")
        );
        listener.Stop();
        PortGuard.EnsureFree(port, persistentFrontDoor: false, null, "podman");
        PortGuard.EnsureFree(port, persistentFrontDoor: false, "localtest-0123abcd", "podman");
    }
}
