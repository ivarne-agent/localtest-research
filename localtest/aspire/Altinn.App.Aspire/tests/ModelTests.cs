using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>The application model the package builds, without running it.</summary>
public sealed class ModelTests
{
    private static readonly string[] _default =
    [
        "postgres",
        "register",
        "profile",
        "authentication",
        "authorization",
        "storage",
        "events",
        "resource-registry",
        "notifications",
        "localtest",
        "pdf3",
        "workflow-engine",
        "access-management",
    ];

    private static readonly string[] _dialogporten =
    [
        "redis",
        "dialogporten-webapi",
        "dialogporten-graphql",
        "dialogporten-service",
        "dialogporten-adapter",
    ];

    private readonly SampleModel _model = SampleModel.Build();

    [Fact]
    public void TheDefaultStackIsTheDefaultGroupAndWhatItDependsOn()
    {
        var model = SampleModel.Build([]);

        Assert.Equal(["altinn-localtest", .. _default], model.Resources.Select(r => r.Name));
        Assert.Empty(model.Localtest.Resource.Groups);
    }

    [Fact]
    public void TheSampleAddsTheDefaultStackAndDialogporten()
    {
        // AddProject adds the hidden app-rebuilder beside the app.
        Assert.Equal(
            ["altinn-localtest", .. _default, .. _dialogporten, "app", "app-rebuilder"],
            _model.Resources.Select(r => r.Name)
        );
        Assert.Equal(["dialogporten"], _model.Localtest.Resource.Groups);
        Assert.Equal(new Uri("http://local.altinn.cloud:8000/"), _model.Localtest.Resource.PlatformUri);
    }

    [Fact]
    public void EverythingAddsEveryContainer()
    {
        var model = SampleModel.Build(["everything"]);

        // Every container but the monitoring group's, which the package ignores, and minimal mode's otel-files, an
        // OTLP receiver, which it never runs.
        var monitoring = LocaltestManifest.Default.Select([LocaltestManifest.MonitoringGroup]).Append("otel-files");
        Assert.Equal(
            LocaltestManifest.Default.Names.Except(monitoring).Order(StringComparer.Ordinal),
            model.Containers.Select(c => c.Name).Order(StringComparer.Ordinal)
        );
        Assert.Equal(["arbeidsflate", "correspondence", "dialogporten", "receipt"], model.Localtest.Resource.Groups);
    }

    [Fact]
    public void AGroupAddsOnlyWhatItStarts()
    {
        var model = SampleModel.Build(["correspondence"]);

        // Correspondence depends on Redis and Dialogporten's WebApi (and what that depends on), not on its other hosts.
        Assert.Equal(
            [.. _default, "redis", "dialogporten-webapi", "correspondence"],
            model.Containers.Select(c => c.Name)
        );
    }

