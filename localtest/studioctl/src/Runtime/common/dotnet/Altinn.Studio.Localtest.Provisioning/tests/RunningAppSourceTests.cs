using System.Net;

namespace Altinn.Studio.Localtest.Provisioning.Tests;

public sealed class RunningAppSourceTests
{
    private static readonly AppId _frontendTest = AppId.Parse("ttd/frontend-test");

    [Fact]
    public async Task ReadsTheAppsOwnApi()
    {
        var handler = new RecordingHandler(request =>
            request.PathAndQuery switch
            {
                "/ttd/frontend-test/api/v1/applicationmetadata?checkOrgApp=false" => RecordingHandler.Text(
                    """{"id":"ttd/frontend-test"}"""
                ),
                "/ttd/frontend-test/api/v1/meta/authorizationpolicy" => RecordingHandler.Text("<policy/>"),
                "/ttd/frontend-test/testData.json" => RecordingHandler.Text("""{"persons":[]}"""),
                "/ttd/frontend-test/api/v1/applicationlanguages" => RecordingHandler.Text(
                    """[{"language":"nb"},{"language":"en"},{"language":" "}]"""
                ),
                "/ttd/frontend-test/api/v1/texts/nb" => RecordingHandler.Text("""{"language":"nb"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }
        );
        using var client = new HttpClient(handler);
        var source = new RunningAppSource(client, new Uri("http://127.0.0.1:5005/"));

        var artifacts = await source.Read(_frontendTest, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET /ttd/frontend-test/api/v1/applicationmetadata?checkOrgApp=false",
                "GET /ttd/frontend-test/api/v1/meta/authorizationpolicy",
                "GET /ttd/frontend-test/testData.json",
                "GET /ttd/frontend-test/api/v1/applicationlanguages",
                "GET /ttd/frontend-test/api/v1/texts/nb",
                "GET /ttd/frontend-test/api/v1/texts/en",
            ],
            handler.Calls("127.0.0.1")
        );
        Assert.Equal("<policy/>", artifacts.Policy);
        Assert.NotNull(artifacts.TestData);
        Assert.Equal(["nb"], artifacts.Texts.Keys);
        Assert.Equal("http://127.0.0.1:5005/", source.ToString());
    }

    [Fact]
    public async Task NotFoundMeansNoneExceptForTheMetadata()
    {
        var handler = new RecordingHandler(request =>
            request.PathAndQuery.Contains("applicationmetadata", StringComparison.Ordinal)
                ? RecordingHandler.Text("{}")
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        );
        using var client = new HttpClient(handler);

        var artifacts = await new RunningAppSource(client, new Uri("http://127.0.0.1:5005/")).Read(
            _frontendTest,
            TestContext.Current.CancellationToken
        );

        Assert.Null(artifacts.Policy);
        Assert.Null(artifacts.TestData);
        Assert.Empty(artifacts.Texts);
    }

    [Fact]
    public async Task WithoutMetadataTheReadFails()
    {
        using var client = new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunningAppSource(client, new Uri("http://127.0.0.1:5005/")).Read(
                _frontendTest,
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal("the app returned no application metadata", ex.Message);
    }
}
