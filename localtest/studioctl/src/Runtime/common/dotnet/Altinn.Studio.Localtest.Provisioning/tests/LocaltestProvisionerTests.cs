using System.Net;
using Microsoft.Extensions.Logging;

namespace Altinn.Studio.Localtest.Provisioning.Tests;

public sealed class LocaltestProvisionerTests
{
    private const string PlatformHost = "local.altinn.cloud";
    private const string Token = "studio-token";
    private static readonly Uri _platform = new("http://local.altinn.cloud:8000/");
    private static readonly AppId _frontendTest = AppId.Parse("ttd/frontend-test");

    [Fact]
    public async Task PushesTheStepsInDesignersOrder()
    {
        var handler = Platform();
        var (provisioner, logger) = Provisioner(handler, dialogportenRunning: true);

        var result = await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(TestApp.FrontendTestRoot),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "GET /Home/GetTestStudioToken",
                "PUT /register/api/localtest/apps/ttd/frontend-test",
                "POST /storage/api/v1/applications?appId=ttd/frontend-test",
                "POST /storage/api/v1/applications/ttd/frontend-test/texts",
                "POST /storage/api/v1/applications/ttd/frontend-test/texts",
                "POST /authorization/api/v1/policies?org=ttd&app=frontend-test",
                "GET /resourceregistry/api/v1/resource/app_ttd_frontend-test/policy/subjects?reloadFromXacml=true",
                "POST /accessmanagement/api/localtest/refresh",
                "POST /dialogporten/api/v1/localtest/sync",
            ],
            handler.Calls(PlatformHost)
        );
        Assert.Equal(new ProvisionResult(2, true, true, DialogportenSync.Synced), result);
        Assert.Contains(
            logger.Entries,
            e =>
                e.Message
                == $"Provisioned ttd/frontend-test from {Path.Combine(TestApp.FrontendTestRoot, "App")}: metadata, 2 text resource(s), policy True, test data True"
        );
        Assert.Contains(
            logger.Entries,
            e =>
                e.Message
                == "Dialogporten synced the Resource Registry for ttd/frontend-test: 3 subject-resource pair(s), 1 resource policies, caches cleared"
        );
    }

    [Fact]
    public async Task SendsTheStudioTokenOnlyToStorageAndThePdp()
    {
        var handler = Platform();
        var (provisioner, _) = Provisioner(handler, dialogportenRunning: true);

        await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(TestApp.FrontendTestRoot),
            TestContext.Current.CancellationToken
        );

        foreach (var request in handler.Requests)
        {
            var authorized =
                request.PathAndQuery.StartsWith("/storage/", StringComparison.Ordinal)
                || request.PathAndQuery.StartsWith("/authorization/", StringComparison.Ordinal);
            Assert.Equal(authorized ? $"Bearer {Token}" : null, request.Authorization);
        }
    }

    [Fact]
    public async Task PushesTheFilesAsTheyAreAndThePolicyWithItsPlaceholdersReplaced()
    {
        using var app = TestApp.CopyFrontendTest();
        await File.WriteAllTextAsync(
            app.AppFile("config/authorization/policy.xml"),
            "<Policy><Rule RuleId=\"urn:altinn:example:ruleid:[ORG]:[app]:1\"/></Policy>",
            TestContext.Current.CancellationToken
        );
        var handler = Platform();
        var (provisioner, _) = Provisioner(handler);

        await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(app.Root),
            TestContext.Current.CancellationToken
        );

        var policy = Assert.Single(
            handler.Requests,
            r => r.PathAndQuery.StartsWith("/authorization/", StringComparison.Ordinal)
        );
        Assert.Equal("<Policy><Rule RuleId=\"urn:altinn:example:ruleid:ttd:frontend-test:1\"/></Policy>", policy.Body);
        Assert.Equal("application/xml", policy.ContentType);

        var register = Assert.Single(
            handler.Requests,
            r => r.Method == "PUT" && r.PathAndQuery.StartsWith("/register/", StringComparison.Ordinal)
        );
        Assert.Equal(
            await File.ReadAllTextAsync(app.AppFile("wwwroot/testData.json"), TestContext.Current.CancellationToken),
            register.Body
        );
        Assert.Equal("application/json", register.ContentType);

        var metadata = handler.Requests.Single(r =>
            r.PathAndQuery == "/storage/api/v1/applications?appId=ttd/frontend-test"
        );
        Assert.Equal(
            await File.ReadAllTextAsync(
                app.AppFile("config/applicationmetadata.json"),
                TestContext.Current.CancellationToken
            ),
            metadata.Body
        );
    }

    [Fact]
    public async Task ReplacesWhatStorageAlreadyHas()
    {
        var handler = Platform(request =>
            request.Method == "POST" && request.PathAndQuery.StartsWith("/storage/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                : null
        );
        var (provisioner, _) = Provisioner(handler);

        await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(TestApp.FrontendTestRoot),
            TestContext.Current.CancellationToken
        );

        var storage = handler.Calls(PlatformHost).Where(c => c.Contains("/storage/", StringComparison.Ordinal));
        Assert.Equal(
            [
                "POST /storage/api/v1/applications?appId=ttd/frontend-test",
                "PUT /storage/api/v1/applications/ttd/frontend-test",
                "POST /storage/api/v1/applications/ttd/frontend-test/texts",
                "PUT /storage/api/v1/applications/ttd/frontend-test/texts/en",
                "POST /storage/api/v1/applications/ttd/frontend-test/texts",
                "PUT /storage/api/v1/applications/ttd/frontend-test/texts/nb",
            ],
            storage
        );
    }

    [Fact]
    public async Task WithoutPolicyOrTestDataTheRegisterGetsAnEmptyBodyAndThePolicyStepsAreLeftOut()
    {
        using var app = TestApp.CopyFrontendTest();
        File.Delete(app.AppFile("config/authorization/policy.xml"));
        File.Delete(app.AppFile("wwwroot/testData.json"));
        var handler = Platform();
        var (provisioner, logger) = Provisioner(handler);

        var result = await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(app.Root),
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(
            handler.Requests,
            r => r.PathAndQuery.StartsWith("/authorization/", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            handler.Requests,
            r => r.PathAndQuery.StartsWith("/resourceregistry/", StringComparison.Ordinal)
        );
        var register = Assert.Single(
            handler.Requests,
            r => r.PathAndQuery.StartsWith("/register/", StringComparison.Ordinal)
        );
        Assert.Equal("", register.Body);
        Assert.Null(register.ContentType);
        Assert.Equal(new ProvisionResult(2, false, false, DialogportenSync.Skipped), result);
        Assert.Contains(
            logger.Entries,
            e => e.Message == "Dialogporten sync for ttd/frontend-test skipped: the dialogporten group is not running"
        );
    }

    [Fact]
    public async Task AFailingStepStopsTheProvisioningWithItsStatus()
    {
        var handler = Platform(request =>
            request.PathAndQuery.StartsWith("/authorization/", StringComparison.Ordinal)
                ? RecordingHandler.Text("no", HttpStatusCode.Forbidden)
                : null
        );
        var (provisioner, _) = Provisioner(handler, dialogportenRunning: true);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provisioner.Provision(
                _frontendTest,
                new DirectoryAppSource(TestApp.FrontendTestRoot),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            "POST http://local.altinn.cloud:8000/authorization/api/v1/policies?org=ttd&app=frontend-test returned 403: no",
            ex.Message
        );
        Assert.DoesNotContain(
            handler.Requests,
            r => r.PathAndQuery.StartsWith("/accessmanagement/", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task AFailingDialogportenSyncIsLoggedOnceAndDoesNotFailTheProvisioning()
    {
        var handler = Platform(request =>
            request.PathAndQuery.StartsWith("/dialogporten/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : null
        );
        var (provisioner, logger) = Provisioner(handler, dialogportenRunning: true);
        var source = new DirectoryAppSource(TestApp.FrontendTestRoot);

        var first = await provisioner.Provision(_frontendTest, source, TestContext.Current.CancellationToken);
        var second = await provisioner.SyncDialogporten("the provisioned apps", TestContext.Current.CancellationToken);

        Assert.Equal(DialogportenSync.Failed, first.Dialogporten);
        Assert.Equal(DialogportenSync.Failed, second);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(
            "Dialogporten sync for ttd/frontend-test failed; retrying on the next refresh (by hand: POST http://local.altinn.cloud:8000/dialogporten/api/v1/localtest/sync)",
            warning.Message
        );
    }

    [Fact]
    public async Task UsesTheStudioTokenSourceItIsGiven()
    {
        var handler = Platform();
        using var client = new HttpClient(handler);
        var provisioner = new LocaltestProvisioner(
            new LocaltestProvisionerOptions
            {
                PlatformUri = _platform,
                StudioToken = _ => Task.FromResult("other-token"),
            },
            client,
            new ListLogger()
        );

        await provisioner.Provision(
            _frontendTest,
            new DirectoryAppSource(TestApp.FrontendTestRoot),
            TestContext.Current.CancellationToken
        );

        Assert.DoesNotContain(handler.Requests, r => r.PathAndQuery == "/Home/GetTestStudioToken");
        Assert.All(
            handler.Requests.Where(r => r.Authorization is not null),
            r => Assert.Equal("Bearer other-token", r.Authorization)
        );
    }

    [Fact]
    public async Task DeprovisionRemovesTheTestUsersAndRefreshesAccessManagement()
    {
        var handler = Platform();
        var (provisioner, logger) = Provisioner(handler);

        await provisioner.Deprovision(_frontendTest, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["DELETE /register/api/localtest/apps/ttd/frontend-test", "POST /accessmanagement/api/localtest/refresh"],
            handler.Calls(PlatformHost)
        );
        Assert.Contains(logger.Entries, e => e.Message == "Removed ttd/frontend-test's test users from the platform");
    }

    [Fact]
    public async Task ProvisionsFromTheRunningAppWithTheSameSteps()
    {
        var handler = Platform(request =>
            request.Host == "127.0.0.1"
                ? request.PathAndQuery switch
                {
                    "/ttd/frontend-test/api/v1/applicationmetadata?checkOrgApp=false" => RecordingHandler.Text("{}"),
                    "/ttd/frontend-test/api/v1/applicationlanguages" => RecordingHandler.Text(
                        """[{"language":"nb"}]"""
                    ),
                    "/ttd/frontend-test/api/v1/texts/nb" => RecordingHandler.Text("{}"),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                }
                : null
        );
        var (provisioner, logger) = Provisioner(handler);
        using var appClient = new HttpClient(handler);

        await provisioner.Provision(
            _frontendTest,
            new RunningAppSource(appClient, new Uri("http://127.0.0.1:5005/")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            [
                "GET /Home/GetTestStudioToken",
                "PUT /register/api/localtest/apps/ttd/frontend-test",
                "POST /storage/api/v1/applications?appId=ttd/frontend-test",
                "POST /storage/api/v1/applications/ttd/frontend-test/texts",
                "POST /accessmanagement/api/localtest/refresh",
            ],
            handler.Calls(PlatformHost)
        );
        Assert.Contains(
            logger.Entries,
            e =>
                e.Message
                == "Provisioned ttd/frontend-test from http://127.0.0.1:5005/: metadata, 1 text resource(s), policy False, test data False"
        );
    }

    /// <summary>The platform: the Studio token, Dialogporten's sync summary, 200 for the rest unless overridden.</summary>
    private static RecordingHandler Platform(Func<RecordedRequest, HttpResponseMessage?>? respond = null) =>
        new(request =>
            respond?.Invoke(request)
            ?? request.PathAndQuery switch
            {
                "/Home/GetTestStudioToken" => RecordingHandler.Text($"\"{Token}\""),
                "/dialogporten/api/v1/localtest/sync" => RecordingHandler.Text(
                    """{"subjectResources":3,"resourcePolicyInformation":1}"""
                ),
                _ => null,
            }
        );

    private static (LocaltestProvisioner Provisioner, ListLogger Logger) Provisioner(
        RecordingHandler handler,
        bool dialogportenRunning = false
    )
    {
        var logger = new ListLogger();
        var client = new HttpClient(handler);
        var provisioner = new LocaltestProvisioner(
            new LocaltestProvisionerOptions
            {
                PlatformUri = _platform,
                DialogportenRunning = () => dialogportenRunning,
            },
            client,
            logger
        );
        return (provisioner, logger);
    }
}
