using Digdir.Domain.Dialogporten.GraphQL.Common;
using Digdir.Domain.Dialogporten.GraphQL.Common.Authentication;
using Digdir.Domain.Dialogporten.GraphQL.Localtest.Authentication;
using Digdir.Domain.Dialogporten.Infrastructure.Localtest;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Digdir.Domain.Dialogporten.GraphQL.Localtest;

/// <summary>
/// The localtest hook in the GraphQL host's <c>Program.cs</c>, called right after <c>WebApplication.CreateBuilder</c>.
/// Same shape as the WebApi's: configuration defaults now, service replacements after every production registration.
/// </summary>
public static class LocaltestGraphQl
{
    /// <summary>The origin of Arbeidsflate (task 11), the browser client of the subscription stream. Required, at least one.</summary>
    private const string ArbeidsflateOrigin = "http://af.local.altinn.cloud:8000";

    public static void Apply(WebApplicationBuilder builder)
    {
        var options = LocaltestConfiguration.BindOptions(builder.Configuration);
        var defaults = new Dictionary<string, string?>(LocaltestConfiguration.JwtBearerSchemes(GraphQlSettings.SectionName, options.PlatformEndpoint))
        {
            ["GraphQl:Cors:AllowedOrigins:0"] = ArbeidsflateOrigin,
        };
        LocaltestConfiguration.AddDefaults(builder.Configuration, options, defaults);

        LocaltestOverrides.AfterAllRegistrations(builder.Host, services =>
        {
            LocaltestInfrastructure.Apply(services, builder.Configuration);

            // See LocaltestWebApi: http metadata, set with Configure so it is in place before the post-configure step.
            services.ConfigureAll<JwtBearerOptions>(o => o.RequireHttpsMetadata = false);

            services.RemoveAll<ITokenIssuerCache>();
            services.AddSingleton<ITokenIssuerCache, LocaltestTokenIssuerCache>();
        });
    }
}
