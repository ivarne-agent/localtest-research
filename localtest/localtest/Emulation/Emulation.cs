using Localtest.Emulated;
using Localtest.Home;
using Localtest.IdPorten;
using Localtest.Provisioning;
using Localtest.Proxy;

namespace Localtest;

/// <summary>
/// The minimal mode (<c>planned-components/minimal-emulation/README.md</c>): the front door emulates every platform
/// service whose <c>services__{name}__http__0</c> is its own address (<see cref="EmulationMode"/>), in memory, under the
/// service's public prefix, ahead of the forwarder. With no such entry (the default stack) <see cref="Add"/> registers
/// nothing and <see cref="Map"/> maps nothing. Program.cs calls the two; everything else of the mode is in this folder
/// (README.md here), apart from the seams it plugs into: <see cref="IProvisioningTarget"/>, <see cref="ITestUserSource"/>,
/// <see cref="IInProcessAuthentication"/>, <see cref="HomeBanner"/> and <see cref="IEmulatedServices"/>.
/// </summary>
public static class Emulation
{
    /// <summary>The response header that marks every emulated answer, with the service's name.</summary>
    public const string Header = "X-Localtest-Emulated";

    /// <summary>Where the start page's banner points.</summary>
    public const string DocsLink = "https://github.com/ivarne-agent/localtest-research/blob/main/localtest/localtest/Emulation/README.md";

    /// <summary>
    /// Registers the emulated services' stores and the in-process seams when any service is emulated. Reads the
    /// configuration the builder has (the runtime's environment), so the mode is fixed at start.
    /// </summary>
    /// <exception cref="InvalidOperationException">A service without an emulation has the front door's address.</exception>
    public static void Add(WebApplicationBuilder builder)
    {
        if (EmulationMode.FromConfiguration(builder.Configuration) is not { } mode)
        {
            return;
        }

        var services = builder.Services;
        services.AddSingleton(mode);
        services.AddSingleton<IEmulatedServices>(new EmulatedServices(mode));
        services.AddSingleton(new HomeBanner(BannerText(mode), DocsLink));
        services.AddSingleton<TestDataStore>();
        services.AddSingleton<PolicyStore>();
        services.AddSingleton<TokenMinter>();
        services.AddSingleton<AuthenticationEmulation>();
        services.AddSingleton<RegisterEmulation>();
        services.AddSingleton<EventLog>();
        services.AddSingleton<NotificationLog>();
        services.AddSingleton<DelegationLog>();
        services.AddSingleton<IProvisioningTarget, InProcessProvisioning>();
        if (mode.Emulates(RegisterEmulation.Service))
        {
            services.AddSingleton<ITestUserSource, InProcessTestUsers>();
        }

        if (mode.Emulates(AuthenticationEmulation.Service))
        {
            services.AddSingleton<IInProcessAuthentication, InProcessAuthentication>();
        }

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Add(EmulationJsonContext.Default));
    }

    /// <summary>
    /// Maps each emulated service under its public prefix, with a 501 for any other path under it, and marks every
    /// emulated answer with <see cref="Header"/>. Called before the front door's host routing, so the PDF host is
    /// answered here when pdf3 is emulated.
    /// </summary>
    public static void Map(WebApplication app)
    {
        var mode = app.Services.GetService<EmulationMode>();
        if (mode is null)
        {
            // The builder saw no emulated service; the built configuration must agree, or a self-address would loop.
            if (EmulationMode.FromConfiguration(app.Configuration) is { } late)
            {
                throw new InvalidOperationException(
                    $"services__<name>__http__0 names the front door itself ({late.SelfAddress}) only in configuration added after the builder was "
                    + "created; the minimal mode is decided from the builder's configuration (the environment or the command line).");
            }

            return;
        }

        var prefixes = PlatformRoutes.Table.Where(r => r.Prefix is not null).GroupBy(r => r.Service).ToDictionary(g => g.Key, g => g.First().Prefix!);
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (string.Equals(host, PlatformRoutes.Hosts.Pdf, StringComparison.OrdinalIgnoreCase))
            {
                if (mode.Emulates(PdfEmulation.Service))
                {
                    context.Response.Headers[Header] = PdfEmulation.Service;
                    await PdfEmulation.Handle(context);
                    return;
                }
            }
            else if (PlatformRoutes.ResolveHost(host) is null
                && context.RequestServices.GetRequiredService<DevServerRoute>().Resolve(host) is null
                && PlatformRoutes.Resolve(context.Request.Path.Value ?? "/") is { Name: var service }
                && mode.Emulates(service))
            {
                context.Response.Headers[Header] = service;
            }

            await next(context);
        });

        foreach (var service in mode.Services)
        {
            if (!prefixes.TryGetValue(service, out var prefix))
            {
                continue; // pdf3: its own host, answered above.
            }

            switch (service)
            {
                case RegisterEmulation.Service:
                    RegisterEmulation.Map(app, prefix);
                    break;
                case ProfileEmulation.Service:
                    ProfileEmulation.Map(app, prefix);
                    break;
                case AuthenticationEmulation.Service:
                    AuthenticationEmulation.Map(app.MapGroup(prefix + "/api/v1"));
                    break;
                case AuthorizationEmulation.Service:
                    AuthorizationEmulation.Map(app, prefix);
                    break;
                case AccessManagementEmulation.Service:
                    AccessManagementEmulation.Map(app, prefix);
                    break;
                case EventsEmulation.Service:
                    EventsEmulation.Map(app, prefix);
                    break;
                case NotificationsEmulation.Service:
                    NotificationsEmulation.Map(app, prefix);
                    break;
                case ResourceRegistryEmulation.Service:
                    // Its own catch-all, with its own message.
                    ResourceRegistryEmulation.Map(app, prefix);
                    continue;
            }

            var name = service;
            app.Map(prefix + "/{**rest}", (HttpRequest request) => EmulationResults.NotEmulated(name, request));
        }
    }

    /// <summary>The start page's notice: the spec's banner text, listing the emulated services.</summary>
    private static string BannerText(EmulationMode mode)
    {
        var names = mode.Services.Where(s => s != ResourceRegistryEmulation.Service).Select(s => s switch
        {
            "register" => "Register",
            "profile" => "Profile",
            "authentication" => "Authentication",
            "authorization" => "Authorization (permits everything)",
            "access-management" => "Access Management",
            "events" => "Events",
            "notifications" => "Notifications",
            "pdf3" => "PDF",
            _ => s,
        }).ToList();
        var list = names.Count > 1 ? string.Join(", ", names[..^1]) + " and " + names[^1] : string.Concat(names);
        return $"Minimal mode: {list} {(names.Count == 1 ? "is" : "are")} emulated. Do not report platform bugs from this mode.";
    }

    private sealed class EmulatedServices(EmulationMode mode) : IEmulatedServices
    {
        public bool Emulates(string service) => mode.Emulates(service);
    }
}
