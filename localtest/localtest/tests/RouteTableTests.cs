using Localtest.Proxy;
using Microsoft.Extensions.Configuration;

namespace Localtest.Tests;

/// <summary>The route table in code: order, rewrites, hosts, and the bare-prefix redirect.</summary>
public sealed class RouteTableTests
{
    private static readonly LocaltestSettings Default = LocaltestSettings.FromConfiguration(new ConfigurationBuilder().Build());

    private static readonly LocaltestSettings Custom = LocaltestSettings.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localtest:PlatformEndpoint"] = "http://origin.test:18000",
            ["Localtest:PublicSubdomains"] = "pdf, workflow-engine,app-frontend,af",
        }).Build());

    [Theory]
    [InlineData("/dialogporten/graphql", "dialogporten-graphql")]
    [InlineData("/dialogporten/graphql/x", "dialogporten-graphql")]
    [InlineData("/dialogporten/api/v1/enduser/dialogs", "dialogporten-webapi")]
    [InlineData("/dialogporten/graphqlx", "dialogporten-webapi")]
    [InlineData("/storage/dialogporten/api/v1/syncDialog", "dialogporten-adapter")]
    [InlineData("/storage/api/v1/applications", "storage")]
    [InlineData("/register/api/v1/parties", "register")]
    [InlineData("/profile/api/v1/users/current", "profile")]
    [InlineData("/authentication/api/v1/openid/.well-known/openid-configuration", "authentication")]
    [InlineData("/authorization/api/v1/decision", "authorization")]
    [InlineData("/accessmanagement/api/v1/resourceowner", "access-management")]
    [InlineData("/events/api/v1/app", "events")]
    [InlineData("/resourceregistry/api/v1/resource", "resource-registry")]
    [InlineData("/notifications/api/v1/orders", "notifications")]
    [InlineData("/receipt/1/2", "receipt")]
    [InlineData("/correspondence/api/v1/correspondence", "correspondence")]
    public void PrefixesResolveToTheirServiceInOrder(string path, string service)
    {
        var route = PlatformRoutes.Resolve(path);
        Assert.NotNull(route);
        Assert.Equal(service, route.Name);
        Assert.Equal("http://" + service, route.Destination);
        Assert.Equal(RouteKind.Platform, route.Kind);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Home")]
    [InlineData("/storagex/api")]
    [InlineData("/ttd/app/")]
    public void OtherPathsHaveNoPlatformRoute(string path) => Assert.Null(PlatformRoutes.Resolve(path));

    [Fact]
    public void GraphqlIsRewrittenToGraphqlAndKeepsTheRest()
    {
        var route = PlatformRoutes.Resolve("/dialogporten/graphql/schema");
        Assert.NotNull(route?.Rewrite);
        Assert.Equal("/graphql/schema", route.Rewrite.Apply("/dialogporten/graphql/schema").Value);
        Assert.Equal("/graphql", route.Rewrite.Apply("/dialogporten/graphql").Value);
        Assert.Null(PlatformRoutes.Resolve("/dialogporten/api/v1/x")!.Rewrite);
    }

    [Theory]
    [InlineData("pdf.local.altinn.cloud", "pdf3")]
    [InlineData("PDF.local.altinn.cloud", "pdf3")]
    [InlineData("workflow-engine.local.altinn.cloud", "workflow-engine")]
    [InlineData("af.local.altinn.cloud", "arbeidsflate")]
    public void DedicatedHostsBelongToTheirService(string host, string service)
        => Assert.Equal(service, PlatformRoutes.ResolveHost(host, Default)?.Name);

    [Theory]
    [InlineData("local.altinn.cloud")]
    [InlineData("localhost")]
    [InlineData("app-frontend.local.altinn.cloud")]
    public void TheMainHostAndTheDevServerHostAreNotPlatformHosts(string host) => Assert.Null(PlatformRoutes.ResolveHost(host, Default));

    [Theory]
    [InlineData("pdf.origin.test", "pdf3")]
    [InlineData("workflow-engine.origin.test", "workflow-engine")]
    [InlineData("af.origin.test", "arbeidsflate")]
    [InlineData("pdf.local.altinn.cloud", null)]
    [InlineData("origin.test", null)]
    [InlineData("app-frontend.origin.test", null)]
    public void TheDedicatedHostsAreSubdomainsOfTheConfiguredPublicHost(string host, string? service)
        => Assert.Equal(service, PlatformRoutes.ResolveHost(host, Custom)?.Name);

    [Fact]
    public void ASubdomainTheRunnerDoesNotListIsOff()
    {
        var withoutArbeidsflate = new LocaltestSettings("http://origin.test:18000/", "/certs/x.pfx", ["pdf", "workflow-engine", "app-frontend"]);
        Assert.Null(PlatformRoutes.ResolveHost("af.origin.test", withoutArbeidsflate));
        Assert.Equal("pdf3", PlatformRoutes.ResolveHost("pdf.origin.test", withoutArbeidsflate)?.Name);
        Assert.DoesNotContain(PlatformRoutes.Table(withoutArbeidsflate), r => r.Service == "arbeidsflate");
        Assert.Contains(PlatformRoutes.Table(withoutArbeidsflate), r => r.Host == "pdf.origin.test" && r.Service == "pdf3");
    }

    [Fact]
    public void ASubdomainTheFrontDoorDoesNotKnowIsAnError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new LocaltestSettings("http://origin.test:18000/", "/certs/x.pfx", ["pdf", "grafana"]));
        Assert.Contains("grafana", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublicHostAndPortComeFromThePlatformEndpoint()
    {
        Assert.Equal("local.altinn.cloud", Default.PublicHostName);
        Assert.Equal(8000, Default.PublicPort);
        Assert.Equal(LocaltestSettings.KnownSubdomains, Default.PublicSubdomains);
        Assert.Equal("origin.test", Custom.PublicHostName);
        Assert.Equal(18000, Custom.PublicPort);
        Assert.Equal("origin.test:18000", Custom.PublicHost);
        Assert.Equal("http://af.origin.test:18000", Custom.OriginOf("af"));
        Assert.Equal("pdf.origin.test", Custom.HostOf("pdf"));
    }

    [Theory]
    [InlineData("/storage", true)]
    [InlineData("/storage/dialogporten", true)]
    [InlineData("/dialogporten", true)]
    [InlineData("/storage/", false)]
    [InlineData("/dialogporten/graphql", false)]
    [InlineData("/nope", false)]
    public void ABarePrefixIsRedirectedUnlessItIsRewritten(string path, bool redirected)
        => Assert.Equal(redirected, PlatformRoutes.IsPrefixWithoutSlash(path));

    [Fact]
    public void EveryManifestServiceWithARouteIsInTheTable()
        => Assert.Equal(
            new[]
            {
                "access-management", "arbeidsflate", "authentication", "authorization", "correspondence", "dialogporten-adapter",
                "dialogporten-graphql", "dialogporten-webapi", "events", "grafana", "notifications", "otel-collector", "pdf3", "profile",
                "receipt", "register", "resource-registry", "storage", "workflow-engine",
            },
            PlatformRoutes.Services.Order(StringComparer.Ordinal));
}
