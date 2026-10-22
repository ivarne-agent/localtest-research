namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// Where an app finds the shared test certificate, which only its runner knows: <see cref="Path" /> is the file as the
/// app opens it (<c>{{appCertPath}}</c>, app-lib v8's 3.x access token client), <see cref="KeysFolder" /> the directory
/// as the 1.x client resolves it, appended to the parent of the app's working directory, with a leading and a trailing
/// slash (<c>{{appCertKeysFolder}}</c>).
/// </summary>
/// <param name="Path">The certificate's path.</param>
/// <param name="KeysFolder">The certificate's directory relative to the app root.</param>
internal sealed record AppCertificate(string Path, string KeysFolder)
{
    /// <summary>
    /// The certificate of a process run, as studioctl's <c>nativeAccessTokenSigning</c> computes it: the host path,
    /// and a traversal from the app root (the parent of the project folder, the app's working directory) to the
    /// certificate directory.
    /// </summary>
    public static AppCertificate ForProcess(string appRoot, string certificateDirectory, string fileName)
    {
        var directory = System.IO.Path.GetFullPath(certificateDirectory);
        var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(appRoot), directory);
        return new AppCertificate(System.IO.Path.Combine(directory, fileName), "/" + relative.Replace('\\', '/') + "/");
    }
}

/// <summary>
/// The manifest's app section resolved for one run. <see cref="Defaults" /> are those studioctl sets unless its shell
/// has them; <see cref="Fixed" /> are always set. An Aspire project inherits nothing from the AppHost, so the package
/// sets both.
/// </summary>
/// <param name="Defaults">The environment and the access token signing variables.</param>
/// <param name="Fixed">The fixed environment and the secrets variables.</param>
internal sealed record ResolvedAppEnvironment(
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyDictionary<string, string> Fixed
)
{
    /// <summary>Every variable, sorted.</summary>
    public IReadOnlyDictionary<string, string> AllVariables() =>
        new SortedDictionary<string, string>(
            Defaults.Concat(Fixed).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            StringComparer.Ordinal
        );
}
