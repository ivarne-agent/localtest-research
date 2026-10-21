#:sdk Aspire.AppHost.Sdk@13.6.2
#:project ../../src/Altinn.App.Aspire.csproj

// The localtest platform (the default stack plus Dialogporten) behind its front door on
// http://local.altinn.cloud:8000, as `studioctl env up -r localtest-v2 --with dialogporten` runs it. No app yet (21e).
//
// Run it with Podman: ASPIRE_CONTAINER_RUNTIME=podman aspire run (or dotnet run apphost.cs), with the studioctl stack
// down (port 8000; the named volumes are shared with studioctl on purpose).
//
// Known gap for apps on this host (21e): the stock YARP front door cannot strip `Secure` from app-lib's keepAlive
// cookie, so an app loses its ten-minute token refresh over http until app-lib sets it from the request (task 22 in the research repository).
using Altinn.App.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAltinnLocaltest().WithGroup("dialogporten");

await builder.Build().RunAsync();
