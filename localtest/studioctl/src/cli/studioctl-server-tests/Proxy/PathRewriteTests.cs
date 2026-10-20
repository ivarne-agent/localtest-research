using Altinn.Studio.EnvTopology;
using Altinn.Studio.StudioctlServer.Proxy;
using Microsoft.AspNetCore.Http;

namespace Studioctl.Tests.Proxy;

public sealed class PathRewriteTests
{
    private const string GraphQlPrefix = "/dialogporten/graphql";

    [Theory]
    [InlineData("/dialogporten/graphql", "/graphql")]
    [InlineData("/dialogporten/graphql/", "/graphql/")]
    [InlineData("/dialogporten/graphql/schema.graphql", "/graphql/schema.graphql")]
    [InlineData("/Dialogporten/GraphQL/stitching", "/graphql/stitching")]
    public void ReplacesTheMatchedPrefixAndKeepsTheRest(string path, string expected)
    {
        var rewrite = Rewrite(GraphQlPrefix, "/graphql");

        Assert.Equal(expected, rewrite.Apply(path).Value);
    }

    [Fact]
    public void LeavesAPathOutsideThePrefixAlone()
    {
        var rewrite = Rewrite(GraphQlPrefix, "/graphql");

        Assert.Equal("/dialogporten/graphqlx", rewrite.Apply("/dialogporten/graphqlx").Value);
        Assert.Equal("/dialogporten/api/v1", rewrite.Apply("/dialogporten/api/v1").Value);
    }

    [Fact]
    public void AnEmptyReplacementStripsThePrefix()
    {
        Assert.Equal("/a/b", Rewrite("/strip", "").Apply("/strip/a/b").Value);
    }

    [Fact]
    public void ARouteWithoutTheMetadataOrWithoutAPrefixHasNoRewrite()
    {
        Assert.Null(PathRewrite.From(Route("/dialogporten", rewrite: null)));
        Assert.Null(PathRewrite.From(Route("", "/graphql")));
    }

    [Fact]
    public async Task TheTransformerForwardsUnderTheReplacementAndKeepsTheQuery()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("local.altinn.cloud", 8000);
        context.Request.Path = "/dialogporten/graphql/schema.graphql";
        context.Request.QueryString = new QueryString("?sdl=1");
        var transformer = new PlatformTransformer(PathRewrite.From(Route(GraphQlPrefix, "/graphql")));
        using var proxyRequest = new HttpRequestMessage();

        await transformer.TransformRequestAsync(
            context,
            proxyRequest,
            "http://127.0.0.1:8031/",
            CancellationToken.None
        );

        Assert.Equal("http://127.0.0.1:8031/graphql/schema.graphql?sdl=1", proxyRequest.RequestUri?.ToString());
        Assert.Equal("local.altinn.cloud:8000", proxyRequest.Headers.Host);
    }

    [Fact]
    public async Task TheTransformerWithoutARewriteLeavesTheDestinationToTheForwarder()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("local.altinn.cloud", 8000);
        context.Request.Path = "/dialogporten/api/v1/serviceowner/dialogs";
        var transformer = new PlatformTransformer();
        using var proxyRequest = new HttpRequestMessage();

        await transformer.TransformRequestAsync(
            context,
            proxyRequest,
            "http://127.0.0.1:8030/",
            CancellationToken.None
        );

        // YARP builds the destination from the request path after the transform, unchanged.
        Assert.Null(proxyRequest.RequestUri);
    }

    private static PathRewrite Rewrite(string prefix, string replacement) =>
        PathRewrite.From(Route(prefix, replacement)) ?? throw new InvalidOperationException("no rewrite on the route");

    private static BoundTopologyRoute Route(string prefix, string? rewrite) =>
        new()
        {
            Component = "dialogporten-graphql",
            Enabled = true,
            Match = new BoundTopologyRouteMatch { Host = "local.altinn.cloud", PathPrefix = prefix },
            Metadata = rewrite is null
                ? []
                : [new BoundTopologyMetadataEntry { Key = "rewritePrefix", Value = rewrite }],
        };
}
