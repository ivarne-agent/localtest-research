namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The certificate an app signs its platform access tokens with, as the runner finds it. Two generations of the access
/// token client read it differently: 3.x (app-lib v8) opens <see cref="FullPath" />, 1.x (the v9 backend) appends
/// <see cref="KeysFolder" /> and <see cref="FileName" /> to the parent of the app's working directory.
/// </summary>
/// <param name="FullPath">The certificate's path.</param>
/// <param name="KeysFolder">The certificate's directory as a path from the app root, with both slashes.</param>
/// <param name="FileName">The certificate's file name.</param>
internal sealed record AppCertificate(string FullPath, string KeysFolder, string FileName)
{
    /// <summary>
    /// The certificate of a process run, as studioctl computes it: the host path, and a traversal from the app root (the
    /// parent of the project folder, the app's working directory) to the certificate directory.
    /// </summary>
    public static AppCertificate ForProcess(string appRoot, string certificateDirectory)
    {
        var directory = Path.GetFullPath(certificateDirectory);
        var relative = Path.GetRelativePath(Path.GetFullPath(appRoot), directory);
        return new AppCertificate(
            Path.Combine(directory, Topology.CertificateFileName),
            "/" + relative.Replace('\\', '/') + "/",
            Topology.CertificateFileName
        );
    }
}

/// <summary>
/// The environment an app runs with against the stack: a port of studioctl's <c>internal/cmd/app/env.go</c>
/// (<c>newAppRunEnv</c>), held to the same golden file (<c>internal/cmd/app/testdata/app-environment.golden.json</c>).
/// </summary>
internal static class AppEnvironment
{
    /// <summary>The variable naming the app's secrets directory.</summary>
    public const string SecretsDirectory = "RUNTIME_APP_SECRETS_DIR";

    /// <summary>The variable naming the Maskinporten client's file in it.</summary>
    public const string MaskinportenFileNameVariable = "RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME";

    /// <summary>The variable naming the app codes' file in it.</summary>
    public const string AppCodesFileNameVariable = "RUNTIME_APP_SECRETS_APPCODES_FILENAME";

    /// <summary>The Maskinporten client's file name.</summary>
    public const string MaskinportenFileName = "maskinporten-settings.json";

    /// <summary>The app codes' file name.</summary>
    public const string AppCodesFileName = "app-codes.json";

    /// <summary>The Kestrel endpoint variable, which the Aspire package sets from the allocated port.</summary>
    public const string KestrelUrl = "Kestrel__EndPoints__Http__Url";

    /// <summary>The OpenTelemetry endpoint variable, which Aspire sets to its dashboard.</summary>
    public const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The app-frontend asset base URL variable (the frontend dev server).</summary>
    public const string AppFrontendAssetBaseUrl = "AppSettings__AppFrontendAssetBaseUrl";

    /// <summary>studioctl's OpenTelemetry collector, which Aspire replaces with its dashboard.</summary>
    private static readonly string _otelUrl = Topology.Url("otel." + Topology.PublicHost, 4317);

    /// <summary>
    /// The environment, sorted: <paramref name="current" /> (the first value of a key wins, a malformed entry is
    /// skipped), the run's own variables (always set), and the platform's defaults (set unless
    /// <paramref name="current" /> has them).
    /// </summary>
    public static SortedDictionary<string, string> Build(
        IEnumerable<string> current,
        string kestrelUrl,
        int ingressPort,
        string appFrontendAssetBaseUrl,
        string secretsDirectory,
        string keysDirectory,
        AppCertificate? signing
    )
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in current)
        {
            var at = entry.IndexOf('=', StringComparison.Ordinal);
            if (at > 0)
            {
                values.TryAdd(entry[..at], entry[(at + 1)..]);
            }
        }
        void Default(string key, string value) => values.TryAdd(key, value);

        var platform = Topology.Url(Topology.PublicHost, ingressPort);
        values["STUDIOCTL_APP_RUN"] = "1";
        values[SecretsDirectory] = secretsDirectory;
        values[MaskinportenFileNameVariable] = MaskinportenFileName;
        values[AppCodesFileNameVariable] = AppCodesFileName;
        if (keysDirectory.Length > 0)
        {
            values["ALTINN_KEYS_DIRECTORY"] = keysDirectory;
        }
        Default("ASPNETCORE_ENVIRONMENT", "Development");
        Default(KestrelUrl, kestrelUrl);
        if (appFrontendAssetBaseUrl.Length > 0)
        {
            values[AppFrontendAssetBaseUrl] = appFrontendAssetBaseUrl;
        }
        Default("AppSettings__OpenIdWellKnownEndpoint", platform + "/authentication/api/v1/openid/");
        Default("GeneralSettings__ExternalAppBaseUrl", platform + "/{org}/{app}/");
        Default("GeneralSettings__HostName", Topology.PublicHost);
        Default(OtlpEndpoint, _otelUrl);
        foreach (
            var (key, component) in new[]
            {
                ("ApiStorageEndpoint", "storage"),
                ("ApiRegisterEndpoint", "register"),
                ("ApiProfileEndpoint", "profile"),
                ("ApiAuthenticationEndpoint", "authentication"),
                ("ApiAuthorizationEndpoint", "authorization"),
                ("ApiEventsEndpoint", "events"),
                ("ApiNotificationEndpoint", "notifications"),
                ("ApiCorrespondenceEndpoint", "correspondence"),
                ("ApiAccessManagementEndpoint", "accessmanagement"),
            }
        )
        {
            Default("PlatformSettings__" + key, $"{platform}/{component}/api/v1/");
        }
        Default("PlatformSettings__ApiPdf2Endpoint", Topology.Url("pdf." + Topology.PublicHost, ingressPort) + "/pdf");
        Default(
            "PlatformSettings__ApiWorkflowEngineEndpoint",
            Topology.Url("workflow-engine." + Topology.PublicHost, ingressPort) + "/api/v1/"
        );
        if (signing is not null)
        {
            Default("AccessTokenSettings__AccessTokenSigningCertificateFullPath", signing.FullPath);
            Default("AccessTokenSettings__AccessTokenSigningKeysFolder", signing.KeysFolder);
            Default("AccessTokenSettings__AccessTokenSigningCertificateFileName", signing.FileName);
        }
        return values;
    }
}
