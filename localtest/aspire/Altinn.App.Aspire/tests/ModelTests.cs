using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>The application model the sample AppHost builds, without running it.</summary>
public sealed class ModelTests
{
    private static readonly string[] _sampleComponents =
    [
        "postgres",
        "register",
        "profile",
        "authentication",
        "authorization",
        "storage",
        "access-management",
        "events",
        "resource-registry",
        "notifications",
        "localtest-app",
        "pdf3",
        "workflow-engine",
        "redis",
        "dialogporten-webapi",
        "dialogporten-graphql",
        "dialogporten-service",
        "dialogporten-adapter",
    ];

    private readonly SampleModel _model = SampleModel.Build();

    [Fact]
    public void TheSampleAddsTheDefaultStackAndDialogporten()
    {
        var names = _model.Resources.Select(r => r.Name).ToList();

        // AddProject adds the hidden app-rebuilder beside the app.
        Assert.Equal(["localtest", "frontdoor", .. _sampleComponents, "app", "app-rebuilder"], names);
        Assert.DoesNotContain(names, n => n.StartsWith("receipt", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("correspondence", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("arbeidsflate", StringComparison.Ordinal));
        Assert.Equal(["dialogporten"], _model.Localtest.Resource.Groups);
        Assert.Equal(new Uri("http://local.altinn.cloud:8000/"), _model.Localtest.Resource.PlatformUri);
    }

    [Fact]
    public void EveryComponentIsAPersistentChildOfTheLocaltestResource()
    {
        foreach (var name in _sampleComponents)
        {
            var container = _model.Container(name);
            Assert.Equal(
                ContainerLifetime.Persistent,
                container.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime
            );
            Assert.Contains(
                container.Annotations.OfType<ResourceRelationshipAnnotation>(),
                r => r.Type == "Parent" && r.Resource == _model.Localtest.Resource
            );
        }
    }

    [Fact]
    public void EveryContainerHasItsEndpointAndHealthCheck()
    {
        var resolver = new ManifestResolver(ManifestLoader.Default, "/certs", getEnvironment: _ => null);
        foreach (var component in resolver.Components(["dialogporten"]))
        {
            var container = _model.Container(component.Name == "localtest" ? "localtest-app" : component.Name);
            var endpoints = container.Annotations.OfType<EndpointAnnotation>().ToList();
            var endpoint = Assert.Single(endpoints);
            Assert.Equal(component.ContainerPort, endpoint.TargetPort);
            if (component.HostPort is { } hostPort)
            {
                Assert.Equal("http", endpoint.Name);
                Assert.Equal(hostPort, endpoint.Port);
                Assert.False(endpoint.IsProxied);
            }
            else
            {
                Assert.Equal("tcp", endpoint.Name);
                Assert.Equal(component.Name == "postgres" ? 15432 : 16379, endpoint.Port);
                Assert.False(endpoint.IsProxied);
            }
            Assert.Single(container.Annotations.OfType<HealthCheckAnnotation>());
        }
    }

    [Fact]
    public void ImagesComeFromTheManifest()
    {
        var pin = ManifestLoader.Default.Images.Tag;
        var storage = _model.Container("storage").Annotations.OfType<ContainerImageAnnotation>().Single();
        var redis = _model.Container("redis").Annotations.OfType<ContainerImageAnnotation>().Single();
        var pdf = _model.Container("pdf3");

        Assert.Equal(
            ("ghcr.io", "ivarne-agent/localtest-research/localtest-storage", pin),
            (storage.Registry, storage.Image, storage.Tag)
        );
        // Aspire drops the tag when a digest is set: the reference is library/redis@sha256:..., the same image.
        Assert.Equal(("docker.io", "library/redis", null), (redis.Registry, redis.Image, redis.Tag));
        Assert.Equal("29e8589c3f9ba699b5f7aa4b3c7733c58852a3626439e619aa0ee78de08c6ca0", redis.SHA256);
        Assert.Equal(
            ImagePullPolicy.Always,
            pdf.Annotations.OfType<ContainerImagePullPolicyAnnotation>().Single().ImagePullPolicy
        );
    }

    [Fact]
    public async Task ComponentsMapTheIngressHostsToTheHostGateway()
    {
        string[] ingress =
        [
            "--add-host",
            "local.altinn.cloud:host-gateway",
            "--add-host",
            "pdf.local.altinn.cloud:host-gateway",
            "--add-host",
            "workflow-engine.local.altinn.cloud:host-gateway",
            "--add-host",
            "pgadmin.local.altinn.cloud:host-gateway",
            "--add-host",
            "app-frontend.local.altinn.cloud:host-gateway",
            "--add-host",
            "af.local.altinn.cloud:host-gateway",
        ];

        Assert.Equal(ingress, await SampleModel.RuntimeArgs(_model.Container("storage")));
        Assert.Equal(
            [.. ingress, "--add-host", "host.docker.internal:host-gateway"],
            await SampleModel.RuntimeArgs(_model.Container("events"))
        );
        Assert.Empty(await SampleModel.RuntimeArgs(_model.Container("postgres")));
        Assert.Empty(await SampleModel.RuntimeArgs(_model.Container("redis")));
    }

    [Fact]
    public async Task EnvironmentMountsAliasesAndCommandFollowTheManifest()
    {
        var storage = _model.Container("storage");
        var environment = await _model.Environment(storage);
        Assert.Equal("http://local.altinn.cloud:8000/", environment["GeneralSettings__PlatformEndpoint"]);
        Assert.Equal(
            "/certs/localtest-accesstoken.pfx",
            environment["AccessTokenSettings__AccessTokenSigningCertificateFullPath"]
        );

        var mounts = storage
            .Annotations.OfType<ContainerMountAnnotation>()
            .Select(m => (m.Type, m.Source, m.Target, m.IsReadOnly));
        Assert.Equal(
            [
                (ContainerMountType.BindMount, "/data-directory/certs", "/certs", true),
                (ContainerMountType.Volume, "localtest-v2-storage-blobs", "/data/blobs", false),
            ],
            mounts
        );
        var events = _model
            .Container("events")
            .Annotations.OfType<ContainerMountAnnotation>()
            .Select(m => (m.Source, m.Target));
        Assert.Contains(
            ("/data-directory/certs/localtest-accesstoken.pfx", "/accesstoken/accesstokencredentials.pfx"),
            events
        );

        // postgres and redis are their resource names already; the localtest app keeps its component name as an alias.
        Assert.Empty(_model.Container("postgres").Annotations.OfType<ContainerNetworkAliasAnnotation>());
        Assert.Equal(
            ["localtest"],
            _model.Container("localtest-app").Annotations.OfType<ContainerNetworkAliasAnnotation>().Select(a => a.Alias)
        );

        Assert.Equal(
            [
                "postgres",
                "-c",
                "shared_preload_libraries=pg_stat_statements",
                "-c",
                "timezone=UTC",
                "-c",
                "log_timezone=UTC",
            ],
            await SampleModel.Args(_model.Container("postgres"))
        );
        Assert.Equal(
            ["redis-server", "--save", "", "--appendonly", "no"],
            await SampleModel.Args(_model.Container("redis"))
        );
    }

    [Fact]
    public void DependenciesBecomeWaits()
    {
        static IEnumerable<(string, WaitType)> Waits(IResource r) =>
            r.Annotations.OfType<WaitAnnotation>().Select(w => (w.Resource.Name, w.WaitType));

        Assert.Equal(
            [
                ("frontdoor", WaitType.WaitUntilStarted),
                ("postgres", WaitType.WaitUntilHealthy),
                ("register", WaitType.WaitUntilHealthy),
            ],
            Waits(_model.Container("profile")).Order()
        );
        Assert.Empty(Waits(_model.Container("postgres")));
        Assert.Contains(
            ("dialogporten-webapi", WaitType.WaitUntilHealthy),
            Waits(_model.Container("dialogporten-graphql"))
        );
    }

    [Fact]
    public void TheFrontDoorPublishesPort8000OnEveryInterface()
    {
        var endpoint = _model.FrontDoor.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http");

        Assert.Equal(8000, endpoint.Port);
        Assert.False(endpoint.IsProxied);
        Assert.Equal("0.0.0.0", endpoint.TargetHost);
        Assert.Single(_model.FrontDoor.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public void TheFrontDoorHasARouteForEveryManifestRouteOfAnAddedComponent()
    {
        var routes = _model.YarpRoutes;
        var clusters = _model.YarpClusterIds;

        // One cluster per routed component, named after its resource.
        Assert.Equal(
            [
                "register",
                "profile",
                "authentication",
                "authorization",
                "storage",
                "access-management",
                "events",
                "resource-registry",
                "notifications",
                "localtest-app",
                "pdf3",
                "workflow-engine",
                "dialogporten-webapi",
                "dialogporten-graphql",
                "dialogporten-adapter",
                "app",
            ],
            clusters.Select(c => c.Replace("cluster_", "", StringComparison.Ordinal))
        );
        Assert.Equal(clusters.Count, routes.Count);
        Assert.DoesNotContain(routes, r => r.Match.Path!.StartsWith("/receipt", StringComparison.Ordinal));

        var graphql = routes.Single(r => r.ClusterId == "cluster_dialogporten-graphql");
        Assert.Equal("/dialogporten/graphql/{**catch-all}", graphql.Match.Path);
        Assert.Equal(["local.altinn.cloud", "localhost", "127.0.0.1"], graphql.Match.Hosts);
        Assert.Equal(-"/dialogporten/graphql".Length, graphql.Order);
        Assert.Equal(
            [
                new Dictionary<string, string> { ["RequestHeaderOriginalHost"] = "True" },
                new Dictionary<string, string> { ["PathRemovePrefix"] = "/dialogporten/graphql" },
                new Dictionary<string, string> { ["PathPrefix"] = "/graphql" },
            ],
            graphql.Transforms!.Select(t => t.ToDictionary())
        );

        var localtest = routes.Single(r => r.ClusterId == "cluster_localtest-app");
        Assert.Equal("/{**catch-all}", localtest.Match.Path);
        Assert.Equal(FrontDoorRoutes.HostOnlyOrder, localtest.Order);
        Assert.True(
            routes
                .Where(r => r != localtest && r.Match.Hosts!.Contains("local.altinn.cloud"))
                .All(r => r.Order < localtest.Order)
        );

        var pdf = routes.Single(r => r.ClusterId == "cluster_pdf3");
        Assert.Equal(["pdf.local.altinn.cloud"], pdf.Match.Hosts);
        Assert.Equal("/{**catch-all}", pdf.Match.Path);
    }
}
