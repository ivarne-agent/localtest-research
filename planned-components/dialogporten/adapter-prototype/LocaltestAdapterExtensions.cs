using Altinn.ApiClients.Maskinporten.Interfaces;
using Altinn.DialogportenAdapter.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>Localtest composition for the vendored adapter host, called from the two LOCALTEST sites in Program.cs.</summary>
public static class LocaltestAdapterExtensions
{
    public static WebApplicationBuilder AddLocaltestAdapter(WebApplicationBuilder builder)
    {
        // Plain http inside the stack: the OpenID metadata of the vendored Authentication is fetched over http.
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.RequireHttpsMetadata = false);
        // Maskinporten: the shared localtest fake (shared-prerequisites strand) instead of *.maskinporten.no.
        builder.Services.RemoveAll<IMaskinportenService>();
        builder.Services.AddSingleton<IMaskinportenService, LocaltestMaskinportenService>();
        return builder;
    }

    public static WebApplication MapLocaltestRoutes(WebApplication app)
    {
        // Storage's localtest handler posts here; the command goes through the production handler and error policies.
        app.MapPost("storage/dialogporten/api/localtest/enqueue", async ([FromBody] SyncInstanceCommand command, [FromServices] IMessageBus bus) =>
        {
            await bus.PublishAsync(command);
            return Results.Accepted();
        }).RequireAuthorization();
        return app;
    }
}
