using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// The configuration defaults of a localtest host: the settings upstream's <c>appsettings.Development.json</c> carries
/// "TODO: Add to local secrets" or at23 values for, plus the fourteen <c>LocalDevelopment</c> flags, which default to
/// <c>true</c> and are set explicitly here so no flag depends on a default or a development file.
/// </summary>
public static class LocaltestConfiguration
{
    /// <summary>
    /// Upstream's development scope list, the one the production clients ask for: the events publish scopes (the vendored
    /// Events short-circuits the PDP for the admin scope), Register's party lookup, the PDP and Access Management's
    /// authorized parties.
    /// </summary>
    private const string MaskinportenScope =
        "altinn:events.publish altinn:events.publish.admin altinn:register/partylookup.admin altinn:authorization/authorize.admin altinn:accessmanagement/authorizedparties.admin";

    /// <summary>
    /// The dialog-token key pairs, test-only like the shared certificate (generated with openssl, see the component
    /// README). Base64url of the raw 32-byte Ed25519 seed and public key. The Dialogporten JWKS publishes the public
    /// halves, and the GraphQL host validates dialog tokens against them.
    /// </summary>
    private static readonly (string Kid, string Private, string Public) PrimaryKey =
        ("localtest-primary", "YTY1c-1T_IK_gSeyThx4wzHDKJI87LSdHgpZS9CXiCc", "Xg0SfHY0oNB6M3om3bY6jJ6JFE16gP4JY6mQeL-1FR4");

    private static readonly (string Kid, string Private, string Public) SecondaryKey =
        ("localtest-secondary", "f99ScXcAgl6XGG9MUIN3nRXnR0kYNlXsdDO-amjKrWc", "wzuM7xyqsySfrhbWomQh_cfa3HETBjleagfPZQwLva8");

    public static LocaltestDialogportenOptions BindOptions(IConfiguration configuration)
    {
        var options = new LocaltestDialogportenOptions();
        configuration.GetSection(LocaltestDialogportenOptions.SectionName).Bind(options);
        options.PlatformEndpoint = options.PlatformEndpoint.TrimEnd('/') + "/";
        return options;
    }

