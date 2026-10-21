# profile

Production Altinn Profile (`vendor/`, see `vendor/UPSTREAM.md`) on the shared Postgres, without Azure, Maskinporten,
KRR or brreg. Every `profile/api/v1/…` call in the stack gets production answers: the user profiles apps, Receipt,
Authentication, Authorization and Access Management read, the contact point lookups of Notifications and Correspondence,
and what Arbeidsflate's profile page uses (favourites and party groups, profile settings, professional notification
settings, address verification, organisations' notification addresses). The controllers, services, EF repositories and
migrations, the KRR and organisation sync jobs, and the Register, PDP and Notifications clients are production code.
Only the two registers outside the stack are replaced, by stand-ins that read the test data from the Register component,
so test users' contact details and organisations' official addresses reach Profile through production's own sync code.

Until task 14 the Register component answered `/profile` with three fake controllers; they are deleted, and the route is
this component's (host port 8036, container port 5030).

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (`src/`, `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.editorconfig`, `stylecop.json`) except two additive `LOCALTEST` sites, below |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST` for the vendored projects and compile `fakes/**/*.cs` into the host (upstream has no `Directory.Build.props` to import) |
| `fakes/` | Compiled into the host assembly (namespace `Altinn.Profile.Localtest`), table below |
| `postgres-init/80-profile.sh` | Postgres init script: `profiledb` owned by `platform_profile_admin` (not a superuser) and the login role `platform_profile`, password `Password` as in upstream's `appsettings.json`. Idempotent; for an existing volume, where Postgres skips init scripts, run it by hand: `podman exec -i localtest-v2-postgres sh < components/profile/postgres-init/80-profile.sh` |
| `Dockerfile` | Mirrors upstream's `vendor/Dockerfile` (pinned Alpine images and digests, layered restore, publish, the `Migration/` copy, non-root `dotnet`, `/tmp/logtelemetry`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the running stack, through the proxy (below) |

## Directive sites in `vendor/`

Two, both in `src/Altinn.Profile/Program.cs`, both pure additions; there is no `#if !LOCALTEST`:

1. After `SetConfigurationProviders(builder.Configuration);`: `#if LOCALTEST`
   `AddLocaltestConfiguration(builder.Configuration)` `#endif`. `AddRegisterService` reads the connection string,
   `AddMaskinportenClient` binds its settings and `AddSyncJobs` reads `JobSettings` while services are registered, so
   the defaults must be in place first. They sit just below the environment variables.
2. After `services.AddSyncJobs(config);`: `#if LOCALTEST` `AddLocaltestServices(services, config)` `#endif`, after
   `AddRegisterService` and `AddMaskinportenClient`, whose KRR and brreg clients it replaces.

## What is replaced

| File (`fakes/`) | Lines | Production | Here |
| --- | --- | --- | --- |
| `LocaltestProfileExtensions.cs` | 125 | settings from the deployment | the two entry points: configuration defaults below the environment (endpoints derived from `Localtest:Profile:PlatformEndpoint`, both sync jobs on every minute, 10 s Register id cache), Key Vault and Application Insights forced off; the DI replacements below |
| `LocaltestProfileOptions.cs` | 18 | | `Localtest:Profile:PlatformEndpoint`, `StartupSyncWaitSeconds` |
| `Auth/LocaltestPublicSigningKeyProvider.cs` | 40 | Key Vault signing keys | the shared test certificate validates the apps' platform access tokens through the production handler (copy of Notifications') |
| `Register/LocaltestRegisterTestData.cs` | 77 | | reads the Register's anonymous `register/api/localtest/` API: `test-users`, `organizations`, the KRR stand-in |
| `Krr/LocaltestContactRegisterClient.cs` | 111 | `IContactRegisterHttpClient`: KRR's change log `hentEndringer` with a Maskinporten token | the Register's KRR stand-in's answer for every test person, as one change log entry per person (below) |
| `Brreg/LocaltestOrganizationNotificationAddressRegistry.cs` | 172 | `IOrganizationNotificationAddressUpdateClient` and `IOrganizationNotificationAddressSyncClient`: brreg's `kof-reception` and `kof-sync` (KoFuVi), Maskinporten | writes get a local registry id; the change log seeds organisations' official addresses from the test data and reports Profile's own writes back as accepted (below) |
| `Seed/LocaltestProfileSettingsSeeder.cs` | 103 | nothing (users set their own) | `profile_settings` once per test user without a row, and its recurring job |
| `Startup/LocaltestDatabaseStartup.cs` | 126 | `--run-db-migrations`, a Kubernetes pre-upgrade hook | the same EF migrations with the admin connection string before anything starts, then one seed run |
| `Startup/LocaltestSync.cs` | 75 | | runs the KRR sync, the organisation sync (each under its job's lease) and the settings seed, at start and on demand |
| `Inspection/LocaltestProfileController.cs` | 127 | nothing | `profile/api/localtest/` (below) |

974 lines in all (the plan estimated about 660 without the organisation address seed and the brreg echo).

## The seed: what is written when, and what wins

The Register's test data (`components/register/testdata/testData.json` plus apps' posted `testData.json`, see
[`../../testdata/README.md`](../../testdata/README.md)) stays the single source. Profile never reads the file; it reads
the Register's localtest API, and the data reaches its tables through the paths production data takes.

