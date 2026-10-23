namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>
/// Settings for the localtest additions, bound from <c>Localtest:Adapter</c>. They only feed the configuration defaults
/// (<see cref="LocaltestAdapterExtensions.AddLocaltestConfiguration"/>); each default can still be overridden by the
/// production setting it produces (for instance <c>DialogportenAdapter__Dialogporten__BaseUri</c>), because the defaults
/// sit below the environment variables.
/// </summary>
public sealed class LocaltestAdapterOptions
{
    public const string SectionName = "Localtest:Adapter";

    /// <summary>
    /// The public origin: the links in the dialogs and the OpenID metadata derive from it. Storage, the Register,
    /// Authentication and Dialogporten are called by service name.
    /// </summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";
}
