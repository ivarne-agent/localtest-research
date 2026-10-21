# notifications

Production Altinn Notifications (the orders API host, `vendor/`, see `vendor/UPSTREAM.md`) on the shared Postgres,
without Azure. Apps, Authentication and later Correspondence order notifications at `notifications/api/v1/…` exactly as
against production: orders, order chains, idempotency, recipient resolution through Profile, Register and the PDP,
status, shipments and the 422 problem details are production code with its own repositories and Yuniql migrations. Only
the transport to the Email and SMS hosts is replaced: every e-mail and SMS the production code would put on the Service
Bus is **captured instead, in inboxes**, and then delivered by an in-process responder that applies the production
status machine, so every captured message ends as `Delivered`. `notifications/api/localtest/` shows the inboxes.
Nothing leaves the machine.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (`components/api/src`, `components/shared/src`, the root build files) except two `LOCALTEST` sites, below |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import upstream's root `Directory.Build.props`/`.targets` for the vendored projects, define `LOCALTEST`, compile `fakes/**/*.cs` into the host and embed `fakes/**/*.sql` |
| `fakes/` | Compiled into the host assembly (namespace `Altinn.Notifications.Localtest`): configuration defaults, the capturing publishers, the responder, the instant endpoints, signing keys, the Maskinporten fake, startup scripts, the inspection API |
| `postgres-init/60-notifications.sh` | Postgres init script: `notificationsdb` and the roles upstream's migrations expect (`platform_notifications_admin` superuser, `platform_notifications`), password `Password`. Idempotent; for an existing volume, where Postgres skips init scripts, run it by hand: `podman exec -i localtest-v2-postgres sh < components/notifications/postgres-init/60-notifications.sh` |
| `Dockerfile` | Mirrors upstream's `vendor/components/api/Dockerfile` (pinned Alpine images, layered restore, `tzdata`, `Migration/` and `Views/`, non-root `dotnet`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the running stack: authentication, direct, person, self-identified and organisation recipients, order chains, idempotency, instant orders, cancel, the inboxes, reset |

## Directive sites in `vendor/`

Both in `components/api/src/Altinn.Notifications/Program.cs`:

1. After `SetConfigurationProviders`, a pure addition: `#if LOCALTEST` `AddLocaltestConfiguration(builder.Configuration)`
   `#endif`. `ConfigureServices` reads `GeneralSettings`, `PlatformSettings` and the process loop settings eagerly, so
   the localtest defaults must be in place first. They sit just below the environment variables.
2. In `ConfigureServices`, where `services.AddWolverineServices(...)` is: `#if !LOCALTEST` the production call `#else`
   `AddLocaltestMessaging(services, config)` `#endif`. `AddWolverineServices` throws without a Service Bus connection
   string and starts hosted services that connect, so it cannot be neutralised by configuration or a later `Replace`.

## What is replaced

| Production | Here |
| --- | --- |
| `IEmailCommandPublisher`, `IComposedEmailCommandPublisher`, `ISendSmsPublisher` (Service Bus commands) | `Messaging/CapturingPublishers.cs`: record the message in `localtest.capture` and queue it for the responder |
| The five Wolverine listeners (send results and delivery reports) | `Messaging/LocaltestResponder.cs`: calls `UpdateSendStatus` on the production services, as the production handlers do |
| The Email and SMS hosts' instant endpoints | `Messaging/LocaltestInstantController.cs` serves `notifications/email/api/v1/instantemail` and `notifications/sms/api/v1/instantmessage/send` on this host; the production instant clients and service run unchanged, pointed at `localhost:5090` |
| `IPublicSigningKeyProvider` (Key Vault) | `Auth/`: the shared test certificate validates platform access tokens through the production handler |
| `IMaskinportenService` for the condition check (`SendConditionClient`) | `Maskinporten/`: a copy of `components/shared/fakes/Maskinporten/`, production semantics (token from the fake Maskinporten, exchanged through production Authentication) |
| Settings from the deployment | `LocaltestNotificationsExtensions.AddLocaltestConfiguration`: platform endpoints, 1 task and a 1 s idle delay per process loop (production 30 tasks, 30 s), SMS send window 0 to 24 (production 09 to 17, Europe/Oslo), Dialogporten off |

Unchanged production behaviour worth knowing: Profile, Register and the PDP are called for real (production Profile, the
profile component, answers the four contact point lookups, see `../profile/README.md`); `trigger/*` endpoints are anonymous and
run one loop iteration on demand; Swagger is on at `/swagger` (Development).

