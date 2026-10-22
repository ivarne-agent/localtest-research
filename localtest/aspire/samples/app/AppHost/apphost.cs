#:sdk Aspire.AppHost.Sdk@13.6.2
#:project ../../../Altinn.App.Aspire/src/Altinn.App.Aspire.csproj
#:project ../App/App.csproj

// The sample app ttd/testapp (../App) against the localtest platform (the default stack plus Dialogporten) behind its
// front door, the localtest container on http://local.altinn.cloud:8000, as `studioctl env up -r localtest-v2 --with
// dialogporten` and `studioctl run` run it. Once the app is healthy it is registered with the front door, which routes
// it and provisions it from the app's own API (test users, metadata, texts, policy); the dashboard's provision and
// deprovision commands register and deregister it again, and the platform resource's reset command removes the
// containers and the volumes.
//
// Run it with Podman: ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost.cs
// (the tunnel's image does not build on Podman 5.7, see the package README), with no studioctl stack and no other
// AppHost running: the package refuses to start while port 8000 is taken. local.altinn.cloud must resolve to this
// machine (studioctl's hosts entries), since the AppHost registers the app through the front door.
//
// The dashboard reaches the AppHost over https with the ASP.NET Core development certificate; where it is not trusted
// (Linux), trust it (dotnet dev-certs https --trust) or add ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
// ASPNETCORE_URLS=http://localhost:18888 ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:18889
// ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:18887, or the dashboard shows no resources and no commands.
using Altinn.App.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltest().WithDialogporten();
builder.AddAltinnApp<Projects.App>(localtest);

await builder.Build().RunAsync();
