#:sdk Aspire.AppHost.Sdk@13.6.2
#:project ../../../Altinn.App.Aspire/src/Altinn.App.Aspire.csproj
#:project ../App/App.csproj
#:project ../App2/App2.csproj

// The sample apps ttd/testapp and ttd/testapp2 against the localtest platform in minimal mode, as
// `studioctl env up -r localtest-v2 --minimal` runs it: four containers (postgres, storage, workflow-engine and the
// localtest container as the front door on http://local.altinn.cloud:8000) under the resource
// altinn-localtest-minimal; the localtest container emulates Register, Profile, Authentication, Authorization (it
// permits everything), Access Management, Events, the Resource Registry, Notifications and PDF. Nothing persists: the
// containers live for this run, each data path is a tmpfs, so Ctrl-C removes everything and the next start is a clean
// slate (the apps are registered and provisioned again once healthy). There are no optional groups and no reset
// command. The full stack: apphost.cs. What minimal mode does not test: the package README, "Minimal mode".
//
// Run it as apphost.cs (the Podman flags and the dashboard's variables are there):
// ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost-minimal.cs
// (apphost-minimal.run.json runs it in Development, as apphost.run.json does the full stack).
using Altinn.App.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltestMinimal();
builder.AddAltinnApp<Projects.App>(localtest);
builder.AddAltinnApp<Projects.App2>(localtest, "app2");

await builder.Build().RunAsync();
