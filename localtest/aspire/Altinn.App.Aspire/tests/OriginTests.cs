using System.Text;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The public origin: the manifest's placeholders (the same vocabulary and faults as studioctl's
/// <c>manifest/origin.go</c>), <c>PublicHost</c> and <c>PublicPort</c>, and a port Aspire allocates
/// (<c>PublicPort = 0</c>), which every value derived from the origin refers to through the front door's endpoint.
/// </summary>
public sealed class OriginTests
{
    private static readonly PublicOrigin _fixed = new("origin.test", 18000, ["pdf", "af"]);
    private static readonly PublicOrigin _allocated = new("origin.test", null, ["pdf", "af"]);

    public static TheoryData<string, string, string> Placeholders =>
        new()
        {
            { "{{origin}}", "http://origin.test:18000", "http://origin.test:{port}" },
            { "{{origin}}/", "http://origin.test:18000/", "http://origin.test:{port}/" },
            { "{{host}}", "origin.test", "origin.test" },
            { "{{port}}", "18000", "{port}" },
            { "{{origin:af}}", "http://af.origin.test:18000", "http://af.origin.test:{port}" },
            { "{{host:pdf}}", "pdf.origin.test", "pdf.origin.test" },
            {
                "{{origin}}/a,{{origin:af}}/b",
                "http://origin.test:18000/a,http://af.origin.test:18000/b",
                "http://origin.test:{port}/a,http://af.origin.test:{port}/b"
            },
            {
                "{{origin}}/{Org}/{App}/",
                "http://origin.test:18000/{Org}/{App}/",
                "http://origin.test:{port}/{Org}/{App}/"
            },
            { "Host=postgres;Port=5432", "Host=postgres;Port=5432", "Host=postgres;Port=5432" },
        };

    [Theory]
    [MemberData(nameof(Placeholders))]
    public void EveryPlaceholderResolves(string value, string resolved, string allocated)
    {
        Assert.Null(_fixed.PlaceholderFault(value));
        Assert.True(_fixed.Resolve(value).IsLiteral);
        Assert.Equal(resolved, _fixed.Resolve(value).Text);
        Assert.Equal(allocated, _allocated.Resolve(value).ToString());
        Assert.Equal(resolved, _allocated.Resolve(value).Render(18000));
    }

    public static TheoryData<string> BadPlaceholders =>
        new()
        {
            "{{scheme}}://x",
            "{{url:pdf}}",
            "{{port:pdf}}",
            "{{origin:grafana}}",
            "{{}}",
            "{{origin",
            "{{{{origin}}}}",
            "{{Origin}}",
        };

    [Theory]
    [MemberData(nameof(BadPlaceholders))]
    public void ABadPlaceholderIsAManifestErrorNamingTheContainerAndKey(string value)
    {
        var json = Encoding
            .UTF8.GetString(ManifestBytes())
            .Replace(
                "\"GeneralSettings__PlatformEndpoint\": \"{{origin}}/\",\n        \"PostgreSqlSettings__AdminConnectionString\": \"Host=postgres;Port=5432;Username=platform_storage_admin",
                $"\"GeneralSettings__PlatformEndpoint\": \"{value}\",\n        \"PostgreSqlSettings__AdminConnectionString\": \"Host=postgres;Port=5432;Username=platform_storage_admin",
                StringComparison.Ordinal
            );
        Assert.Contains(value, json, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() =>
            LocaltestManifest.Parse(Encoding.UTF8.GetBytes(json), ManifestFiles.Embedded)
        );

        Assert.Contains(
            "container storage: envs GeneralSettings__PlatformEndpoint: bad placeholder",
            error.Message,
            StringComparison.Ordinal
        );
    }

