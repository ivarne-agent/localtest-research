#nullable enable
namespace Altinn.Platform.Authentication.Localtest;

/// <summary>Settings for the localtest Authentication fakes, bound from <c>Localtest:Authentication</c>.</summary>
public sealed class LocaltestAuthenticationOptions
{
    /// <summary>Base URL of the register component's localtest endpoints (test users and organisations).</summary>
    public string RegisterLocaltestEndpoint { get; set; } = "http://local.altinn.cloud:8000/register/api/localtest/";

    /// <summary>The acr the picker preselects when the app did not ask for a level.</summary>
    public string DefaultAcr { get; set; } = "idporten-loa-substantial";
}
