using Localtest.Proxy;

namespace Localtest.Tests;

/// <summary>The route table in code: order, rewrites, hosts, and the bare-prefix redirect.</summary>
public sealed class RouteTableTests
{
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
        => Assert.Equal(service, PlatformRoutes.ResolveHost(host)?.Name);

    [Theory]
    [InlineData("local.altinn.cloud")]
    [InlineData("localhost")]
    [InlineData("app-frontend.local.altinn.cloud")]
    public void TheMainHostAndTheDevServerHostAreNotPlatformHosts(string host) => Assert.Null(PlatformRoutes.ResolveHost(host));

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
                "dialogporten-graphql", "dialogporten-webapi", "events", "notifications", "pdf3", "profile", "receipt", "register",
                "resource-registry", "storage", "workflow-engine",
            },
            PlatformRoutes.Services.Order(StringComparer.Ordinal));
}
