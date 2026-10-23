namespace Localtest.Shared.Maskinporten;

/// <summary>Where the fake <c>IMaskinportenService</c> sends its requests; bound from <c>Localtest:Maskinporten</c>.</summary>
public sealed class LocaltestMaskinportenOptions
{
    public const string SectionName = "Localtest:Maskinporten";

    /// <summary>
    /// The fake Maskinporten's token endpoint in the localtest app, by its service name. The token's <c>iss</c> is the
    /// localtest app's own (public) issuer, whatever address was called.
    /// </summary>
    public string TokenEndpoint { get; set; } = "http://localtest/localtest/api/maskinporten/token";

    /// <summary>Production Authentication's <c>exchange/maskinporten</c>, by its service name.</summary>
    public string ExchangeEndpoint { get; set; } = "http://authentication/authentication/api/v1/exchange/maskinporten";
}
