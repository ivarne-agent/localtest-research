namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>Storage's selfLinks.platform say https://platform.{host}/storage/… (hard-coded in Altinn.Platform.Storage.Interface); locally the platform is {Altinn:BaseUri}/storage/….</summary>
internal static class LocaltestLinks
{
    public static string ToLocalPlatformUrl(string? platformUrl, Settings settings)
    {
        if (string.IsNullOrEmpty(platformUrl)) return platformUrl ?? string.Empty;
        var prefix = $"https://platform.{settings.DialogportenAdapter.Altinn.BaseUri.Host}/";
        return platformUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? settings.DialogportenAdapter.Altinn.BaseUri.ToString().TrimEnd('/') + "/" + platformUrl[prefix.Length..]
            : platformUrl;
    }
}
