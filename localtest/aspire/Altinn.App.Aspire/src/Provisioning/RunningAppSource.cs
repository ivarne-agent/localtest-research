using System.Text.Json;

namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>
/// Reads the artefacts from the running app's own API under <c>{baseUri}/{org}/{app}/</c>: the application metadata,
/// the policy (<c>api/v1/meta/authorizationpolicy</c>), <c>testData.json</c> from its <c>wwwroot</c>, and the texts of
/// each language <c>api/v1/applicationlanguages</c> lists. studioctl-server's case: it discovers an app by its address
/// only, without its folder. A 404 means "the app has none"; any other failure throws.
/// </summary>
public sealed class RunningAppSource : IAppSource
{
    private readonly HttpClient _client;

    /// <param name="client">Not disposed here; the caller owns it.</param>
    /// <param name="baseUri">The app's origin, for example <c>http://127.0.0.1:5005/</c>.</param>
    public RunningAppSource(HttpClient client, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(baseUri);
        _client = client;
        BaseUri = baseUri;
    }

    /// <summary>The app's origin.</summary>
    public Uri BaseUri { get; }

    /// <inheritdoc />
    public async Task<AppArtifacts> Read(AppId appId, CancellationToken cancellationToken)
    {
        var appRoot = new Uri(BaseUri, $"/{appId.Org}/{appId.App}/");
        var metadata =
            await PlatformHttp.GetBytes(
                _client,
                new Uri(appRoot, "api/v1/applicationmetadata?checkOrgApp=false"),
                cancellationToken
            ) ?? throw new InvalidOperationException("the app returned no application metadata");
        var policy = await PlatformHttp.GetString(
            _client,
            new Uri(appRoot, "api/v1/meta/authorizationpolicy"),
            cancellationToken
        );
        var testData = await PlatformHttp.GetBytes(_client, new Uri(appRoot, "testData.json"), cancellationToken);
        var texts = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in await GetLanguages(appRoot, cancellationToken))
        {
            if (
                await PlatformHttp.GetBytes(
                    _client,
                    new Uri(appRoot, $"api/v1/texts/{Uri.EscapeDataString(language)}"),
                    cancellationToken
                ) is
                { } text
            )
                texts[language] = text;
        }

        return new AppArtifacts(metadata, policy, testData is null ? (ReadOnlyMemory<byte>?)null : testData, texts);
    }

    /// <summary>The app's origin, as the provisioner logs it.</summary>
    public override string ToString() => BaseUri.ToString();

    private async Task<IReadOnlyList<string>> GetLanguages(Uri appRoot, CancellationToken cancellationToken)
    {
        var body = await PlatformHttp.GetBytes(
            _client,
            new Uri(appRoot, "api/v1/applicationlanguages"),
            cancellationToken
        );
        if (body is null)
            return [];
        var languages = JsonSerializer.Deserialize(body, ProvisioningJsonContext.Default.ListApplicationLanguage) ?? [];
        return [.. languages.Select(static l => l.Language).Where(static l => !string.IsNullOrWhiteSpace(l))];
    }
}