    /// <summary>
    /// Inserts the shared defaults and <paramref name="hostSpecific"/> above the JSON files and below the environment
    /// variables, so a single value can still be overridden per container.
    /// </summary>
    public static void AddDefaults(
        ConfigurationManager configuration,
        LocaltestDialogportenOptions options,
        IReadOnlyDictionary<string, string?> hostSpecific)
    {
        var platform = options.PlatformEndpoint;
        var data = new Dictionary<string, string?>
        {
            [$"{LocaltestDialogportenOptions.SectionName}:PlatformEndpoint"] = platform,

            ["Infrastructure:DialogDbConnectionString"] =
                $"Host={options.DatabaseHost};Port={options.DatabasePort};Database={options.DatabaseName};Username={options.DatabaseUser};Password={options.DatabasePassword};Include Error Detail=True",
            ["Infrastructure:Redis:ConnectionString"] = options.RedisConnectionString,
            ["Infrastructure:Altinn:BaseUri"] = platform,
            ["Infrastructure:Altinn:EventsBaseUri"] = platform,
            ["Infrastructure:Altinn:SubscriptionKey"] = "localtest",

            // All four are NotEmpty-validated; the fake IMaskinportenService never reads the JWK. A client id that is not
            // an organisation number makes the token endpoint issue for Digdir, which is who Dialogporten acts as.
            ["Infrastructure:Maskinporten:Environment"] = "test",
            ["Infrastructure:Maskinporten:ClientId"] = "localtest-dialogporten",
            ["Infrastructure:Maskinporten:Scope"] = MaskinportenScope,
            ["Infrastructure:Maskinporten:EncodedJwk"] = Convert.ToBase64String("""{"kty":"RSA"}"""u8),
            // Production semantics: no Altinn test organisation, so the exchanged token names digdir, not ttd.
            ["Infrastructure:Maskinporten:UseAltinnTestOrg"] = "false",
            ["Localtest:Maskinporten:Issuer"] = platform + "localtest/api/maskinporten/",
            ["Localtest:Maskinporten:ExchangeEndpoint"] = platform + "authentication/api/v1/exchange/maskinporten",

            // Warmup runs optional phases against the Resource Registry and an end-user search for a synthetic pid.
            ["Infrastructure:Warmup:Enabled"] = "false",

            ["Application:Dialogporten:BaseUri"] = platform + "dialogporten",
            ["Application:Dialogporten:Ed25519KeyPairs:Primary:Kid"] = PrimaryKey.Kid,
            ["Application:Dialogporten:Ed25519KeyPairs:Primary:PrivateComponent"] = PrimaryKey.Private,
            ["Application:Dialogporten:Ed25519KeyPairs:Primary:PublicComponent"] = PrimaryKey.Public,
            ["Application:Dialogporten:Ed25519KeyPairs:Secondary:Kid"] = SecondaryKey.Kid,
            ["Application:Dialogporten:Ed25519KeyPairs:Secondary:PrivateComponent"] = SecondaryKey.Private,
            ["Application:Dialogporten:Ed25519KeyPairs:Secondary:PublicComponent"] = SecondaryKey.Public,

            // Upstream hosts default to Warning; Information shows each request and the clients' calls to the platform.
            ["Serilog:MinimumLevel:Default"] = "Information",

            // Every flag explicit (they default to true and the development files set most of them to true). Real
            // authentication and authorization, the production Resource Registry, name and party lookups, event
            // publishing and dialog-token signing; only the bus transport stays in-process, as in upstream's own
            // development setup, and the two startup syncs stay off (the app runner triggers them instead).
            ["LocalDevelopment:UseLocalDevelopmentUser"] = "false",
            ["LocalDevelopment:DisableAuth"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentAltinnAuthorization"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentResourceRegister"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentCloudEventBus"] = "false",
            ["LocalDevelopment:DisableCache"] = "true",
            ["LocalDevelopment:UseLocalDevelopmentNameRegister"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentPartyNameRegistry"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentOrganizationRegister"] = "false",
            ["LocalDevelopment:UseLocalDevelopmentCompactJwsGenerator"] = "false",
            ["LocalDevelopment:UseInMemoryServiceBusTransport"] = "true",
            ["LocalDevelopment:DisableSubjectResourceSyncOnStartup"] = "true",
            ["LocalDevelopment:DisablePolicyInformationSyncOnStartup"] = "true",
            ["LocalDevelopment:UseLocalMetricsAggregationStorage"] = "false",
        };
        foreach (var (key, value) in hostSpecific)
        {
            data[key] = value;
        }

        var lastEnvironmentSource = configuration.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        configuration.Sources.Insert(
            lastEnvironmentSource < 0 ? configuration.Sources.Count : lastEnvironmentSource,
            new MemoryConfigurationSource { InitialData = data });
    }

    /// <summary>
    /// The JWT bearer schemes of the WebApi and GraphQL hosts, under <paramref name="section"/> (<c>WebApi</c> or
    /// <c>GraphQl</c>). Upstream's development file carries three (Maskinporten, Altinn, Idporten, all https) and arrays
    /// merge by index, so all three are set: Maskinporten and ID-porten are the localtest app's fakes, and the Altinn
    /// scheme is the stack's Authentication, whose tokens the smoke test and apps use.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> JwtBearerSchemes(string section, string platform)
    {
        string[] names = ["Maskinporten", "Altinn", "Idporten"];
        string[] wellKnown =
        [
            platform + "localtest/api/maskinporten/.well-known/oauth-authorization-server",
            platform + "authentication/api/v1/openid/.well-known/openid-configuration",
            platform + "localtest/api/idporten/.well-known/openid-configuration",
        ];
        for (var i = 0; i < names.Length; i++)
        {
            yield return new($"{section}:Authentication:JwtBearerTokenSchemas:{i}:Name", names[i]);
            yield return new($"{section}:Authentication:JwtBearerTokenSchemas:{i}:WellKnown", wellKnown[i]);
        }
    }
}
