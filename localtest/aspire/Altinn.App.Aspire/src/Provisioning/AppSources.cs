namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>Picks where an app's artefacts are read from.</summary>
public static class AppSources
{
    /// <summary>
    /// The app folder when it is known and holds the app's metadata, otherwise the running app's API: the folder is
    /// what Designer deploys, and the running app is the fallback for a runner that only knows the app's address.
    /// </summary>
    /// <param name="appRoot">The app repository or its <c>App/</c> folder, or <c>null</c> when unknown.</param>
    /// <param name="client">Used by the running-app source; not disposed.</param>
    /// <param name="baseUri">The running app's origin.</param>
    public static IAppSource DirectoryOrRunningApp(string? appRoot, HttpClient client, Uri baseUri)
    {
        if (!string.IsNullOrWhiteSpace(appRoot))
        {
            var directory = new DirectoryAppSource(appRoot);
            if (directory.HasApp)
                return directory;
        }
        return new RunningAppSource(client, baseUri);
    }
}
