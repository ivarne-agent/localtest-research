// Copied from studioctl's provisioning library, localtest/studioctl/src/Runtime/common/dotnet/
// Altinn.Studio.Localtest.Provisioning/src/ (AppId, IAppSource, RunningAppSource, LocaltestProvisioner and its options,
// PlatformHttp, PolicyPlaceholders, ProvisionResult, ProvisioningJsonContext), with the namespace changed, the platform
// called by service name instead of the public origin and the Studio token minted in-process. The library stays for
// studioctl-server, which still provisions until task 27 moves both runtimes onto this front door and deletes it; until
// then a change to the steps belongs in both copies. DirectoryAppSource did not come along: the front door only gets
// the running app's address.
namespace Localtest.Provisioning;

/// <summary>An app's identity on the platform, <c>{org}/{app}</c>, as in its application metadata's <c>id</c>.</summary>
public readonly record struct AppId
{
    private AppId(string org, string app)
    {
        Org = org;
        App = app;
    }

    /// <summary>The owning organisation, the first segment.</summary>
    public string Org { get; }

    /// <summary>The app name, the second segment.</summary>
    public string App { get; }

    /// <summary>Parses <c>{org}/{app}</c>; both segments are required.</summary>
    /// <exception cref="ArgumentException">The value is not <c>{org}/{app}</c>.</exception>
    public static AppId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            throw new ArgumentException($"invalid app id {value}", nameof(value));
        return new AppId(parts[0], parts[1]);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Org}/{App}";
}