    [Theory]
    [InlineData("default")]
    [InlineData("routes")]
    public void UnknownGroupsAndTheDefaultGroupAreRefused(string group)
    {
        var model = SampleModel.Build([]);

        var error = Assert.Throws<ArgumentException>(() => model.Localtest.WithGroup(group));

        Assert.Contains(group, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryContainerAnnouncesEveryRunningHttpContainer()
    {
        var expected = _default
            .Concat(_dialogporten)
            .Where(n => n is not "postgres" and not "redis")
            .Select(n => $"services__{n}__http__0")
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var container in _model.Containers)
        {
            var environment = await _model.Environment(container);

            Assert.Equal(
                expected,
                environment
                    .Keys.Where(k => k.StartsWith("services__", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
            );
        }
        var storage = await _model.Environment(_model.Container("storage"));
        Assert.Equal("http://storage:5010", storage["services__storage__http__0"]);
        Assert.Equal("http://dialogporten-webapi:8080", storage["services__dialogporten-webapi__http__0"]);
        Assert.Equal("http://local.altinn.cloud:8000/", storage["GeneralSettings__PlatformEndpoint"]);
    }

    [Fact]
    public async Task AGroupEnabledLaterIsAnnouncedToTheContainersAddedBefore()
    {
        var model = SampleModel.Build([]);
        Assert.DoesNotContain("services__receipt__http__0", (await model.Environment(model.Container("storage"))).Keys);

        model.Localtest.WithReceipt();

        Assert.Equal(
            "http://receipt:5060",
            (await model.Environment(model.Container("storage")))["services__receipt__http__0"]
        );
    }

    [Fact]
    public void OnlyTheLocaltestContainerPublishesAPort()
    {
        foreach (var container in _model.Containers.Where(c => c.Name != "localtest"))
        {
            Assert.Empty(container.Annotations.OfType<EndpointAnnotation>());
        }
        var endpoint = Assert.Single(_model.Container("localtest").Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(("http", 8000, 8080), (endpoint.Name, endpoint.Port, endpoint.TargetPort));
        Assert.False(endpoint.IsProxied);
        Assert.Equal("0.0.0.0", endpoint.TargetHost);
    }

    [Fact]
    public void PostgresIsPublishedOnlyWhenAsked()
    {
        var model = SampleModel.Build([], configure: o => o.PostgresHostPort = 15432);

        var endpoint = Assert.Single(model.Container("postgres").Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(
            ("tcp", 15432, 5432, false),
            (endpoint.Name, endpoint.Port, endpoint.TargetPort, endpoint.IsProxied)
        );
    }

    [Fact]
    public void EveryContainerIsAPersistentChildWithItsNameAsAliasAndOneHealthCheck()
    {
        foreach (var container in _model.Containers)
        {
            Assert.Equal(
                ContainerLifetime.Persistent,
                container.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime
            );
            Assert.Contains(
                container.Annotations.OfType<ResourceRelationshipAnnotation>(),
                r => r.Type == "Parent" && r.Resource == _model.Localtest.Resource
            );
            // Aspire's own alias is the resource name, which is the manifest name.
            Assert.Empty(container.Annotations.OfType<ContainerNetworkAliasAnnotation>());
            var check = Assert.Single(container.Annotations.OfType<HealthCheckAnnotation>());
            Assert.Equal(
                container.Name == "localtest"
                    ? "localtest_http_/health_200_check"
                    : $"{container.Name}_in_network_check",
                check.Key
            );
        }
    }

    [Fact]
    public async Task EveryContainerMapsThePublicHostsToTheHostGateway()
    {
        string[] hosts =
        [
            "local.altinn.cloud",
            "pdf.local.altinn.cloud",
            "workflow-engine.local.altinn.cloud",
            "app-frontend.local.altinn.cloud",
            "af.local.altinn.cloud",
            "host.docker.internal",
        ];
        foreach (var container in _model.Containers)
        {
            Assert.Equal(
                hosts.SelectMany(h => new[] { "--add-host", h + ":host-gateway" }),
                await SampleModel.RuntimeArgs(container)
            );
        }
    }

    [Fact]
    public void TheMountsAreTheCertificateAndTheNamedVolumes()
    {
        Assert.Equal(
            [
                (ContainerMountType.BindMount, "/data-directory/certs", "/certs", true),
                (
                    ContainerMountType.BindMount,
                    "/data-directory/certs/localtest-accesstoken.pfx",
                    "/accesstoken/accesstokencredentials.pfx",
                    true
                ),
                (ContainerMountType.Volume, "altinn-localtest-storage-blobs", "/data/blobs", false),
            ],
            _model
                .Container("storage")
                .Annotations.OfType<ContainerMountAnnotation>()
                .Select(m => (m.Type, m.Source, m.Target, m.IsReadOnly))
        );
    }

    [Fact]
    public void ImagesFollowTheManifest()
    {
        var pin = LocaltestManifest.Default.Containers["storage"].Tag;
        var storage = _model.Container("storage").Annotations.OfType<ContainerImageAnnotation>().Single();
        var redis = _model.Container("redis").Annotations.OfType<ContainerImageAnnotation>().Single();

        Assert.Equal(
            ("ghcr.io", "ivarne-agent/localtest-research/localtest-storage", pin),
            (storage.Registry, storage.Image, storage.Tag)
        );
        Assert.Equal(
            ("ghcr.io", "ivarne-agent/localtest-research/localtest-redis", pin, null),
            (redis.Registry, redis.Image, redis.Tag, redis.SHA256)
        );
        Assert.Equal(
            ImagePullPolicy.Always,
            _model.Container("pdf3").Annotations.OfType<ContainerImagePullPolicyAnnotation>().Single().ImagePullPolicy
        );
        Assert.Empty(_model.Container("storage").Annotations.OfType<ContainerImagePullPolicyAnnotation>());
    }

    [Fact]
    public void TheTagOptionReplacesThePinsOnly()
    {
        var model = SampleModel.Build(["dialogporten"], configure: o => o.Tag = "latest");

        Assert.Equal("latest", model.Container("storage").Annotations.OfType<ContainerImageAnnotation>().Single().Tag);
        Assert.Equal(
            ImagePullPolicy.Always,
            model.Container("storage").Annotations.OfType<ContainerImagePullPolicyAnnotation>().Single().ImagePullPolicy
        );
        Assert.Equal("tt02", model.Container("pdf3").Annotations.OfType<ContainerImageAnnotation>().Single().Tag);
        Assert.Equal("latest", model.Container("redis").Annotations.OfType<ContainerImageAnnotation>().Single().Tag);
    }

    [Fact]
    public void ADigestPinsTheImageAndATagWithoutOneFloats()
    {
        var pinned = StackSpec.ImageOf(
            new ManifestContainer { Registry = "docker.io/library/redis", Tag = "7.2-alpine@sha256:29e8" },
            "latest"
        );
        var floating = StackSpec.ImageOf(new ManifestContainer { Registry = "localhost:5000/x", Tag = "tt02" }, null);

        Assert.Equal(
            ("docker.io", "library/redis", "7.2-alpine", "29e8", false),
            (pinned.Registry, pinned.Image, pinned.Tag, pinned.Sha256, pinned.Floating)
        );
        Assert.Equal("docker.io/library/redis:7.2-alpine@sha256:29e8", pinned.Reference);
        Assert.Equal(
            ("localhost:5000", "x", "tt02", true),
            (floating.Registry, floating.Image, floating.Tag, floating.Floating)
        );
    }

    [Fact]
    public void DependenciesBecomeWaitsAndHttpContainersWaitForTheFrontDoorToStart()
    {
        static IEnumerable<(string, WaitType)> Waits(IResource r) =>
            r.Annotations.OfType<WaitAnnotation>().Select(w => (w.Resource.Name, w.WaitType));

        Assert.Equal(
            [
                ("postgres", WaitType.WaitUntilHealthy),
                ("register", WaitType.WaitUntilHealthy),
                ("localtest", WaitType.WaitUntilStarted),
            ],
            Waits(_model.Container("profile"))
        );
        Assert.Empty(Waits(_model.Container("postgres")));
        Assert.Empty(Waits(_model.Container("redis")));
        Assert.Empty(Waits(_model.Container("localtest")));
        Assert.Equal(
            [("dialogporten-webapi", WaitType.WaitUntilHealthy), ("localtest", WaitType.WaitUntilStarted)],
            Waits(_model.Container("dialogporten-graphql"))
        );
    }

    [Fact]
    public void TheInNetworkProbeIsStudioctls()
    {
        Assert.Equal(
            "for i in $(seq 1 10); do if { exec 3<>/dev/tcp/postgres/5432; } 2>/dev/null; then exec 3<&-; exit 0; fi; "
                + "sleep 0.5; done; exit 1",
            InNetworkHealthCheck.Script(new HealthTarget("postgres", 5432, null))
        );
        Assert.Equal(
            "for i in $(seq 1 10); do if { exec 3<>/dev/tcp/storage/5010; } 2>/dev/null; then "
                + "printf 'GET /health HTTP/1.0\\r\\nHost: storage\\r\\n\\r\\n' >&3; "
                + "IFS= read -r -t 5 line <&3 || line=; exec 3<&-; case \"$line\" in *' 200 '*) exit 0;; esac; fi; "
                + "sleep 0.5; done; exit 1",
            InNetworkHealthCheck.Script(new HealthTarget("storage", 5010, "/health"))
        );
    }
}
