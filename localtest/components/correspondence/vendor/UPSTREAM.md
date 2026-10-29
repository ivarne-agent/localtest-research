# Vendored copy of Altinn/altinn-correspondence

```json vendor-sync
{
  "repository": "Altinn/altinn-correspondence",
  "branch": "main",
  "commit": "d2dc9123cd07523a35ba25b5ff9adb4c068f125e",
  "date": "2026-10-22",
  "paths": ["src", "Dockerfile", "docker-compose.yml"],
  "group": null,
  "local": "directives",
  "mirrors": [
    { "upstream": "Dockerfile", "ours": "Dockerfile", "merge": true }
  ],
  "watch": [],
  "projects": [
    "src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj"
  ],
  "follow": [
    {
      "package": "OpenTelemetry.Exporter.OpenTelemetryProtocol",
      "follows": "OpenTelemetry"
    }
  ],
  "reminders": [
    "grep the new tree for cookies written with a hard-coded `Secure` or `SameSite=None` besides `ExpireCookie` (its directive is the only local change for cookies since task 63): `grep -rn 'Secure = true\\|SameSiteMode.None\\|Set-Cookie' vendor/src`",
    "`ConfigureAuthentication` must still name the correlation cookie and the callback path the fakes and the smoke test rely on",
    "update the EF migration count `smoke-test.sh` expects when `src/Altinn.Correspondence.Persistence/Migrations` changed"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-correspondence
- Commit: [`d2dc9123cd07523a35ba25b5ff9adb4c068f125e`](https://github.com/Altinn/altinn-correspondence/tree/d2dc9123cd07523a35ba25b5ff9adb4c068f125e) (main, 2026-10-22)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/` (the six projects `Altinn.Correspondence.{API,Application,Common,Core,Integrations,Persistence}`
  with their `Properties/` and the 69 EF migrations), the root `Dockerfile` (the reference for `../Dockerfile`) and `docker-compose.yml`
  (reference only, nothing runs it). Upstream has no `Directory.Build.props`/`.targets`, no `Directory.Packages.props`, no `global.json`
  and no lock files. Not copied: `Test/`, `flux/`, the Postman collection, the READMEs, `start.bat`, `version.txt`, `renovate.json`, the
  solution file.
