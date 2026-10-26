# dialogporten-adapter

The production Altinn Dialogporten adapter (`vendor/`, see `UPSTREAM.md`): it turns Storage's instances into dialogs in
Dialogporten and keeps them in step, so an app's instances show up in the inbox (Arbeidsflate) as they do in production.
A root of the optional `dialogporten` group (`studioctl env up -r localtest-v2 --with dialogporten`, or `--with everything`),
with Dialogporten's three hosts and Redis (`../dialogporten/`). `--with correspondence` and `--with arbeidsflate` do not
start it: groups close over `dependsOn` ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-manifest)), and nothing depends
on the adapter.

How an instance becomes a dialog: Storage writes an outbox row with every instance change (production code), its outbox
service publishes a `SyncInstanceCommand` to Wolverine, and in `Development` a localtest handler in Storage's fakes posts
it to this adapter's anonymous `POST storage/dialogporten/api/localtest/enqueue` (`../storage/README.md`, "Dialogporten
outbox"). The endpoint puts the command on the adapter's local Wolverine queue, where the production handler, sync
service and retry policies process it as they process an Azure Service Bus message in production: read the instance, its
events, the application and its texts from Storage, the parties from the Register, the service owner from altinncdn.no,
create or update the dialog in Dialogporten, and write `dialog.id` back into the instance's data values.

| Path | What |
| --- | --- |
| `vendor/` | `Altinn/altinn-dialogporten-adapter` at `86364b8`: the WebApi host and the Contracts project, unmodified except four `#if !LOCALTEST` sites and three additive `#if LOCALTEST` hooks (listed in `vendor/UPSTREAM.md`, with the re-pin recipe) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import upstream's `Directory.Build.props` (net10.0, nullable, `AnalysisMode=Recommended`), define `LOCALTEST`, turn warnings-as-errors off, compile `fakes/` into the host |
| `fakes/` | Compiled into the host (namespace `Altinn.DialogportenAdapter.WebApi.Localtest`): configuration defaults, the enqueue endpoint, the link helper, and `Maskinporten/`, an unchanged copy of `../shared/fakes/Maskinporten/` |
| `.editorconfig` | Silences two analyzer rules (`CA1716`, `CA5351`) for the Maskinporten copy only, which upstream's `Recommended` analysis flags; the copy stays byte-equal to the shared one |
| `Dockerfile` | Mirrors upstream's `vendor/Dockerfile` (same pinned SDK and runtime images, `dotnet build -c Release`, uid/gid 3000, `/tmp/logtelemetry`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the stack, with the group (`./smoke-test.sh`) and without it (`./smoke-test.sh --off`); see [Smoke test](#smoke-test) |

## Directive sites

All in `vendor/src/Altinn.DialogportenAdapter.WebApi/`, lines in `vendor/UPSTREAM.md`:

1. `Common/Extensions/ServiceCollectionExtensions.cs`, the Wolverine bootstrap: `ConfigureAdapterDefaults` (Azure Service
   Bus, throws on an empty connection string) under `#if !LOCALTEST`, `#else` its three settings that are not about the
   transport. Every error policy below it is unchanged.
2. The same file, the two Service Bus listeners: `#else` one **sequential** local queue under the production queue name
   (`altinn.dialogportenadapter.webapi`) and the publish rule that sends `SyncInstanceCommand` to it. Sequential, so two
   syncs of one instance never race into a 412.
3. `Settings.cs`, `GetAppUriForOrg` / `GetPlatformUri`: production builds `https://{org}.apps.{host}/{org}/{app}` and
   `https://platform.{host}` (dropping the port); `#else` both are the one origin of `DialogportenAdapter:Altinn:BaseUri`,
   `http://local.altinn.cloud:8000`, port kept. Every link in a dialog (go-to, copy, read API, receipt, attachments through
   `authentication?goto=`) goes through these two.
4. `Features/Command/Sync/StorageDialogportenDataMerger.cs`, `element.SelfLinks.Platform` in the attachment and the
   transmission-attachment factory: Storage hard-codes `https://platform.{host}/storage/...` in its Interface package;
   `#else` `LocaltestLinks.ToLocalPlatformUrl` puts the same path on the stack's origin.
