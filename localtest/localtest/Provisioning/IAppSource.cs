namespace Localtest.Provisioning;

/// <summary>
/// Where the provisioner reads an app's artefacts. The localtest app has one source, the running app's API
/// (<see cref="RunningAppSource"/>): it gets the app's address and nothing else, whichever runner started the app.
/// </summary>
public interface IAppSource
{
    /// <summary>Reads everything one provisioning pushes, in one go, before anything is sent to the platform.</summary>
    /// <exception cref="InvalidOperationException">The source has no application metadata.</exception>
    Task<AppArtifacts> Read(AppId appId, CancellationToken cancellationToken);
}

/// <summary>The artefacts Altinn Studio pushes to the platform on deploy, as the app holds them.</summary>
/// <param name="ApplicationMetadata"><c>config/applicationmetadata.json</c>, required.</param>
/// <param name="Policy"><c>config/authorization/policy.xml</c> with its placeholders, or <c>null</c> when the app has none.</param>
/// <param name="TestData"><c>wwwroot/testData.json</c>, or <c>null</c> when the app has none.</param>
/// <param name="Texts">Each <c>config/texts/resource.{language}.json</c> by language.</param>
public sealed record AppArtifacts(
    ReadOnlyMemory<byte> ApplicationMetadata,
    string? Policy,
    ReadOnlyMemory<byte>? TestData,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Texts
);