| Profile data | From | Path into Profile | When | What wins |
| --- | --- | --- | --- | --- |
| Persons' e-mail, mobile number (`phoneNumber`), `isReserved` (`contact_and_reservation.person`, the KRR mirror) | the test data's `email`, `mobileNumber` (`telephoneNumber` as fallback) and `reserved`, as the Register's KRR stand-in answers them | the KRR fake's change log → production `KrrSyncJob` → `ContactRegisterUpdateJob` → `PersonRepository` | at start, then every minute when the test data changed, and on `POST profile/api/localtest/sync` | the test data, always (KRR is the authority in production too; Profile has no write path for it). A person who leaves the test data keeps the row |
| Language, "do not prompt for party", preselected party (`user_preferences.profile_settings`) | `language`, `doNotPromptForParty`, `preSelectedPartyId` (turned into the party's UUID) | the settings seeder through the production `ProfileSettingsRepository` | at start, then every minute (users of an app's data registered later), and on `sync` | the user: only a user without a row is seeded, so a `PUT`/`PATCH users/current/profilesettings` is never overwritten. `env reset` brings the seed back |
| Organisations' official notification addresses (`organization_notification_address`, the KoFuVi mirror) | organisations' `notificationAddresses` (DDG Fitness 897069644 and Digitaliseringsdirektoratet 991825827 in the seed) | the brreg fake's change log → production `OrgSyncJob` → `OrganizationNotificationAddressUpdateJob` → `OrganizationNotificationAddressRepository` (rows `UpdateSource` KoFuVi, accepted, registry ids `lt-seed-{orgNumber}-{n}`) | once per organisation: only an organisation Profile has no row for is seeded (at start, every minute for organisations of apps' data, on `sync`) | the user: once Profile has the organisation, the seed never writes to it again; addresses created, changed or deleted through `organizations/{orgNumber}/notificationaddresses/mandatory` stay as the user left them. Sub-units without addresses get no row, so production's main-unit fallback answers for them |
| Self-identified users' e-mail | the Register (`si.email`), as in production | none needed | live | |
| Favourites and party groups, professional notification settings, verified addresses | not seeded | the API (Arbeidsflate or curl); kept in Postgres across restarts | | the user |