5. `Program.cs`, four additions: service discovery right after `CreateBuilder` (task 25; since task 40 `LocaltestServiceDiscovery.AddLocaltestServiceDiscovery` from `fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`, without pass-through: a name without an entry fails at once, a real host is used as it is; [`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)), the configuration defaults before `ConfigureDialogportenAdapterServices` (which reads
   the settings eagerly), the service replacements after it (the Maskinporten fake must replace the package's `TryAdd`),
   and the localtest routes after the production ones.

Nothing in the sync logic, the merger's content, the receipt or the delete flow is changed.

## What is faked, and why

| Production | Localtest | Why |
| --- | --- | --- |
| Azure Service Bus queues `altinn.dialogportenadapter.webapi` (and `-history`), 50 listeners, duplicate detection | Wolverine's in-process local queue, one listener, fed by `POST storage/dialogporten/api/localtest/enqueue` (`fakes/LocaltestAdapterExtensions.cs`), anonymous like every `api/localtest/` endpoint, `202` | No Azure in the stack. The endpoint publishes to the same queue the production handler listens on, so the handler, its timeouts and every retry and error policy run unchanged; the alternative, Storage posting to the production `syncDialog`, would bypass them |
| `IMaskinportenService` against `maskinporten.no`, exchanged to an Altinn token | `fakes/Maskinporten/`, the shared fake: an unsigned assertion to the localtest app's Maskinporten token endpoint, then production Authentication's `exchange/maskinporten` | The package hard-codes the Maskinporten hosts. Same rule as Dialogporten (task 4): `?test=true` only when `UseAltinnTestOrg` is true, which it is not here, and the client id `localtest-dialogporten-adapter` is not an organisation number, so the token is issued for Digdir (991825827) and the exchanged token names **`digdir`**. The adapter acts as Digdir towards Storage (scope `altinn:storage/instances.syncadapter`), the Register (`altinn:register/partylookup.admin`) and Dialogporten (`digdir:dialogporten.serviceprovider[.admin|.legacyhtml]`), with production's full scope list |
| `{org}.apps.{host}` and `platform.{host}` sub-domains | One origin (directive sites 3 and 4) | The stack serves everything on `local.altinn.cloud:8000` |
| OpenID metadata over https | `RequireHttpsMetadata=false` on the JWT bearer (`Configure<JwtBearerOptions>`: the framework's own post-configure rejects an http address before a later `PostConfigure` runs) | The stack's Authentication is plain http |
| Azure Monitor, Key Vault | Azure Monitor under `#if !LOCALTEST`, OTLP in the `#else` (see "Telemetry (OTLP)"); no `kvSetting` (Key Vault is skipped) | Console logs; OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set |

### Configuration defaults

`fakes/LocaltestAdapterExtensions.AddLocaltestConfiguration` derives the public values from `Localtest:Adapter:PlatformEndpoint`
(default `http://local.altinn.cloud:8000/`, set by studioctl), names the services it calls by service name (task 40), and
inserts it all just below the environment variables, so the
vendored `appsettings*.json` (tt02 and `PopulateFromEnvironmentVariable` values) are overridden while a
`DialogportenAdapter__...` variable still wins:

| Key | Value |
| --- | --- |
| `DialogportenAdapter:Altinn:BaseUri` | `{platform}` (public: the links in the dialogs) |
| `DialogportenAdapter:Altinn:InternalStorageEndpoint`, `InternalRegisterEndpoint` | `http://storage/`, `http://register/` (the Refit paths carry the prefixes) |
| `DialogportenAdapter:Altinn:SubscriptionKey` | `localtest` (APIM's header, ignored) |
| `DialogportenAdapter:Altinn:AltinnCdn` | not set: upstream's `https://altinncdn.no`, read from the internet as Dialogporten and the Resource Registry do |
| `DialogportenAdapter:Dialogporten:BaseUri` | `http://dialogporten-webapi/dialogporten` |
| `DialogportenAdapter:Adapter:BaseUri` | `{platform}storage/dialogporten` (public: Arbeidsflate calls the delete action and the receipts) |
| `DialogportenAdapter:Adapter:FeatureFlag:EnableSubmissionTransmissions` | `true` (as tt02: a submission becomes a transmission with the adapter's receipt) |
| `DialogportenAdapter:Authentication:JwtBearerWellKnown` | `{platform}authentication/api/v1/openid/.well-known/openid-configuration` |
| `DialogportenAdapter:Maskinporten:*` | `Environment=test`, `TokenExchangeEnvironment=tt02`, `ClientId=localtest-dialogporten-adapter`, a dummy `EncodedJwk`, production's `Scope`, `ExhangeToAltinnToken=true`, `UseAltinnTestOrg=false` |
| `Localtest:Maskinporten:TokenEndpoint`, `ExchangeEndpoint` | not set: the fake's defaults, `http://localtest/localtest/api/maskinporten/token`, `http://authentication/authentication/api/v1/exchange/maskinporten` |
| `WolverineSettings:ServiceBusConnectionString` | `unused-in-localtest` (bound by `Settings`, read only behind `#if !LOCALTEST`) |
| `Logging:LogLevel:Altinn.DialogportenAdapter` | `Information` (upstream `Warning`): each sync, skip and no-op, and each enqueue |

The image runs with `ASPNETCORE_ENVIRONMENT=Development`. In the adapter that only selects `appsettings.Development.json`
(overridden by the defaults above) and the optional `appsettings.local.json`; its own mocks (`LocalDevelopment:MockDialogportenApi`,
`DisableAuth`) need a `LocalDevelopment` section, which nothing sets, so real authentication and the real Dialogporten are used.

## Endpoints and their authentication

| Endpoint | Authentication |
| --- | --- |
| `POST storage/dialogporten/api/localtest/enqueue` (localtest) | Anonymous. Body: `{ appId, partyId, instanceId, instanceCreatedAt, isMigration }`; `202` once queued |
| `POST storage/dialogporten/api/v1/syncDialog`, `POST .../syncDialog/simple/{partyId}/{instanceGuid}` | Production: a JWT from the stack's Authentication with the scope `altinn:storage/instances.syncadapter` (`/Home/GetTestOrgToken?orgNumber=991825827&scopes=altinn:storage/instances.syncadapter`). The manual re-sync of one instance, for one changed while the group was off |
| `DELETE storage/dialogporten/api/v1/instance/{partyId}/{instanceGuid}` | Production: a dialog token from Dialogporten with the `delete` action (Arbeidsflate's delete button). Soft-deletes the instance in Storage; the outbox then deletes the dialog |
| `GET storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}?lang=` | Production: a dialog token for that dialog. The adapter renders the receipt as Markdown from Dialogporten, Storage, the Register and the CDN |
| `GET /health`, `GET /openapi/v1.json` | Anonymous; no public route (`dialogporten-adapter:5011` inside the stack, no host port) |

No localtest source mints dialog tokens: the smoke test reads one from Dialogporten's end-user API with a code-flow token.

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Transport | Two ASB queues (live and history), 50 listeners, duplicate detection | One in-process sequential local queue fed over HTTP; no history queue (only the migration uses it) |
| Delivery | The outbox row stays until ASB accepts the message | The row is deleted once Storage's local queue has the command; if the adapter is down, Storage logs one line and drops it (re-sync with `syncDialog/simple`, or the instance's next change sends a new one) |
| Error queue | ASB dead-letter queue, inspected by hand | Wolverine's error queue on a non-durable local queue: logged and discarded; scheduled retries are in memory and lost on restart |
| Links | `{org}.apps.{host}`, `platform.{host}` | The one origin (directive sites 3 and 4) |
| Archived instances' go-to link | The platform receipt app at `/receipt/{partyId}/{guid}` | The same link, served by the optional `receipt` group (`--with receipt`, `../receipt/`). Without that group the front door answers `503` "Component not running" (problem details, or an HTML page for a browser) naming the group and the `--with receipt` command. The adapter's own transmission receipt works either way |
| Organisation list | altinncdn.no | altinncdn.no too (internet access needed) |
| Telemetry | Azure Monitor, 5 % of traces | Console; all three signals over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, every trace |
| Outbox delays | Storage batches low-priority events for 60 s, high-priority ones for 5 s | 2 s and 0 s (see below) |

### Known pain point: `ttd` and Digdir's organisation number

`ttd` has no organisation number. Production lets it borrow Digdir's (991825827): the adapter maps `ttd` to `digdir` to
name the service owner (`StorageDialogportenDataMerger`, upstream #280), the Resource Registry lists `ttd` under
991825827, and the adapter acts as `digdir`. Dialogporten names a service-owner caller's organisation by the first short
name the registry lists for that number (`digdir`), while a dialog for a `ttd` app carries the org `ttd`. The result: a
service owner's **search** (`GET dialogporten/api/v1/serviceowner/dialogs?...`) does not find the dialogs the adapter
made for `ttd` apps; reading one by id works. This is production behaviour, not something the stack fixes (TODO task 10);
testers will meet it, and the smoke test reads dialogs by the id the adapter writes into the instance (`dialog.id`).

## Where localtest is faster than production

Storage's outbox delays are configuration in Storage's fakes: `WolverineSettings:LowPriorityDelaySecs=2` (production 60;
saves, process steps) and `HighPriorityDelaySecs=0` (production 5; other events), `UrgentPriorityDelaySecs` 0 as in
production (create, delete). A tester sees that a sync is not instant, but does not wait a minute. The row is in
`../../README.md`, "Where localtest is faster than production".

## In the stack

The runtime starts the `dialogporten-adapter` container (studioctl: `localtest-v2-dialogporten-adapter`) with the
`dialogporten` group, after Storage, the Register, Authentication and Dialogporten's WebApi, with
`Localtest__Adapter__PlatformEndpoint` set to the public origin. It listens on 5011 (no host port; its `/health` is
checked from inside the stack's network) and the front door routes `/storage/dialogporten` to it by
`services__dialogporten-adapter__http__0`, before Storage's `/storage`. Without the group there is no container and no
`services__` address: `/storage/dialogporten/...` answers 503 problem details ("Component not
running", `component: dialogporten-adapter`, `group: dialogporten`), which Storage's handler takes for "not running".
Image `ghcr.io/ivarne-agent/localtest-research/localtest-dialogporten-adapter` at the manifest's pin;
`--dev-dialogporten-adapter` builds it from this folder.

## Smoke test

```
./smoke-test.sh [BASE]           # the stack with --with dialogporten
./smoke-test.sh --off [BASE]     # the default stack
```

With the group: the adapter's health; the `ttd/testapp` fixture pushed as the runner does and Dialogporten's sync;
`syncDialog` 401 without a token and 403 with a user token, the enqueue endpoint anonymous (400 for a malformed body);
an instance for party 500000 created by user 1337 → `dataValues["dialog.id"]` within 10 s, the dialog with
`urn:altinn:resource:app_ttd_testapp`, the owner's party urn, the go-to action through `authentication?goto=` to the app
on the stack's origin and the delete action on the adapter's prefix; process start and an upload → the attachment, its
URL on the stack's origin, a new revision; process end with a `Submited` event → a new status and a transmission whose
receipt is the adapter's; the code-flow token for user 1337 → the end-user dialog's `dialogToken` → the receipt 200
`text/markdown` (401 without a token or with a user token); `syncDialog/simple` twice 204 with the revision unchanged;
the delete with the dialog token 204 → Storage `isSoftDeleted` → the dialog's `deletedAt` within 10 s.

Without the group: no adapter container, the enqueue path a 503 from the front door naming the component and group, an instance created as usual without `dialog.id`, and
(through `podman logs`) exactly one "adapter is not running" line in Storage's log for it and no outbox warning.

## Memory

`podman stats --no-stream` (2026-10-20, right after the smoke test): `dialogporten-adapter` 261 MB. With the three Dialogporten
hosts and Redis (318, 148, 177 and 7 MB at the same moment) the group takes about 0.9 GB.

## Telemetry (OTLP)

The stack's rule (TODO.md task 53): traces, metrics and logs go over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc`, the .NET default, or `http/protobuf`) and `OTEL_SERVICE_NAME`; without it nothing is exported and no export error is logged. A per-signal endpoint alone does not turn it on. Directive site `Common/Extensions/ServiceCollectionExtensions.cs:56-65`: production's `ConfigureTelemetry` (`UseAzureMonitor`, which brings its own ASP.NET Core and HttpClient instrumentation and throws without a connection string) under `#if !LOCALTEST`, `#else` `fakes/LocaltestAdapterTelemetry.cs`: when the variable is set, the same pipeline without Azure Monitor (the `HealthCheckFilterProcessor`, `service.name` `platform-dialogporten-adapter`, ASP.NET Core and HttpClient traces and metrics) plus `LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`, a copy of `../shared/fakes/Otlp/`, which adds the logs); nothing otherwise. `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3, the OpenTelemetry version `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 resolves, in `localtest.build.targets`; `.editorconfig` silences CA1716 for the copy as for the other shared copies. Production samples 5 % of traces; here the SDK default (everything) applies. The fakes no longer force `ApplicationInsights:Enabled=false`.

`dotnet build -c Release` of the host with the overrides: 0 errors, no new warnings (2026-10-26). The host was run without Docker against a fake OTLP/HTTP collector (2026-10-26): with the variable and `http/protobuf`, `/v1/traces`, `/v1/metrics` and `/v1/logs` arrived named `platform-dialogporten-adapter`; without it nothing arrived and the log has no exporter line. The `GET /health` span is exported although the health filter is meant to drop it: the filter sets `IsAllDataRequested = false` in `OnEnd`, which the OTLP exporter does not check (it exports what is `Recorded`); whether Azure Monitor drops it in production was not checked. Not yet verified in the image: rebuild it (one image build at a time), bump the tag with `../../tools/bump-tag.sh`, and add the smoke check (with the variable pointing at a throwaway collector, a request creates a span with the right `service.name`; without it the container log shows no export errors).

## Verified (2026-10-20, task 10)

Stack: `studioctl env up -r localtest-v2 --with dialogporten --dev dialogporten --dev dialogporten-adapter --dev storage`
(the rest at the pinned `sha-a3f62a8`), images built from this branch.

- `./smoke-test.sh`: 39 checks, all pass (three runs; the last on 2026-10-21 with the final Storage image). The instance's dialog was there within the 10 s; after the
  submission its status went from `Draft` to `Awaiting` with one transmission; the receipt answered 200 `text/markdown`
  with the end user's dialog token; the delete with the token soft-deleted the instance and the dialog had `deletedAt`
  within 10 s.
- `./smoke-test.sh --off` on the default stack (group off, `--dev storage`): 7 checks, all pass. The front door answered
  503 problem details with `component: dialogporten-adapter` and `group: dialogporten`, the instance got no `dialog.id`, and
  Storage logged exactly one line for it: `Dialogporten sync of instance 500000/{guid} dropped: the Dialogporten adapter is
  not running (the dialogporten group is off?): 503 from http://local.altinn.cloud:8000/storage/dialogporten/api/localtest/enqueue`.
- Regressions with the group on (2026-10-21, same images): `../events/smoke-test.sh` 22 of 22 (run first), `../dialogporten/smoke-test.sh`
  60 of 60, this smoke test 39 of 39, the root `../../smoke-test.sh` 23 of 23; no outbox warning in Storage's log, nothing in the
  adapter's error queue. `../storage/smoke-test.sh` was not run: it needs ports 8000 and 5433 free (the stack down).
- On the default stack (group off, outbox on, `--dev storage`), 2026-10-21: `./smoke-test.sh --off` 7 of 7 and
  `../../run-platform-regression.sh --no-apps` (apps started with `studioctl run --dev-frontend`, the monorepo's built
  `src/App/frontend/dist` served on port 8080 with `Access-Control-Allow-Origin: *`): 44 of 44 Cypress tests in the six specs, no retries.
  The static server needs that CORS header: the app loads the bundle as a cross-origin module script, and without it every page renders
  empty (the first attempt failed every spec for that reason).
- Found on the way, and fixed:
  - Dialogporten refused every dialog the adapter made with 400 `'Url' is not a well-formatted HTTPS URL.` (GUI actions,
    the API action): production's validators require https and the stack is http. Fixed with one `#if !LOCALTEST` site in
    Dialogporten's Application project (`../dialogporten/README.md`, "Directive sites"), not in the adapter, so the dialogs
    carry the real local links.
  - Storage's first handler run logged a JasperFx warning ("Utilizing service location ...") because the client was a typed
    `HttpClient`; it is now a singleton over a named client, and the default stack's log has no outbox warning.
  - The smoke test's own instance events must carry `processInfo` as the app's do: an `EndTask` event without it makes the
    adapter's merger throw (`NullReferenceException` in `FindCreatedForDateElements`), which Wolverine moves to the error
    queue after the production policy. Production apps always send it.
- Production behaviour the smoke test asserts as such: the receipt and the delete endpoint bind the `Authorization` header
  as a required parameter, so without one they answer 400, not 401; a token that is not a valid dialog token gives 401.
- Order matters the other way too: run straight after `../dialogporten/smoke-test.sh`, whose last step deletes every dialog and
  clears Dialogporten's caches, the first service-owner `GET dialogs/{id}` of this test answered 404 once (2026-10-21, the
  coordinator's run; the next four checks failed with it); the rerun on its own passed 39 of 39. Run this test first, or wait a
  few seconds after the Dialogporten one.
- Order matters for `../events/smoke-test.sh`: right after this smoke test, Dialogporten's events for the test's dialog
  (`dialogporten.dialog.updated.v1`, `...transmission.created.v1`, `...deleted.v1` and others, six in all) arrive in the
  Events component a few seconds later, after its reset, and its "stored once" check fails; run on its own it passes 22 of 22.

Not verified: Arbeidsflate (task 11) using the links, attachments opened through `authentication?goto=` in a browser,
and an archived instance's go-to link followed to the receipt (the `receipt` group serves it since task 16; no test
chains the two).

Superseded 2026-10-21: the published image is pinned (`sha-3c9b03f`, proven with `--with everything`; since task 27 the
pin is in the manifest, `sha-6d928ad` on 2026-10-23), so `--with dialogporten` no longer needs `--dev dialogporten-adapter`.