- Copied with `git archive` of that commit, no subtree. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST ... #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                | Lines   | Kind        | First line of the `LOCALTEST` branch                                                         |
| ------------------------------------------------------------------- | ------- | ----------- | -------------------------------------------------------------------------------------------- |
| `src/Altinn.Correspondence.API/Auth/DependencyInjection.cs`         | 149-156 | replacement | `Secure = false,`                                                                            |
| `src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs`      | 26-31   | replacement | `new HttpDocumentRetriever { RequireHttps = false });`                                       |
| `src/Altinn.Correspondence.API/Helpers/RecurringJobRegistration.cs` | 20-28   | replacement | `recurringJobManager.RemoveIfExists("Update IP restrictions to apimIp and current ...`       |
| `src/Altinn.Correspondence.API/Program.cs`                          | 44-49   | addition    | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery....`                    |
| `src/Altinn.Correspondence.API/Program.cs`                          | 56-58   | addition    | `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(...`      |
| `src/Altinn.Correspondence.API/Program.cs`                          | 66-78   | addition    | `if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))`      |
| `src/Altinn.Correspondence.API/Program.cs`                          | 186-188 | addition    | `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, ...` |

<!-- vendor-sync:end sites -->

Four pure additions, all in `src/Altinn.Correspondence.API/Program.cs`, and three `#if !LOCALTEST` / `#else` / `#endif` blocks, in
`src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs` (task 11b),
`src/Altinn.Correspondence.API/Helpers/RecurringJobRegistration.cs` and `src/Altinn.Correspondence.API/Auth/DependencyInjection.cs`
(both task 63). Task 7b's block in
`src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs` (the party-id branch of
`CreateResourceCategory`, any positive 64-bit integer for the test data's 6-digit ids) was removed in task 58's follow-up
(2026-10-27): upstream #2157 (`d2dc912`) passes `urn:altinn:partyid:{id}` from `CreateDecisionRequest` and
`CreateUserContactPointDecisionRequest`, option (c) of `planned-components/correspondence/party-ids.md`.

| Lines | Adds | Why there |
| --- | --- | --- |
| `Program.cs`, right after `var builder = WebApplication.CreateBuilder(args);` | the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`) | before every registration, so every factory client resolves service names |
| `Program.cs`, right after the `.AddEnvironmentVariables();` that ends the `builder.Configuration` chain | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(builder.Configuration);` `#endif` | `Program.cs` adds the JSON files and the environment variables a second time, so the localtest defaults have to be inserted after that chain (just below its environment source, above `appsettings.Development.json`) and before `ConfigureServices`, which reads the settings eagerly |
| `Program.cs`, right after `services.ConfigureHangfire(config);` in `ConfigureServices` | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, config);` `#endif` | after every production registration (the last but Kestrel's), so `RemoveAll`/`Add` win, and after `ConfigureAuthentication`, so the JWT and OIDC `Configure` actions run after upstream's |
| `Program.cs`, right after `builder.Services.ConfigureOpenTelemetry(generalSettings);` | `#if LOCALTEST` the inlined OTLP rule (task 63, in place of a copy of `components/shared/fakes/Otlp/`): with `OTEL_EXPORTER_OTLP_ENDPOINT` set, `UseOtlpExporter()` for traces, metrics and logs from the standard variables, the environment detector after the host's own resource and the `System.Net.Http.HttpClient.Otlp` log category at Warning; nothing otherwise (TODO.md task 53) | The pipeline is built in the Integrations project (`OpenTelemetry/DependencyInjection.cs`), which the fakes are not compiled into; this is the first place in the API host after it. A pure addition, not a `#if !LOCALTEST`/`#else`: production's call stays, and adds no Azure Monitor exporter here because the fakes force `GeneralSettings:ApplicationInsightsConnectionString` empty |
| `IdportenTokenValidator.cs`, the document retriever of `ValidateTokenAsync`'s `ConfigurationManager` | `#if !LOCALTEST` upstream's `new HttpDocumentRetriever());` `#else` a `// localtest:` comment and `new HttpDocumentRetriever { RequireHttps = false });` `#endif` | The browser download of an attachment (an inbox link, no token) ends in the cascade's OIDC branch, which validates the access token it cached at the callback against the discovery document and JWKS at `{Authority}/.well-known/openid-configuration`, Authentication's on `http://local.altinn.cloud:8000`. The retriever's `RequireHttps` defaults to true, so `GetConfigurationAsync` threw, the cascade fell through to a new challenge, and Authentication's live session answered with a new code at once: a redirect loop. The fakes' `RequireHttpsMetadata = false` does not reach this retriever (built here, not from the scheme's options), and the validator is a concrete class injected by type, so no fake can replace it. Reported first in `../../arbeidsflate/README.md`, "Correspondence attachments (11b)" |
| `Auth/DependencyInjection.cs`, `ExpireCookie` in the OIDC scheme's `OnRemoteFailure` | `#if !LOCALTEST` upstream's `Secure = true,` `SameSite = SameSiteMode.None,` `#else` a `// localtest:` comment, `Secure = false,` `SameSite = SameSiteMode.Lax,` `#endif` | Over http a browser ignores the retry path's expiry of the correlation and nonce cookies otherwise. Replaced Correspondence's copy of the shared cookie policy (`fakes/Cookies/`, task 21b) in task 63; Authentication keeps its copy (twelve sites) |
| `RecurringJobRegistration.cs`, `Register`, the daily IP restrictions job | `#if !LOCALTEST` upstream's `AddOrUpdate<IpSecurityRestrictionUpdater>(...)` `#else` a `// localtest:` comment and `RemoveIfExists` for the same job id `#endif` | No container app and no Azure Resource Manager: the job failed ten retries on `DefaultAzureCredential` daily. Replaced the fake `LocaltestResourceManager` (task 63); upstream's `AzureResourceManagerService` stays, its storage account deployment skipped in Development |

Considered and not used (see the plan, section 6.2): the Redis connect (`Program.cs`, configuration only: the group's Redis), the
Development-only migrations and the recurring jobs (both in `Program.cs`; task 63 then took the IP restrictions job out in
`RecurringJobRegistration.cs`). For task 11b: the OIDC scheme's correlation
cookie (`DependencyInjection.cs`, `SecurePolicy=Always`, `SameSite=None`) and the nonce cookie, set in the fakes after
upstream's configuration instead (`../fakes/Auth/LocaltestAuthentication.cs`). For task 21b: the retry path's `ExpireCookie`
(`DependencyInjection.cs`, hard-coded `Secure`, `SameSite=None`), handled by a copy of the shared cookie policy until task 63
made it the directive above. A fake that a 3-4 line directive can replace becomes the directive (the user's rule of
2026-10-29, TODO.md task 63).

## Syncing

`python3 localtest/tools/vendor-sync.py sync correspondence` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `Dockerfile` (the six-project restore layer, publish, port 2525, the `dotnet` user,
`/mnt/storage`), and every deviation is marked `localtest:`; the tool three-way merges upstream's changes into it when
that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a different build. Its two
pinned base-image digests move to what `10.0` points at then.

What the localtest code follows: `IStorageRepository`, `IResourceManager`, the scheme names and
`ConfigureAuthentication`, the configuration keys of `../fakes/LocaltestCorrespondence.cs`, the entities and the
Hangfire schema the inspection API reads, and the migration count the smoke test expects.

The `#if !LOCALTEST` branch is checked against upstream by the tool. The validator's block goes when upstream
builds the retriever from the scheme's options (its `RequireHttpsMetadata`, which the fakes set to false) or reads
`options.Configuration`'s discovery; the smoke test's browser-download section proves the http metadata is still read.