Nothing is truncated at start. Registered app test data reaches Profile at the jobs' next minute (or at once with `POST
profile/api/localtest/sync`); the app runner does not call it. Names and party ids are live (Profile asks the Register on
every lookup, cached 10 s), so a newly registered app user can log in at once; only their e-mail, phone number and
settings can lag by up to a minute.

### The KRR stand-in as a change log

KRR's change log has one entry per changed person with a running change number. Here the whole test population is one
change, numbered one past what Profile stored, and it is emitted only when its content (identity number, reservation,
e-mail, mobile number) differs from the change this process emitted last and Profile stored; a restart emits it once
more, which the production upsert absorbs. Every contact point is "updated" at the time of the change, so production's
18-month staleness rule in the contact point lookups never hides a test person. Empty values from the stand-in are
absent values.

### The brreg stand-in

In production an address written through Profile's API goes to brreg first and is stored with `has_registry_accepted =
false`; the lookups (`organizations/notificationaddresses/lookup` and Correspondence's) hide it until brreg's change log
reports it back and the organisation sync marks it accepted. The stand-in keeps that: its change log reports every row
still waiting as accepted (a soft-deleted one as a deletion, which the sync removes), so a written address shows in the
lookups after the next organisation sync, every minute or at once with `sync`. Without the sync (the plan's default had
it off) a written address would never reach Notifications. Synthetic addresses (production's derived ones) are never
reported.

## Settings

| Setting | Default | |
| --- | --- | --- |
| `Localtest:Profile:PlatformEndpoint` | `http://local.altinn.cloud:8000/` | The platform origin. The OpenID metadata, `RegisterSettings:ApiRegisterEndpoint`, `PlatformSettings:ApiAuthorizationEndpoint`, `NotificationsSettings:ApiNotificationsEndpoint` and the Register's localtest API derive from it |
| `Localtest:Profile:StartupSyncWaitSeconds` | `90` | How long the start waits for the Register before it leaves the seed to the jobs |
| `JobSettings:KrrSyncEnabled`, `OrgSyncEnabled` | `true` (upstream `false`) | The two syncs, with the stand-ins |
| `JobSettings:KrrSyncWaitTimeInMinutes`, `OrgSyncWaitTimeInMinutes` | `1` (upstream `10`) | Also the settings seed's interval |
| `CoreSettings:ProfileCacheLifetimeSeconds` | `10` (production `600`) | The Register id cache |
| `PostgreSqlSettings:AdminConnectionString`, `ConnectionString` | upstream's `localhost`; studioctl sets `Host=postgres` | `{0}` is the password (`ProfileDbAdminPwd`, `ProfileDbPwd`, both `Password` in upstream's appsettings) |
| `AccessTokenSettings:AccessTokenSigningCertificateFullPath` | set by studioctl | The shared certificate: validates incoming platform tokens (fake provider) and signs outgoing ones (production generator, `platform`/`profile`, to the Register and Notifications) |