## Inboxes and the capture

One inbox per e-mail address or phone number, keyed by the **exact string** a message was addressed to: nothing is
normalised (`Test@Example.com` and `test@example.com` are two inboxes, `87654321` and `+4787654321` two more). What
Notifications does with a number before it gets that far is production behaviour (below).

`notifications/api/localtest/` (anonymous):

| Call | Returns |
| --- | --- |
| `GET inboxes` | `{ items }`: `address`, `channel` (`email`/`sms`), `receiving`, `messageCount`, `createdAt`, `lastMessageAt` |
| `GET inboxes/{address}` | `{ inbox, messages }`, 404 for an address nothing was sent to. Escape `+` as `%2B` |
| `GET messages?orderId=&channel=&after=&limit=` | `{ items, next }` in capture order. `orderId` is the id a v1 order answers, the `shipmentId` of a `future/orders` order, or its `notificationOrderId` (the chain). `after` is a message id, for polling; `limit` 1 to 1000, default 200 |
| `DELETE messages` | 204. Empties the capture only: the inboxes stay (with 0 messages) and no production table is touched |

A message: `id`, `channel`, `kind` (`queued`, `composed`, `instant`), `address`, `recipient`, `sender`, `subject` (e-mail),
`body`, `contentType`, `attachments` (composed e-mail, by reference: filename, MIME type, SAS URL, nothing downloaded),
`orderId`, `orderChainId`, `notificationId`, `creator`, `sendersReference`, `capturedAt`, `outcome` and `outcomeAt`.
The order, creator and outcome are not stored in the capture: they are the production notification's own row, joined in
on read, so the outcome is always what the production status machine says. They are empty until the production code has
written the notification (instant orders write it after the post).

**`receiving` is always `true` in this version.** It is in the data shape (and the `localtest.inbox` table) so the
planned follow-up only adds behaviour: reception switched on or off per inbox, with a default for new inboxes; a message to
an inbox with reception off would fail with a production failure status instead of being delivered. Nothing reads the
flag yet.

### Delivery: everything ends `Delivered`

For each captured message the responder first applies the send result and then a delivery report through the production
services, as the production handlers do: e-mail `Succeeded` (with an operation id), then `Delivered` addressed by that
operation id alone; SMS `Accepted` (with a gateway reference), then `Delivered` by the gateway reference alone. The
pauses are `Localtest:Notifications:ResultDelayMs` (100) and `DeliveryReportDelayMs` (500). Orders become `Completed`
and shipments `Order_Completed` through the production code. No failure simulation yet.

