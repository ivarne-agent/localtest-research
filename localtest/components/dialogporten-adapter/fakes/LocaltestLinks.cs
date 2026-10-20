namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>
/// Storage's <c>selfLinks.platform</c> say <c>https://platform.{host}/storage/...</c>: the prefix is hard-coded in the
/// <c>Altinn.Platform.Storage.Interface</c> package, built from Storage's <c>GeneralSettings:Hostname</c>. The stack has
/// no <c>platform.</c> sub-domain; its platform is the one origin of <c>DialogportenAdapter:Altinn:BaseUri</c>.
/// </summary>
internal static class LocaltestLinks
{
    public static string ToLocalPlatformUrl(string? platformUrl, Settings settings)
    {
        if (string.IsNullOrEmpty(platformUrl))
        {
            return platformUrl ?? string.Empty;
        }

        var altinn = settings.DialogportenAdapter.Altinn;
        var productionPrefix = $"https://platform.{altinn.BaseUri.Host}/";
        return platformUrl.StartsWith(productionPrefix, StringComparison.OrdinalIgnoreCase)
            ? $"{altinn.GetPlatformUri().ToString().TrimEnd('/')}/{platformUrl[productionPrefix.Length..]}"
            : platformUrl;
    }
}