The image sets `ASPNETCORE_ENVIRONMENT=Development` (the JWT handler reads the OpenID metadata over http only there) and
`ASPNETCORE_URLS=http://+:5030` (upstream's compose file sets it).

## The localtest API

Anonymous, under `profile/api/localtest/`; nothing here is a Profile contract.

| Endpoint | What |
| --- | --- |
| `GET status` | applied and pending migrations, the KRR change number and person count, the organisation sync watermark with organisation, address and "waiting for the registry" counts, the number of settings rows |
| `POST sync` | the KRR sync, the organisation sync and the settings seed now, in that order; answers each step's outcome (`ok`, `skipped` when its job held the lease for 30 s, `failed` with the message) |
| `GET users/{userId}` | the user's rows: settings, the KRR row (by the national identity number from the Register), the self-identified row, favourites and groups, professional notification settings, verified addresses, open verification codes (without the code) |
| `GET organizations/{orgNumber}` | the organisation's notification address rows with registry id, source and whether the registry accepted them; 404 when Profile has no row |

## Differences from production

1. The KRR mirror is filled from the test data, not from Kontaktregisteret, and every contact point is "just updated",
   so the 18-month staleness rule never applies.
2. brreg never sees an organisation address change: writes get a local registry id and the stand-in accepts every one
   at the next organisation sync. Production brreg can refuse or rewrite an address; here it never does, and an invalid
   organisation number is never refused (`InvalidOrgNumberException` is not raised).
3. Official organisation addresses come from the test data once per organisation, not from brreg's register.
4. Migrations run at start, not in a pre-upgrade hook; the admin role owns the database and is not a superuser.
5. `PortalAccessSettings:EnforceAccessCheck` stays `false`, upstream's default: tokens without `altinn:portal/enduser`
   are logged, not refused.
6. `notificationsettings/private/phonenumber`, which Arbeidsflate's BFF calls, does not exist at the pin; it is a 404 as
   against Profile in production test environments (noted for task 11).

### Behaviour changes for consumers, compared with the Register's fakes

- **`phoneNumber`** in user profiles is the KRR mobile number (the seed's `mobileNumber`), no longer
  `telephoneNumber ?? mobileNumber`. App prefill of `UserProfile.PhoneNumber` changes accordingly.
- **`units/contactpoint/lookup`** (Notifications' organisation recipients, Correspondence's `correspondence/` variant)
  returns only addresses users registered for the organisation (professional notification settings, verified first),
  not every holder of a role or access package there. With the seed alone it returns no users.
- **`users/contactpoint/lookup`** answers `userId: 0` and the `…IsOutdated` flags, as production.
- **`users/contactpoint/lookupsi`** answers every `idporten-email` identity with the e-mail from the URN.
- **`POST users`** takes only a national identity number string (production); the Register fake also took lookup
  objects.

## Where localtest is faster than production

| What | Production | Here |
| --- | --- | --- |
| Register id cache (user, party and organisation ids, main units) | 600 s | 10 s |
| Migrations | a pre-upgrade hook before the deployment | at every start, before the host listens |
| KRR sync | `KrrSyncWaitTimeInMinutes` from the deployment (upstream default 10) against Kontaktregisteret | every minute, plus at start and on `POST sync` |
| Organisation addresses accepted by the registry | brreg's change log, `OrgSyncWaitTimeInMinutes` (upstream default 10) | every minute, or at once with `POST sync` |

## Known gaps

- Not run yet against Postgres 18.3 with a non-superuser owner (phase 2). If an old Postgres volume predates this
  component, `profiledb` does not exist and the start fails with a critical log naming `80-profile.sh`.
- A person who leaves the test data keeps their KRR row; an organisation whose seeded addresses changed in the test data
  keeps Profile's (once per organisation). `env reset` starts over.
- The org address endpoints need authentication level 3 (the resource policy's obligation); `GetTestUserToken` takes
  `authenticationLevel=3`.
- No sync call from the app runner (plan question 5); testers who need an app user's contact details at once call
  `POST profile/api/localtest/sync`.

## Smoke test

`./smoke-test.sh [BASE]` (default `http://local.altinn.cloud:8000`), against the stack: health; `POST sync` with all
three steps ok; six migrations; the seeded DDG Fitness addresses (accepted, from the change log) and no row for its
sub-unit; the user endpoints the Register fakes served (users by id, UUID and SSN, `internal/user` incl. the
self-identified user, `users/current`, 401 and 403 without the user or platform token); production semantics
(`phoneNumber` from KRR, `isReserved` for Rik Forelder, `contactpoint/lookup` and `lookupsi`, the organisation lookup
with the main-unit fallback); Correspondence's two lookups (401, 403 without the scope, 200 with it); favourites;
profile settings (a user change survives a seed run); address verification with the code read from the Notifications
inbox, professional notification settings feeding `units/contactpoint/lookup`, 403 for a foreign party; organisation
addresses through the PDP (read as DAGL at level 3, 403 without a role, a written address hidden until the organisation
sync and then in the lookup, deleted again). `PERSIST=set` and `PERSIST=check` around an `env down`/`env up` check that
a favourite and a setting survive.

## Running without Docker

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
dotnet build -c Release vendor/src/Altinn.Profile/Altinn.Profile.csproj
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:15036 \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath=$PWD/../../certs/localtest-accesstoken.pfx \
  'PostgreSqlSettings__AdminConnectionString=Host=127.0.0.1;Port=5432;Username=platform_profile_admin;Password={0};Database=profiledb' \
  'PostgreSqlSettings__ConnectionString=Host=127.0.0.1;Port=5432;Username=platform_profile;Password={0};Database=profiledb' \
  dotnet vendor/src/Altinn.Profile/bin/Release/net10.0/Altinn.Profile.dll
```

On a machine whose default SDK is a preview (11.0), pin 10.0 with a `global.json` next to the folder you build from.

## Verified

Phase 1 (2026-10-21, no stack): `dotnet build -c Release` of the host with the fakes: 0 warnings, 0 errors. A start
in `Development` with the database off: DI validation passes, `GET /health` 200, `users/current` 401 without a token.
Nothing has run against Postgres or the stack yet.
