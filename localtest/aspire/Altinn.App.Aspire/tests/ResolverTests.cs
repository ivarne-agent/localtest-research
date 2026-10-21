using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

public sealed class ResolverTests
{
    private static ManifestResolver Resolver(
        IReadOnlyDictionary<string, string>? environment = null,
        string? tag = null
    ) => new(ManifestLoader.Default, "/certs-on-host", tag, name => environment?.GetValueOrDefault(name));

    private static ResolvedComponent Component(ManifestResolver resolver, string name) =>
        resolver.AllComponents().Single(c => c.Name == name);

    [Fact]
    public void PlaceholdersResolveAsStudioctlDoes()
    {
        var resolver = Resolver();
        var storage = Component(resolver, "storage");
        var bff = Component(resolver, "arbeidsflate-bff");

        Assert.Equal("http://local.altinn.cloud:8000/", storage.Environment["GeneralSettings__PlatformEndpoint"]);
        Assert.Equal(
            "/certs/localtest-accesstoken.pfx",
            storage.Environment["AccessTokenSettings__AccessTokenSigningCertificateFullPath"]
        );
        // Single braces belong to the component.
        Assert.StartsWith(
            "Host=postgres;Port=5432;Username=platform_storage;Password={0};",
            storage.Environment["PostgreSqlSettings__ConnectionString"],
            StringComparison.Ordinal
        );
        Assert.Equal("http://af.local.altinn.cloud:8000", bff.Environment["HOSTNAME"]);
        Assert.Equal("redis://redis:6379/2", bff.Environment["REDIS_CONNECTION_STRING"]);
        Assert.Equal("local.altinn.cloud", bff.Environment["AUTH_CONTEXT_COOKIE_DOMAIN"]);
    }

    [Theory]
    [InlineData("{{nope}}")]
    [InlineData("{{host:nowhere}}")]
    [InlineData("{{alias:storage}}")]
    [InlineData("{{port:nothing}}")]
    [InlineData("{{platform")]
    [InlineData("{{url}}")]
    public void ABadPlaceholderIsRefused(string value)
    {
        var placeholders = new Placeholders(ManifestLoader.Default, LocaltestTopology.Default);

        Assert.Throws<InvalidDataException>(() => placeholders.Expand(value));
    }

    [Fact]
    public void GroupsCloseOverWhatTheyImply()
    {
        var resolver = Resolver();

        Assert.Empty(resolver.ResolveGroups([]));
        Assert.Equal(["dialogporten"], resolver.ResolveGroups(["dialogporten"]));
        Assert.Equal(["correspondence", "dialogporten"], resolver.ResolveGroups(["correspondence"]));
        Assert.Equal(
            ["arbeidsflate", "correspondence", "dialogporten", "everything", "receipt"],
            resolver.ResolveGroups(["everything"])
        );
        Assert.Throws<ArgumentException>(() => resolver.ResolveGroups(["nope"]));
    }

    [Fact]
    public void TheDialogportenGroupAddsItsComponentsOnly()
    {
        var resolver = Resolver();
        var names = resolver.Components(resolver.ResolveGroups(["dialogporten"])).Select(c => c.Name).ToList();

        Assert.Contains("redis", names);
        Assert.Contains("dialogporten-adapter", names);
        Assert.DoesNotContain("receipt", names);
        Assert.DoesNotContain("correspondence", names);
        Assert.DoesNotContain("arbeidsflate", names);
        Assert.Equal(18, names.Count);
    }

    [Fact]
    public void ImagesFollowTheTagAndTheOverrides()
    {
        var pin = ManifestLoader.Default.Images.Tag;
        Assert.Equal(
            $"ghcr.io/ivarne-agent/localtest-research/localtest-storage:{pin}",
            Component(Resolver(), "storage").Image.Reference
        );

        var tagged = Component(
            Resolver(new Dictionary<string, string> { ["STUDIOCTL_LOCALTEST_V2_TAG"] = "latest" }),
            "storage"
        );
        Assert.Equal("ghcr.io/ivarne-agent/localtest-research/localtest-storage:latest", tagged.Image.Reference);
        Assert.True(tagged.Image.Floating);

        // The option wins over the variable; a per-image variable wins over both and does not float.
        var option = Resolver(
            new Dictionary<string, string> { ["STUDIOCTL_LOCALTEST_V2_TAG"] = "latest" },
            "sha-1234567"
        );
        Assert.Equal("sha-1234567", Component(option, "storage").Image.Tag);
        var overridden = Component(
            Resolver(
                new Dictionary<string, string>
                {
                    ["STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE"] = "localhost:5000/mine/storage:dev",
                }
            ),
            "storage"
        ).Image;
        Assert.Equal(new ImageReference("localhost:5000", "mine/storage", "dev", null, false), overridden);

        var pdf = Component(Resolver(), "pdf3").Image;
        Assert.Equal(
            new ImageReference("ghcr.io", "altinn/altinn-studio/runtime-pdf3-worker", "tt02", null, true),
            pdf
        );
        var pdfOverride = Component(
            Resolver(new Dictionary<string, string> { ["STUDIOCTL_IMAGE_PDF3"] = "localtest-pdf3:dev" }),
            "pdf3"
        ).Image;
        Assert.Equal(new ImageReference(null, "localtest-pdf3", "dev", null, false), pdfOverride);
    }

    [Fact]
    public void TheThirdPartyImageIsPinnedByDigest()
    {
        var redis = Component(Resolver(), "redis").Image;

        Assert.Equal("docker.io", redis.Registry);
        Assert.Equal("library/redis", redis.Image);
        Assert.Equal("7.2-alpine", redis.Tag);
        Assert.Equal("29e8589c3f9ba699b5f7aa4b3c7733c58852a3626439e619aa0ee78de08c6ca0", redis.Sha256);
        Assert.Equal(
            "docker.io/library/redis:7.2-alpine@sha256:29e8589c3f9ba699b5f7aa4b3c7733c58852a3626439e619aa0ee78de08c6ca0",
            redis.Reference
        );
    }
}
