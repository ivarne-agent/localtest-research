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

    /// <summary>The platform origin every other component (Storage, Register, Authentication, Dialogporten) is reached through.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";
}