The result must be applied **after the claim transaction commits.** The publisher is called between claiming a
notification (a row locked `FOR UPDATE`) and committing the claim, so a result written from inside the publisher would
wait for a lock its own caller holds; production avoids it because the result comes back through the queue. The
publishers therefore only queue (an in-process channel), and the responder waits until the *committed* row has left
`New` (the claim function sets `Sending`; an instant order's row is written as `Sending` after its post) before it writes
anything, polling every 100 ms for at most `ClaimCommitTimeoutSeconds` (30, then it applies the result anyway and logs a
warning). An uncommitted transaction cannot show that state to another connection, so the wait is the ordering.

## Settings

Bound from `Localtest:Notifications` (environment `Localtest__Notifications__…`):

| Setting | Default | |
| --- | --- | --- |
| `PlatformEndpoint` | `http://local.altinn.cloud:8000/` | The platform origin; Profile, Register, PDP and OpenID endpoints and `GeneralSettings:BaseUri` (response links) derive from it |
| `ResetOnStart` | `false` | Empty the notificationsdb data tables and the capture at start (`fakes/Startup/reset-on-start.sql`). Off by default: orders, notifications and inboxes persist across restarts like Storage's data; `studioctl env reset` removes everything |
| `ResultDelayMs`, `DeliveryReportDelayMs`, `ClaimCommitTimeoutSeconds` | 100, 500, 30 | The responder, above |

## Postgres

`notificationsdb`, created by `postgres-init/60-notifications.sh`. The 104 Yuniql migration folders run unchanged at
every start (`SetUpPostgreSql`, before the host starts; Development resolves the folder relative to the parent of the
working directory, hence `PostgreSQLSettings__MigrationScriptPath=app/Migration` in the image). After them,
`Startup/LocaltestDatabaseStartup` runs `prepare.sql` as `platform_notifications_admin` (the `localtest` schema:
`localtest.inbox` and `localtest.capture`, kept apart from the production `notifications` schema) and, only with
`ResetOnStart`, `reset-on-start.sql`, before any hosted service (the process loops) starts.

## Differences from production

- No Service Bus, Email or SMS host, Azure Communication Services or Link Mobility. Wolverine's retry and dead-letter
  behaviour for results is not exercised; a result is applied once, in process.
- Every message is delivered; there is no failure, bounce or reservation (the seed has no reserved flag).
- Process loops run 1 task each with a 1 s idle delay, and SMS is sent at any hour.
- Profile's contact points come from the test data through Profile's KRR and brreg stand-ins (below,
  `../profile/README.md`), Dialogporten is off, Key Vault and Application Insights are off.
- The vendored `Yuniql` trace logs every migration statement at start (`ConsoleTraceService` is always in debug mode); the
  first start of a fresh volume prints thousands of lines.

## Recipients and the seed

Direct addresses (`recipientEmail`, `recipientSms`, v1 `recipients[]`) need no lookup. Person, organisation and
self-identified recipients are resolved by production code through production Profile (the profile component:
`users/contactpoint/lookup`, `lookupsi`, `units/contactpoint/lookup`, `organizations/notificationaddresses/lookup`).
Profile fills its KRR mirror (persons' e-mail, mobile number, reservation) and its mirror of organisations' official
addresses from the test data through stand-ins for KRR and brreg and its production sync jobs, and returns the addresses
and numbers exactly as stored. Production Notifications then decides what it accepts:

- **SMS numbers are validated.** Direct SMS recipients must be E.164 (`+4799123456`): a bare eight-digit number is
  answered `400`. For contact points from Profile, Notifications adds `+47` itself to a bare eight-digit number that
  starts with 4 or 9, and then validates it with libphonenumber; a number that fails is dropped
  (`Failed_RecipientNotIdentified`, nothing captured). Before schema v3 that was the seed's `87654321` for Sophie Salt
  (1337) and 1002, 1003, 12345 (`+4787654321` is not valid either). Since v3 ([`../../testdata/`](../../testdata/README.md))
  every seeded person has a valid `+47` number and gets both e-mail and SMS: Sophie Salt
  (`1337@altinnstudiotestusers.com`, `+4799001337`), Kari Nordvik (2002, `kari.nordvik@test.com` and `+4791234567`),
  Pengelens Partner (1001, `test@test.com` and `+4799315829`).
- A recipient without any contact point (a national identity number not in the seed) answers `422` with the problem code
  `NOT-00001`.
- **Reserved persons.** A person with `reserved: true` in the test data (the seed: Rik Forelder, 1003, 08829698278)
  comes back from Profile with `isReserved: true` and contact points, which production Profile takes from its KRR mirror. Production
  Notifications accepts the order (`201`; reserved is not missing contact information; a v1 order answers
  `recipientLookup: { status: "Failed", isReserved: ["08829698278"] }`), and the shipment ends with the recipient
  `Email_Failed_RecipientReserved` (destination empty): nothing is captured. With
  `ignoreReservation: true` on the recipient the message is sent as for anyone else.
- **Organisation recipients** get the organisation's official addresses, the seed's `notificationAddresses` (schema v3),
  which Profile seeds once per organisation into its brreg mirror (after that, changes made through Profile's
  `organizations/{orgNumber}/notificationaddresses/mandatory` win):
  DDG Fitness (897069644, `varsling@ddgfitness.no` and `+4799500000`, also for its sub-units through Profile's main-unit
  fallback) and Digitaliseringsdirektoratet (991825827, `varsling@digdir.no` and `+4799500006`). An organisation without
  official addresses (every other seeded one) answers `422` `NOT-00001`: Notifications adds the user-registered contact
  points (the Profile `units/contactpoint/lookup` users, filtered by the PDP for the `resourceId`, which must be written
  `urn:altinn:resource:…`) only while *processing* an order, not when registering it, so registration fails whatever the
  policy says (checked before v3 with DDG Fitness against `altinn_keyrole_access`, which Sophie Salt's DAGL role permits,
  and against the pushed `ttd/testapp` policy).
- **Behaviour change with production Profile (task 14): unit contact points are only self-registered addresses.**
  Production Profile's `units/contactpoint/lookup` returns the addresses users registered for the organisation
  themselves (professional notification settings: an address verified through Profile's verification code, then
  registered for the party, optionally for given resources), not every holder of a role or access package there, which
  the Register component's fake returned before. With the seed alone it returns no users, so an organisation order
  reaches only the official addresses. A tester who wants a person's address on an organisation's orders registers it
  through Arbeidsflate or with curl as that user (`users/current/verification/send` and `verify`, the code is in this
  component's inbox for the address, then `PUT users/current/notificationsettings/parties/{partyUuid}`; the profile
  component's smoke test does exactly this).

## Known gaps

- No per-inbox reception switch (planned, see above), no failure simulation.
- Correspondence (task 7, the optional `correspondence` group) orders through `future/orders` with a condition endpoint on itself; its messages appear in this capture. Not yet run on the stack.
- Attachments of composed e-mail are recorded by reference only.
- The condition-check callback (`conditionEndpoint`) is production code with the Maskinporten fake; checked against the production sink `notifications/api/v1/tests/sendcondition?conditionMet=`. That the Maskinporten token it carries is accepted by a real consumer is not checked.
- The support dashboard and `metrics` endpoints are production code and untested here.

## Running without Docker

```sh
cd vendor/components/api/src/Altinn.Notifications
export DirectoryBuildPropsPath=$PWD/../../../../../localtest.build.props DirectoryBuildTargetsPath=$PWD/../../../../../localtest.build.targets
dotnet build -c Release     # needs the SDK in vendor/global.json (10.0.402), or run from a directory with its own global.json
```

Then run `bin/Release/net10.0/Altinn.Notifications.dll` with `ASPNETCORE_ENVIRONMENT=Development`,
`ASPNETCORE_URLS=http://127.0.0.1:5090` (the instant endpoints are configured as `localhost:5090`),
`PostgreSQLSettings__AdminConnectionString` and `__ConnectionString` (`Host=…;Username=platform_notifications_admin;Password={0};Database=notificationsdb`,
likewise `platform_notifications`), `PostgreSQLSettings__MigrationScriptPath` as an absolute path to
`Altinn.Notifications.Persistence/Migration`, `Localtest__Notifications__PlatformEndpoint` and
`AccessTokenSettings__AccessTokenSigningCertificateFullPath=…/certs/localtest-accesstoken.pfx`.

## Verified

2026-10-21, schema v3 seed (task 18), `env up -r localtest-v2 --dev register` on the `sha-3c9b03f` images:
`smoke-test.sh` 64 of 64, twice, now including Sophie Salt's SMS, the reserved person (accepted,
`Email_Failed_RecipientReserved`, nothing captured; delivered with `ignoreReservation`), DDG Fitness's official e-mail
and SMS, its sub-unit DDG Fitness Oslo through Profile's main-unit fallback, and 422 for an organisation without
addresses.

2026-10-20, on the rebased branch (main a04d0a5) with `studioctl env up -r localtest-v2 --dev notifications --dev register --dev postgres`
after `env reset`, Postgres 18.3:

- `smoke-test.sh`: 55 ok, 0 FAIL, and again 55 ok on a second run against the same data (the 104 Yuniql migrations ran on 18.3
  at first start; the host was healthy in the 76 s `env up`, build included). Covers a service-owner token without the scope
  (403), no token and a bad platform token (401), v1 e-mail and SMS, `links.self` followed, future orders with idempotency
  (201 then 200, same chain), shipments `Order_Completed`/`Email_Delivered`, persons, self-identified users, the 422
  `NOT-00001`, organisation (422), condition endpoint true and false, composed e-mail, instant e-mail and SMS, cancel, reset.
- Responder ordering: a burst of 20 two-recipient orders (40 messages) all ended `Delivered`, every `outcomeAt` after its
  `capturedAt`, and the log has no "applying the result anyway" (claim not committed in 30 s) and no "could not deliver"
  line; over all runs the database holds only `Delivered` and the three expected `Failed_RecipientNotIdentified` (Sophie's
  bare `87654321`), nothing left in `New` or `Sending`.
- Restart: `podman restart` of the container keeps the 40 captured messages and the production tables (`ResetOnStart` is off).
- Memory: `podman stats --no-stream` 133 MB, 5 % CPU idle with the six process loops at a 1 s delay.
- Regressions with the component running: root `smoke-test.sh` 23 ok, `components/register/smoke-test.sh` 24 ok (20 before
  the four contact point checks), `components/resource-registry/smoke-test.sh` 35 ok, `components/events/smoke-test.sh` 22 ok.
