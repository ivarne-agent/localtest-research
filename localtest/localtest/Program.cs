using Localtest;
using Localtest.Authentication;
using Localtest.Home;
using Localtest.Home.Instances;
using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Provisioning;
using Localtest.Proxy;
using Localtest.Telemetry;
using Localtest.Tokens;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// The localtest app: the front door of the stack, and home of the localtest features that are not a platform
// component: the proxy (Proxy/), one origin, by default local.altinn.cloud:8000 (Localtest:PlatformEndpoint, published
// onto this app's 8080), split by host (its subdomains pdf., workflow-engine., af., app-frontend.) and
// path prefix onto the platform services by name (service discovery from services__<name>__http__0, no pass-through)
// and onto the apps the runners register, which are provisioned from their own API on registration (Provisioning/);
// a fake ID-porten (IdPorten/) with the test-user picker and a
// fake Maskinporten (Maskinporten/), which production Authentication trusts; the old localtest's start page and
// test helpers (Home/), which sign in and exchange tokens through production Authentication; and a headless
// authorization-code flow for the registered OIDC clients production Authentication is seeded with (Authentication/).
// Forwarding is YARP's IHttpForwarder; the route table is code (PlatformRoutes) and the app and dev-server routes arrive
// over HTTP (RoutesApi), saved on the data volume. Each feature keeps its own API under localtest/api/{feature}/.
var builder = WebApplication.CreateSlimBuilder(args);
// One line per request at Information drowned everything else during a Cypress run; warnings and the start-up lines
// stay. Logging__LogLevel__… in the environment still wins.
builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource
{
    InitialData = new Dictionary<string, string?>
    {
        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Information",
    },
});
// Traces, metrics and logs over OTLP exactly when OTEL_EXPORTER_OTLP_ENDPOINT is set (Telemetry/); nothing otherwise.
FrontDoorTelemetry.Add(builder);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Data element uploads; the nginx ingress allowed the same.
    kestrel.Limits.MaxRequestBodySize = 200 * 1024 * 1024;
});
builder.Services.AddHttpForwarder();
// Settings are read from the built configuration (sp), so a test host's configuration counts too.
static string AppRoutesFile(IServiceProvider sp) => sp.GetRequiredService<IConfiguration>()["Proxy:AppRoutesFile"] ?? "/data/app-routes.json";
builder.Services.AddSingleton(sp => new AppRoutes(AppRoutesFile(sp), sp.GetRequiredService<ILogger<AppRoutes>>()));
builder.Services.AddSingleton(sp => new DevServerRoute(
    Path.Combine(Path.GetDirectoryName(AppRoutesFile(sp)) ?? "/data", "frontend-dev-server.json"),
    sp.GetRequiredService<LocaltestSettings>(),
    sp.GetRequiredService<ILogger<DevServerRoute>>()));
builder.Services.AddSingleton<Forwarder>();
builder.Services.AddHttpClient();
// Every other service by name (http://<service>/...) from services__<service>__http__0: the core and the configuration
// provider only, not the pass-through provider, so a name without an entry fails at once instead of trying DNS (the
// front door answers 503 "not running" for it; planned-components/service-discovery.md sections 3 and 8). Every factory
// client resolves; the forwarder wraps its own handler with the same resolver for the platform routes.
builder.Services.AddServiceDiscoveryCore();
builder.Services.AddConfigurationServiceEndpointProvider();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
builder.Services.AddSingleton(sp => LocaltestSettings.FromConfiguration(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp => IdPortenSettings.FromConfiguration(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<LocaltestSettings>()));
builder.Services.AddKeyedSingleton<SigningKey>(IdPortenSettings.KeyName);
builder.Services.AddSingleton<AuthorizationCodes>();
builder.Services.AddSingleton<TestUsers>();
builder.Services.AddSingleton(sp => MaskinportenSettings.FromConfiguration(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<LocaltestSettings>()));
builder.Services.AddKeyedSingleton<SigningKey>(MaskinportenApi.KeyName);
builder.Services.AddSingleton<AuthenticationClient>();
builder.Services.AddSingleton<StudioToken>();
builder.Services.AddSingleton<TestTokens>();
// The instances page (Home/Instances/): Storage's instance query with a test token, rendered as HTML.
builder.Services.AddSingleton<StorageClient>();
builder.Services.AddSingleton<InstanceLinks>();
builder.Services.AddSingleton<PartyUrns>();
builder.Services.AddSingleton<CodeFlow>();
builder.Services.AddHttpClient(AppProvisioning.PlatformClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<AppProvisioning>();
builder.Services.AddHttpClient(AuthenticationClient.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, LocaltestJsonContext.Default));
Emulation.Add(builder);

var app = builder.Build();
// The public origin and its subdomains now, not at the first request: an unknown subdomain stops the start.
var localtestSettings = app.Services.GetRequiredService<LocaltestSettings>();
Emulation.Map(app);

// The dedicated hosts (the PDF generator, the workflow engine, Arbeidsflate, and the frontend dev server when one is
// registered) belong to their destination entirely, the localtest app's own endpoints included.
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if ((PlatformRoutes.ResolveHost(host, localtestSettings) ?? context.RequestServices.GetRequiredService<DevServerRoute>().Resolve(host)) is { } hostRoute)
    {
        await context.RequestServices.GetRequiredService<Forwarder>().Send(context, hostRoute);
        return;
    }

    await next(context);
});

// The app-frontend bundle built from src/App/frontend (dist mounted at /frontend), served as the apps' asset base.
var frontendRoot = app.Configuration["Proxy:FrontendRoot"] ?? "/frontend";
if (Directory.Exists(frontendRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(frontendRoot),
        RequestPath = "/app-frontend",
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream",
        ContentTypeProvider = new FileExtensionContentTypeProvider(),
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.AccessControlAllowOrigin = "*";
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        },
    });
}

// Routing after the static files: the catch-all forwarder below matches every path, and the static file middleware
// stays out of a request that already has an endpoint.
app.UseRouting();

app.MapGet("/health", () => "ok");
RoutesApi.Map(app);
IdPortenApi.Map(app);
MaskinportenApi.Map(app);
HomeApi.Map(app);
InstancesApi.Map(app);
AuthenticationApi.Map(app);
// Any other localtest/api/… path is the localtest app's own and unknown: its own 404, not the app-runner hint below.
// (The specific feature routes win by route precedence; a plain RequestDelegate, as for the fallback.)
app.Map("/localtest/api/{**rest}", context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync("Unknown localtest API path. The features' APIs are under localtest/api/{apps,routes,idporten,maskinporten,authentication}/.\n");
});
// Everything else is forwarded. An explicit catch-all pattern: MapFallback's default ({*path:nonfile}) would skip
// every path that looks like a file (…/report.pdf, …/main.js). A plain RequestDelegate, because the AOT request
// delegate generator rewrites the typed-lambda overload of MapFallback and loses the pattern.
app.MapFallback("{**path}", context => context.RequestServices.GetRequiredService<Forwarder>().Forward(context));

app.Run();
