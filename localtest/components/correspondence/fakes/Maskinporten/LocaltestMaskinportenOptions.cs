namespace Localtest.Shared.Maskinporten;

/// <summary>Where the fake <c>IMaskinportenService</c> sends its requests; bound from <c>Localtest:Maskinporten</c>.</summary>
public sealed class LocaltestMaskinportenOptions
{
    public const string SectionName = "Localtest:Maskinporten";

    /// <summary>The fake Maskinporten's issuer, with its trailing slash; the token endpoint is <c>{Issuer}token</c>.</summary>
    public string Issuer { get; set; } = "http://local.altinn.cloud:8000/localtest/api/maskinporten/";

    /// <summary>Production Authentication's <c>exchange/maskinporten</c>, as the consumer's container reaches it.</summary>
    public string ExchangeEndpoint { get; set; } = "http://local.altinn.cloud:8000/authentication/api/v1/exchange/maskinporten";
}
