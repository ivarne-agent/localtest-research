using Altinn.Common.AccessToken.Services;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Localtest.Auth;
using Altinn.Register.Localtest.Controllers;
using Altinn.Register.Localtest.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Register.Localtest;

/// <summary>
/// Localtest composition for the vendored Register host. Called from the single <c>#if LOCALTEST</c> block in
/// <c>RegisterHost.cs</c>, after the production registrations, so it only replaces what must differ locally.
/// </summary>
public static class LocaltestRegisterExtensions
{
    public static WebApplicationBuilder AddLocaltestRegister(this WebApplicationBuilder builder)
    {
        // Lowest-priority defaults; environment variables and appsettings still win.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Altinn:IsTest"] = "true",                       // no MassTransit, import jobs, leases or CCR federation
                ["Altinn:LocalDev"] = "true",                     // service defaults: LOCAL environment
                ["Altinn:register:ApiSource:Default"] = "DB",     // v1 endpoints answer from persistence, not the A2 bridge
                ["GeneralSettings:JwtCookieName"] = "AltinnStudioRuntime",
                ["AccessTokenSettings:AccessTokenHeaderId"] = "PlatformAccessToken",
            },
        });

        var services = builder.Services;
        services.AddOptions<LocaltestRegisterOptions>().BindConfiguration("Localtest:Register");

        // Persistence: in-memory, seeded from testData.json, reached through Core's own unit-of-work manager.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<StaticExternalRoleDefinitionPersistence>();
        services.AddSingleton<IExternalRoleDefinitionPersistence>(sp => sp.GetRequiredService<StaticExternalRoleDefinitionPersistence>());
        services.AddSingleton<RegisterStore>();
        services.AddSingleton<InMemoryPartyPersistence>();
        services.AddSingleton<InMemoryPartyExternalRolePersistence>();
        services.AddUnitOfWorkService<IPartyPersistence>(sp => sp.GetRequiredService<InMemoryPartyPersistence>());
        services.AddUnitOfWorkService<IPartyExternalRolePersistence>(sp => sp.GetRequiredService<InMemoryPartyExternalRolePersistence>());
        services.AddSingleton<IRateLimitProvider, InMemoryRateLimitProvider>();

        // Production code paths that are never taken locally (A2 bridge imports, CCR federation) still have
        // scoped handlers whose dependencies only the skipped MassTransit/CCR blocks register. Startup
        // validation needs them resolvable; actually resolving one is a bug, so they throw.
        services.TryAddSingleton<Altinn.Authorization.ServiceDefaults.MassTransit.ICommandSender>(_ => throw NotAvailable("ICommandSender (MassTransit)"));
        services.TryAddSingleton<Altinn.Register.Core.Ccr.ICcrUpdateFederator>(_ => throw NotAvailable("ICcrUpdateFederator (CCR federation)"));
        services.TryAddSingleton<Altinn.Register.Core.Location.ILocationLookupProvider>(_ => throw NotAvailable("ILocationLookupProvider (CCR import)"));

        // Platform access tokens: no Key Vault locally.
        services.RemoveAll<IPublicSigningKeyProvider>();
        services.AddSingleton<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>();
        var productionHandler = services.FirstOrDefault(d => d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType == typeof(Altinn.Common.AccessToken.AccessTokenHandler));
        if (productionHandler is not null)
        {
            services.Remove(productionHandler);
        }

        services.AddSingleton<IAuthorizationHandler, LocaltestAccessTokenHandler>();

        // Localtest-only controllers. The host uses AddControllersAsServices, so they must be in DI too.
        services.AddControllers().AddApplicationPart(typeof(LocaltestTestUsersController).Assembly);
        services.AddTransient<LocaltestTestUsersController>();

        return builder;
    }

    private static NotSupportedException NotAvailable(string what)
        => new($"{what} is not available in localtest; the vendored code path that asked for it should not run here.");
}
