# dialogporten-adapter

The production Altinn Dialogporten adapter (`vendor/`, see `UPSTREAM.md`): it turns Storage's instances into dialogs in
Dialogporten and keeps them in step, so an app's instances show up in the inbox (Arbeidsflate) as they do in production.
Part of the optional `dialogporten` group (`studioctl env up -r localtest-v2 --with dialogporten`), with Dialogporten's
three hosts and Redis (`../dialogporten/`).

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
5. `Program.cs`, three additions: the configuration defaults before `ConfigureDialogportenAdapterServices` (which reads
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
| Azure Monitor, Key Vault | `ApplicationInsights:Enabled=false` (forced; it throws without a connection string), no `kvSetting` (Key Vault is skipped) | Console logs only |

### Configuration defaults

`fakes/LocaltestAdapterExtensions.AddLocaltestConfiguration` derives everything from `Localtest:Adapter:PlatformEndpoint`
(default `http://local.altinn.cloud:8000/`, set by studioctl) and inserts it just below the environment variables, so the
vendored `appsettings*.json` (tt02 and `PopulateFromEnvironmentVariable` values) are overridden while a
`DialogportenAdapter__...` variable still wins:

| Key | Value |
| --- | --- |
| `DialogportenAdapter:Altinn:BaseUri`, `InternalStorageEndpoint`, `InternalRegisterEndpoint` | `{platform}` |
| `DialogportenAdapter:Altinn:SubscriptionKey` | `localtest` (APIM's header, ignored) |
| `DialogportenAdapter:Altinn:AltinnCdn` | not set: upstream's `https://altinncdn.no`, read from the internet as Dialogporten and the Resource Registry do |
| `DialogportenAdapter:Dialogporten:BaseUri` | `{platform}dialogporten` |
| `DialogportenAdapter:Adapter:BaseUri` | `{platform}storage/dialogporten` (public: Arbeidsflate calls the delete action and the receipts) |
| `DialogportenAdapter:Adapter:FeatureFlag:EnableSubmissionTransmissions` | `true` (as tt02: a submission becomes a transmission with the adapter's receipt) |
| `DialogportenAdapter:Authentication:JwtBearerWellKnown` | `{platform}authentication/api/v1/openid/.well-known/openid-configuration` |
| `DialogportenAdapter:Maskinporten:*` | `Environment=test`, `TokenExchangeEnvironment=tt02`, `ClientId=localtest-dialogporten-adapter`, a dummy `EncodedJwk`, production's `Scope`, `ExhangeToAltinnToken=true`, `UseAltinnTestOrg=false` |
| `Localtest:Maskinporten:Issuer`, `ExchangeEndpoint` | `{platform}localtest/api/maskinporten/`, `{platform}authentication/api/v1/exchange/maskinporten` |
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
| `GET /health`, `GET /openapi/v1.json` | Anonymous; no public route (host port 8035) |

No localtest source mints dialog tokens: the smoke test reads one from Dialogporten's end-user API with a code-flow token.

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Transport | Two ASB queues (live and history), 50 listeners, duplicate detection | One in-process sequential local queue fed over HTTP; no history queue (only the migration uses it) |
| Delivery | The outbox row stays until ASB accepts the message | The row is deleted once Storage's local queue has the command; if the adapter is down, Storage logs one line and drops it (re-sync with `syncDialog/simple`, or the instance's next change sends a new one) |
| Error queue | ASB dead-letter queue, inspected by hand | Wolverine's error queue on a non-durable local queue: logged and discarded; scheduled retries are in memory and lost on restart |
| Links | `{org}.apps.{host}`, `platform.{host}` | The one origin (directive sites 3 and 4) |
| Archived instances' go-to link | The platform receipt app at `/receipt/{partyId}/{guid}` | **404** until the receipt group exists (TODO task 16). The adapter's own transmission receipt works |
| Organisation list | altinncdn.no | altinncdn.no too (internet access needed) |
| Telemetry | Azure Monitor | Console only |
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

studioctl's `localtest-v2` runtime starts `localtest-v2-dialogporten-adapter` with the `dialogporten` group, after Storage,
the Register, Authentication and Dialogporten's WebApi, with `Localtest__Adapter__PlatformEndpoint` set to the public
origin, published on host port 8035 (`/health` is polled there) and routed under `/storage/dialogporten` on the platform
host. The proxy matches the longest prefix, so the adapter's paths win over Storage's `/storage`. Without the group there
is no container and no route: `/storage/dialogporten/...` reaches Storage under `/storage` and is a 404, which Storage's
handler takes for "not running" (once the front door answers 503 for components that are not running, TODO task 16, it
is a 503, which the handler takes the same way). Image `ghcr.io/ivarne-agent/localtest-research/localtest-dialogporten-adapter`, override
with `STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_ADAPTER`; `--dev dialogporten-adapter` builds it from this folder.

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

Without the group: no adapter on 8035, the enqueue path a 404 (503 after task 16's front door), an instance created as usual without `dialog.id`, and
(through `podman logs`) exactly one "adapter is not running" line in Storage's log for it and no outbox warning.

## Verified

Phase 1 (2026-10-20, no stack): `dotnet build -c Release` of the WebApi with the overrides and fakes on SDK 10.0.401,
0 warnings, 0 errors. The built host started with `ASPNETCORE_ENVIRONMENT=Development` and a platform endpoint on a closed
port: `/health` 200, `syncDialog` without a token 401, the anonymous enqueue 202, and the command ran from the local
queue into the production handler, which called the Maskinporten fake (connection refused) and went into the production
5xx/`HttpRequestException` retry policy.

Not yet verified (needs the image and the stack): the image build, the runtime in the stack, the Storage handler under
Wolverine inside Storage, and every check of the smoke test.