    public static TheoryData<string, string, string> PublicFaults =>
        new()
        {
            { "bad host name", "\"host\": \"local.altinn.cloud\"", "\"host\": \"Local.Altinn.Cloud\"" },
            { "a name, not an IP address", "\"host\": \"local.altinn.cloud\"", "\"host\": \"127.0.0.1\"" },
            { "public: port 70000", "\"port\": 8000,", "\"port\": 70000," },
            { "duplicate subdomain pdf", "\"subdomains\": [\"pdf\",", "\"subdomains\": [\"pdf\", \"pdf\"," },
            { "\"grafana\" is not a listed subdomain", "{{origin:af}}", "{{origin:grafana}}" },
        };

    [Theory]
    [MemberData(nameof(PublicFaults))]
    public void ABadPublicSectionIsRefused(string fault, string find, string replace)
    {
        var json = Encoding.UTF8.GetString(ManifestBytes());
        Assert.Contains(find, json, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() =>
            LocaltestManifest.Parse(
                Encoding.UTF8.GetBytes(json.Replace(find, replace, StringComparison.Ordinal)),
                ManifestFiles.Embedded
            )
        );

        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicSectionIsTheDefaultOrigin()
    {
        var origin = LocaltestManifest.Default.Origin();

        Assert.Equal("http://local.altinn.cloud:8000", origin.Url.Text);
        Assert.Equal(
            [
                "local.altinn.cloud",
                "pdf.local.altinn.cloud",
                "workflow-engine.local.altinn.cloud",
                "app-frontend.local.altinn.cloud",
                "af.local.altinn.cloud",
            ],
            origin.Hosts
        );
        Assert.Null(LocaltestManifest.Default.Origin(port: 0).Port);
        Assert.Equal(8000, LocaltestManifest.Default.Origin("origin.test").Port);
    }

    [Fact]
    public async Task ACustomHostAndPortMoveTheEndpointTheHostsAndTheValues()
    {
        var model = SampleModel.Build(
            [],
            withApp: true,
            configure: o =>
            {
                o.PublicHost = "origin.test";
                o.PublicPort = 18000;
            }
        );
        var frontDoor = model.Container(Topology.FrontDoor);

        Assert.Equal(new Uri("http://origin.test:18000/"), model.Localtest.Resource.PlatformUri);
        Assert.Equal(18000, frontDoor.Annotations.OfType<EndpointAnnotation>().Single().Port);
        Assert.Contains("af.origin.test:host-gateway", await SampleModel.RuntimeArgs(model.Container("storage")));
        Assert.Equal(
            "http://origin.test:18000/",
            (await model.Environment(model.Container("storage")))["GeneralSettings__PlatformEndpoint"]
        );
        var app = await model.Environment(model.App!.Resource);
        Assert.Equal("http://origin.test:18000/storage/api/v1/", app["PlatformSettings__ApiStorageEndpoint"]);
        // Without the arbeidsflate group the inbox link is the front door's Home.
        Assert.Equal("http://origin.test:18000/", app["PlatformFrontendSettings__ArbeidsflateInboxUrl"]);
        Assert.Equal("origin.test", app["GeneralSettings__HostName"]);
    }

    /// <summary>
    /// The arbeidsflate group moves the app frontend's inbox links into the local Arbeidsflate, also when it is enabled
    /// after the app is added; without it only the inbox link is set, to the front door's Home. The logout link is the
    /// platform's logout either way.
    /// </summary>
    [Fact]
    public async Task TheArbeidsflateGroupMovesTheInboxLinksIntoArbeidsflate()
    {
        var model = SampleModel.Build(
            [],
            withApp: true,
            configure: o =>
            {
                o.PublicHost = "origin.test";
                o.PublicPort = 18000;
            }
        );
        var without = await model.Environment(model.App!.Resource);
        Assert.Equal(
            [
                ("PlatformFrontendSettings__ArbeidsflateInboxUrl", "http://origin.test:18000/"),
                ("PlatformFrontendSettings__LogoutUrl", "http://origin.test:18000/authentication/api/v1/logout"),
            ],
            without
                .Where(kv => kv.Key.StartsWith("PlatformFrontendSettings__", StringComparison.Ordinal))
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
        );

        model.Localtest.WithArbeidsflate();
        var with = await model.Environment(model.App.Resource);
        Assert.Equal(
            [
                ("PlatformFrontendSettings__ArbeidsflateDialogUrl", "http://af.origin.test:18000/inbox/{dialogId}"),
                ("PlatformFrontendSettings__ArbeidsflateInboxUrl", "http://af.origin.test:18000/"),
                ("PlatformFrontendSettings__ArbeidsflatePartiesUrl", "http://af.origin.test:18000/profile/parties"),
                ("PlatformFrontendSettings__ArbeidsflateProfileUrl", "http://af.origin.test:18000/profile"),
                ("PlatformFrontendSettings__LogoutUrl", "http://origin.test:18000/authentication/api/v1/logout"),
            ],
            with.Where(kv => kv.Key.StartsWith("PlatformFrontendSettings__", StringComparison.Ordinal))
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task PortZeroLeavesThePortToAspireAndEveryValueRefersToTheFrontDoorsEndpoint()
    {
        var model = SampleModel.BuildMinimal(withApps: true, configure: o => o.PublicPort = 0);
        var frontDoor = model.Container(Topology.FrontDoor);
        var endpoint = frontDoor.Annotations.OfType<EndpointAnnotation>().Single();

        // Unproxied and declared without a port: for a container, Aspire 13 allocates a free host port for it while it
        // prepares the run (DcpExecutor.EnsureProxylessEndpointPort reads the declared port, which is unset; the
        // annotation's Port getter shows the target port until then).
        Assert.False(endpoint.IsProxied);
        Assert.Equal(8080, endpoint.TargetPort);
        const string port = "{localtest.bindings.http.port}";
        Assert.Equal(
            $"http://local.altinn.cloud:{port}/",
            await model.EnvironmentExpression(model.Container("storage"), "GeneralSettings__PlatformEndpoint")
        );
        Assert.Equal(
            $"http://local.altinn.cloud:{port}/{{Org}}/{{App}}/instances/{{InstanceOwnerPartyId}}/{{InstanceGuid}}/workflow-engine-callbacks/",
            await model.EnvironmentExpression(model.Container("workflow-engine"), "AppCommandSettings__CommandEndpoint")
        );
        Assert.Equal(
            $"http://local.altinn.cloud:{port}/storage/api/v1/",
            await model.EnvironmentExpression(model.App!.Resource, "PlatformSettings__ApiStorageEndpoint")
        );
        // Literal values stay strings; the host names need no port.
        Assert.Equal(
            "pdf,workflow-engine,app-frontend,af",
            (await model.Environment(frontDoor))[Topology.PublicSubdomainsVariable]
        );
        Assert.Contains("pdf.local.altinn.cloud:host-gateway", await SampleModel.RuntimeArgs(frontDoor));
        Assert.Throws<InvalidOperationException>(() => model.Minimal.Resource.PlatformUri);

        // Once Aspire has allocated the port (here by hand, as the orchestrator does), every value resolves to it.
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", 34567);
        Assert.Equal(new Uri("http://local.altinn.cloud:34567/"), model.Minimal.Resource.PlatformUri);
        var expression = await model.EnvironmentExpressionValue(
            model.Container("storage"),
            "GeneralSettings__PlatformEndpoint"
        );
        Assert.Equal(
            "http://local.altinn.cloud:34567/",
            await expression!.GetValueAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void ABadHostOrPortIsRefusedWhenTheStackIsAdded()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleModel.Build([], configure: o => o.PublicHost = "origin.test:18000")
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SampleModel.BuildMinimal(configure: o => o.PublicPort = 70000)
        );
    }

    private static byte[] ManifestBytes()
    {
        using var stream = typeof(LocaltestManifest).Assembly.GetManifestResourceStream(LocaltestManifest.Resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
