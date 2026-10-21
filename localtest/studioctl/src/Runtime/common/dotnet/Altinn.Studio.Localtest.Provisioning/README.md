# Altinn.Studio.Localtest.Provisioning

Deploys an Altinn app to the localtest platform the way Altinn Studio deploys one. It is the one implementation of
the steps in [`localtest/README.md`, "What the app runner pushes"](../../../../../../README.md#what-the-app-runner-pushes),
shared by studioctl-server (`src/cli/studioctl-server/Provisioning/AppProvisioner.cs`) and the planned Aspire hosting
package ([`planned-components/aspire/aspire.md`](../../../../../../../planned-components/aspire/aspire.md) section 4).

net10.0. It depends only on `HttpClient`, `System.Text.Json` (source-generated) and
`Microsoft.Extensions.Logging.Abstractions`, and is marked `IsAotCompatible`. The build is standalone like
`Altinn.Studio.Runtime.Common`: its own `Directory.Build.props`, `Directory.Packages.props` and `global.json`, so
nothing above this folder is imported.

| Path | What |
| --- | --- |
| `src/` | The library |
| `tests/` | xunit v3 tests; `tests/TestApps/frontend-test/App/` is a fixture copy of a real app's `config/` (see its README) |

## Shape

| Type | What |
| --- | --- |
| `AppId` | `{org}/{app}`, `AppId.Parse("ttd/frontend-test")` |
| `IAppSource` | `Read(appId, ct)` returns `AppArtifacts`: application metadata (required), the policy with its placeholders, `testData.json`, and the texts by language. A missing optional file means "none" |
| `DirectoryAppSource(appRoot)` | Reads `App/config/applicationmetadata.json`, `App/config/authorization/policy.xml`, every `App/config/texts/resource.{lang}.json` and `App/wwwroot/testData.json`. `appRoot` is the app repository or its `App/` folder. `HasApp`, and `ReadAppId(ct)` from the metadata's `id`. The app does not need to be running |
| `RunningAppSource(httpClient, baseUri)` | Reads the same through the running app's API under `{baseUri}/{org}/{app}/`: `api/v1/applicationmetadata?checkOrgApp=false`, `api/v1/meta/authorizationpolicy`, `testData.json`, `api/v1/applicationlanguages` and `api/v1/texts/{lang}`. A 404 means "none" |
| `AppSources.DirectoryOrRunningApp(appRoot, httpClient, baseUri)` | The folder when it holds the app, otherwise the running app |
| `LocaltestProvisionerOptions` | `PlatformUri` (required, for example `http://local.altinn.cloud:8000/`); `StudioToken`, a `Func<CancellationToken, Task<string>>` (default: `GET {PlatformUri}/Home/GetTestStudioToken` once per provisioning); `DialogportenRunning`, a `Func<bool>` asked before step 5 (default: never) |
| `LocaltestProvisioner(options, httpClient, logger)` | `Provision(appId, source, ct)` runs steps 1 to 5 and returns a `ProvisionResult` (text count, policy, test data, the `DialogportenSync` outcome). `Deprovision(appId, ct)` deletes the app's test users from the Register and refreshes Access Management. `SyncDialogporten(reason, ct)` runs step 5 alone |

Every step is idempotent: running `Provision` again replaces what the previous run pushed (Storage is created or
replaced). A failing step throws `HttpRequestException` with the method, URI, status and the start of the body, and
leaves the later steps undone. Step 5 never throws: a failed sync is logged once as a warning, returned as
`DialogportenSync.Failed`, and should be retried by the caller (studioctl-server retries on its next refresh). One
instance serves one platform, and its calls are not meant to run concurrently. The `HttpClient` is not disposed by the
library.

The log lines are those studioctl-server has always written (`Provisioned {AppId} from {Source}: metadata, ...`,
`Dialogporten sync for {Reason} skipped: ...`, `Dialogporten synced the Resource Registry for {Reason}: ...`,
`Removed {AppId}'s test users from the platform`); the caller picks the logger category.

## Callers

studioctl-server discovers an app by its address only, so it passes a `RunningAppSource`, reads the Dialogporten group
from its route table, and keeps its own reconcile loop (which apps are new, moved or gone, and the retry of a missed
Dialogporten sync). It references the library as a project: the server is published self-contained without trimming or
AOT, so one extra assembly costs nothing, while the Aspire package references the same project.

The Aspire package knows the app's project folder and which groups it added, so it would provision from disk once the
app is ready:

```csharp
var provisioner = new LocaltestProvisioner(
    new LocaltestProvisionerOptions
    {
        PlatformUri = new Uri("http://local.altinn.cloud:8000/"),
        DialogportenRunning = () => dialogportenAdded,
    },
    httpClient,
    logger
);

app.OnResourceReady(async (resource, _, ct) =>
{
    var source = new DirectoryAppSource(appProjectFolder);
    await provisioner.Provision(await source.ReadAppId(ct), source, ct);
});
```

## Build and test

```sh
dotnet build -c Release src
dotnet test tests
```

Both also run from `src/cli` (`make test` builds `studioctl.slnx`, which includes both projects).
