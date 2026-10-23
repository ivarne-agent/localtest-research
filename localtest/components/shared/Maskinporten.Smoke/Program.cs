using System.IdentityModel.Tokens.Jwt;
using Altinn.ApiClients.Maskinporten.Config;
using Altinn.ApiClients.Maskinporten.Extensions;
using Altinn.ApiClients.Maskinporten.Interfaces;
using Altinn.ApiClients.Maskinporten.Services;
using Localtest.Shared.Maskinporten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Smoke test of the IMaskinportenService fake against the running stack:
//   dotnet run -- <base url> <UseAltinnTestOrg: true|false> <expected urn:altinn:org>
var baseUrl = args.ElementAtOrDefault(0) ?? "http://local.altinn.cloud:8000";
var useTestOrg = bool.Parse(args.ElementAtOrDefault(1) ?? "true");
var expectedOrg = args.ElementAtOrDefault(2) ?? (useTestOrg ? "ttd" : "digdir");

var builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Localtest:Maskinporten:TokenEndpoint"] = baseUrl + "/localtest/api/maskinporten/token",
    ["Localtest:Maskinporten:ExchangeEndpoint"] = baseUrl + "/authentication/api/v1/exchange/maskinporten",
});

// What a consumer such as the Dialogporten adapter registers: a settings-based JWK client definition.
var settings = new MaskinportenSettings
{
    ClientId = "991825827",
    Scope = "altinn:serviceowner/instances.read",
    Environment = "test",
    TokenExchangeEnvironment = "at23",
    EncodedJwk = "eyJrdHkiOiJSU0EifQ==",
    ExhangeToAltinnToken = true,
    UseAltinnTestOrg = useTestOrg,
};
builder.Services.RegisterMaskinportenClientDefinition<SettingsJwkClientDefinition>("smoke", settings);
builder.Services.AddLocaltestMaskinporten(builder.Configuration);

using var host = builder.Build();
var service = host.Services.GetRequiredService<IMaskinportenService>();
var definition = new SettingsJwkClientDefinition { ClientSettings = settings };

var exchanged = await service.GetToken(definition);
var maskinportenToken = await GetMaskinportenToken(host.Services, definition);

var maskinporten = Claims(maskinportenToken);
var altinn = Claims(exchanged.AccessToken);
Console.WriteLine($"UseAltinnTestOrg={useTestOrg}");
Console.WriteLine($"Maskinporten token claims: {string.Join(", ", maskinporten.Select(c => $"{c.Key}={c.Value}"))}");
Console.WriteLine($"Altinn token claims:       {string.Join(", ", altinn.Select(c => $"{c.Key}={c.Value}"))}");

var failures = new List<string>();
Check("Maskinporten token consumer.ID", "0192:991825827", ConsumerId(maskinportenToken));
Check("Altinn token urn:altinn:org", expectedOrg, altinn.GetValueOrDefault("urn:altinn:org"));
Check("Altinn token type", "altinn", exchanged.TokenType);

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("ok");
return 0;

void Check(string name, string? expected, string? actual)
{
    if (expected != actual)
    {
        failures.Add($"{name}: expected {expected}, got {actual}");
    }
}

static string? ConsumerId(string jwt)
{
    var consumer = new JwtSecurityTokenHandler().ReadJwtToken(jwt).Payload["consumer"]?.ToString();
    return consumer is null ? null : System.Text.Json.JsonDocument.Parse(consumer).RootElement.GetProperty("ID").GetString();
}

static Dictionary<string, string> Claims(string jwt)
    => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims
        .GroupBy(claim => claim.Type)
        .ToDictionary(group => group.Key, group => string.Join("|", group.Select(claim => claim.Value)));

// The exchange wraps the Maskinporten token; ask for it without the exchange to print its claims.
static async Task<string> GetMaskinportenToken(IServiceProvider services, SettingsJwkClientDefinition definition)
{
    var withoutExchange = new SettingsJwkClientDefinition
    {
        ClientSettings = new MaskinportenSettings
        {
            ClientId = definition.ClientSettings.ClientId,
            Scope = definition.ClientSettings.Scope,
            Environment = definition.ClientSettings.Environment,
        },
    };
    return (await services.GetRequiredService<IMaskinportenService>().GetToken(withoutExchange)).AccessToken;
}
