using Digdir.Domain.Dialogporten.Infrastructure.Localtest;
using Digdir.Domain.Dialogporten.WebApi.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Digdir.Domain.Dialogporten.WebApi.Localtest;

/// <summary>
/// The localtest hook in the WebApi's <c>Program.cs</c>, called right after <c>WebApplication.CreateBuilder</c>:
/// configuration defaults now (the host reads its settings eagerly while registering), service replacements once every
/// production registration is in (<see cref="LocaltestOverrides"/>). Everything else runs as in production.
/// </summary>
public static class LocaltestWebApi
{
    public static void Apply(WebApplicationBuilder builder)
    {
        var options = LocaltestConfiguration.BindOptions(builder.Configuration);
        var platform = options.PlatformEndpoint;
        var defaults = new Dictionary<string, string?>(LocaltestConfiguration.JwtBearerSchemes(WebApiSettings.SectionName, platform))
        {
            // The four URLs are NotEmpty-validated and only the Swagger UI and Scalar use them; the ID-porten ones point at
            // the localtest app's fake, which has no logout endpoint.
            ["WebApi:OpenApi:EnableTryItOut"] = "true",
            ["WebApi:OpenApi:IdportenAuthorizationUrl"] = platform + "localtest/api/idporten/authorize",
            ["WebApi:OpenApi:IdportenTokenUrl"] = platform + "localtest/api/idporten/token",
            ["WebApi:OpenApi:IdportenLogoutUrl"] = platform + "localtest/api/idporten/logout",
            ["WebApi:OpenApi:IdportenClientId"] = "localtest",
            ["WebApi:OpenApi:MaskinportenTokenUrl"] = platform + "localtest/api/maskinporten/token",
        };
        LocaltestConfiguration.AddDefaults(builder.Configuration, options, defaults);

        LocaltestOverrides.AfterAllRegistrations(builder.Host, services =>
        {
            LocaltestInfrastructure.Apply(services, builder.Configuration);

            // The metadata of the localtest Authentication and the fake Maskinporten and ID-porten is plain http. Must be a
            // Configure, not a PostConfigure: JwtBearerPostConfigureOptions builds the metadata manager from this value and
            // post-configure callbacks run in registration order, so one added here would run after it.
            // The production TokenIssuerCache reads the same metadata over http through a directive in its own file.
            services.ConfigureAll<JwtBearerOptions>(o => o.RequireHttpsMetadata = false);
        });
    }
}
