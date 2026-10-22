using Localtest;
using Localtest.Authentication;
using Localtest.Home;
using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Proxy;
using Localtest.Tokens;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

// The localtest app: the front door of the stack, and home of the localtest features that are not a platform
// component: the proxy (Proxy/), one origin, local.altinn.cloud:8000, split by host and path prefix onto the platform
// components and the apps the app runner has registered; a fake ID-porten (IdPorten/) with the test-user picker and a
// fake Maskinporten (Maskinporten/), which production Authentication trusts; the old localtest's start page and
// test helpers (Home/), which sign in and exchange tokens through production Authentication; and a headless
// authorization-code flow for the registered OIDC clients production Authentication is seeded with (Authentication/).
// Forwarding is YARP's IHttpForwarder; the route table is code (PlatformRoutes) and the app routes arrive over HTTP
// (AppRoutes), so nothing reads files or reloads. Each feature keeps its own API under localtest/api/{feature}/.
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
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Data element uploads; the nginx ingress allowed the same.
    kestrel.Limits.MaxRequestBodySize = 200 * 1024 * 1024;
});
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton(PlatformRoutes.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(sp => new AppRoutes(
    builder.Configuration["Proxy:AppRoutesFile"] ?? "/data/app-routes.json",
    sp.GetRequiredService<ILogger<AppRoutes>>()));
builder.Services.AddSingleton<Forwarder>();
builder.Services.AddHttpClient();
// Other components by name (http://<service>/...) from services__<service>__http__0; names without an entry pass
// through to DNS (planned-components/service-discovery.md). The forwarder's own invoker does not resolve names.
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
var localtest = LocaltestSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(localtest);
builder.Services.AddSingleton(IdPortenSettings.FromConfiguration(builder.Configuration, localtest));
builder.Services.AddKeyedSingleton<SigningKey>(IdPortenSettings.KeyName);
builder.Services.AddSingleton<AuthorizationCodes>();
builder.Services.AddSingleton<TestUsers>();
builder.Services.AddSingleton(MaskinportenSettings.FromConfiguration(builder.Configuration, localtest));
builder.Services.AddKeyedSingleton<SigningKey>(MaskinportenApi.KeyName);
builder.Services.AddSingleton<AuthenticationClient>();
builder.Services.AddSingleton<CodeFlow>();
builder.Services.AddHttpClient(AuthenticationClient.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, LocaltestJsonContext.Default));

var app = builder.Build();

// The PDF generator's and the workflow engine's host names belong to them entirely, own endpoints included.
app.Use(async (context, next) =>
{
    if (context.RequestServices.GetRequiredService<PlatformRoutes>().ResolveHost(context.Request.Host.Host) is { } hostRoute)
    {
        await context.RequestServices.GetRequiredService<Forwarder>().Send(context, hostRoute);
        return;
    }

    await next(context);
});

// The app-frontend bundle built from src/App/frontend (dist mounted at /frontend), served as the apps' asset base.
var frontendRoot = builder.Configuration["Proxy:FrontendRoot"] ?? "/frontend";
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
AppRoutesApi.Map(app);
IdPortenApi.Map(app);
MaskinportenApi.Map(app);
HomeApi.Map(app);
AuthenticationApi.Map(app);
// Any other localtest/api/… path is the localtest app's own and unknown: its own 404, not the app-runner hint below.
// (The specific feature routes win by route precedence; a plain RequestDelegate, as for the fallback.)
app.Map("/localtest/api/{**rest}", context =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync("Unknown localtest API path. The features' APIs are under localtest/api/{proxy,idporten,maskinporten,authentication}/.\n");
});
// Everything else is forwarded. An explicit catch-all pattern: MapFallback's default ({*path:nonfile}) would skip
// every path that looks like a file (…/report.pdf, …/main.js). A plain RequestDelegate, because the AOT request
// delegate generator rewrites the typed-lambda overload of MapFallback and loses the pattern.
app.MapFallback("{**path}", context => context.RequestServices.GetRequiredService<Forwarder>().Forward(context));

app.Run();
