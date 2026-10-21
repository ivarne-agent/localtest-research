using System.Text;

namespace Altinn.Studio.Localtest.Provisioning.Tests;

public sealed class DirectoryAppSourceTests
{
    private static readonly AppId _frontendTest = AppId.Parse("ttd/frontend-test");

    [Fact]
    public async Task ReadsTheAppFolderAsDesignerDeploysIt()
    {
        var source = new DirectoryAppSource(TestApp.FrontendTestRoot);

        var artifacts = await source.Read(_frontendTest, TestContext.Current.CancellationToken);

        Assert.Equal(
            await File.ReadAllBytesAsync(
                TestApp.FrontendTestFile("config/applicationmetadata.json"),
                TestContext.Current.CancellationToken
            ),
            artifacts.ApplicationMetadata.ToArray()
        );
        Assert.Equal(
            await File.ReadAllTextAsync(
                TestApp.FrontendTestFile("config/authorization/policy.xml"),
                TestContext.Current.CancellationToken
            ),
            artifacts.Policy
        );
        Assert.Equal(["en", "nb"], artifacts.Texts.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            await File.ReadAllBytesAsync(
                TestApp.FrontendTestFile("config/texts/resource.nb.json"),
                TestContext.Current.CancellationToken
            ),
            artifacts.Texts["nb"].ToArray()
        );
        Assert.NotNull(artifacts.TestData);
        Assert.Contains("FIXTURE", Encoding.UTF8.GetString(artifacts.TestData.Value.Span), StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTheRepositoryOrTheAppProjectFolder()
    {
        var fromRepository = new DirectoryAppSource(TestApp.FrontendTestRoot);
        var fromProject = new DirectoryAppSource(Path.Combine(TestApp.FrontendTestRoot, "App"));

        Assert.Equal(fromRepository.AppDirectory, fromProject.AppDirectory);
        Assert.True(fromProject.HasApp);
    }

    [Fact]
    public async Task ReadsTheAppIdFromTheMetadata()
    {
        var id = await new DirectoryAppSource(TestApp.FrontendTestRoot).ReadAppId(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(_frontendTest, id);
    }

    [Fact]
    public async Task MissingPolicyTestDataAndTextsMeanNone()
    {
        using var app = TestApp.CopyFrontendTest();
        File.Delete(app.AppFile("config/authorization/policy.xml"));
        File.Delete(app.AppFile("wwwroot/testData.json"));
        Directory.Delete(app.AppFile("config/texts"), recursive: true);

        var artifacts = await new DirectoryAppSource(app.Root).Read(
            _frontendTest,
            TestContext.Current.CancellationToken
        );

        Assert.Null(artifacts.Policy);
        Assert.Null(artifacts.TestData);
        Assert.Empty(artifacts.Texts);
    }

    [Fact]
    public async Task MissingMetadataFails()
    {
        using var app = TestApp.CopyFrontendTest();
        File.Delete(app.AppFile("config/applicationmetadata.json"));
        var source = new DirectoryAppSource(app.Root);

        Assert.False(source.HasApp);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.Read(_frontendTest, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public void FallsBackToTheRunningAppWithoutTheFolder()
    {
        using var client = new HttpClient(new RecordingHandler());
        var baseUri = new Uri("http://127.0.0.1:5005/");

        Assert.IsType<DirectoryAppSource>(AppSources.DirectoryOrRunningApp(TestApp.FrontendTestRoot, client, baseUri));
        Assert.IsType<RunningAppSource>(AppSources.DirectoryOrRunningApp(null, client, baseUri));
        Assert.IsType<RunningAppSource>(
            AppSources.DirectoryOrRunningApp(Path.Combine(TestApp.FrontendTestRoot, "nowhere"), client, baseUri)
        );
    }
}
