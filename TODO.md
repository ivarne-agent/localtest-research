# TODO

The open work on the new localtest, gathered from the "Not yet" paragraph of [`localtest/README.md`](localtest/README.md),
the components' "Known gaps", [`localtest/reviews/`](localtest/reviews/README.md) and the plan indexes under
[`planned-components/`](planned-components/). Each task is written so it can become a subagent's brief once approved.

Status values: **unapproved** (not yet discussed, or discussed and not yet released to an agent), **approved**
(design settled, an agent may be dispatched), **deferred** (discussed, parked until something needs it), **won't do** (discussed and dropped),
**in progress** (agent name, worktree, branch), **done** (commit).
Older rows also use **research done** (a research task whose document is merged), **plan written** and **brief drafted**
(stages of an approved task before dispatch), **PR open** (waiting on an upstream review) and **pinned** (the images
carry it); read them as the nearest of the values above.
Nothing is dispatched while it is unapproved.

Rules every brief inherits: work in a worktree of this repository under `/home/agent/code/.worktrees/<task>/`; a
change to a `vendor/` file is only ever a `#if !LOCALTEST` (production first) / `#else` / `#endif` block or an additive
`#if LOCALTEST` block; anywhere a fake class only requires 3-4 line changes with a preprocessor directive, it goes in the
directive and the fake is deleted (the user's decision of 2026-10-29, task 63; such a site needs no report first), and
real local behaviour (stores, controllers, seeders, token issuing) goes in `fakes/`; localtest owns the initialization of the database and the creation of the users (the component's `postgres-init/NN-*.sh`, role passwords as `{{password:<role>}}` from the runner's generated file, never a fixed value in an image, fake or the manifest), and platform services that run Yuniql or other migration software at startup do so in localtest too (the user's principles, task 62; `localtest/CONVENTIONS.md`); a component's Dockerfile mirrors upstream's with `localtest:` deviations; the old localtest in
`altinn-studio/src/Runtime/localtest` is reference only, never a basis; one image build or AOT publish at a time on
this machine (7.8 GB RAM), never next to a Cypress run; extend the component's `smoke-test.sh`; update the component
README and `vendor/UPSTREAM.md`; no `Co-Authored-By` trailers. The studioctl side of a
component (container spec, image pin, proxy prefix, provisioning calls, health) is changed in the vendored studioctl
source under `localtest/studioctl/src/cli/` (ordinary commits on top of the baseline, see
`localtest/studioctl/UPSTREAM.md`), built with `make dev-install` there and verified with
`studioctl env up -r localtest-v2 --dev <component>`; nothing is edited in an altinn-studio checkout.

**Optional component groups (decided 2026-10-20; superseded 2026-10-22 by manifest v2, task 27a: the groups are in
the manifest, each lists its root containers and `dependsOn` decides what else starts; no group implies another, so
`--with correspondence` starts Dialogporten's WebApi, Service host and Redis (task 65), and `--with arbeidsflate` its
WebApi, GraphQL host and Redis; see `localtest/CONVENTIONS.md`, "The manifest"; the original text follows).**
`localtest/studioctl/src/cli/internal/cmd/env/localtestv2/groups.go` becomes a group table: each group has a default (all off) and the groups it implies; `--with x` enables `x` and,
transitively, what it implies; `--without` removes only the named group. Groups: `dialogporten` (Redis, the three
Dialogporten hosts, the adapter from task 10) implies nothing; `receipt` implies nothing; `correspondence` implies
`dialogporten`; `everything` implies every other group. Task 16 lands the table (its first commit, merged ahead of the
rest); 7 and 10 build on it. **Front door for components that are not running (decided 2026-10-20, in task 16's
studioctl step; since tasks 26 and 27 the front door, the localtest container, answers both `503` kinds, and
studioctl-server no longer proxies):** `env up` writes every known component's route, enabled or not, and the front door answers `503`
with an RFC 9457 ProblemDetails (`application/problem+json`; a small HTML page when `Accept` prefers `text/html`)
naming the component and group and the exact `studioctl env up -r localtest-v2 --with <group>` command (and
`--with everything`); an enabled component that does not answer gets the same shape pointing at `studioctl env logs`.
A path matching no route still falls through to the localtest app.

| # | Task | Status | Size | Needs the stack |
| --- | --- | --- | --- | --- |
| 1 | [Vendor sync check](#1-vendor-sync-check) | done as `vendor-sync.py check` in task 58 (2026-10-27; no CI, by the user's decision) | small | no |
| 2 | [Authentication as an OIDC provider, logout, `pid`](#2-authentication-as-an-oidc-provider-logout-pid) | done (2026-10-20, commits 4b9b5f7..ddd0ecf; code-flow tokens carry `pid`, Dialogporten end-user API 200; pin sha-a3f62a8) | medium | yes |
| 3 | [Local altinncdn.no](#3-local-altinncdnno) | won't do (2026-10-20: the stack may read altinncdn.no) | small | yes |
| 4 | [Maskinporten token endpoint and the shared `IMaskinportenService` fake](#4-maskinporten-token-endpoint-and-the-shared-imaskinportenservice-fake) | done (2026-10-20, commit 798f18e) | small | yes |
| 5 | [Resource Registry component](#5-resource-registry-component) | done (2026-10-20: 5a commits e0cba40..b6f4c47, pin a3422419; 5b commits 9b7278d..aaa099e) | large | yes |
| 6 | [Notifications](#6-notifications) | done (2026-10-20, commits 45e99a1..0b38452, pin a4aa959; follow-up: seed data for SMS numbers and organisation addresses, see section 6) | medium | yes |
| 7 | [Correspondence](#7-correspondence) | done (2026-10-21, commits 94c1acb..e96f904; group `correspondence` implying `dialogporten` (until manifest v2, 2026-10-22: dependencies decide, see the header), port 8033; 7b done the same day closed the 6-digit party-id gap with one directive site, see `planned-components/correspondence/party-ids.md`; pin sha-735e5d2, proven 2026-10-21) | medium | yes |
| 8 | [Inspection GUI](#8-inspection-gui) | deferred (2026-10-20: possible overlap with Altinn Studio's admin of running services, see task 17; plan in `planned-components/inspection-gui/`) | medium | yes |
| 9 | [Dialogporten core](#9-dialogporten-core) | 9a done (2026-10-20, commits 4ab03f0..76b8787, pin ec333e4); 9b done (2026-10-20, commits 4749bd7..d7827bf, pin sha-d7827bf) | large | yes |
| 10 | [Dialogporten adapter and the Storage outbox](#10-dialogporten-adapter-and-the-storage-outbox) | done (2026-10-21, commits c940fd6..3c9b03f; adapter in the dialogporten group, Storage outbox on in the default stack, one `#if !LOCALTEST` site added in Dialogporten for http URLs; pin sha-3c9b03f, proven 2026-10-21 with `--with everything`) | large | yes |
| 11 | [Arbeidsflate](#11-arbeidsflate) | done (2026-10-21, commits dc1ae37..b136eabf; group `arbeidsflate` implying `dialogporten` (until manifest v2, 2026-10-22: dependencies decide, see the header), host `af.local.altinn.cloud:8000`, nginx 8037 and BFF 8038, two build-time patches on an unmodified vendor tree, 56-check smoke test, inbox verified in Chromium; 11b done the same day (Correspondence attachments from the inbox, commits 789edcd3..8e510e91, one directive site in Correspondence, see section 11); pin sha-7f21882, proven 2026-10-21 on plain images with `--with correspondence`: root, authentication, correspondence and dialogporten smoke tests pass) | large | yes |
| 12 | [Review leftovers](#12-review-leftovers) | done for the audit findings (2026-10-21, commits 6ba99ee..90c6bc4: Postgres in UTC, two Events directive sites for Dialogporten sources, authorization and root smoke tests fixed, Storage standalone test removed, `reviews/test-apps.md`; still open: HEAD on the localtest app, a WebSocket test, the Events reset not draining queues, Slack deliveries without their event, the Altinn 2 roles stub; pin sha-3143730, proven 2026-10-21 on plain images) | small each | mostly |
| 13 | [studioctl integration leftovers](#13-studioctl-integration-leftovers) | unapproved | medium | yes |
| 14 | [Profile beyond the Register](#14-profile-beyond-the-register) | done (2026-10-21, commits f7b7f50..097b474; production Profile in the default stack on port 8036, `/profile` off the Register, two additive hooks; pin sha-0485d0a, proven 2026-10-21 on plain images) | unknown | yes |
| 15 | [Localtest URLs in the Maskinporten client package](#15-localtest-urls-in-the-maskinporten-client-package) | approved (owner 2026-10-23: "dispatch agents on the maskinporten package"); brief `/home/agent/code/.briefs/task15-maskinporten-package.md`; **superseded by [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92)** (task 47, 2026-10-24: another contributor's draft with the same setting names covers our need; ours stays unopened on the fork; the owner may post the draft review comment from `localtest/reviews/2026-10-24-maskinporten-pr92.md`); #92 merged and released as 10.2.0 on 2026-10-26; the consumer follow-up (the version bumps that delete the `IMaskinportenService` fakes) stands, as items in `localtest/upstreaming/` | small | no |
| 16 | [Receipt](#16-receipt) | done (2026-10-21, commits f40027a..319a712; group `receipt`, port 8034; group table and the 503 front door landed from this task as 6b41c96 and adf6ee6; pin sha-902ddfc, proven 2026-10-21) | medium | yes |
| 17 | [Admin of running services and the inspection GUI](#17-admin-of-running-services-and-the-inspection-gui) | done (2026-10-20: plan in `planned-components/admin-running-services/`; finding: not straightforward, the admin frontend depends on the runtime gateway, Kubernetes, Gitea teams and Designer\'s database; recommends building 8 as planned with admin\'s instance labels; 8 stays deferred pending the owner\'s decision) | small | no |
| 18 | [Test data format: docs, JSON schema, extended baseline](#18-test-data-format-docs-json-schema-extended-baseline) | done (2026-10-21, commits 76fa790..96f1917; `localtest/testdata/` with the v3 schema, seed on v3, `reserved` mirrored into `isReserved`; pin sha-55ce57f, proven 2026-10-21 under task 7) | small | yes |
| 19 | [Documentation bookkeeping](#19-documentation-bookkeeping) | done (2026-10-21, branches `docs/root`, `docs/components-a`, `docs/components-b`, `docs/plans` merged; TODO corrected the same day) | small each | no |
| 20 | [Aspire host for the components](#20-aspire-host-for-the-components) | done (2026-10-21: plan in `planned-components/aspire/`; recommendation: no second runtime; a hybrid where an AppHost runs only the app beside `env up` should need no new code, 0.5 to 1 day to verify and document; a generated hosting package only if Aspire becomes a supported way in, 6 to 9 days; owner\'s decision) | small | no |
| 21 | [Aspire host package for apps](#21-aspire-host-package-for-apps) | done (2026-10-22; history: 2026-10-21: 21a done and merged, commits 125aa9db..2b5b2ca9: the embedded manifest `src/cli/internal/localtestv2/manifest/manifest.json` with its schema is the source of the component set, groups and the image pin, `components.go` is a loader, golden snapshot test; state file and route tables byte-identical to the table build for default, `--with correspondence` and `--with everything`, every smoke test passed on plain images; 21c done and merged, commit 0e8d02a5: `Altinn.Studio.Localtest.Provisioning` under `src/Runtime/common/dotnet/` (IAppSource with DirectoryAppSource and RunningAppSource, LocaltestProvisioner with Provision/Deprovision/SyncDialogporten, pluggable Studio token), studioctl-server a thin adapter by project reference, every provisioning capture identical to the inline code for two test apps, root and authorization smoke tests and the 44-test platform regression passed; 21b done and merged, commits 4b53d7d7..98e2baeb: cookie-policy fake in Authentication's and Correspondence's `fakes/` (shared source `components/shared/fakes/Cookies/`), Arbeidsflate patch 0003 and nginx without the forged `X-Forwarded-Proto`, both front doors strip `Secure` on app routes only, inventory and phase-2 results in `localtest/reviews/cookies.md`; verified by the coordinator on the dev images: 32 login `Set-Cookie` headers without `Secure`, authentication 34, correspondence 78, arbeidsflate 59, root and shared passing; pin sha-98e2bae, proven 2026-10-22 on plain images with `--with everything`: authentication 34, correspondence 78, arbeidsflate 59, root and receipt passing, 32 login `Set-Cookie` headers without `Secure`; 21d done and merged, commits 49368288..71109d1a: `localtest/aspire/Altinn.App.Aspire` (C# manifest reader matching the Go golden file, `AddAltinnLocaltest`/`WithGroup`, stock YARP front door from the manifest's routes, persistent containers on the shared volumes, 51 tests), proven under Podman 5.7 (rootful) with the default stack plus `dialogporten`: root 26, authentication 34, dialogporten 60 through port 8000, containers reach the front door through `--add-host`, AppHost restarts reuse all 18 containers (front door up in 8 s on the coordinator's own restart), Aspire itself costs about 0.5 GB; findings: Aspire's container tunnel does not build on this Podman (`ASPIRE_ENABLE_CONTAINER_TUNNEL=false` required), Postgres and Redis get fixed loopback ports 15432/16379 for the host-side probes, the front door has session lifetime; 21e done and merged, commits 4b7b7199..d208ee0d: the manifest's `app` section with `studioctl run` as its loader and a golden snapshot, `AddAltinnApp<T>` (Aspire-assigned port on `0.0.0.0`, the route through `host.docker.internal`, provisioning on the ready event, `provision`/`deprovision`/`reset` commands, the port-8000 guard naming the holder), the sample app `localtest/aspire/samples/app/` (`ttd/testapp` on app-lib 8.12.15, template files unchanged, five-line single-file AppHost), the `aspire-package.yml` workflow; proven under Podman 2026-10-22: the app provisioned and answering through the front door, a browser sign-in to the receipt (`docs/sample-app-*.png`), the keepAlive `Secure` gap shown, the commands from the dashboard (which needs `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` here), a first start on empty volumes after `reset` (healthy in 36 s, provisioned in 47 s), root 26 and authentication 34 after a deprovision and a PDP refresh (a provisioned app's `testData.json` replaces the Register's seed, documented), pdf3 cannot reach the CDN through this sandbox's proxy (environment limit); coordinator verified the live stack (app via the front door, the Register's apps, the `0.0.0.0` binding) and the 76 tests; the workflow's first run on main succeeded 2026-10-22 and pushed `Altinn.App.Aspire 0.1.0-alpha.2` to the `ivarne-agent` GitHub Packages NuGet feed (run 39701658014); 21f done and merged, commits 5e1fa1be..0bf5b502 (`manifest-parity.yml` with Go, .NET and schema jobs, all four jobs green on its first run on main, run 39729636927; post-merge fix 001bc64e: the package's compile glob excludes the provisioning library's `bin`/`obj`, which had produced duplicate assembly attributes once that library was built in the same checkout, both workflows green on it and 0.1.0-alpha.4 published), the smoke tests' container-name helper `localtest/smoke/lib/containers.sh` for studioctl's and Aspire's names, the typed group aliases, the two-runtimes docs; root 26, authentication 34, dialogporten 60, authorization 31 and the localtest app 64 of 66 against the Aspire-hosted sample; coordinator verified the helper and the root and authorization suites on its own AppHost run, 78 tests); open after 21: task 22 (app-lib) and task 24 (HTTPS mode); brief was `/home/agent/code/.briefs/task21f.md`; defaults taken: parity = both golden suites plus a stale-golden check in one workflow on every change to the manifest, the Go loaders or the package; the smoke tests resolve container names through one helper that knows studioctl's and Aspire's naming; docs stay in this repository until adoption; owner's addition 2026-10-22: typed group aliases `WithDialogporten()`, `WithReceipt()`, `WithCorrespondence()`, `WithArbeidsflate()`, `WithEverything()` over `WithGroup`, with a test that the typed set equals the manifest's groups); 21b's inventory `localtest/reviews/cookies.md` approved by the owner 2026-10-21 (cookie-policy fake instead of twelve directive sites in Authentication; app routes keep the stripping until task 22 fixes app-lib's `keepAlive`); 21d to 21f later) | large | yes |
| 22 | [app-lib honours an explicit localtest environment](#22-app-lib-honours-an-explicit-localtest-environment) | unapproved (follow-up of 21; two items: the explicit environment, and `keepAlive`'s `Secure` from the request, decided 2026-10-21) | small | no |
| 23 | [HTTPS for the Aspire-hosted stack](#23-https-for-the-aspire-hosted-stack) | research done (2026-10-22, commit 43915959, `planned-components/aspire/https.md`: yes, Aspire 13.6 terminates TLS at the YARP front door with a package-generated CA and server certificate and gives every container the CA without image changes; follow-up task 24) | small | no |
| 24 | [HTTPS mode for the Aspire package](#24-https-mode-for-the-aspire-package) | unapproved (follow-up of 23) | medium | no |
| 25 | [Service discovery in the components](#25-service-discovery-in-the-components) | research done and registration merged (2026-10-22, commits 688ea0ee..d6a2174f fast-forwarded onto main; report `planned-components/service-discovery.md`; the `#if LOCALTEST` registration in every vendored host, pass-through still on until each component's URLs are migrated; Dialogporten builds without upstream's locked restore instead of carrying lock files; coordinator rebuilt the localtest app and restored and published the Dialogporten Service host from a scratch copy, both clean; brief `/home/agent/code/.briefs/task25-service-discovery.md`) | small | no |
| 26 | [The localtest container as the shared front door](#26-the-localtest-container-as-the-shared-front-door) | done (2026-10-22; commits b9e3aa31..f76b62ba fast-forwarded onto main; **scope: the localtest image only**, the runtimes' switch is in 27; the image: routes for every component by service name through the discovery package without pass-through, two 503 kinds ported from studioctl-server, `PUT/DELETE localtest/api/apps/{org}/{app}` doing route and provisioning in one call with the provisioning code copied into the image and the Studio token minted in-process, the dev-server route as a pushed route, port 8080, 68 tests, 36.5 MB Native AOT image; phase 2 on today's stack: PUT provisioned the sample app with every call made by the container (packet capture), DELETE idempotent, root 26, authentication 34, localtest 65 of 67 through 8000 (studioctl-server's two known gaps), 52 of 81 through a second container on 8001 (the 29 identity checks cannot apply beside the real front door), both 503s shown; fixes: idempotent DELETE on a Register 404, `run-app.sh` pid file, readable quotes in problem details; coordinator verified phase 1 and phase 2 branches (tests, image, nothing left running); pin bumped to sha-f76b62b (images run 39848837148 green; proven by the coordinator 2026-10-22 on plain images: root 26, authentication 32 with the two Dialogporten checks skipped on the default stack, localtest 65 of 67 with studioctl-server's two known gaps); brief `/home/agent/code/.briefs/task26-front-door.md`) | medium | yes |
| 27 | [Manifest v2: services only](#27-manifest-v2-services-only) | 27a done (2026-10-23; commits bfda1497..6d928ad3 fast-forwarded onto main: manifest v2 with `groups` and `containers` (280 lines, no schema), the Go loader rewritten (health through probe containers on the network, `services__*` injected for running containers only, 8000 published from `localtest` only, `--dev-<container>[=<path>]`, `bump-tag.sh`), studioctl-server without proxy and provisioning (discovery registers apps with the front door), the provisioning library deleted, the smoke suites reading unrouted hosts from inside the network; proven on the pinned images: root 26, authentication 34, authorization 31, dialogporten 60, adapter 39, events 27, notifications 64, profile 52, localtest 80 of 81, correspondence 78 and receipt 27 with their groups, `env up` 20 s cached and 21.6 s on empty volumes after `reset` with all nine init scripts and every migration, `--dev-storage` 46 s; coordinator verified the branch (Go tests, server build, Aspire 78, actionlint) and the golden diff; pin bumped to sha-6d928ad by the coordinator 2026-10-23 with Redis on our image, proven on empty volumes: `env up --with dialogporten` 43 s with pulls, Postgres `TimeZone`/`log_timezone` UTC from the command line, Redis `save` empty, root 26, dialogporten 60); **27b done** (2026-10-23; commits 7cf646d1..6df9e6f8 fast-forwarded onto main: the Aspire package on manifest v2 as a thin loader, 9,959 → 4,319 tracked lines, `Aspire.Hosting` its only dependency, YARP front door, v1 reader, probe ports, compiled-in provisioning and `OnlyComponents` removed, health through `<runtime> exec` of the bash probe in the Postgres container, registration `PUT` on the ready event, `WithFrontendDevServer(port)`, 70 tests comparing byte for byte with the Go goldens; proven under Podman on `sha-6d928ad` with `WithDialogporten()`: platform healthy in 22 s (containers created), 4 s (restart reusing 18 containers), 32 s (empty volumes after the dashboard reset); only `localtest-<hash>` publishes 8000; root 26, authentication 34, dialogporten 60, authorization 31, localtest 80 of 81; 503 for groups off; fixes: the port guard accepts the AppHost's own persistent front door on restart, the fake front door ignores studioctl-server's probes (the flaky test), Redis tests on the pinned image; coordinator verified the branch (70 tests twice, sample AppHost build, Go green, machine clean); not verified: `WithFrontendDevServer` at run time and the dashboard commands; brief `/home/agent/code/.briefs/task27b-manifest-v2-aspire.md`) | large | yes |
| 28 | [Browser-visible links across the stack](#28-browser-visible-links-across-the-stack) | done for "back to inbox" (2026-10-23; commits 68631f2f..f626ed3b fast-forwarded onto main: the app runners set the four Arbeidsflate links as defaults when localtest-v2 is up (Go, C#, `run-app.sh`, shared golden), `studioctl run --dev-frontend` registers the dev-server route with the front door and removes it on stop; verified in Chromium on the stack with `--with arbeidsflate --with dialogporten` and the monorepo app `ttd/expression-validation-test` on the PR frontend: "Tilbake til innboks" opens Arbeidsflate signed in (sign in at level 3; level 2 shows the picker because Arbeidsflate requires `idporten-loa-substantial`), inside an instance and on the receipt the link deep-links to the instance's dialog, the fallback with empty settings goes to the start page; screenshots `localtest/reviews/task28-back-to-inbox/`; coordinator verified the branch and the screenshots; the upstream frontend PR hand-over `/home/agent/code/pr-app-frontend-local-platform-settings.md` now has the "Verified on the localtest stack" section; the full link inventory remains unapproved, narrowed by the owner 2026-10-22) | small | yes |
| 29 | [Documentation refresh for manifest v2 and the front door](#29-documentation-refresh-for-manifest-v2-and-the-front-door) | approved (owner 2026-10-23: refresh all Markdown to the current situation, studioctl and Aspire first, plus the service-discovery convention); done (2026-10-23; commits c4b74310..476a2fb9 fast-forwarded onto main: `localtest/CONVENTIONS.md` (manifest, tags, runtimes, public origin, front door, service discovery, port and health with the transitional table, certificate, volumes, app runner), `localtest/README.md` and the studioctl, Aspire, localtest-app and 15 component READMEs corrected against the code, 30 dated reviews and plans with superseded notes, stale terms gone from every current document (the coordinator's own sweep found none; remaining hits are in dated plans with notes, plus upstream's `{{GIT_SHA}}`), relative links checked, typos clean; the coordinator applied the code-side comments: the sample AppHost header, `bump-tag.sh`, `build-pdf3.sh`, the smoke tests' old host-port remarks; open question raised by the task, for task 13: whether studioctl-server registers apps again after `env up --with <group>` recreates the localtest container (its registration cache suggests not; the docs say to register again); brief `/home/agent/code/.briefs/task29-docs-refresh.md`) | medium | no |
| 30 | [The Aspire host with several apps](#30-the-aspire-host-with-several-apps) | done (2026-10-23; commits ebe096ef..5d646d89 fast-forwarded onto main: two apps work in one AppHost as the package was, each with its own resource, port, secrets directory keyed on the app id, registration and provisioning health check; the one gap, two resources of the same app id sharing a route and secrets, is now refused by `AddAltinnApp`; the sample has `App2` (`ttd/testapp2`) and a two-app `apphost.cs`; model test `MultipleAppsTests` (75 tests); proven under Podman: both provisioned 35 s after `dotnet run`, both registered on their own ports and answering through 8000, the Register lists both apps' test data (it rebuilds from every registered app, identical entries allowed; with two identical files the merge of distinct user sets is not shown), `deprovision` of `app2` from the dashboard left `ttd/testapp` registered and answering, `provision` brought it back, a restart reused the 18 containers and re-registered both in 12 s, 4.3 GB available with both apps up; coordinator verified the branch (75 tests, sample build) and the evidence in `.scripts/t30/`; note for task 13: Aspire's stopped persistent containers hold the shared volumes and block studioctl's `env reset` until removed, as seen twice now; brief `/home/agent/code/.briefs/task30-aspire-multiple-apps.md`) | small | yes |
| 31 | [Runtime-prefixed volumes](#31-runtime-prefixed-volumes) | done (2026-10-23; commits b9492e17..2c25542b fast-forwarded onto main: the manifest's `volumes` keys are ids (`postgres-data`, `storage-blobs`, `policy-store`, `localtest-data`, `correspondence-blobs`), studioctl prefixes `localtest-v2-` with mount names unchanged, the Aspire package prefixes `altinn-localtest-`, each reset removes its own volumes only, goldens compared by id, 78 Aspire tests; proven: `env reset` removed only `localtest-v2-*`, `env up` recreated them (24 s, root 26), the AppHost created `altinn-localtest-*` beside them (both sample apps provisioned in 40 s, root 26 through the containers helper), the dashboard reset removed only `altinn-localtest-*`; coordinator verified both phases; brief `/home/agent/code/.briefs/task31-volume-prefix.md`) | small | yes |
| 32 | [studioctl PR refresh with review](#32-studioctl-pr-refresh-with-review) | approved (owner 2026-10-23: "plan and execute a refresh of the studioctl PR; don't blindly copy, fix errors and simplify"); brief `/home/agent/code/.briefs/task32-studioctl-pr-refresh.md`; **done** 2026-10-23: A+B merged (2ccabb35), C pushed to the fork (`feat/studioctl-localtest-v2-stack` at dfc5fdd348, six commits on altinn-studio main f240887cfa); owner pastes the description from `/home/agent/code/pr-studioctl-localtest-v2-stack.md` and the comment file | large | yes |
| 33 | [minimal in-memory emulation in the localtest image](#33-minimal-in-memory-emulation-in-the-localtest-image) | research, approved (owner 2026-10-23: "dispatch some research on how to add some truly minimal emulation of the services into the localtest image"); brief `/home/agent/code/.briefs/task33-minimal-emulation.md`; **done, merged** 2026-10-23 (`planned-components/minimal-emulation/README.md`); owner to decide whether to build phase 1 | medium | no |
| 34 | [`studioctl run --mode container` against localtest-v2](#34-studioctl-run---mode-container-against-localtest-v2) | approved (owner 2026-10-24: "dispatch container mode"); brief `/home/agent/code/.briefs/task34-container-mode.md`; **done, merged** 2026-10-24 (ff to 63452dc3): container-mode apps on the stack network, proven with the app-lib 8 sample; the monorepo app blocked by an existing studioctl build bug | medium | yes |
| 35 | [minimal mode phase 1](#35-minimal-mode-phase-1) | approved (owner 2026-10-23: "draft the brief for phase 1 of the minimal mode and send it to an agent for implementation; the minimal implementations stay isolated in a separate folder"); brief `/home/agent/code/.briefs/task35-minimal-mode-phase1.md`; **done, merged** 2026-10-23 (ff to 6d0adddc): minimal mode proven on the stack, default stack unchanged; follow-ups: remember the mode after `env down`, Aspire `WithMinimal` (spec phase 2), pin bump | large | yes (phase 2) |
| 36 | [Anonymous volumes removed with their containers](#36-anonymous-volumes-removed-with-their-containers) | approved (owner 2026-10-23: "dispatch this task"); brief `/home/agent/code/.briefs/task36-anonymous-volumes.md`; **done, merged** 2026-10-23 (ff to 48648ecc); the Storage and localtest images carry the Dockerfile change at the next pin bump | small | yes (phase 2) |
| 37 | [Aspire minimal mode](#37-aspire-minimal-mode) | approved (owner 2026-10-23: "dispatch the Aspire minimal mode task"); brief `/home/agent/code/.briefs/task37-aspire-minimal.md`; API decision: `builder.AddAltinnLocaltestMinimal()` with no extension that enables another service; **done, merged** 2026-10-23 (ff to 2976956a): `AddAltinnLocaltestMinimal()` proven under Podman on sha-48648ec | medium | yes (phase 2) |
| 38 | [Memory statistics of the stack](#38-memory-statistics-of-the-stack) | approved (owner 2026-10-23: "dispatch an agent to grab some stats"); brief `/home/agent/code/.briefs/task38-memory-stats.md`; **done, merged** 2026-10-23 (`localtest/reviews/2026-10-23-memory.md`, `localtest/tools/memory-stats.sh`) | small | yes |
| 39 | [Configurable public host names and port](#39-configurable-public-host-names-and-port) | approved (owner 2026-10-23: "dispatch"); brief `/home/agent/code/.briefs/task39-configurable-origin.md`; **done, merged** 2026-10-23 (ff to 6fd85eff); the front door and fake changes reach the stack at the next pin bump | large | yes (phase 2) |
| 40 | [Service names for the components' URLs, pass-through off](#40-service-names-for-the-components-urls-pass-through-off) | approved (owner 2026-10-23: "dispatch an agent to use service names for the urls and turn pass-through provider off"); brief `/home/agent/code/.briefs/task40-service-names.md`; **done, merged** 2026-10-23 (ff to e2a9b82c); the images carry it at the next pin bump | large | yes (phase 2) |
| 41 | [`env up` tears down after a failed start](#41-env-up-tears-down-after-a-failed-start) | approved (owner 2026-10-23: "if it has a simple fix it would be nice"); brief `/home/agent/code/.briefs/task41-up-failure-teardown.md`; **done, merged** 2026-10-23 (ff to 80dc9815) | small | no |
| 42 | [App frontend: platform links from the settings alone](#42-app-frontend-platform-links-from-the-settings-alone) | done, merged upstream 2026-10-26 ([altinn-studio#21481](https://github.com/Altinn/altinn-studio/pull/21481)); history: PR opened at head b0e5555fc5; 2026-10-25 follow-up dispatched (owner: "add a changelog entry … see if there is any hints about why cypress fails"): brief `/home/agent/code/.briefs/task42b-changelog-and-cypress.md`, the studioctl changelog check and a Cypress 90-minute timeout; **done** 2026-10-25 (head bdaac448ca: changelog entry + comment, validator passes; Cypress: the runner stalled on `payment-test/payment.ts` after spec 81, nothing points at the PR, the earlier run on 4f865e2a62 passed 101/101; rerun needed); approved (owner 2026-10-23: #21436 was merged, "we need to create a new one"); design `/home/agent/code/pr-app-frontend-host-rule.md`, brief `/home/agent/code/.briefs/task42-frontend-settings-only.md`; **PR open as [altinn-studio#21481](https://github.com/Altinn/altinn-studio/pull/21481) at b0e5555fc5** (2026-10-24: unconfigured links visibly broken with an alert naming the setting; the owner pastes the revised body from the hand-over, the token cannot edit it); the research runner branch merged (ff to 9d0d8f5d); the v4 app-frontend-react fix is a separate upstream PR, not yet tasked | medium | yes (proof) |
| 43 | [The apps' Maskinporten clients on localtest-v2](#43-the-apps-maskinporten-clients-on-localtest-v2) | approved (owner 2026-10-23: "look into the maskinporten client in monorepo if that need adaption"); brief `/home/agent/code/.briefs/task43-app-maskinporten-clients.md`; **done, merged** 2026-10-23 (`localtest/reviews/2026-10-23-app-maskinporten.md`); finding: a monorepo app does not start at a custom origin without a Maskinporten client | medium | yes |
| 44 | [Egress domains of apps and the stack](#44-egress-domains-of-apps-and-the-stack) | research, approved (owner 2026-10-23: "track the domain name for all external network calls, for security so we can block anything not on the list in production"); brief `/home/agent/code/.briefs/task44-egress-domains.md`; **done, merged** 2026-10-24 (ff to 13326070): static inventory plus the observed run; three domains not predicted | medium | yes (phase 2) |
| 45 | [Egress control: declared hosts enforced on localtest](#45-egress-control-declared-hosts-enforced-on-localtest) | plan written (owner 2026-10-23: "block any domain not declared in applicationmetadata"; "a rather big feature, create a new folder for the planning"): `planned-components/egress-control/README.md`; open questions with the owner before phase 1 is briefed | large | yes |
| 46 | [Egress inventory (egress control phase 1)](#46-egress-inventory-egress-control-phase-1) | brief drafted (owner 2026-10-23: "draft the brief for phase 1 of egress control"): `/home/agent/code/.briefs/task46-egress-inventory.md`, awaiting dispatch | large | yes (phase B) |
| 47 | [Compare altinn-apiclient-maskinporten#92 with our change](#47-compare-altinn-apiclient-maskinporten92-with-our-change) | research, approved (owner 2026-10-24: "#92 is not my PR; dispatch an agent to look at the differences"); brief `/home/agent/code/.briefs/task47-compare-pr92.md`; **done, merged** 2026-10-24 (`localtest/reviews/2026-10-24-maskinporten-pr92.md`): support #92, withdraw ours | small | no |
| 48 | [Text parameters as React elements (explore)](#48-text-parameters-as-react-elements-explore) | approved (owner 2026-10-24: "explore, give up if hard, a PR for making text parameters react elements"); brief `/home/agent/code/.briefs/task48-text-params-elements.md`; **done, no PR** (2026-10-24: feasible but not an improvement; notes `/home/agent/code/notes-text-params-elements.md`) | small | no |
| 49 | [localtest-v2 "up" from the containers, not the state file](#49-localtest-v2-up-from-the-containers-not-the-state-file) | done (2026-10-25; commits aae4355b, 70e5e5c4, 3b263f5b, ec0c4ebc: `localtestv2.Current` (state file plus devenv `GraphRunning`) for every reader outside the runtime, a stale file is no localtest-v2, `run` stops at once only when the containers run and the front door does not answer (the agent's stop on a stale file was changed by the coordinator: it refused runs against `-r localtest`); proven on the stack: after `docker rm -f` of the 13 containers `app env` on loopback without the certificate, `run` in 2.5 s, the server with the host bridge, `env up -r localtest` and a run against it, `env down -r localtest-v2` removed the file; with the front door paused `run` stopped after 5 s; ported to altinn-studio#21394 as 7b4c81fc13; brief `/home/agent/code/.briefs/task49-state-file.md`) | medium | yes |
| 50 | [Front-door registration off the discovery loop](#50-front-door-registration-off-the-discovery-loop) | done (2026-10-25; commits 7899901f, 11fb0b25: `Reconcile` starts the calls on their own tasks, one per app, `Settled` gates each start, `WhenIdle` for the tests, three new tests; proven on the stack: with the front door paused B's `PUT` hung 17 s while `app ps` answered in 0.2 s and A's removal and `DELETE` went out at once, both completed on unpause and B answered through 8000; ported to altinn-studio#21394 as d6220f4b9c; brief `/home/agent/code/.briefs/task50-registration-worker.md`) | small | yes |
| 51 | [Monorepo-ready references in the studioctl change](#51-monorepo-ready-references-in-the-studioctl-change) | done (2026-10-25; commit 296929ac: no task numbers, no links out of `src/`, the manifest README states tags, port and health and the certificate, `CONVENTIONS.md` points there, remaining references listed in `localtest/studioctl/README.md` "Not mergeable yet"; ported to altinn-studio#21394 as 1ab83430e1) | small | no |
| 52 | [Monitoring in localtest-v2: OpenTelemetry collector and Grafana](#52-monitoring-in-localtest-v2-opentelemetry-collector-and-grafana) | done (2026-10-26; trace-links follow-up done 2026-10-26 (commits 06032322, 83ad35d6, brief `/home/agent/code/.briefs/task52-trace-links.md`): the engine's Grafana pair only from the runner while the monitoring group runs, Aspire sets `EngineSettings__Dashboard__TraceUrlTemplate` to its dashboard (unverified until the engine change is in `tt02`); Aspire follow-up done 2026-10-26 (commit 3a9c9b6d, brief `/home/agent/code/.briefs/task52-aspire-otlp.md`): every localtest container sends traces, logs and metrics to the Aspire dashboard (`WithOtlpExporter()`, the manifest name, 5 s metrics; the dashboard's OTLP endpoints on every interface without the container tunnel; the OTLP key kept so persistent containers are reused), proven with both samples under Podman (screenshots `/home/agent/code/.handover/task52-aspire-otlp/`); collector-first follow-up done 2026-10-26 (commits 569ef9a4, 8ca809a0, brief `/home/agent/code/.briefs/task52-collector-first.md`): every other container depends on the collector, metrics every 5 s, the engine dashboard's Grafana links; part 2: the `monitoring` group with five stock images, the runner's OTLP variables while the collector runs (gRPC to `otel-collector:4317`), `/grafana` and `/otlp` on the front door; proven on the stack: 18 containers healthy, an app's trace, dashboard and logs in Grafana (screenshots `/home/agent/code/.handover/task52/`), 377-480 MB for the five; the collector is a named special case for `services__` (its HTTP receiver has no health path); the front door's new image tag waits for the images workflow on main; brief `/home/agent/code/.briefs/task52-monitoring.md`; engine follow-up done 2026-10-26 (commit f8f4148f, brief `/home/agent/code/.briefs/task52-engine-otel.md`): `EngineSettings__EnableTelemetry=false` dropped now that the engine's `tt02` is altinn-studio#21515 (revision 884b8ed755), research images pinned to `sha-38fbbba` (the last green images run on main, with the front door's task 52 routes and task 54 telemetry); on the stack with the pinned images: the engine's spans under `workflow-engine`, its enqueue joined to the front door's trace, the Workflow Engine dashboard with data (its `$service` comes from the metrics, so no `OTEL_SERVICE_NAME=workflow-engine-app`), without the group no `OTEL_*` and no connection attempts (screenshots `/home/agent/code/.handover/task52-engine/`); part 1: commits a7c97ca9 and 18c54d53: the `files` field in both loaders, written to `<DataDir>/infra/localtest-v2/files/` and mounted read-only, Aspire ignores `monitoring`; verified on Podman 2026-10-26 with a scratch entry on the minimal front door: readable by uid 1654, read-only, `ro` binds, 0644/0755 host copies, in-place rewrite seen by the running container, removed files gone. Known: a plain `env up` on a healthy, unchanged environment says "already running" and does not rewrite the files (only a real start does), and any `self install`, including `make dev-install`, stops every localtest-v2 environment; part 2 must add `monitoring` to the `go:embed` directive in `manifest.go` and leave the monitoring containers out of Aspire's `GoldenComparisonTests` (the `everything` case and the per-group loop); brief `/home/agent/code/.briefs/task52-manifest-files.md`) approved: a `monitoring` group in the shared manifest, part of `everything`, which the Aspire host ignores explicitly; stock images configured through a new `files` field, `args` only if needed; Grafana under `/grafana/` and OTLP under `/otlp` on the front door; every container gets the standard `OTEL_EXPORTER_OTLP_*` variables when the collector runs; not in minimal mode; needs 53 and 54 for full coverage) | medium | yes |
| 53 | [The standard OTLP variables in every component](#53-the-standard-otlp-variables-in-every-component) | done (settled from the records in task 65, 2026-10-29: every component exports by the rule, the workflow engine upstream, pdf3's WARN-only logs kept on purpose (decided 2026-10-27, section 53's last item), the log filter in the images since `sha-ae45f06`; the components do not wait for the collector, which `localtest/docs/verify-after-sync.md` 6.11 records as no failure); history: in progress (2026-10-26; part 1 done, commits 49e56853 and a841a9f1: the audit in the section below and `AddLocaltestOtlp` in `components/shared/fakes/Otlp/`, inlined in each component's directive since task 63's follow-up, the folder gone; the workflow engine is done: altinn-studio#21515 merged (884b8ed755), in `tt02`, and the manifest's switch removed (commit f8f4148f, task 52's engine follow-up); it was PR altinn-studio#21515 from `ivarne-agent:feat/workflow-engine-otlp-env` (522ff4cc87, cf006182fd and 834dab1db9; exports exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, per-signal variables do not turn it on, `EngineSettings.EnableTelemetry` removed, so the manifest's `EnableTelemetry=false` is ignored once the image has it; the old `localtest` runtime checked with a branch image: no attempts to localhost:4317 without monitoring, identical series, traces and logs with it; verified on Podman 2026-10-26 against collector 0.161.0: no variable, no export and no connection attempts; variable, all three signals with `OTEL_SERVICE_NAME`; `EnableTelemetry=true` alone, localhost:4317; both, the variable wins. Export errors go to the SDK's self-diagnostics, not the engine log), PR handover in `/home/agent/code/.handover/wfe-otlp-pr.md`; batches 1-3 done in the localtest builds (the Authorization family `7c1e0dd4`; the platform components and Dialogporten `e64a2a3e`, `3973509e`), stack proof done (2026-10-26, branch `task53-stack-proof`: images `sha-609c646` (ef213725), every changed component's smoke test green with its OTLP span check on `--with everything`, every component of the table reports traces, metrics and logs under its manifest name, one `ttd/testapp` submission as one trace through the front door, Storage, the PDP and Access Management, nothing without the group; the helper filters the exporters' own HTTP logs (ea60bbf9, in the next images); open: pdf3); per-service upstream reviews in `localtest/reviews/otel/`) approved: every component, eventually all of them | medium | yes |
| 54 | [Apps and the front door on the same OTLP variable](#54-apps-and-the-front-door-on-the-same-otlp-variable) | in progress (2026-10-26); front-door half done (2026-10-26, branch `task54-frontdoor-otel`: OpenTelemetry in the front door by the rule, `/otlp`, `/grafana`, `/health` and `/app-frontend/` not traced, a span per emulated call in minimal mode; proven on the stack with `ttd/testapp`: app client span → front door server span → forwarding client span → Register's server span in one trace, screenshot `/home/agent/code/.handover/task54-frontdoor/`; the shared helper no longer turns on for a per-signal endpoint alone; brief `/home/agent/code/.briefs/task54-frontdoor-otel.md`); apps half open | small | yes |
| 55 | [Telemetry files in minimal mode](#55-telemetry-files-in-minimal-mode) | done, merged (2026-10-26, main `673facf4`..`ae45f069`, pinned `sha-ae45f06` in `e7629021`; history: done on branch `task55-minimal-telemetry-files`, commits 575088d7, 8626bdba, 35e6fb7c and the docs commit before the rebase): minimal mode's `otel-files` collector writes `<home>/data/infra/localtest-v2/telemetry/<service>/{traces,logs,metrics}.jsonl` for agents to read with `jq` (guide in the bundled app-development skill), size-rotated, emptied by `env up`, deleted by `env down`; a manifest field `otlpReceiver` replaces the runner's `otel-collector` special cases and the front door's `/otlp` targets the role `otlp`; proven with `--dev-localtest`, the pinned front door needs the next images run; brief `/home/agent/code/.briefs/task55-minimal-telemetry-files.md` | small | yes |
| 56 | [A query page for Storage's instances](#56-a-query-page-for-storages-instances) | done, merged (2026-10-26, main `510facd6` and `ddef4331`, pinned `sha-ddef433` in `25734221`; history: done on branch `storage-instances-page`; brief `/home/agent/code/.briefs/storage-instances-query-page.md`): `/Home/Instances` form, list, instance page, downloads through the proxy, links only while they work, similar instances; route tests; proven with `--dev-localtest` with and without `arbeidsflate`/`receipt` (evidence `/home/agent/code/.handover/storage-instances-page/`); the pinned front door needs the next images run | small | yes |
| 57 | [The host's CA bundle for pdf3](#57-the-hosts-ca-bundle-for-pdf3) | done, merged (2026-10-27, main `f3052353`..`ea0bd3f6`, runner code only, no image change; history: done on branch `ca-bundle`; brief `/home/agent/code/.briefs/pdf3-ca-bundle.md`): manifest field `caBundle` (pdf3), studioctl and Aspire mount a copy of the host's `STUDIO_CA_BUNDLE` at `/etc/localtest/ca-bundle.pem` and set the variable; proven on the stack with and without (evidence `/home/agent/code/.handover/ca-bundle/`); review item 11 fixed; the old `localtest` runtime fixed too on altinn-studio branch `fix/studioctl-pdf3-ca-bundle` (fork, PR not opened) | small | yes |
| 58 | [Scripted vendor sync for the components](#58-scripted-vendor-sync-for-the-components) | done (2026-10-27, merged as main `5f13eea3`, pinned check on `sha-5f13eea` green; brief `/home/agent/code/.briefs/task58-tool.md`): `localtest/tools/vendor-sync.py` (`status`, `check`, `render`, `sync` staging only), metadata in every `UPSTREAM.md`, 39 unit tests, the drifts fixed, Profile synced to `d0cd92d` and verified on a local image (root 26, Profile 52, 53 with monitoring), workflow-engine no longer vendored, `repin-vendor.py` retired; follow-up done on branch `task58-followup` (2026-10-27): the Resource Registry retargeted to altinn-auth `3d51810`, Correspondence synced to `d2dc912` and its party-id site removed, verified on local images | large | no (the verification after a sync does) |
| 59 | [Upstreaming the localtest modifications](#59-upstreaming-the-localtest-modifications) | in progress (the owner opens the PRs, tracked in `localtest/upstreaming/`; open on 2026-10-29: [altinn-storage#1173](https://github.com/Altinn/altinn-storage/pull/1173), [#1174](https://github.com/Altinn/altinn-storage/pull/1174), [#1175](https://github.com/Altinn/altinn-storage/pull/1175), [#1178](https://github.com/Altinn/altinn-storage/pull/1178) and [altinn-profile#1076](https://github.com/Altinn/altinn-profile/pull/1076); [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280), another contributor's, relates to the signing-key items); history: unapproved (2026-10-27: per-service TODOs written in `localtest/upstreaming/`; phase 1, easy fixes, first) | small each | no |
| 60 | [Database users and passwords owned by studioctl and Aspire](#60-database-users-and-passwords-owned-by-studioctl-and-aspire) | done (2026-10-29, main `d7e15e9e` and `e089ff84`, pinned `sha-e089ff8`; brief `/home/agent/code/.briefs/task60-db-passwords.md`; evidence `/home/agent/code/.handover/task60-db-passwords/`): the passwords in the manifest (superseded by task 62: generated per installation, `{{password:<role>}}` in the manifest), the init scripts read them from the Postgres container's environment, Storage's users created by `10-storage.sh`; proven on studioctl (fresh and existing volume) and under Aspire (sample AppHost, local images, existing and fresh volume) | medium | yes |
| 61 | [Remove the OTLP resource workaround from the ServiceDefaults hosts](#61-remove-the-otlp-resource-workaround-from-the-servicedefaults-hosts) | pinned (`sha-e089ff8`, 2026-10-29, with task 60; see section 61) — done (2026-10-28, branch `otlp-resource-workaround-removal`; evidence `/home/agent/code/.handover/otlp-resource-workaround-removal/`): the call and the four `Otlp/` copies removed; on local images with `--with monitoring` all four export traces, metrics and logs under their plain names; root 27, front door 85 + the known FRONTEND_DIST failure, Authentication 33, Register 15, Resource Registry 44 | small | yes |
| 62 | [Generated database passwords per installation](#62-generated-database-passwords-per-installation) | done (2026-10-29, main `ea619ad5` and `3e586ebb`, pinned `sha-3e586eb`; brief `/home/agent/code/.briefs/task62-generated-db-passwords.md`; evidence `/home/agent/code/.handover/task62-generated-db-passwords/`): a random password per role in each runner's owner-only JSON file, `{{password:<role>}}` in the manifest, superuser `Password`, reconciliation in Postgres before its dependants start; proven on studioctl and under Aspire on a fresh volume, after the file was deleted and on an older volume; known gap, left as is (owner 2026-10-29): under Aspire, DCP's own `podman create` command line carries the containers' environment, passwords included, while it runs | medium | yes |
| 63 | [A directive instead of a small fake](#63-a-directive-instead-of-a-small-fake) | done (2026-10-29; main `6e1b1105`..`27e07fb5`, `4840c5e9`, then the follow-up `c42c24b6` and its parents; pinned `sha-c42c24b`; brief `/home/agent/code/.briefs/task63-directives-over-small-fakes.md`; evidence `/home/agent/code/.handover/task63-directives-over-small-fakes/`): 19 fake files (1073 lines) became 13 new directive blocks plus the OTLP rule inlined in six existing sites, in Authorization, Dialogporten, the Resource Registry, Correspondence, Access Management, Events and the OTLP consumers; `components/shared/fakes/Otlp/` is gone (the front door keeps its copy, which `Otlp.Tests` now tests); the upstream token-generator change is task 64 | medium | yes |
| 64 | [Local platform access tokens in the Integration.Platform token generator](#64-local-platform-access-tokens-in-the-integration-platform-token-generator) | **superseded by [66](#66-a-test-tool-token-generator-mock-in-the-front-door)** (the owner, 2026-10-29: "Can localtest just use a mock of the online token generator?"); was unapproved (created at the owner's request 2026-10-29; not to be dispatched); its plan stays as history and may come back as an optional idea for `dotnet run` developers | small | no |
| 65 | [Docs audit fixes and Correspondence's Service host](#65-docs-audit-fixes-and-correspondences-service-host) | done (2026-10-29; main `23dc68e3` (Correspondence depends on the Service host) and the docs commits after it; no image change, no pin; brief `/home/agent/code/.briefs/task65-audit-fixes.md`; evidence `/home/agent/code/.handover/task65-audit-fixes/`): `--with correspondence` starts Dialogporten's WebApi, Service host and Redis (17 containers), Correspondence 79 (the new event check) and root 26 on the pinned images; the audit's 79 findings: 17 wrong and 52 stale fixed (dated plans and reviews kept as history with a superseded note), 9 of 10 unclear settled, one open (whether the rules every brief inherits carry task 62's database principles) | medium | yes |
| 66 | [A test-tool token generator mock in the front door](#66-a-test-tool-token-generator-mock-in-the-front-door) | done (2026-10-29; main `db670995`..`8023057f`, pinned `sha-8023057`; brief `/home/agent/code/.briefs/task66-testtool-mock.md`; evidence `/home/agent/code/.handover/task66-testtool-mock/`): `GET /mockporten/api/GetPlatformAccessToken` on the front door (for now also at the root `/api/…`), Access Management on production's `TestTool` against it from the manifest's variables (the two `TokenGenerator.cs` directives removed); the upstream full-URL setting pushed to `ivarne-agent/altinn-auth` for the owner to open; follow-up in section 66 | small | yes |
| 67 | [File-based Maskinporten token source for apps on localtest](#67-file-based-maskinporten-token-source-for-apps-on-localtest) | in progress (2026-10-29; approved by the owner: "Add the file based token source to the TODO and dispatch agents to fix all 4"; briefs `/home/agent/code/.briefs/task67a-runners-and-exchange.md`, `task67b-monorepo-resolver.md`, `task67c-applib8-resolver.md`); **67a done** (2026-10-30, commits `2f8a249e`..`d3bbf4a2`, pinned `sha-d3bbf4a`, see section 67; evidence `/home/agent/code/.handover/task67a/`); 67b and 67c in progress | medium | yes |
| 68 | [The Access Management GUI (am.ui) in the stack](#68-the-access-management-gui-amui-in-the-stack) | unapproved (created at the owner's request 2026-10-30: "yes, write it up as a task"; a research step first, see section 68) | medium | yes |

## 1. Vendor sync check

**Goal.** A GitHub workflow in this repository that proves every `components/<name>/vendor/` is upstream at its pinned
commit plus `LOCALTEST` directive blocks and nothing else, and reports when upstream has moved past the pin.

**Design (settled so far).** The pin stays in `vendor/UPSTREAM.md` (decided 2026-10-20, no separate machine-readable
pin file for now); the check parses it the way `localtest/tools/repin-vendor.py` already does, so the two share one
parser. For each component: fetch the pinned commit of the upstream repository (shallow, `git fetch --depth 1
<url> <sha>`), `git archive` the copied paths, `diff -rq` against `vendor/`. A differing file passes only if the diff
consists of complete `#if !LOCALTEST … #else … #endif` blocks whose `!LOCALTEST` branch equals upstream's text.
Report, do not fail, when upstream `main` has commits after the pin (count and the newest subject).

**Open.** Pull requests and pushes to `main` only, or also a weekly schedule that opens or updates one issue when
upstream has moved? Does the Dockerfile comparison (upstream Dockerfile versus ours with `localtest:` lines removed)
belong in the same check?

**Verification.** The workflow passes on `main`; a deliberate one-line edit in a vendored file fails it with the file
and hunk named; a deliberate directive block passes.

**Inputs.** `localtest/tools/repin-vendor.py`, every `vendor/UPSTREAM.md`, `planned-components/studioctl-integration.md`
section 1.

**Proposed (2026-10-27).** Task 58's plan makes this check the `check` command of the sync tool, with the pin and the
paths in a JSON block inside `UPSTREAM.md` instead of parsed prose; still deferred until 58 is approved.

**Done (2026-10-27, task 58).** `python3 localtest/tools/vendor-sync.py check --all`; no workflow (the user: no CI for
now). The Dockerfile comparison is not a check: the tool merges or reports upstream's changes to the mirrored files.

## 2. Authentication as an OIDC provider, logout, `pid`

**Goal.** Production Authentication in the stack issues tokens to a registered OIDC client through the
authorization-code flow (what Arbeidsflate's BFF and any other local relying party needs), its logout works, and the
question whether the issued tokens carry `pid` is answered empirically.

**What is known.**

- The cookie token (`AltinnStudioRuntime`, what `/Home/GetTestUserToken` returns) has no `pid` **by design**:
  `ClaimsPrincipalBuilder.GetClaimsPrincipal(…, isAuthCookie: true)` skips it (vendored
  `Helpers/ClaimsPrincipalBuilder.cs:200-202`). Production behaves the same. The "pid is lost" finding in
  `planned-components/dialogporten/arbeidsflate.md` section 3.4 was made on that token and is most likely not a
  defect. The access token the OIDC `token` endpoint issues builds `pid` from the session's `ExternalId`
  (`urn:altinn:person:identifier-no:{SSN}`, `OidcServerService.cs:1327-1337`, `ClaimsPrincipalBuilder.cs:80`). That
  endpoint has never been reached in the stack, because the code flow needs repositories the component lacks.
- `GET openid/logout` answers 500: `OidcProviders:idporten:LogoutEndpoint` is unset and the fake ID-porten has no
  end-session endpoint (verified on the stack, `arbeidsflate.md` section 3.4 item 1).
- `GeneralSettings:OidcRefreshTokenPepper` is `localtest-pepper` in `fakes/LocaltestAuthenticationExtensions.cs:62`;
  production decodes it with `Convert.FromBase64String` (three sites in `TokenService.cs`), so client-secret
  authentication and refresh tokens fail (`shared-prerequisites.md` section 3.3 item 3).

**Scope.**

1. Fakes in `localtest/components/authentication/fakes/Persistence/`: in-memory `IOidcServerClientRepository`
   (seeded from `Localtest:Authentication:OidcClients`, secrets hashed with the production
   `ClientSecretHasher.HashHmac` and the pepper), `ILoginTransactionRepository`, `IAuthorizationCodeRepository`,
   `IRefreshTokenRepository`. Shapes and sizes in `shared-prerequisites.md` section 3.4 (about 350 lines). Registered
   next to the three existing in-memory repositories. No directive sites expected.
2. Pepper: `bG9jYWx0ZXN0LXBlcHBlcg==` (base64 of the same string). Configuration only.
3. Fake ID-porten end-session endpoint in the localtest app (`localtest/localtest/IdPorten/IdPortenApi.cs`):
   `GET localtest/api/idporten/endsession?id_token_hint&post_logout_redirect_uri&state` answers 302 to
   `post_logout_redirect_uri` (with `state`), listed as `end_session_endpoint` in the discovery document;
   `OidcProviders:idporten:LogoutEndpoint` set in the authentication fakes. The fake keeps no login session of its
   own (codes are one-time, 60 s), so there is nothing to invalidate; the picker simply shows again on the next
   `authorize`.
4. A seeded localtest client for tests (id `localtest-smoke`, a plain secret, redirect to a `/Home` helper) and a
   headless helper in the localtest app, `GET /Home/GetTestUserToken?client=<id>…` or a new
   `localtest/api/idporten/…` helper, that drives the production code flow for that client and returns the token
   response. This is what answers the `pid` question and what the Dialogporten smoke test later uses (open question 5
   in `planned-components/dialogporten/README.md`).
5. If the code-flow access token still lacks `pid`: trace `IdentifyOrCreateAltinnUser` (Profile lookup by SSN,
   `UserProfile.Party.SSN` deserialisation in `ProfileService.cs:51`) and report before adding any directive site.

**Decisions (2026-10-20).** Seed two clients: `localtest-smoke` for tests and `arbeidsflate-localtest` with the
redirect, post-logout and front-channel-logout URLs from `planned-components/dialogporten/arbeidsflate.md` section
3.4, so task 11 only adds containers. The headless code-flow helper is a new localtest-app feature,
`localtest/api/authentication/` (not `/Home`, whose contract is the old localtest's and does not grow), e.g.
`GET localtest/api/authentication/token?client=&userId=&partyId=&authenticationLevel=` returning the token response.
A third directive site in Authentication is not acceptable without a report first.

**Verification.** `components/authentication` smoke test extended: discovery lists `end_session_endpoint`; the code flow
for the seeded client (authorize → picker → callback → `POST token` with `client_secret_basic` and PKCE) returns
`access_token`, `id_token`, `refresh_token`; the access token's `pid` equals the picked user's national identity
number; refresh rotates; `GET openid/logout?id_token_hint=…` answers 302 and a later refresh fails; the existing
`goTo` flow and the `/Home` contract are unchanged (`localtest/localtest/smoke-test.sh`, 65 checks). Run
`run-platform-regression.sh` afterwards.

**Inputs.** `planned-components/dialogporten/shared-prerequisites.md` section 3, `arbeidsflate.md` section 3,
`dialogporten-core.md` section 7, the vendored `OidcServerService.cs`, `TokenService.cs`, `ClaimsPrincipalBuilder.cs`,
`PersistanceDependencyInjection.cs`; `localtest/reviews/authentication.md`.

**Phase 1 (2026-10-20, branch `task/authentication-oidc-provider`, e494026..ade5849, reviewed).** Four in-memory
repositories, the pepper, both seeded clients, `endsession` in the fake ID-porten, and the helper under
`localtest/api/authentication/` (`code`, `token`, `refresh`, `callback`). New `components/authentication/smoke-test.sh`,
30 ok as plain processes with a stub Register and Profile. Findings, all production behaviour and mirrored, not fixed:
logout only ends the session found through the `AltinnStudioRuntime` cookie (Authentication cannot validate its own
`id_token_hint`); `post_logout_redirect_uri` is never honoured; a refresh family revoked by reuse detection cannot be
recreated for the same session (500 at `token`); the access token's `scope` is the session's, and includes
`digdir:dialogporten.noconsent`. Nits for phase 2: `LocaltestOidcClients.ConfigurationDefaults` emits hard-coded
`ClientType`, `TokenEndpointAuthMethod` and `SubjectType` strings (`Pairwise`, while the class defaults to `Public`)
instead of the object's values; the Dialogporten smoke-test header still says no token carries `pid` (after 9b merges).
Phase 2 (on the stack, after 9b): `pid` against the real Register, the Dialogporten end-user API check, the
localtest-app and root smoke tests, `run-platform-regression.sh`.

## 3. Local altinncdn.no

**Goal.** Nothing in the stack needs internet access. Today Authentication's `exchange/maskinporten` reads
`https://altinncdn.no/orgs/altinn-orgs.json`, Storage's Studio and metrics endpoints read the same file, and pdf3
loads fonts and the logo from the CDN (`localtest/reviews/README.md`, "Internet").

**Design.** The localtest app serves a committed copy under `localtest/api/cdn/` (or a dedicated host
`altinncdn.local.altinn.cloud`, decide); components get the URL by configuration where upstream has a setting, and
only where upstream hard-codes the host does a `#if !LOCALTEST` site appear (list the sites first, from
`shared-prerequisites.md` section 6). The committed `altinn-orgs.json` is trimmed to the organisations the test data
uses plus `ttd` and `digdir`, or a full copy (open question 7 in the Dialogporten plan).

**Open.** Path on the main host or a dedicated host? Full or trimmed `altinn-orgs.json`? Does pdf3 (monorepo image,
not vendored here) get its fonts through configuration, and if not, is it out of scope?

**Verification.** With the sandbox's outbound network blocked for the containers (or `altinncdn.no` resolved to
nothing), `/Home/GetTestOrgToken` and the Storage endpoints that read the org list still work; the root smoke test
passes.

## 4. Maskinporten token endpoint and the shared `IMaskinportenService` fake

**Goal.** A `POST localtest/api/maskinporten/token` in the localtest app (`client_credentials` with a JWT grant,
answering a token the production `exchange/maskinporten` accepts) and one `IMaskinportenService` fake source file
compiled into the .NET consumers that need a Maskinporten token (the Dialogporten adapter, Dialogporten, later
Correspondence). Contract in `shared-prerequisites.md` sections 1 and 2.

**Decisions (2026-10-20).**

- Client id to organisation number: `consumer_org` in the assertion when present, else a nine-digit client id is the
  organisation number, else the default 991825827 (Digdir). No configured client table. Unknown clients are accepted
  and logged, not refused.
- The assertion's signature, `exp` and `aud` are not checked; only the payload is read.
- The fake's exchange keeps production semantics: `test=true` only when the consumer's `UseAltinnTestOrg` is true.
- The `IMaskinportenService` fake is one canonical source under `localtest/components/shared/fakes/Maskinporten/`,
  **copied** into each consumer's `fakes/` when that consumer is vendored (no shared build context for now); a small
  host under `components/shared/` compiles it against `Altinn.ApiClients.Maskinporten` 10.1.0 and is its smoke test.
  Task 15 is the long-term replacement.
- No consumer exists in the stack until tasks 9 to 11, so the smoke tests are the only callers at first.

**Verification.** `localtest/localtest/smoke-test.sh`: a token from the endpoint exchanges through production
`exchange/maskinporten`; the fake, compiled into a scratch console host, obtains an Altinn token.

## 5. Resource Registry component

**Goal.** Vendor production `Altinn/altinn-resource-registry` as `localtest/components/resource-registry/` (Option A in
`planned-components/dialogporten/resource-registry.md`), on the shared Postgres, deriving app resources from Storage's
application list and the shared `policy-store` volume as production does; retire the stand-ins Access Management and
the PDP carry today (section 3.9); the app runner (studioctl-server's `AppProvisioner`) calls it after the policy push
(section 3.8).

**Design.** As the report: two directive sites, fakes under `Altinn.ResourceRegistry.Localtest`, Dockerfile mirroring
upstream, init script added to `localtest/postgres/`, proxy prefix `/resourceregistry/`, image added to
`.github/workflows/images.yml`, a runtime component in the studioctl patch (`localtest/studioctl/`).

**Decisions (2026-10-20).**

- The production 10-minute application cache in `GetResourceList` goes away locally: a fake replaces the Storage
  applications client (or the cache) so `resourcelist` reflects a pushed app at once. Prefer a replaced service in
  `fakes/` over a directive; no `api/localtest/refresh` endpoint is needed for the cache.
- Non-app resources (today the PDP's `policies/resourceregistry/` seed) are a built-in seed in the registry's fakes,
  applied at start through the production service layer, like the Register's built-in seed. The provisioner does
  not push platform seed data. Correspondence resources wait for task 7.
- A separate `resourceregistrydb` on the shared Postgres (18); the Yuniql run on 18 is the first thing to verify.
- The org list is read from altinncdn.no as in production (task 3 is won't do).
- The registry is the only source for `urn:altinn:resource` policies; the PDP's `resourceregistry/` policy folder moves
  into the seed and its fallback goes.
- The provisioner (studioctl-server `AppProvisioner`, in `localtest/studioctl/src/cli/`) calls
  `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` after the policy push,
  as Designer does; the runtime gets the component spec, image and proxy prefix there too.

**Steps (decided 2026-10-20).** 5a: the component, its seed, the studioctl runtime integration (spec, image, proxy
prefix, health, provisioning call) and the smoke test, with the Access Management and PDP stand-ins untouched. 5b: the
stand-ins removed, Access Management and the PDP on the production clients, the six platform specs re-run.

**Open.** The `updated` feed's start for Access Management's sync (5b).

**Verification.** `components/resource-registry/smoke-test.sh` as planned in section 6.4; `run-platform-regression.sh`
still passes; Access Management's refresh still produces the same `app_{org}_{app}` resources.

## 6. Notifications

**Goal.** Apps that order notifications (`notifications/api/v1/orders…`) get production answers instead of a 404, and a
tester can see what was ordered.

**Plan (2026-10-20).** [`planned-components/notifications/notifications.md`](planned-components/notifications/notifications.md).
The Kafka premise was stale: upstream `altinn-notifications` (pin a4aa959) uses Azure Service Bus through Wolverine,
reached from the orders API host through one registration call and three publisher classes; the Email and SMS hosts
sit behind the queue and are not needed locally. Recommendation: Option A, vendor the orders API host
(`components/api` + `components/shared`) with capture fakes in place of the publishers (every would-be e-mail and SMS
recorded under `notifications/api/localtest/`, the production status machinery running on the shared Postgres), two
directive sites (one additive, one `#if !LOCALTEST`), about 1 400 lines of fakes, 6 to 7 days including the Profile
contact-point lookups the Register component must gain (four endpoints) and the studioctl wiring. Verified with a
scratch build and start of the host; the 104 migration folders have not been run on Postgres 18. The capture fake
must apply send results after the claim transaction commits (section 6.4).

**Follow-up found while verifying (2026-10-20), resolved by task 18 on 2026-10-21 (v3 seed: `+47` numbers, valid numbers for the four persons, official organisation addresses, `reserved`).** Production Notifications rejects bare eight-digit SMS
numbers (direct recipients: 400; Profile contact points: dropped, `Failed_RecipientNotIdentified`) and adds `+47` only
to numbers starting with 4 or 9, so the seed numbers of users 1337, 1002, 1003 and 12345 (`87654321`-style) never
receive an SMS. Organisation recipients always get 422 at registration because the seed has no official organisation
addresses. Both are seed-data changes in the Register component (`testdata/`), not fakes; the old localtest's apps read
the same seed, so check what else depends on those numbers before changing them. Open questions in section 6.14.

**Decisions (2026-10-20).** Option A as planned. Every captured e-mail and SMS is marked **Delivered** for now
(section 6.14 question 1). The capture is modelled as **inboxes**: one per e-mail address or phone number, each
listing its messages under `notifications/api/localtest/inboxes/…`; a later version adds reception on/off per inbox
with a default for new inboxes (an inbox with reception off makes its messages fail), so the data shape carries a
`receiving` flag now, always true. **No number normalisation** (question 2, decided 2026-10-20): the Profile fake
returns the seed's numbers and addresses exactly as stored, inboxes are keyed by the exact string the message is
addressed to (any number, any text), and whatever production Notifications does with a bare eight-digit number is
production behaviour to observe and document, not to mask in a fake; if it rejects them, the seed data changes. The four Profile contact-point lookups live in the Register component's fakes (question 3). No failure
simulation and no reserved-recipient flag yet (questions 4 and 5; the inbox toggle covers failures later). The
condition-check client keeps the shared Maskinporten fake (question 6, consistent with task 4). Instant and composed
orders captured like the rest, attachments by reference (question 7). Correspondence does not share the capture
GUI (question 8). The send result is applied after the claim transaction commits, through an in-process channel
(section 6.4).

## 7. Correspondence

**Goal.** Apps that send correspondence (`correspondence/api/v1/…`) get production answers, and the result is visible
locally (and later in the inbox, task 9 to 11).

**Design to settle.** Production `Altinn/altinn-correspondence` needs Postgres, Azure blobs (the Storage component's
file-backed blob pattern applies), Dialogporten, the Resource Registry, Notifications and Maskinporten. It therefore
comes after tasks 4, 5, 6 and 9. Research done 2026-10-20: `planned-components/correspondence/` (brief in its README).

**Decisions (2026-10-20).** Vendor at `6dcf561` (Option A) in its own optional group `correspondence`, which implies
`dialogporten` and shares its Redis (`defaultDatabase=1`); the default stack is unchanged, no fake-only variant.
Plan questions 3 to 9 settled as proposed: the KRR stand-in is an anonymous `register/api/localtest/krr/rest/v2/personer`
in the Register fakes answering "NEI" for every seeded person (a `reserved` seed flag later, shared with Notifications);
the two Profile `correspondence/` lookups in the Register fakes require the production scope
`altinn:profile/correspondence.notificationsettings.read`; the seeded tt02 policies get `ttd` beside `digdir` in the
service-owner rule, marked as a localtest deviation (the org token stays `ttd`); `apps-correspondence-integrasjon2`
is seeded as tt02 has it (`GenericAccessResource`, refused by production too); base images pinned by digest as a
`localtest:` deviation; attachment download from Arbeidsflate goes to task 11; failure visibility is the inspection
API's `jobs` view, no directive for Hangfire retries. Host port 8033, route `/correspondence`, Postgres init
`70-correspondence.sh`.

**7b, party ids (done 2026-10-21, commits 84723f6..0d3361b: one `#if !LOCALTEST` site in `XacmlRequestFactory.cs` accepting any positive 64-bit party id; the Correspondence smoke test has 64 real checks, 67 with the restart; `signering-brukerstyrt`'s call-to-sign and receipts go through production Correspondence; upstream fix drafted in `party-ids.md`, to be filed by the owner).** `planned-components/correspondence/party-ids.md`: production Correspondence
names the party by id in every PDP request and guesses the id's type by length (8 or 10 digits), so the seed's 6-digit
party ids give a 500 on app-lib's send and on the recipient's dialog-token path (two KNOWN checks in the smoke test;
the service-owner token bypasses the PDP). Recommended: one `#if !LOCALTEST` site in `XacmlRequestFactory` accepting
any unsigned 32-bit party id (the component's first directive), the two KNOWN checks turned into assertions, the
`signering-brukerstyrt` call-to-sign verified; in parallel an upstream issue (draft in the report) so Correspondence
sends `urn:altinn:partyid:{id}`. Not renumbering the seed. Needs the owner's approval of the directive.

## 8. Inspection GUI

**Goal.** One page in the localtest app that shows what the components' `api/localtest/` endpoints expose: test users
and parties (Register), events and webhook deliveries (Events), the refresh state and seeded roles (Access
Management), app routes and upstreams (proxy), later notification and correspondence captures. Replaces the old
localtest's StorageExplorer and debug pages for the things developers actually look at.

**Design to settle.** Static HTML plus fetch in the localtest app (Native AOT: no server rendering frameworks, no
build step, source-generated JSON only), under `/localtest/` with the data read from the components through the
front door, or a small SPA built in CI? Which endpoints are missing for a useful view (Storage has no `api/localtest/`
today: instances by app would need one, or pgAdmin stays the answer)?

**Open.** Scope of the first version; whether Storage gets a localtest listing endpoint.

## 9. Dialogporten core

Vendor the WebApi, GraphQL and Service hosts per `planned-components/dialogporten/dialogporten-core.md`: build chain,
Dockerfiles, the fourteen `LocalDevelopment` flags, three additive `Program.cs` hooks and two directive sites, Redis,
a database on the shared Postgres 18, routes, smoke test on the service-owner API with the org token. Tasks 4 and 5
are done, so dialogs for `app_ttd_testapp` can be created from the start. End-user smoke uses the `pid` token of
task 2 (done: the last checks of the authentication smoke test).

**Decisions (2026-10-20).**

- Opt-in: the three hosts and Redis form an optional component group in the studioctl runtime, started with a flag
  (`env up -r localtest-v2 --with dialogporten`); the provisioner's Dialogporten step is skipped when the group is off.
  Apps never call Dialogporten, so the default stack is unchanged (about 1 GB RAM saved).
- Maskinporten: task 4's shared fake (`localtest/components/shared/fakes/Maskinporten/`) copied into the component's
  fakes, production semantics (`UseAltinnTestOrg` false, Dialogporten acts as `digdir`; `ttd` shares Digdir's number
  in the registry so the owner check passes). Not the report's `/Home/GetTestOrgToken` variant.
- Database: `dialogporten` with its own role on the shared Postgres 18 (no `uuidv7()` shim). **No reset on start**:
  dialogs persist across restarts like Storage's instances, and the only reset is `studioctl env reset -r localtest-v2`,
  which deletes the shared Postgres volume and therefore cleans Storage and Dialogporten together (decided
  2026-10-20; instances and their dialogs must never get out of step).
- The `/dialogporten/graphql` → `/graphql` rewrite is a path-rewrite option on the route type in studioctl-server's
  proxy (vendored source), the only route that needs it; it mirrors APIM in production. **Note for task 11
  (Arbeidsflate):** the browser-side GraphQL subscription stream and the BFF's `DIALOGPORTEN_URL` go through this
  route; if WebSockets or the URL shape turn out not to fit, the rewrite (or a dedicated host) may have to change.
- Redis stays, inside the opt-in group (one container, about 10 MB): upstream connects to it eagerly at start in all
  three hosts for HotChocolate GraphQL subscriptions (the Service host publishes, the GraphQL host subscribes, so an
  in-memory transport would never deliver a live update), and uses it as FusionCache's distributed tier and
  backplane and as a health check. Removing it would cost two directive sites in the shared Infrastructure project
  (subscriptions to in-memory, cache registration off) and lose live inbox updates. The component README must say this.
- Fresh pin of upstream `main`; three images from one vendored tree (Dockerfiles mirroring upstream's), three matrix
  entries in the image workflow.
- Steps. **9a**: vendor, build chain, Dockerfiles, Postgres and Redis, hooks and fakes, the optional group in
  studioctl, boot to healthy, the service-owner smoke (create a dialog for `app_ttd_testapp` with the org token,
  read, search, delete; wrong scope and foreign resource refused). **9b**: the provisioner's `sync` step, the cache
  and reset endpoints, the Service host's events into the Events component, GraphQL through the rewrite, CORS for
  `http://af.local.altinn.cloud:8000`, and what Events does with Dialogporten's sources (plan question 9).
- As the report: the fourteen `LocalDevelopment` flags set explicitly, warmup off, committed Ed25519 test key pairs,
  Serilog at Information, the CDN read from the internet, no Janitor and no migration image.

## 10. Dialogporten adapter and the Storage outbox

Vendor the adapter per `planned-components/dialogporten/dialogporten-adapter.md` (four small directive sites plus
additive hooks, prototype in `adapter-prototype/`), switch Storage's outbox on in its fakes, the in-process Wolverine
path POSTing each command to a localtest-only enqueue endpoint on the adapter, which publishes it to the adapter's own
local queue so the production sync handler and its retry and error policies run unchanged. Needs 4, 5 and 9a.

**Decisions (2026-10-20).**

- **Storage always sends.** The Storage fakes stop forcing `WolverineSettings:EnableSending=false`; the outbox and the
  handler run in the default stack too. When the adapter is not running (the `dialogporten` group is off) the handler
  treats an unreachable adapter (connection refused, 502/503/504, or the proxy's no-route answer) as "not enabled":
  one log line, the command dropped, no retry storm, nothing else affected. The adapter joins the `dialogporten`
  group; nothing in Storage's configuration depends on the group.
- **The enqueue endpoint is anonymous.** Only production endpoints carry authentication; every `api/localtest/`
  endpoint in the stack is open, this one included. Storage's handler therefore needs no token for the enqueue (the
  earlier question about `/Home/GetTestOrgToken` in Storage is moot). The adapter's own calls to Storage, Register
  and Dialogporten keep their production tokens through the shared Maskinporten fake.
- **Archived instances' receipt link.** Served by the `receipt` group since task 16; without the group the front door answers 503 (earlier decision: accept the 404).
- **Outbox delays.** Local values `LowPriorityDelaySecs=2`, `HighPriorityDelaySecs=0` (production 60 / 5), by
  configuration: testers should see that a sync is not instant, but not wait a minute. **Every place where localtest
  is faster than production is documented** in one list, `localtest/README.md` section "Where localtest is faster
  than production" (this delay, the Resource Registry's removed 10-minute application cache, Dialogporten's
  cache-clear endpoint against its 15- and 20-minute caches, the Events component's immediate delivery, and whatever
  else exists), each with the production value; new components add their rows.
- **Verification.** The adapter smoke test covers the service-owner-visible path: instance created → dialog within
  10 s with the right resource, party and go-to URL, `dataValues["dialog.id"]` on the instance; process start and a
  data upload → attachment and activity; process end → status change (and a transmission); soft delete → `deleted:
  true` for the service owner; manual `syncDialog/simple` idempotent; the default stack with the group off: an instance
  created produces one log line and nothing else, and `run-platform-regression.sh` stays green. **Dialog-token steps
  are in 10 (decided 2026-10-20, task 2 lands first):** the smoke test takes a code-flow access token for user 1337
  from `GET localtest/api/authentication/token`, reads the dialog through `GET dialogporten/api/v1/enduser/dialogs/{id}`
  for its `dialogToken`, and runs the adapter's two token-validated endpoints with it: the transmission receipt
  (`GET storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}`, which the adapter renders itself as
  Markdown from Dialogporten, Storage, Register and the CDN: expect 200; corrected 2026-10-20 by the Receipt research,
  the 404 accepted earlier concerns only the go-to link of archived instances, `/receipt/{partyId}/{guid}`, until
  task 16) and the delete (`DELETE storage/dialogporten/api/v1/instance/{owner}/{guid}` → Storage soft delete →
  outbox → dialog deleted). No localtest source mints dialog tokens.
- **Organisation list.** The adapter reads `altinn-orgs.json` from the real CDN, as Dialogporten does (task 3 is
  won't do); no committed copy.
- **Re-sync button** per instance (`syncDialog/simple`) is an input to task 8, not part of 10.

**Known pain point, not ours to fix (decided 2026-10-20).** Dialogporten's service-owner search names the caller's
organisation by the first short name the Resource Registry lists for 991825827 (`digdir`), while dialogs for `ttd`
apps carry `ttd` (found in 9a): a `ttd` service owner's search does not find its own dialogs. This is production
behaviour (`ttd` has no organisation number and the registry substitutes Digdir's); the stack does not fix production
issues, so it stays, documented in the Dialogporten and adapter READMEs as a pain point testers will meet.

## 11. Arbeidsflate

Vendor the pnpm monorepo per `arbeidsflate.md`: pristine tree plus two patches, dedicated host `af.local.altinn.cloud`,
nginx in front of the BFF with `X-Forwarded-Proto: https`, Postgres and Redis, login through the OIDC client from
task 2. Needs 2, 9 and 10 (all done; 14 gives the profile page production Profile).

**Decisions (2026-10-21, discussed).** Host name `af.local.altinn.cloud:8000` (studioctl maps every ingress host of the
topology to the host gateway inside the containers, so the authentication container's front-channel logout reaches
it without a hosts entry). The two patches stay as `localtest:` patches; nothing is proposed upstream now. Images are
built locally from the patched tree so they work on plain http; no pulled images. Its own optional group
`arbeidsflate` implying `dialogporten` only (dialogs come from other sources than Correspondence). The profile page is
in scope, served by production Profile. Internet dependencies stay (task 3). Pin: upstream `main` at dispatch.
Postgres init `90-arbeidsflate.sh`, the shared Redis on its own database. **11b, Correspondence attachments from the
browser.** Researched 2026-10-21 in the Arbeidsflate README's "Correspondence attachments" section: the inbox opens
attachments as plain links with cookies; Correspondence then signs the user in through production Authentication's OIDC
provider and validates the token with `IdportenTokenValidator`, whose document retriever requires https
(`IdportenTokenValidator.cs:26`), so plain http loops. Fix: one `#if !LOCALTEST` site (`RequireHttps = false`), a
seeded `correspondence-localtest` OIDC client with scopes `openid profile`, and `SameSite=Lax` /
`SecurePolicy=SameAsRequest` correlation and nonce cookies in Correspondence's fakes. **Done 2026-10-21** (commits 789edcd3..8e510e91: the site at `IdportenTokenValidator.cs:26-31`, the seeded client
`correspondence-localtest`, Lax cookies in the fakes; the Correspondence smoke test has 76 checks, 79 with the restart;
the download clicked in Chromium from the inbox; pin bump pending the images workflow).

## 12. Review leftovers

Small items from `localtest/reviews/` and the components' "Known gaps", each its own brief:

- Localtest app: `HEAD` on most endpoints; a WebSocket test in `localtest/localtest/smoke-test.sh`.
- Events: `DELETE events/api/localtest/events` does not drain Wolverine's in-memory queues; Slack deliveries recorded
  without their event.
- Access Management: a stub for the SBL bridge's Altinn 2 roles calls (today a proxy 404), if any flow needs it.
- Authorization smoke test: its closing "PDP log errors" line reads the compose-era container name
  `localtest_authorization_1`, so under studioctl it always reports 0; read `localtest-v2-authorization` instead.
- Authorization and Events: Debug logging is very noisy under `ASPNETCORE_ENVIRONMENT=Development`; a localtest
  logging override.
- Workflow engine: state reset (left as decided; revisit only on request).
- **Decision (2026-10-21):** work through the items below as one filler task. A test app's policy that production
  code rejects (the `stateless-app` case) is not fixed here: it is recorded in `localtest/reviews/test-apps.md` with the
  rule and the parser's reason, for an upstream fix in the monorepo later.
- Found by the 2026-10-21 documentation audit (task 19):
  - Authorization smoke test: its "store:" line has the same compose-era container name as the "PDP log errors" line.
  - Storage's standalone `smoke-test.sh` cannot run since 2026-10-20: it calls the `/Home` token helpers on
    Authentication, which moved to the localtest app, and sets a setting that no longer exists. Port it or delete it.
  - Events: `registeredTime` is stored as Oslo time because upstream's column default takes `now() at time zone 'utc'`
    on a Postgres running with `TZ=Europe/Oslo`; the read API's 30-second delay therefore never holds anything back
    locally. Fix the container's timezone or document it as a localtest difference (read from the code, not tested).
  - Events: `AppsDomain` treats every Dialogporten source as one app (9b finding); a `LOCALTEST` site or a separate
    host name for apps.
  - Resource Registry: `policy/rules` (and probably `policy/rights`) answers 500 for `stateless-app`'s policy, whose
    rule 3 mixes action and resource matches in one `AnyOf`; production's parser throws on it. Known gap in the README.
  - Workflow engine smoke test: its default `--image` is `localtest-workflow-engine:dev`, which nothing builds; its
    README said Postgres 16.
  - Dockerfile comments in register, authorization, access-management and workflow-engine still mention compose
    healthchecks and mounts; `groups.go`'s dialogporten comment omits the adapter and overstates the memory saving.
  - `localtest/studioctl/UPSTREAM.md` names only the older fork branch; the current one is
    `feat/studioctl-localtest-v2-stack` (pinned at sha-55ce57f, lacking the Correspondence and Profile commits).
  - Correspondence smoke test (seen 2026-10-21 on the pinned images): the first run after `env up` fails one check on
    Hangfire job timing ("reaches Published" once, "the opened and download activity jobs have run" once); the
    second run passes 64 of 64. Raise the waits so the first run passes.

## 13. studioctl integration leftovers

From `planned-components/studioctl-integration.md`, sections 1, 4, 5, 6 and its open questions, against the vendored
studioctl in `localtest/studioctl/src/cli/` (upstreamed later as one patch, see `localtest/studioctl/UPSTREAM.md`): moving the stack into the monorepo (`src/Runtime/localtest-v2/`) with its image workflow, the
`/health` body (`ok` versus the integration tests' `Healthy` snapshot), docs and the bundled skill, Cypress and
`StudioctlEnvironment` tests in CI against the new runtime, the rollout and the default flip, re-push on app change
(watch, restart or `studioctl app sync`), frontend version override and cookie-domain rewriting (port or drop). The two
front-door gaps seen on 2026-10-20 (studioctl-server's 404 for a platform prefix without its trailing slash and for
`/app-frontend/`) are gone: since task 27a studioctl-server no longer proxies, and the front door (the localtest
container) redirects a bare prefix to its slashed form and serves `/app-frontend/`. The maintainer's; not for a subagent until
split into pieces.

## 14. Profile beyond the Register

Done 2026-10-21: production Profile runs in the default stack (`localtest/components/profile/`, port 8036, route
`/profile`); the Register's Profile controllers are gone. Original question (2026-10-20): the Register component
answered the Profile endpoints Authentication and Authorization call, apps and Dialogporten use more of Profile,
and the research was to decide between vendoring `Altinn/altinn-profile` and extending the Register fakes.

**Conditional approval (2026-10-20).** If the research finds vendoring `Altinn/altinn-profile` (or the alternative it
recommends) straightforward under the vendoring model, dispatch the implementation as a follow-on task after the
running tasks (10, 16, 7) are verified, with the plan's "Proposed brief" and the inherited rules, without a further
round. Otherwise report back.

## 15. Localtest URLs in the Maskinporten client package

**Goal.** `Altinn.ApiClients.Maskinporten` accepts a configured token endpoint and exchange endpoint (or a custom
environment) so that a consumer can point it at the localtest app and production Authentication by settings alone,
and the compiled-in `IMaskinportenService` fake from task 4 can be deleted. An upstream change in
`Altinn/altinn-apiclient-maskinporten` (the `switch` tables in `MaskinportenService.GetTokenEndpoint` and
`GetTokenExchangeEndpoint` throw for unknown environments; `MaskinportenSettings` has no URL fields), then a version
bump in each consumer. Also covers the BFF, which is already configurable, and Correspondence's own token service.

**Outcome (2026-10-23, task 15 phase 1).** Upstream change prepared: `TokenEndpoint` and `TokenExchangeEndpoint` on
`MaskinportenSettings`/`IMaskinportenSettings` (empty = unset; absolute http(s) URL validated with the property
named), used before the `Environment`/`TokenExchangeEnvironment` switches; the assertion `aud` follows `Environment`
when set, else the token endpoint's base; `UseAltinnTestOrg` appends `test=true` correctly; a new xUnit test project
(25 tests, the repo had none; CI does not run tests) and a README section. Interface addition is a minor-version break
in the repo's own precedent (#83). The fork was refused (403), so the owner forks and pushes (steps in the hand-over);
release is tag-driven (`v10.2.0` asked for). Follow-up after the release: bump Dialogporten, Notifications, the
adapter, Correspondence and the shared copy, delete their `IMaskinportenService` fakes, and give each a throwaway RSA
JWK (the placeholders `e30=`/`{"kty":"RSA"}` will fail once the package signs). Coordinator ran the tests.

**Released (2026-10-26).** Not through this branch: another contributor's
[altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92) (task 47) merged
and was released as `v10.2.0` with `TokenEndpoint` and `AssertionAudience`. The consumer follow-up above applies to
10.2.0; it is tracked as items in `localtest/upstreaming/` (the `Altinn.ApiClients.Maskinporten` theme in its README).

## 16. Receipt

**Goal.** Vendor `Altinn/altinn-receipt`, the platform receipt app, so that archived instances' receipt links
(`/receipt/{instanceOwnerPartyId}/{instanceGuid}`, which Dialogporten's go-to action and the adapter emit for archived
instances, and which the app frontend links to) work locally instead of answering 404 (decided 2026-10-20 in task
10). Research done 2026-10-20: `planned-components/receipt/` (brief in its README).

**Decisions (2026-10-20).** Its own optional group `receipt`, implying nothing (not the default stack). Accept the
`selfLinks` rewrite fake. "Tilbake til innboks" stays as upstream has it, raised upstream with task 11. Pin `babc0ed`,
re-pin on backend changes only. Image builds may reach the npm registry. Host port 8034, route `/receipt`.

## 17. Admin of running services and the inspection GUI

**Goal.** Find out whether Altinn Studio's administration of running services (the Designer `admin` frontend,
`src/Designer/frontend/admin`, and its backend) overlaps with the planned inspection GUI (task 8), and whether it
could be pointed at the localtest stack instead of building a separate page. Research only, under
`planned-components/admin-running-services/`: what the admin frontend shows and calls today, what it would need
from the stack, what task 8 would still add, and a recommendation.

**Conditional approval (2026-10-20).** If the research finds that vendoring the admin frontend and its backend into
the stack as an optional group (`admin`, implying whatever it needs) is straightforward, dispatch that vendoring and
inclusion as a follow-on task once 10, 16 and 7 are finished and verified, without a further round of discussion;
the brief is the plan's "Proposed brief" plus the inherited rules. If it is not straightforward (Designer backend
coupling, Gitea or Kubernetes dependencies, auth model), report back and 8 is reconsidered instead.

## 18. Test data format: docs, JSON schema, extended baseline

**Goal (2026-10-20).** One folder documenting the `testData.json` format the stack reads (the Register component's
seed `components/register/testdata/testData.json` and every app's `App/wwwroot/testData.json`): a Markdown
reference of every field with its consumer, and a JSON Schema the files declare in `$schema`. The schema includes
the extensions decided so far: persons' `reserved` flag (KRR reservation; Notifications plan question 5, task 7's
stand-in), mobile numbers in full international form (`+47…`, because production adds `+47` only to numbers starting
with 4 or 9 and rejects bare eight-digit numbers), organisations' official notification addresses (e-mail and SMS, for
Notifications' organisation recipients, 422 today), and an extension point for the Profile data task 14 identifies.
The baseline seed is modified to use them (ids, names and `userId.partyId` pairs unchanged: Cypress depends on
them), `generate-testdata.py` follows, and the Register fakes load the new fields. The KRR stand-in reads
`reserved` once task 7 has merged.

**Phase 1 (2026-10-20, branch `task/testdata-format`).** Folder `localtest/testdata/` with `README.md` and
`test-users.schema.v3.json` (2020-12, `additionalProperties: false`, `$id` in the altinncdn.no line but unpublished;
v2 was never published, the v1 URL is used by the old localtest's exports and the docs, nothing validates at run time).
Seed on v3: `+47` on every number, `reserved: true` on Rik Forelder (1003) only, official addresses for DDG Fitness
and Digitaliseringsdirektoratet, generator back in step with the file. Kept deviation: the four persons whose numbers
were `87654321`-style get valid Norwegian numbers (`+4799001337` etc.), because libphonenumber in production
Notifications rejects `+4787654321` and the seed would still never receive an SMS; nothing in Cypress or the apps
uses the old digits. Done in phase 2 (96f1917): `reserved` is mirrored into `isReserved` of `users/contactpoint/lookup`, and Profile's KRR sync carries it on since task 14. 7 must rebase onto this: its KRR stand-in answers `JA` for reserved persons, its
`correspondence/organizations/notificationaddresses/lookup` calls the official addresses, its smoke expectations change.

## 19. Documentation bookkeeping

**Goal (2026-10-21).** Every README, review and plan states only what is true of the current behaviour: ports, routes,
groups, which component serves what, smoke-test counts, known gaps, pins, "not yet" and "pending" remarks. Historical
sections stay, dated, as history. Four agents by folder: the root and shared docs (`README.md`, `localtest/README.md`,
`localtest/localtest/`, `localtest/testdata/`, `localtest/studioctl/`, `localtest/postgres/`, `localtest/certs/`,
`localtest/smoke/`), the components of the default stack from before Dialogporten (register, authentication,
authorization, access-management, storage, events, workflow-engine, shared, `localtest/reviews/`), the newer
components (notifications, resource-registry, dialogporten, dialogporten-adapter, receipt, profile; correspondence is
left to task 7b), and `planned-components/` (status headers saying what was implemented and which statements are
superseded, never rewriting the research). Agents report what they could not verify and anything stale in `TODO.md`,
which only the coordinator edits.

## 20. Aspire host for the components

**Goal (2026-10-21).** Find out whether the stack's components can run as container resources of a .NET Aspire
AppHost next to an app project, so an app developer gets the platform from `dotnet run` on the AppHost instead of
`studioctl env up`: what Aspire would need (the images from the registry, the environment studioctl's `components.go`
sets, the front door that studioctl-server provides, the provisioning steps, test data, groups), what it cannot give
(the Unix-socket server, the app runner's provisioning), how app-lib or the app template would reference it, and
whether this competes with or complements studioctl. Research only, under `planned-components/aspire/`, with a
recommendation and a proposed brief.

## 21. Aspire host package for apps

**Goal (2026-10-21, owner's direction).** A separate Aspire hosting package that an app imports in a single-file AppHost
(`var localtest = builder.AddAltinnLocaltest(); builder.AddAltinnApp<App>(localtest);`) to get the platform
components as container resources beside the app, with the same images, settings and provisioning as studioctl. Both
runtimes read one **common manifest** (image tags, ports, environment, mounts, dependencies, groups, routes, init
scripts) and share the **setup functions** (the provisioning steps). The front door no longer strips `Secure` from
cookies: the components that set `Secure` cookies get `#if !LOCALTEST` blocks instead, so a plain YARP (or the localtest
app's proxy) can be the front door. The AppHost package reads the app's `testData.json`, policy, metadata and texts and
coordinates the registration in Storage, the PDP, the Resource Registry, the Register, Access Management and
Dialogporten, as the provisioner does today. Research: `planned-components/aspire/` (its option 2, the generated
layer).

**Sub-items (each its own brief once approved).**

- **21a, the manifest.** Define `localtest/manifest.json` with a JSON schema as the single source of truth: components
  (name, image repository and tag, container port, host port, environment with templated references such as
  `{platform}` and `{postgres}`, mounts, dependencies and health, group), groups with implications, routes (host,
  prefix, rewrite), Postgres init scripts, the certificate. studioctl embeds it (`go:embed`) and `components.go`
  becomes a loader with a test that the two agree during the transition; the pin `tagLocaltestV2` moves into it.
  Decide templating and secrets handling.
- **21b, cookies without the proxy.** Inventory every `Secure` cookie the components set (Authentication's
  `AltinnStudioRuntime` and session cookies, Correspondence's OIDC cookies, the Arbeidsflate BFF's session cookie
  (Node: a patch), the localtest app's own cookies, app-lib's XSRF cookie if any), add `#if !LOCALTEST` blocks or fake
  configuration so they are not `Secure` on plain http, then remove the stripping from studioctl-server and the
  localtest app's proxy. Report before each new directive site, as the model requires.
- **21c, the shared provisioning library.** Extract `AppProvisioner`'s steps (register, Storage metadata and texts, PDP
  policy, Resource Registry subject reload, Access Management refresh, Dialogporten sync) into a .NET library both
  studioctl-server and the Aspire package reference, reading the app's `testData.json`, policy, metadata and texts from
  the app folder or the running app, as today.
- **21d, the front door for Aspire.** `Aspire.Hosting.Yarp` with routes generated from the manifest (host + prefix,
  path rewrite) and the app routes the provisioner adds; the 503 answer for groups that are off is deferred; `local.altinn.cloud:8000` reachable from
  the containers (Aspire network aliases, `--add-host` under Podman).
- **21e, the package.** `Altinn.App.Aspire` (decided 2026-10-22, see the 21d decisions): `AddAltinnLocaltest(options)` adds the containers
  from the embedded manifest with groups, Postgres and Redis, the certificate, waits and health; `AddAltinnApp<T>()`
  sets the app's environment (platform URLs, test certificate), runs the provisioning after the app is healthy, and
  exposes a dashboard command to rerun it; `env reset` as a command; a sample single-file AppHost for a test app; the
  app-lib `studioctl app env --json` import (app-lib v9 in Development, overrides AppHost values unless
  `STUDIOCTL_APP_RUN` is set) handled; Podman verified. Published to GitHub Packages from this repo for the prototype.
- **21f, docs and parity.** A CI check that the manifest, studioctl and the package agree; docs in `localtest/README.md`
  and, when adopted, altinn-studio-docs; the smoke tests runnable against an Aspire-hosted stack.

**Decisions (2026-10-21, discussed one by one).** The checked-in `localtest/manifest.json` with a JSON schema is the
source of truth; studioctl embeds it and `components.go` becomes a loader, with a transition test that the loader
reproduces today's table; the pin moves into it. The Aspire front door is `Aspire.Hosting.Yarp` with routes generated
from the manifest (host + prefix, rewrites), plus the app routes the provisioner adds; the 503 for known components whose group is off is **deferred** for the
Aspire front door (owner, 2026-10-21: low priority), so a request to one falls through to whatever YARP does by default,
documented. The shared provisioning library reads the app's files from disk first (the Aspire
package knows the project path) and falls back to the running app's API (studioctl-server's case). app-lib's import of
studioctl's environment: the package sets `STUDIOCTL_APP_RUN` now, and an app-lib change (honour an explicit localtest
environment) is filed as a follow-up (task 22). 21b runs as one agent: inventory, the sites with a report per
component in its README, then the stripping removed from both proxies; the owner reviews the inventory in the report
before merge. Prototype scope: default stack plus `dialogporten`, one sample test app, the smoke tests against the
Aspire-hosted stack, Podman verified; package `Altinn.App.Aspire` published to GitHub Packages from this repo.
Order: 21a and 21c first, 21b in parallel (code-only), then 21d and 21e, 21f last. Dispatched 2026-10-21 (21a, 21b, 21c).

**21d decisions (2026-10-22, discussed one by one).** Front door: stock `Aspire.Hosting.Yarp`, accepting that it cannot
strip `Secure` from app-lib's `keepAlive` cookie (no cookie transform in the stock image): apps on the Aspire host lose
the ten-minute token refresh over http until task 22 ships; documented in the sample AppHost. Reachability: `--add-host <host>:host-gateway`
on every component as studioctl does (the manifest's `ingressHosts` and `extraHostGatewayNames`), the front door's host
port published on 8000; network aliases on the YARP container are the fallback if `WithContainerRuntimeArgs` fails
under Podman (reported, not silently switched). The app route: YARP routes to the app project resource and Aspire's
container tunnel carries it to the host, the app stays on loopback; `host.docker.internal` with the app on `0.0.0.0` is
the documented fallback if the tunnel fails under Podman (reported). **Outcome (21d phase 2):** the tunnel image does
not build on this Podman, so the fallback applies: the app binds `0.0.0.0` on its Aspire-assigned port and the front
door's route targets `host.docker.internal:<port>` through `--add-host`. The package is `Altinn.App.Aspire` at
`localtest/aspire/Altinn.App.Aspire/` (owner, 2026-10-22), outside the vendored studioctl tree, so it is this
repository's own and not part of the studioctl upstream patch; it embeds the manifest from the Go tree by relative
path (`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, single copy) and references the
provisioning library's sources by relative `<Compile Include>` (as studioctl-server includes Runtime.Common), so the
NuGet is one assembly with no unpublished dependency (owner, 2026-10-22); the C# manifest reader lives in the package
and its test compares against the Go golden file by relative path. Scope of 21d: the first slice of the package (skeleton, the
C# manifest reader tested against the Go golden file, `AddAltinnLocaltest()` with the default stack plus the
`dialogporten` group, the YARP front door), proven by `aspire run` under Podman and the root, authentication and
dialogporten smoke tests through port 8000, no app yet; `aspire run` is proven with Postgres alone first, since Aspire
has never run on this machine. 21e: `AddAltinnApp<T>()`, provisioning hook, commands, sample AppHost, publishing.

**21e decisions (2026-10-22, discussed one by one).** The app's environment moves into the manifest as an `app` section
(templated values for what `studioctl run` sets in `internal/cmd/app/env.go`: the platform endpoints, host name and
base URL, the OpenID well-known endpoint, the certificate variables, the secrets variables), `studioctl run` becomes a
loader of it with a golden test that its generated environment is unchanged, and the package reads the same section;
the development app-codes file stays something each runner writes, with a test that the package's bytes equal
studioctl's. The app project: `AddProject<T>` with `ExcludeLaunchProfile` and `ExcludeKestrelEndpoints`, one HTTP
endpoint with an Aspire-assigned port, proxied; no fixed 5005 under Aspire; the front door reaches the app through the
project resource (the tunnel) and the Kestrel URL is the runner's, not the manifest's. Provisioning: on the app's `ResourceReadyEvent` with a `DirectoryAppSource` on the project folder,
a failure logged with step and URL and shown as a warning on the app resource, not as a failed start; dashboard
commands `provision` (again) and `deprovision`; no deprovision on stop (every step is idempotent; the Register keeps
the last run's test users as Storage and the PDP keep the app, documented); `DialogportenRunning` from the AppHost's
groups; a `reset` command on the localtest resource stops the persistent containers and removes the five named
volumes through the runtime CLI; the package refuses to start when port 8000 is held and names the holder. Sample: a self-contained app under `localtest/aspire/samples/` (the `smoke/apps/ttd/testapp` fixture
completed with the template's `Program.cs`, a project on `Altinn.App.Api` from NuGet, `testData.json`, the template's
`appsettings.json` and `launchSettings.json` unchanged) with the single-file `apphost.cs` beside it; the monorepo's
`frontend-test` is a phase-2 verification target through a scratch AppHost only if a `net11.0` AppHost builds. The package sets `STUDIOCTL_APP_RUN=1` and `AppSettings__UseOpenTelemetry=true` (an option turns the
latter off), so the app's telemetry reaches the dashboard; whether the components export OTLP is a noted follow-up,
not 21e's. The frontend dev server (`--dev-frontend`) is out of the prototype; the sample loads app-frontend from the
CDN as the template does. Publishing: a workflow on `localtest/aspire/**` runs the tests, packs `0.1.0-alpha.<run>` and pushes to the
`ivarne-agent` NuGet feed on GitHub Packages with `GITHUB_TOKEN` (consumers need a `read:packages` token, documented),
and attaches the `.nupkg` to a GitHub release for a token-free path; the sample stays on `#:project` in the repo with
the `#:package` form shown in the README; nuget.org under Altinn's prefix is for adoption, not this repository. The
21e brief is written when 21d's phase 2 is in (its Podman and front-door findings may change the package's shape), and
21e is dispatched without a further "dispatch" once 21d is verified and merged (owner, 2026-10-22).

## 22. app-lib honours an explicit localtest environment

**Goal (2026-10-21, follow-up of 21).** app-lib v9 in Development runs `studioctl app env --json` and lets its values
override the process environment unless `STUDIOCTL_APP_RUN` is set. Propose an app-lib change (and template if needed)
so that an explicitly configured localtest environment, as an Aspire AppHost sets it, is honoured without the
variable. Upstream work in `Altinn/app-lib-dotnet`; unapproved.

**Second item (decided 2026-10-21 under 21b).** `AuthenticationController.KeepAlive` (monorepo `src/App/backend`,
`app-lib-dotnet` v8) re-issues `AltinnStudioRuntime` with `Secure = true` hard-coded in every environment, so an app
behind a plain-http front door loses its ten-minute token refresh: the browser refuses the cookie and the session fails
when the old token expires. Change it to take `Secure` from the request (`Request.IsHttps`, or
`CookieSecurePolicy.SameAsRequest` through a `CookieBuilder`); production behind TLS is unchanged. This is the one
cookie that still makes both front doors strip `Secure`, scoped to app routes since 21b (`localtest/reviews/cookies.md`
rows 19 and A2). When it ships, the app-route stripping goes from studioctl-server and the localtest app's proxy, and
the Aspire front door (21d) can be a plain YARP with no cookie transform; until then 21d carries the same app-route
stripping as a bridge for apps on older app-lib versions. Decision: the components fix their own cookies (21b as
built) rather than keeping the proxy stripping everywhere, because the end state has no front door rewriting cookies.

## 23. HTTPS for the Aspire-hosted stack

**Question (2026-10-22, owner).** Can the Aspire-hosted stack serve `https://local.altinn.cloud:8000` with a
self-signed or development certificate, as Aspire does for its own project resources with the ASP.NET Core
development certificate? Research the Aspire features (project https endpoints, the dev certificate, what Aspire
offers for containers such as YARP), what a certificate for the ingress hosts needs (SANs for `local.altinn.cloud`
and the dedicated hosts, generation and storage in the package's data directory), trust in the browser and inside
every component container (the vendored images' trust stores, the mediated CA model of this sandbox versus a
developer's machine), the front door's TLS termination versus end-to-end, and what https changes for the stack: the
`Secure` cookies of 21b become production's, app-lib's `keepAlive` gap of task 22 disappears, `X-Forwarded-Proto`,
Arbeidsflate's `secure: 'auto'`, the components' own `PlatformEndpoint` values (`{{platform}}` becomes https), the
localtest app's fake ID-porten issuer, studioctl parity (does studioctl-server get the same?). Report in
`planned-components/aspire/https.md` with a status block, a recommendation and an effort estimate; small experiments
allowed (`dotnet dev-certs`, a YARP container on https), the full stack only when needed and one run at a time.

## 24. HTTPS mode for the Aspire package

**Goal (2026-10-22, follow-up of task 23).** An opt-in `Https` option on `AddAltinnLocaltest()`: the package generates
a CA and a server certificate for `local.altinn.cloud` and `*.local.altinn.cloud` in its data directory, the front door
terminates TLS on 8000 (`WithHttpsCertificate`, `WithHostHttpsPort`, the endpoint unproxied on `0.0.0.0`; https only on
8000), every container gets the CA through Aspire's certificate-authority collection (`SSL_CERT_DIR`; `NODE_EXTRA_CA_CERTS`
for the Arbeidsflate BFF; pdf3's `STUDIO_CA_BUNDLE`), the components get `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so
they see https, the scheme becomes a topology setting in Go and C# (`{{platform}}` resolves to https), the five
hard-coded `http://af.local.altinn.cloud:8000` origins in the Authentication, Correspondence and Dialogporten fakes and
the localtest app become configuration, the developer trusts the CA once (documented per OS and browser), the smoke
tests take an https `BASE` with the CA. Under https the 21b cookie policies yield production's `Secure` cookies and
task 22's keepAlive gap disappears. studioctl parity (studioctl-server terminating TLS with the same files) is a
second step. Effort from the research: 4.5 to 6 days for the package, 2 to 3 more for studioctl. Not verified by the
research: the full stack over https, browsers, pdf3's Chromium trust, the app process trusting the CA, Docker.

## Simplification (decided 2026-10-22)

**Owner's direction.** The manifest is too detailed, "a docker compose format in JSON": 711 lines plus a 688-line schema,
two readers (Go and C#), a consistency checker and a parity workflow, for 22 images. The coordinator's inventory of the
file: all 76 environment values resolve to the same constants in both runtimes (the public origin, the shared Postgres
and Redis, the certificate path), the prose is about 200 lines, the per-component host ports are unused by Aspire, the
Postgres init-script list duplicates the Postgres image, and 17 of 22 routes are `/<name>`. Decisions:

- **Manifest v2** is a map of services: `{"<service>": {"registry", "tag", "envs", "dependsOn", "volumes"}}`. Volumes
  carry name and bind location (they change with image internals that are not public API). The app is a service
  without an image whose `dependsOn` is the default stack; optional groups are the services nothing in the default set
  depends on (`--with x` / `.WithX()` adds a root and its transitive dependencies). No `groups`, `images`, `routes`,
  `frontDoor`, `postgres`, `hostPort`, `dev`, `description` or `notes` sections; prose moves to the component READMEs.
- **Service discovery as Aspire does it**: the runtime injects `services__<name>__http__0` for each dependency and
  containers resolve `http://<service>/` themselves. The components do not follow the convention yet, so until they do
  the runtime substitutes service names in `envs` URLs and connection strings. Task 25 researches adding
  `Microsoft.Extensions.ServiceDiscovery` to every .NET component inline in `#if !LOCALTEST` / `#else` blocks (also the
  ones with no cross-service call today), flagging components that create `HttpClient` outside the factory.
- **The front door is the localtest container**: a shared YARP proxy in C# inside the localtest image, with the 503
  fallbacks, the mock HTTP services (Maskinporten, ID-porten), the `Secure` stripping on app routes and the app deploy
  (provisioning) logic, destinations resolved by service discovery. studioctl-server and the Aspire package stop
  carrying their own proxy, routes and provisioning; each runtime publishes one port, 8000 from the localtest
  container. Images we build listen on one conventional port (8080 proposed) and answer `/health`.
- **Order**: 25 (research) → 26 (front door into the localtest image, deploy as an endpoint fed from the running app)
  → 27 (manifest v2 and both loaders; the parity suites shrink to one snapshot of resolved environments) → the
  per-component image changes from 25 as small pin-bump commits.

## 25. Service discovery in the components

**Goal (2026-10-22).** Per component: every outbound HTTP call site (factory clients, `new HttpClient()` flagged,
other transports), which configured URL it reads, direct-capable or public-origin-only; the registration recipe and
package versions per target framework; a prototype branch `research/service-discovery` with the registration added
inline in every vendored .NET composition root, compile-checked one component at a time; what moves into the images
(a localtest appsettings with service names) and what stays in `envs`; the port and health convention per Dockerfile;
whether Aspire honours a container `HEALTHCHECK` for `WaitFor`; the YARP service-discovery resolver. Report
`planned-components/service-discovery.md`.

**Outcome (2026-10-22).** Works for every .NET component through `IHttpClientFactory` clients; Register,
Authentication, Access Management and the Resource Registry already register it in production (`AltinnServiceDefaults`);
the other hosts got a three-line `#if LOCALTEST` block (prototype branch, every component builds, coordinator rebuilt
the localtest app). Findings that change the plan: the call graph has cycles (Storage/Authorization,
Authorization/Access Management, Register/Authorization), so the runtime injects `services__<name>__http__0` for every
running component and `dependsOn` stays start order only; Aspire 13.6 ignores an image `HEALTHCHECK` for `WaitFor`, so
the health convention (`/health` on 8080) is fixed in both loaders with optional `port`/`health` keys for Postgres,
Redis, pdf3 and the workflow engine; the real work is the settings (URLs derived from one public `PlatformEndpoint` per
fake; Correspondence's `PlatformGatewayUrl`, Dialogporten's `Infrastructure:Altinn:BaseUri` and the BFF's
`PLATFORM_BASEURL` serve several services and must be split); one active call bypasses the factory (Access
Management's `ResourceRegistryClient`), the token-metadata fetches stay on the public origin. Estimate 9 to 11 days in
per-component steps. **Decision (owner, 2026-10-22): the pass-through provider is disabled** in the localtest app and
in every component where possible, as the last step of each component's URL migration (today every factory client
calls the public origin, which only passes through, so it cannot go off first); the four `ServiceDefaults` components
remove the pass-through factory from the container in their fakes. Before the registration branch merges: the
`UPSTREAM.md` site lists, and `RestoreLockedMode=false` for Dialogporten's localtest build instead of the 4,500-line
lock files the prototype carries.

## 26. The localtest container as the shared front door

**Goal (2026-10-22, after 25).** YARP in the localtest app with routes by host and prefix to service-discovery
destinations, the 503 page for a destination without an address, the app-route `Secure` stripping (until task 22), the
deploy endpoint on the provisioning library fed from the running app's URL (posted by `studioctl run` and by
`AddAltinnApp` on the app's ready event), `deprovision` and `reset` as endpoints; studioctl-server and the Aspire
package drop their proxies, route tables and provisioning adapters and publish only port 8000 from the container.

**Decisions (2026-10-22).** The localtest app is already a YARP forwarder (Native AOT, routes in code, app routes
pushed over `localtest/api/proxy/apps`, 502 on no answer, `Secure` stripped on app routes); 26 consolidates onto it.
One registration call does both routing and provisioning: `PUT localtest/api/apps/{org}/{app}` with the app's URL
registers the route and provisions from the running app (`RunningAppSource`; `DirectoryAppSource` leaves the library),
`DELETE` deprovisions and removes the route, both idempotent; `AddAltinnApp` calls it on the app's ready event and
re-registers on every start without deprovisioning on stop, studioctl-server's discovery calls it for the apps it
finds (an app started from an IDE keeps working). The proxy resolves upstreams by name through
`Microsoft.Extensions.ServiceDiscovery` (its `IServiceDiscoveryHttpMessageHandlerFactory` wraps the forwarder's own
handler; core plus the configuration provider, **without the pass-through provider**, so a name with no
`services__<name>__http__0` fails resolution at once and the forwarder's error path answers the 503 page naming the
group, while an address that does not answer answers the ported "not answering" 503 with the error detail; the brief said 503 for both and the image does so). The Studio token is minted in-process; the Dialogporten
sync follows whether the adapter has an address; the Vite dev-server route becomes a pushed route on the same API.
Side effects: the keepAlive `Secure` gap closes for Aspire too (task 22's second item becomes cosmetic); the Aspire
package drops `Aspire.Hosting.Yarp` and the compiled-in provisioning sources. Not verified yet: a Native AOT publish
with the discovery package (no trimming or dynamic-code annotations in the net10 assembly).

## 27. Manifest v2: services only

**Goal (2026-10-22, after 26).** The format above; the Go and C# loaders reduced to: network, one container per
service in dependency order with alias equal to its name, its volumes, `services__*` for its dependencies, `envs`
with service names substituted, 8000 published from `localtest`; one golden snapshot of the resolved environments;
the schema, the checker, the placeholder vocabulary and the Aspire manifest reader's route model removed.

**Decisions (2026-10-22).** No env substitution in the manifest: `envs` are literal strings (the public-origin URLs
written out, the data stores and the non-.NET clients by alias and conventional port, because both runtimes give every
container its service name as alias); each runner has its own logic for the `services__<name>__http__0` injection,
one entry per running service, which is the only discovery mechanism. The first v2 manifest carries today's 76
variables as the resolved strings, so the container environment is byte-identical to v1 (one golden), and task 25's
per-component migration deletes them over time. Groups are imageless services (`dependsOn` only). Health kinds: an
HTTP path (default `/health` on 8080) or `tcp`. Runs after 26.

**Owner's corrections (2026-10-22).** The app is **not** in the manifest (it is not a service like the others): its
environment and the signing/secrets layout are runner code in `studioctl run` and `AddAltinnApp`, parity kept by the
app-environment golden both runners compare against; the default stack is marked by an imageless `default` entry
(proposed, same mechanism as the groups). **Tags change only in a separate, tested step**: no workflow writes the
manifest; the images workflow pushes `sha-<commit>` and whoever proved the tag bumps every entry with a small script
and regenerates the golden (pinning some images to a floating tag is a separate decision). `--dev-<component>` takes
the path to the Dockerfile or its directory and defaults to the current directory; the manifest carries no build
information. **The JSON Schema is dropped** (owner, 2026-10-22: "just another thing to not be in sync"; the loader's
test validates the file). **Groups and the default stack are runner code, not manifest entries** (owner: they must be
explicit in code anyway, for the Aspire methods and the command-line arguments): each runner holds the root services of
`default`, `dialogporten`, `receipt`, `correspondence`, `arbeidsflate` and `everything`, and the manifest's `dependsOn`
closes them transitively; the manifest lists real services only, no imageless entries. **Revised (owner, 2026-10-22):
two top-level sections**, `{"groups": [...], "containers": {...}}`: `groups` is a list of `{name, containers}` (the
root containers of `default`, which is always on, and of `dialogporten`, `receipt`, `correspondence`, `arbeidsflate`;
`dependsOn` closes them, so Correspondence implying Dialogporten is its dependency, not a group rule; `everything`
stays a runner constant meaning all groups); `containers` is the map of real services with `registry`, `tag`, `envs`,
`dependsOn`, `volumes` and the optional `port`/`health`. The typed Aspire methods and the `--with` values are code
constants checked against the group names by a test, as today. A container no group reaches is a loader error.

## 28. Browser-visible links across the stack

**Goal (2026-10-22, owner's request).** Every URL a browser can be sent to or click on, in every system, works on the
stack: an inventory first (a Playwright crawl of `--with everything` signed in as a test user, plus a code reading of
where each URL is built), each link classified as into the stack through the public origin, into a host that does not
exist locally, or out to production; then fixes per component in the fakes or the front door, and a smoke test that
follows every link and expects the right destination. Named acceptance case: an app's "back to inbox" lands in
Arbeidsflate at `http://af.local.altinn.cloud:8000/`, signed in. Expected sites: apps (the inbox link app-frontend
builds for the Altinn 2 inbox `/ui/messagebox` or for Arbeidsflate, profile, logout, the receipt link, the
party-selection return, help and privacy links, CDN assets), Receipt (inbox, app, attachment links on the `platform.`
host the fake rewrites), Arbeidsflate (the adapter's GUI-action links into app instances, profile, logout and its
post-logout return, Altinn 2 links in menus), Authentication (every redirect target, logout's post-redirect, the
ID-porten round trip), the localtest start page, the PDF generator's fetch of the app. Open: whether links out to
production hosts (`altinn.no`, `info.altinn.no`, the CDN) stay as they are (coordinator's recommendation) or get a
localtest placeholder page.

**Back-to-inbox analysis (coordinator, 2026-10-22; owner asked to skip the full inventory).** Not possible by
configuration today in any version: the local-host branch is code in all three frontends. The released app-frontend
v4 (the CDN bundle the sample app loads; `src/utils/urls/urlHelper.ts`, `buildArbeidsflateRedirectUrl`) returns
`http://${host}/` for `local.altinn.cloud(:port)`, the localtest start page, and derives `https://af.${altinnHost}/`
from the host name otherwise, with no settings at all; the monorepo frontend (`src/App/frontend`) has the same local
branch and uses app-lib's `PlatformFrontendSettings` (`ArbeidsflateInboxUrl`, `ArbeidsflateDialogUrl`,
`ArbeidsflateProfileUrl`, `AccessManagementChangeAndRedirectUrl`, …, bound from the `PlatformFrontendSettings`
configuration section and sent in the bootstrap response) only for recognised production hosts; app-lib 8.12.15 and
`app-lib-dotnet` main have no `PlatformFrontendSettings` (monorepo backend only); Receipt's frontend
(`vendor/src/frontend/src/utils/urlHelper.ts`, `returnUrlToMessagebox`) has the identical local branch. Since the
inbox link and the "start page" link are the same URL locally, no front-door rule can tell them apart. Proposed
fixes, smallest first: (1) monorepo frontend: on a local host use `platformFrontendSettings.arbeidsflateInboxUrl` /
`arbeidsflateDialogUrl` when set, else the start page (a few lines); then it is configuration, and the app runner
(`studioctl run`, `AddAltinnApp`) sets `PlatformFrontendSettings__ArbeidsflateInboxUrl=http://af.local.altinn.cloud:8000/`,
`…DialogUrl=http://af.local.altinn.cloud:8000/inbox/{dialogId}`, `…ProfileUrl`, and
`…AccessManagementChangeAndRedirectUrl` on the local Access Management; (2) app-frontend v4 and Receipt's frontend:
no settings channel exists, so a convention, `http://af.${host}/` (and `/inbox/{dialogId}`) on a local host, three
lines each, upstream PRs; (3) the stack side needs nothing: `af.local.altinn.cloud` is Arbeidsflate's host, and with
the group off the front door's 503 "not running" page says how to start it; optionally the localtest start page
shows an inbox link when Arbeidsflate has an address. Party switching through Access Management's
`changeandredirect` is skipped locally in (2) (Arbeidsflate opens on the user's default party).

## 29. Documentation refresh for manifest v2 and the front door

**Goal (2026-10-23, owner's request).** Every Markdown file describes the stack after tasks 25 to 28: a new
`localtest/CONVENTIONS.md` (manifest v2, the front door, the service-discovery convention, port and health, the
certificate contract, the public origin, volumes, what the app runner pushes, how a tag is bumped) that the other
documents link to; `localtest/README.md`, the studioctl tree's docs, the Aspire package's README and docs, the
localtest app's README and the 18 component READMEs corrected against the code; reviews and plans kept as dated
records with a "superseded" status line; a stale-term sweep (`Proxy__Upstreams`, the studioctl-server front door,
`Aspire.Hosting.Yarp`, placeholders, the schema, `STUDIOCTL_LOCALTEST_V2_SRC`, `STUDIOCTL_IMAGE_LOCALTEST_V2_*`,
`hostPort`, the provisioning library, the probe ports, `OnlyComponents`, …) with every remaining hit explained;
relative links checked; `typos`. Docs only, no stack. Coordinator's inventory before the task: about 90 Markdown
files outside `vendor/`; stale hits in 23 files for `hostPort`, 9 for the image override variables, 4 each for
`Proxy__Upstreams` and `STUDIOCTL_LOCALTEST_V2_SRC`, 3 each for the studioctl-server front door and
`Aspire.Hosting.Yarp`.

## 30. The Aspire host with several apps

**Goal (2026-10-23, owner's request).** Two apps in one AppHost (`AddAltinnApp<T>(localtest, name)` twice with
distinct names) each get their own port, registration and provisioning, both answer through the front door, and the
`provision`/`deprovision` commands of one leave the other alone. A second sample app `ttd/testapp2` (a copy of the
sample), a model test with two apps, a run under Podman with the registration list, both metadata responses, the
Register's state after both provisionings (the front door's provisioning replaces test data per app: record what
happens), the commands, a restart; fixes in the package if the second app is rejected; a "Several apps" section in the
package README.

## 31. Runtime-prefixed volumes

**Decision (owner, 2026-10-23).** Aspire and studioctl must not share persistent volumes: a stopped Aspire container
holding a shared volume blocked studioctl's `env reset` twice, and a reset in one runtime emptied the other's data.
The manifest's `volumes` keys become ids (`postgres-data`, `storage-blobs`, `policy-store`, `localtest-data`,
`correspondence-blobs`; the same id on several containers is one volume); studioctl prefixes `localtest-v2-` (names
unchanged for existing users), the Aspire package prefixes `altinn-localtest-`; each runtime's reset removes its own
volumes only; the goldens compare ids and bind paths; docs and the sample header lose the "shared with studioctl"
wording.

## 32. studioctl PR refresh with review

**Goal (2026-10-23, owner's request).** Three phases. A: re-vendor `localtest/studioctl/src` from current altinn-studio
main (33 commits past our baseline, about 12,700 insertions under `src/cli` and `src/Runtime`, including `run.go` and a
new manifest test in the old runtime: align with upstream's abstractions where they overlap), re-apply the localtest-v2
change on top, review the whole diff as a reviewer (proxy-era leftovers, swallowed errors, the `ReadinessProbe`
contract, `FrontDoorRegistration`'s cache versus a recreated front door, `run`'s wait, 22 `--dev-<container>` flags
versus one repeatable `--dev`, duplicated goldens) and simplify with each change justified in its commit. B: the proof
on the stack including a recreated front door with a running app. C: re-author the PR branch from upstream main as six
logical commits instead of the 43-commit replay, build and lint in the monorepo, push to the fork, rewrite the
description and a comment for the owner to paste. Coordinator's measurements: our diff against the old baseline is 57
files, about 7,700 insertions; the PR branch is at the task-21f state and describes a proxy and a provisioning library
that no longer exist.

**Phase A (pushed 7512071f, verified by the coordinator 2026-10-23).** New baseline `f45d4143` = altinn-studio
`b908931519` (main, 2026-10-23), checked byte-identical to upstream's four paths; `5c4cd5b1` re-applies the change with
no conflicts (only `CHANGELOG.md` and `run.go` changed on both sides, in different places). Upstream's `env/localtest`
"manifest" is the old runtime's resource graph, not a stack description: no overlap. Review commits, one each: one
repeatable `--dev NAME[=PATH]` instead of 22 generated flags (the generated `--dev-workflow-engine` clashed with
upstream's hidden flag of the same name; the owner may still prefer the per-container form); `env up` refuses the other
runtime's flags instead of ignoring them; `env up --latest` on a running pinned stack converges (state records
`latestImages`); `run --mode container` refused up front under localtest-v2 (it joined a network v2 never creates); the
server's `FrontDoorRegistration` remembers the app instance and re-registers what `GET localtest/api/apps` no longer
lists (recreated front door, restarted app on the same port), deregisters only what it registered; `run`'s wait names
`env status -r localtest-v2` when the front door does not answer, else the last provisioning state; `ReadinessProbe`
contract documented; devenv `make lint` clean; the front door client moved to `internal/localtestv2/frontdoor`
(`run_devserver.go` gone); dead code removed (`manifest.JSON()`, `healthTargets`, duplicate golden case
`native-random-port`). Golden diff: the `container` case lost the certificate and Arbeidsflate variables (refused mode),
`environment.golden.json` unchanged. Checks: go build/test, `make lint`/`fmt`, devenv lint clean (one devenv test
needs the monorepo root marker, pre-existing); server tests 1009 passed; Aspire 77 passed (one fewer: the dropped
case); `actionlint` clean. Known gaps left: an app already registered is not synced to Dialogporten when the group is
added later; a detached `run --dev-frontend` leaves its route; a registration can block the discovery loop up to 2 min.

**Owner decision 2026-10-23 (flag form).** The owner does not like `--dev NAME[=PATH]` and does not see the
`--dev-workflow-engine` clash as a reason: the per-container form `--dev-<container>[=<path>]` comes back in a phase-B
fix commit. `--dev-workflow-engine` stays one flag whose meaning follows the runtime (upstream's hidden host-process
routing under `-r localtest`, a container build under `-r localtest-v2`); `env up` refusing the other runtime's flags
keeps the two meanings apart. The agent was told during phase B.

**Phase B (pushed 2ccabb35, verified by the coordinator and merged ff 2026-10-23).** Rebased on main, so the baseline
is `24562cba` and the re-apply `a1a5df42` (subtree identical to the phase-A hashes). Suites on the pins with `--with
dialogporten`: root 26/26, authentication 34/34, authorization 31/31, dialogporten 60/60, `localtest/smoke-test.sh`
80/81 (known `/app-frontend/` check); root 26/26 with `--dev-storage=localtest/components/storage` and again after
`env reset` + `env up` on empty volumes (19.8 s). Re-registration proven with the app kept running: `env down` +
`env up --with dialogporten` registered it again (first with Dialogporten "Failed" while the group started, then
Synced); a `DELETE` of the registration by hand was answered by "no longer lists … registering it again" within 5 s;
container and volume removed by hand then `env up` registered it again. Three bugs found and fixed in phase B, one
commit each: the server now re-registers until Dialogporten's sync succeeds (the front door answers 200 with
`dialogporten: Failed` and never retried); `env up -r localtest-v2` restarts studioctl-server so an app registered
before a group came up is provisioned for it; a plain `env up` after a `--dev-<container>` run converges back to the
pins (state records dev builds). The owner's flag form `--dev-<container>[=<path>]` restored (1aea5289), the refusal of
the other runtime's flags kept; `--dev-workflow-engine` means host routing under `-r localtest` and a container build
under `-r localtest-v2`. `--latest` on the running pinned stack started pulling (stopped by hand to protect the disk)
and a plain `env up` converged back. Refusal texts recorded in the agent's report. Coordinator's checks on the head:
go build/test/lint/fmt clean, server tests 1010 passed, Aspire 77 passed, help lists `--dev-NAME[=PATH]` with the
manifest's names. Side effect to note: the agent removed the superseded research images `sha-f76b62b` and
`sha-98e2bae` to free disk (2.5 → 5.3 GB); pullable again. Observation: `make dev-install` does not stop a `run
--detach` app (upstream's `self install`), not investigated. Dev studioctl rebuilt from main.

**Phase C (2026-10-23).** PR branch re-authored on altinn-studio `origin/main` f240887cfa (moved past the baseline,
nothing under the four paths) as six commits: devenv readiness probe (c23ec15c97), manifest and reader (a5c7429e35),
the localtest-v2 runtime (78f5dbb1d5), running apps against it (80379bdc57), studioctl-server registration
(b2de05b1f2), docs (dfc5fdd348). Force-pushed to the fork with lease; `diff -r` of the four paths against
`localtest/studioctl/src` empty (coordinator re-ran it); go build/test/lint clean at the head (coordinator re-ran),
server tests 1010, Prettier and actionlint clean. The token was refused for `gh pr edit` and `gh pr comment`
("Resource not accessible by personal access token"): the owner pastes `/home/agent/code/pr-studioctl-localtest-v2-stack.md`
(description, 110 lines, "Since the last draft", "Not mergeable yet") and `...-comment.md` (9 lines) on
[altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394). Worktree `task32-studioctl` and branch
removed; `.worktrees/studioctl-pr` kept as the PR checkout.

## 33. Minimal in-memory emulation in the localtest image

**Goal (2026-10-23, owner's request).** People complain about the number of containers the default stack starts.
Research how the localtest image could carry a truly minimal emulation of the platform services so that one container
serves an app: a simple Register from our seed data, a PDP that always permits, Notifications in an in-memory list, and
whatever else the app cannot run without (Profile, Authentication's tokens, in-memory Storage, a PDF stub), all in
memory. Reference for what is sufficient: the monorepo's `src/Runtime/localtest` (the old runtime's emulation, 36k
lines, file-backed, MVC) and app-lib's clients. The document decides how the front door picks emulation versus a real
service, how registration provisions into memory, what fidelity is lost, how studioctl and Aspire expose it, reuse
versus rewrite, and a phased plan. Deliverable `planned-components/minimal-emulation/README.md`; no code, no stack.

**Owner decision 2026-10-23 (scope).** Storage and the workflow engine are not emulated: too central to apps. Minimal
means the localtest container plus the real `storage`, `workflow-engine` and `pdf3` images with `postgres` (five
containers instead of the default group's thirteen; whether pdf3 can be stubbed is still for the research), with
Register, Profile, Authentication, Authorization (permit-all), Access Management, Events, the Resource Registry and
Notifications emulated in the localtest container, which must then also answer the calls Storage and the workflow
engine make to those services (their `services__*` pointed at `http://localtest:8080`). The agent was told.

**Owner decisions 2026-10-23 (ephemeral, PDF).** In minimal mode Postgres and every read/write volume are ephemeral
(anonymous or tmpfs, gone with the containers; `env reset` has nothing to remove; every start pays the database init and
migrations, the research says what that costs and whether a pre-initialised image is worth it). pdf3 is not in the
minimal set: the localtest container mocks the PDF service with one static file whose page says "Minimal PDF without
content". Minimal set: postgres, storage, workflow-engine, localtest. The agent was told.

**Outcome (merged 2026-10-23, document `planned-components/minimal-emulation/README.md`).** Recommendation: one new
manifest group `minimal` (`localtest`, `storage`, `workflow-engine`; `postgres` via dependsOn) that replaces `default`;
the emulated set derived (every HTTP container of `default` the mode does not start) and injected as
`services__<name>__http__0=http://localtest:8080`; the front door emulates a service whose address is its own (option
C), with a start-up check against self-loops; Storage needs no setting change (its calls already go to the public
origin) and needs only the OIDC discovery/JWKS, the PDP `decision`, `parties/lookup` and a 503 on the adapter path; the
workflow engine makes no platform call. Permit-all PDP must echo `includeInResult` attributes per multi-request
(Storage and app-lib map results by them; a bare Permit gives a 500 process state and empty instance lists), no
obligations. Ephemeral = tmpfs mounts set by the runners (unmounted declared volumes would leak anonymous volumes
since studioctl removes containers without `-v`); Aspire uses session lifetime; `env reset` has nothing to remove.
Start estimated at about 6 s on studioctl, 10 s under Aspire (from 2026-10-22 per-container times, not measured); a
pre-initialised Postgres image not worth it. PDF: a 602-byte hand-written PDF embedded in the binary; app-lib checks
only success and stores bytes. Rewrite in minimal-API style, about 2,000 lines plus 800 of tests under
`localtest/localtest/Emulation/`, only the Register's test-data parser files linked in; phases: 1 basic flow with
`--minimal` in studioctl, 2 Aspire and docs, 3 optional policy-evaluating PDP (ABAC AOT compatibility unverified).
Open for the owner: Correspondence-based signing receipts 503 in this mode; `--with` refused in the mode; a 1 GB cap on
the blob tmpfs; the banner and `X-Localtest-Emulated` header; `--print docker` instead of a hand-written four-container
recipe. Coordinator verified the Storage result-mapping, workflow-engine and `podman rm` claims against the code, typos
and links clean, and answered open question 4 with task 32's re-registration.

## 34. `studioctl run --mode container` against localtest-v2

**Goal (2026-10-23, owner's request).** Container-mode apps must work against localtest-v2. Today (task 32) the mode is
refused up front because the app container joined the old runtime's `localtest` network, which v2 never creates, and
the run failed at container creation. What a working version needs, to discuss before a brief: the app container joins
the `localtest-v2` network (or none, with the public host names mapped to the host gateway so `local.altinn.cloud:8000`
resolves inside it); the certificate mounted at `/certs` as for the components (the `container` golden case gets its
signing settings and Arbeidsflate links back); the registration address: studioctl-server already records
`ContainerId`/`HostPort` per discovered app and registers `host.docker.internal:<hostPort>` today, which works when the
container publishes a port, or the container's alias and internal port on the v2 network if we prefer no published
port (then `FrontDoorUrl` needs the network address); `run --dev-frontend` and the registration wait in the container
path (removed in task 32); the Aspire side is unaffected (apps are projects). Decide: network versus host gateway,
published port versus alias, and whether the app image needs the CA bundle or `/accesstoken` file.

**Owner 2026-10-23 on container-mode apps (task 34):** keep the option of running apps in containers locally; the
host-gateway versus stack-network trade-off is with the owner (coordinator's description given in chat).

**Owner decisions 2026-10-24 (task 34).** Container mode is studioctl only (Aspire runs projects). The app container
runs on the container network, ideally a separate apps network; v4 and v8 apps keep working unchanged, only with the
standard domains (no custom origin in container mode). Brief drafted: an apps network `localtest-v2_apps` with the
front door attached to both, the app by alias `<org>-<app>` without a published port, the host-gateway mappings for
the public host names, the certificate and secrets mounts, the registration URL from discovery (`http://<alias>:<port>`),
the wait and `--dev-frontend` in the container path, cleanup with `stop` and `env down`; proof with the app-lib 8
sample and the monorepo app, a Dialogporten sync, minimal mode once.

**Phase 1 (pushed 31a9c0c3, verified by the coordinator 2026-10-24).** `env up` creates `localtest-v2_apps` beside
the stack network (own label); only the front door joins both (Podman: one `--network` per network, the alias applies
on both; Docker: `NetworkConnect` before start, devenv already supported it). The app container is on the apps network
only, alias `<org>-<app>`, the public host and its four subdomains mapped to the host gateway, a published port kept on
127.0.0.1 (studioctl-server discovers and reads metadata through it), labels `altinn.studio/front-door-url` and
`altinn.studio/localtest-v2=app`, secrets, keys and `/certs` mounted read-only; every teardown path removes labelled app
containers with their volumes (`apps.go`). Registration: `__app-containers` reports the front-door URL from the label,
`FrontDoorRegistration.FrontDoorUrl` uses it and falls back to `host.docker.internal` for host apps; the container path
waits for the registration and routes the dev server. Refusals: a non-default origin (message names `env up --host
local.altinn.cloud --port 8000`); an environment without the apps network (older studioctl, before the build). Golden:
the `container` case regains the certificate settings and the inbox and logout links, Kestrel on `0.0.0.0`; new cases
`container-arbeidsflate` and `container-localtest`; every stack case gains the apps network. Checks: go clean, server
tests 1012, Aspire 127 (coordinator re-ran). Phase 2 confirms the alias on both networks, the host-gateway route, the
certificate path and Kestrel's bind.

**Phase 2, first attempt (2026-10-24): the separate apps network does not work under Podman.** netavark forwards the
front door's published port 8000 with one DNAT rule to its address on the first network listed, and isolates the two
bridges, so with the front door on both networks the stack's calls to `local.altinn.cloud:8000` timed out
(provisioning hung) and, after reordering, the app's calls did; a front door on the stack network only cannot reach
the app's published port through the host gateway either (bridges isolated); the Docker-compatible API refuses
`isolate=false` (only the CLI has it). The front door's alias also applies only on its first network. Coordinator's
decision: option 1, the app container on the stack network under its alias (the old runtime's model), the host-gateway
mappings kept, the apps network removed; the evidence recorded in the studioctl README and the egress plan (DNS and
firewall control for a container app must key on the container, not a network). Also found: the app-lib 8 sample has
no Dockerfile (container mode needs one, as before; the agent used a copy with the template Dockerfile), and the
30-second startup timeout is short while provisioning runs.

**Phase 2 (pushed 63452dc3, verified by the coordinator and merged ff 2026-10-24).** Option 1 implemented
(9d13ddc1): the app container on `localtest-v2_network` under `<org>-<app>`, the label, the host-gateway mappings,
`/certs`, the loopback port for discovery, Kestrel on `0.0.0.0`, registration at the alias; the stack golden back to
main's. Proof with the app-lib 8 sample (a copy with the template Dockerfile, since the sample has none): `run --mode
container` 39 s including the image build, registered at `http://ttd-testapp:5005`, provisioned in 2.5 s with
`dialogporten: Synced`, the app reaching `local.altinn.cloud:8000` from the stack network, metadata 200, a Chromium
flow to the receipt, `--dev-frontend` routed to the host (503 without a server, served with one), `stop` removing the
container and its registration, `env down` with an app running removing it and the network; minimal mode up in 5 s
with the app provisioned in 0.3 s. Root suite 23+3 with the app stopped (with the sample registered about 10 checks
fail because its `testData.json` replaces the seed users, as task 21e documented). Not done: the monorepo app's image
build fails in studioctl's generated Dockerfile (`PackageVersioning.props` not copied; `repo_context.go` is identical
upstream: an existing bug, documented in the README), so the v9 certificate path is only partly checked (the mounted
file resolves). Checks: go clean, server tests 1012, Aspire 127 (coordinator re-ran). Follow-ups: the studioctl
Dockerfile bug for monorepo test apps (upstream); a Dockerfile for the app-lib 8 sample if container mode should work
on it as shipped. Worktree and branch removed; dev studioctl rebuilt from main.

## 35. Minimal mode phase 1

**Goal (2026-10-23, owner's request).** Build phase 1 of `planned-components/minimal-emulation/README.md`: the
emulation of Register, Profile, Authentication, permit-all Authorization, Access Management, Events, Notifications,
the Resource Registry (501) and the PDF stub in the localtest image, **isolated under `localtest/localtest/Emulation/`**
with only small seams outside it (one Add/Map pair in `Program.cs`, target interfaces in `Provisioning/`, a test-user
and token interface for Home, the routes listing's `emulated` address, the header and banner), so the default stack's
behaviour is unchanged; `studioctl env up -r localtest-v2 --minimal` with the manifest's one new group `minimal`, the
derived emulated set injected as `services__<name>__http__0=http://localtest:8080`, tmpfs mounts (new devenv mount
type) and no volumes, `--with` refused in the mode, `env status`/`env reset` aware of it; smoke gates adapted (named
skips for the PDP NotApplicable and 403 checks only); docs. Coordinator's decisions beyond the spec: the Register
test-data parser files and the seed are copied into the folder with a test that fails when they drift (the image's
build context is `localtest/localtest/` only); blob tmpfs capped at 1 GB; header `X-Localtest-Emulated` and the Home
banner; Aspire's `WithMinimal` is phase 2 (the package only has to keep passing). Phase 2 of the task is the proof:
the mode built with `--dev-localtest`, the sample app through instantiate, submit, receipt and PDF in a browser, clean
slate after `env down`/`env up`, and the default stack unchanged with the same image.

**Phase 1 (pushed e1c412aa, verified by the coordinator 2026-10-23).** Isolation holds: `Program.cs` carries only
`Emulation.Add(builder)` and `Emulation.Map(app)`; seams are `Provisioning/IProvisioningTarget.cs` (one interface, the
HTTP code moved behind it unchanged, the in-process target in `Emulation/`), `IdPorten/TestUsers.cs` (`ITestUserSource`),
`Home/AuthenticationClient.cs` (`IInProcessAuthentication`), `Home/HomeApi.cs` (banner), `Proxy/RoutesApi.cs` and
`AppRoutes.cs` (`emulated` address; empty routes file = in memory), `Localtest.csproj` (embedded seed and PDF). About
2,950 application lines under `Emulation/` (Authentication 627, Notifications 429, Register 370, store 348, Authorization
245, Events 233, Access Management 155, Emulation 172, Profile 64, Pdf 61, Resource Registry 19) plus 448 copied
test-data lines and about 960 lines of tests; the copies are checked byte-for-byte against the Register fake's files,
which therefore moved to source-generated JSON (default-stack code, behaviour unchanged, a test proves the canonical
JSON; the Register image will carry it at the next pin bump). Claim-type tables with `file:line` references into the
vendored Authentication; JWKS kid = certificate thumbprint. studioctl: `minimal.go` (derived emulated set,
`http://localtest:8080`, tmpfs with `size=1g` on the blobs, mode switch stops first), devenv `tmpfs` mount type (Podman
`--tmpfs`, Docker `HostConfig.Tmpfs`; hashes unchanged without options), refusals, `env status`/`env reset`, Arbeidsflate
links unset; golden +292 lines, `default`/`everything` cases byte-identical. Aspire: `MinimalGroup` constant, tests
adapted, no `WithMinimal`. Checks (coordinator re-ran): localtest tests 106, go test and lint clean, devenv build and
vet, Aspire 77; agent: AOT publish without warnings, binary 20.6 MB (+2.4 MB), AOT binary as a process passed 74 of
the localtest suite's checks (2 need Storage and the frontend dist). Deviations recorded by the agent: `pdf.` host
`/health` 404 as pdf3; `authentication?goTo=` goes through the ID-porten picker with PKCE as production; a marker
resource for empty-role parties in `authorizedparties`; the dev-server route file still lands on `/data` (tmpfs);
`env down` clears the recorded mode so a following `env reset` acts on the default volumes (to decide). Fixed on the
way: the front door's own `services__localtest__http__0` entry no longer counts for the self-loop check.

**Observation for a follow-up (coordinator, 2026-10-23).** Before phase 2 the machine held about 250 anonymous volumes
left by earlier `env up`/`env down` cycles of the default stack (the declared `VOLUME`s of the Postgres and localtest
images; studioctl removes containers without `-v`, exactly as the spec's section 6.1 says). Pruned by hand with `podman volume prune -f`, which also removed the unused named volumes of the default stack
(`localtest-v2-*`) and the old runtime's `localtest-workflow-engine-db-data`: a coordinator mistake, recoverable (both
runtimes re-initialise empty volumes; task 32 measured 20 s); never prune, remove the anonymous ones by name. Candidate
task: `env down`/`env reset` remove the anonymous volumes of the containers they remove (`rm -v`), in both runtimes.

**Phase 2 (pushed 6d0adddc, verified by the coordinator and merged ff 2026-10-23).** `env up -r localtest-v2 --minimal
--dev-localtest=localtest/localtest`: 65.5 s the first time (AOT image build and the `tt02` re-pull), 5.9 to 6.1 s with
the cached image; four containers, no volume created; `env status` prints the mode line and the emulated list. Sample
app registered and provisioned (`succeeded`, Dialogporten Skipped), metadata through 8000, `X-Localtest-Emulated:
profile` on a profile response. Suites in the mode: root 23 ok with the 3 decision checks skipped by name;
`localtest/localtest/smoke-test.sh` 89 of 90 (the `FRONTEND_DIST` check, as on the default stack) with all 9 emulated
service checks ok; Register 6 read checks ok, 8 skipped by name; events and a posted e-mail order visible on the
inspection endpoints. Browser flow in Chromium: Home with the banner, party selection, form, submit, receipt with the
PDF; the download is byte-identical to `minimal.pdf` and extracts "Minimal PDF without content" (the sample app got
`enablePdfCreation` for the run only); screenshots `localtest/localtest/docs/minimal/01..04.png`. Clean slate after
`env down`/`env up`: instances 5 → 0, no registration, no event. Default stack with the same image over the minimal
stack (mode switch stops first): 23.2 s on empty volumes, root 26/26, localtest 80/81, Register 14/14, no `emulated`
route, no header, no banner. Fixes in phase 2: the health probes (Postgres image, declared `VOLUME`) mounted no tmpfs
and left one anonymous volume per probe in both modes, which explains the hundreds of dangling volumes on this
machine (050c36c5; after it a minimal `env up` adds no volume and a default one only its four named volumes); minimal
tmpfs mounts are `mode=1777` because a tmpfs mount point is root's 0755 and Storage runs non-root (3976ccb9, golden
updated). Coordinator re-ran go build/test/lint, devenv container tests, localtest tests 106, Aspire 77, and looked at
the receipt screenshot. The agent's view on `env reset` after a minimal `env down`: `Down` removes the state file, so
`Reset` sees no mode and deletes the `localtest-v2-*` volumes; it recommends remembering the mode (not changed).
Observations: `make dev-install` takes a running stack down; the emulation's JSON escapes `+` as `\u002B` (valid).

## 36. Anonymous volumes removed with their containers

**Goal (2026-10-23, owner's decision after discussion).** Images that declare a `VOLUME` (Postgres upstream's
`/var/lib/postgresql`, our localtest image's `/data`) give every container started without a mount there an anonymous
volume, and devenv removed containers without them; the localtest-v2 health probes (Postgres image) left one per probe
per `env up`, hence about 230 orphans on the machine. Nothing in the stack wants an anonymous volume. Change: devenv
removes containers with their anonymous volumes on both backends (Podman `rm --volumes`, Docker `RemoveVolumes`; named
volumes are never touched by either), `VOLUME /data` dropped from the localtest Dockerfile, task 35's probe tmpfs
removed as redundant, one sentence in the conventions. Proof: default and minimal `env up`/`env down` add nothing but
the named volumes; a hand-started localtest container has no anonymous volume.

**Phase 1 (pushed e6e15ea9, verified by the coordinator 2026-10-23).** Podman `rm --volumes` and Docker
`RemoveVolumes: true` built in one helper per backend and used by every removal path (`ContainerRemove` and the
best-effort cleanups), with a test per backend; callers inheriting it: the old runtime's `env down`/`env reset`, the
helper container, `run --mode container`, `stop`, `self/transition`, the v2 probes. Doc references in the commit
message (podman-rm(1) `--volumes`: anonymous only; Engine API `v=1`: anonymous only). Upstream devenv has no opinion
(same code without the flag). `VOLUME /data` dropped from the localtest Dockerfile; probe tmpfs removed; golden
unchanged; conventions sentence and a CHANGELOG "Fixed" line. Phase 2 adds: Storage's `VOLUME /data/blobs` dropped and
Aspire's `LocaltestReset` removing with `--volumes`.

**Owner decision 2026-10-23 (`env reset` and minimal mode).** Reset applies to the minimal environment only while it
is up: it stops it and deletes nothing. When the environment is down, `env reset` clears the full environment (the
default stack's `localtest-v2-*` volumes), whatever mode ran last. This is what task 35 implemented (`Reset` spares the
volumes only when the state says minimal; `Down` removes the state), so no change; the agent's suggestion to remember
the mode after `down` is declined.

**Phase 2 (pushed 48648ecc, verified by the coordinator and merged ff 2026-10-23).** Extras: `VOLUME /data/blobs`
dropped from the Storage Dockerfile; Aspire's `LocaltestReset` builds `rm --force --volumes` (test updated, 77 pass).
Proof with both images rebuilt (`--dev-localtest`, `--dev-storage`): the volume count stayed at 7 (three old anonymous
volumes of unrelated exited containers plus the four `localtest-v2-*`) through default `env up` (root 26/26, the probes
now on the plain Postgres image leaving nothing) and `env down`, minimal `env up`/`env down`, and two containers
started by hand from the rebuilt images without mounts (`Mounts = []`); for contrast the pinned localtest image still
creates one anonymous volume, removed by `rm --volumes`. Coordinator re-ran go build/test/lint and the Aspire tests.
Worktree and branch removed. Open: none; the images reach users at the next pin bump.

## 37. Aspire minimal mode

**Goal (2026-10-23, owner's decision).** The spec's phase 2 with the owner's API: `var localtest =
builder.AddAltinnLocaltestMinimal();` returns a builder of a distinct resource type on which no group method compiles
(mind `IResourceBuilder<out T>` covariance: a shared base and two sealed leaves, not inheritance from the full
resource); `AddAltinnApp`, `WithFrontendDevServer`, `Tag` and `PostgresHostPort` work on both, `DataDirectory` and
`Groups` do not apply; the four containers with the golden's `minimal` environment, tmpfs through container runtime
arguments (`mode=1777`, blobs `size=1g,mode=1777`), empty `Proxy__AppRoutesFile`, session lifetime; the parity test
gains the `minimal` case; README and docs; a minimal sample AppHost. Proof under Podman: four containers, no volume,
both sample apps, root 23 with the named skips, clean slate after SIGINT and restart, the full AppHost still 26/26.

**Phase 1 (pushed 85d611fe, verified by the coordinator 2026-10-23).** `AltinnLocaltestResourceBase` (abstract,
`private protected` constructor) with two sealed leaves; the group methods stay on `IResourceBuilder<AltinnLocaltestResource>`;
`AddAltinnApp` takes the base, `WithFrontendDevServer<T>` is generic; a reflection test asserts those two are the only
public methods accepting the minimal builder. Parent `altinn-localtest-minimal`; `StackSpec.ResolveMinimal` derives the
emulated set (9) and injects `http://localtest:8080`; `Proxy__AppRoutesFile=` empty; tmpfs via `--tmpfs path:mode=1777`
(`size=1g,mode=1777` on the blobs); session lifetime; no `reset` command (nothing persists); the port guard refuses a
full stack's persistent front door with the `rm` command named. Options: `Tag` and `PostgresHostPort` apply;
`DataDirectory` and `Groups` not offered (`AltinnLocaltestMinimalOptions`). Parity: the golden's `minimal` case compared
(order, images, env, tmpfs by path and options, port, hosts, dependencies, health); drift tests confirmed. Tests 92
(coordinator re-ran); csharpier, typos, actionlint clean; both sample AppHosts build; nothing under `localtest/studioctl`
changed. Docs: package README "Minimal mode", conventions, localtest README, spec status. Phase 2 needs a localtest
image with the emulation: the pin `sha-6d928ad` predates task 35 and the package has no dev build, so the pin bump
comes first.

**Pin bump 2026-10-23 (coordinator): `sha-6d928ad` → `sha-48648ec`.** The images built by the workflow for the task-36
merge commit carry tasks 35 and 36 (the minimal-mode emulation, no `VOLUME` declarations in our Dockerfiles, the
Register fake's source-generated test-data parser). Proof on the plain pin: default stack with dialogporten up in 61 s
including pulls; root 26/26, authentication 34/34, authorization 31/31, dialogporten 60/60, front door 80/81 (known);
minimal mode up in 5.9 s, four containers, no volume added, root 23/23 with 3 named skips; after both downs the only
volumes are the four `localtest-v2-*`. Observation: our Redis image inherits `VOLUME /data` from its base, so one
anonymous volume exists while the dialogporten group runs and goes with the container (conventions sentence corrected).
Old pin images removed to make room (3.6 GB); logs in `/home/agent/code/.scripts/pin-48648ec/`. Note to self: a
`find -name bin` cleanup under `src/cli` deletes `build/dev-home/bin`; exclude `build/`.

**Phase 2 (pushed 2976956a, verified by the coordinator and merged ff 2026-10-23).** No code fix needed. Minimal
sample (`apphost-minimal.cs`, both apps) under Podman on the pin: four containers healthy 8 s after `dotnet run`, both
apps provisioned at 15 s; no named and no anonymous volume before, during or after; `GET localtest/api/apps` lists
both, both answer through 8000, nine services `emulated` in the routes listing, the Home banner shown; root 23/23 with
the 3 named skips (the suite's PDP refresh gets a 501 in the mode); SIGINT removed the containers in 2 s and DCP and the
apps exited on their own; after the restart 0 instances and both apps registered again at 15 s. `free -m`: 3080 MB
used with both apps. The three unverified points: DCP passes `Proxy__AppRoutesFile=` (routes in memory, `/data` empty);
tmpfs mounts as intended (`mode=1777`, blobs `size=1g,mode=1777`), session containers `<name>-<random>` on a session
network, found by the smoke helper; beside the full stack's persistent containers: running → the port guard refuses
(general message: a different AppHost file has a different hash; README notes it), stopped → the minimal sample starts
normally since the two networks keep the aliases apart. Control: the full sample (with Dialogporten) 18 containers at
34 s, apps at 45 s, root 26/26, 4254 MB used; it creates the four `altinn-localtest-*` volumes plus Redis's inherited
anonymous one (goes with `--volumes`). Cleanup: persistent containers removed by name, the `altinn-localtest-*` volumes
removed (created by this run), `podman ps -a` empty, 6.9 GB free. `.scripts/aspire-sample-run.py` takes the AppHost
file name. Coordinator re-ran the package tests (92) and built the minimal sample.

## 38. Memory statistics of the stack

**Goal (2026-10-23, owner's question).** The reported `free -m` numbers (minimal with two apps 3.1 GB used, full
Aspire stack 4.3 GB) looked low for 18 .NET containers. Measure per container (cgroup `memory.current`, anon, file,
shmem, main-process RSS), per host process (PSS), and the host totals for the studioctl default stack with and without
an app, `--with everything`, minimal, and both Aspire samples; reconcile `free`'s "used" with the sums; explain the
result (GC settings in the images, AOT, shared layers, page cache counted as available). Deliverable: a reusable
`localtest/tools/memory-stats.sh` and `localtest/reviews/2026-10-23-memory.md` with raw snapshots.

**The monitoring group (2026-10-26, task 52, `docker stats --no-stream` under Podman, default stack plus
`--with monitoring`).** Idle, a minute after start: Grafana 232.8 MB, the collector 60.4 MB, VictoriaMetrics 39.3 MB,
VictoriaLogs 27.1 MB, VictoriaTraces 17.5 MB, 377 MB together. After a `studioctl run` of an app sending all three
signals and the default components that already export (a few hundred spans, ~1,500 log lines): Grafana 237 MB, the
collector 71.1 MB, VictoriaLogs 67.2 MB, VictoriaMetrics 55.2 MB, VictoriaTraces 49.6 MB, 480 MB. `free -m` used went
from 925 MB (nothing running) to 2,702 MB (18 containers idle) and 3,339 MB with the app and the browser.

**Minimal mode's telemetry files (2026-10-26, task 55, `podman stats --no-stream`, rootful Podman).** `otel-files`
61 MB with the app's requests in, 70 MB a few minutes later, 88 MB after the front door's smoke test and ten minutes of
metrics, 46 MB right after a restart; Postgres 214-224 MB, the engine 169-189 MB, Storage 91-102 MB, the front door
26-33 MB. `env up -r localtest-v2 --minimal --detach` on the pinned images: 6.0, 5.9, 6.0 s without the collector,
6.3, 6.3, 6.4 s with it. For comparison, the monitoring group on the default stack the same evening: the collector
74 MB, VictoriaMetrics 68 MB, VictoriaLogs 85 MB, VictoriaTraces 57 MB, Grafana 238 MB.

**Open (owner 2026-10-23): the public origin `http://local.altinn.cloud:8000` must be configurable, with a random port
possible.** Coordinator's outline given; the scope question (collision avoidance only, or several stacks side by side)
is with the owner before a brief.

## 39. Configurable public host names and port

**Goal (2026-10-23, owner's decision).** One origin per environment: `http`, a public host (default
`local.altinn.cloud`) and a port (default 8000), the four dedicated hosts derived as subdomains. studioctl `env up
--host --port` with `--port 0` picking a free port once and recording it in the state; Aspire `PublicHost`/`PublicPort`
on both option classes with 0 = random, every derived value a `ReferenceExpression` over the front door's endpoint. The
manifest (owner 2026-10-23) declares the public host, port and the list of dedicated subdomains in a new top-level
section and uses a small fixed placeholder vocabulary in `envs` (`{{origin}}`, `{{host}}`, `{{port}}`,
`{{origin:<sub>}}`, `{{host:<sub>}}`), validated by the reader; `services__*` stay literal. The front door derives its hosts from configuration; the fakes that embed the
origin (Authentication's seeded clients) read it from the environment (reaches the stack after a pin bump: phase 2 uses
`--dev-authentication`). the hosts file is the user's own with a custom host (studioctl's `env hosts` manages only the default names; `env up`
checks resolution and prints the lines to add); `--add-host` maps the configured names inside the containers;
the suites get one override variable; the golden gets an `origin.test:18000` case with the Aspire parity test. Several
instances are not implemented; the report lists what an independent `run --minimal` would still need.

**Outcome (merged 2026-10-23).** `free` "used" (MiB, idle): baseline 928; default stack 1809 (containers 1072);
with an app 2468; `--with everything` 2797 (containers 2108, no OOM, cache filled the rest); minimal 1332 (containers
424, Postgres's tmpfs 175 the largest item); Aspire minimal with two apps 2638 (host side 1219: AppHost 0.3 GiB, DCP 0.3
GiB over 11 processes, dashboard, apps); Aspire full 3802 (containers 1927, host 1143). Reconciliation: used = anon +
shmem + non-reclaimable kernel + 120–190 held back; "available" includes the page cache, among it 0.8–1.1 GiB charged
to Podman's parent cgroup that `podman stats` never shows. Why it fits: idle .NET services hold 15–60 MiB anon (server
GC is on in every image but .NET 10's adapts to live data; no limits set); JIT code lives in a shared memfd region of
17–99 MiB per process; only the front door is AOT; the 14 .NET containers carry 10 runtime copies over four patch
versions, so layer sharing saves little. Storage triples once an app registers. Roslyn's build server (240–410 MiB)
stays after `studioctl run`/`dotnet run`. Unexplained: F's containers used more after the AppHost stopped. Side note:
the agent's `pgrep` pattern once matched its own shell. Worktree and branch removed.

**Phase 1 (pushed 0b0b999d, verified by the coordinator 2026-10-23).** Manifest: top-level `public` (host, port,
subdomains `pdf, workflow-engine, app-frontend, af`), 32 literals rewritten to `{{origin}}`, `{{host}}`,
`{{origin:af}}`; both readers (`manifest/origin.go`, `Manifest/PublicOrigin.cs`) validate the vocabulary with
table-driven tests. studioctl `env up --host --port` (`0` picks a free port, recorded in the state; a plain `env up`
keeps it; other values restart; refused under `-r localtest`); `env status` prints `Origin:`; `run`, `app env`, the
dev-server route and the server's front-door URL follow the state; with a custom host `env up` checks loopback
resolution and prints the `/etc/hosts` line. Aspire: `PublicHost`/`PublicPort` on both option classes, `0` = a port
Aspire allocates (`WithHttpEndpoint(port: null, targetPort: 8080, isProxied: false)`, confirmed in decompiled
Aspire.Hosting 13.6.2 to allocate before containers start); values as `ReferenceExpression` over the localhost-network
endpoint property. Front door: host and port from `Localtest:PlatformEndpoint`, `Localtest:PublicSubdomains` parsed
(unknown → start-up error, unlisted → route off); the `PlatformRoutes.Hosts` constants replaced. Fakes: Authentication's
seeded Arbeidsflate client URIs, Correspondence's Arbeidsflate origins, Dialogporten GraphQL's CORS origin and the
Arbeidsflate image's CSP/cookie patch derive `af.` from the platform endpoint (reach the stack after a pin bump).
Suites: `LOCALTEST_ORIGIN`. Golden: +876 lines (new `customOrigin` case `origin.test:18000`; the default cases gained
only `Localtest__PublicSubdomains`), app golden +45; Aspire parity compares both. Checks: go clean, localtest tests 123,
Aspire 122, csharpier/typos clean, both samples build. Regression found by the coordinator: the localtest test suite
took 17 min (name resolution suspected); the agent fixes it before phase 2. Future independent `run --minimal` needs:
per-instance container/network/volume names, state file, server registration with several front doors, certificate
directory, graph id and probe names, runtime checks.

**Phase 2 (pushed cde7303f, verified by the coordinator 2026-10-23).** The 17-minute test run was not reproduced:
the agent's seven runs took 4–9 s (slowest test 0.57 s; strace shows only loopback plus one pre-existing DNS query
for `register` from `SeamTests`; with DNS dropped still 2 s), and the coordinator's re-run took 3 s; unexplained,
not a blocker. Runs: studioctl at `altinn.test:18000` with Dialogporten and dev builds of Authentication and the
GraphQL host (`--dev-dialogporten-graphql=Dockerfile.graphql` from the component folder): root 26/26, authentication
34/34 (the seeded Arbeidsflate client redirects to `af.altinn.test:18000`), Dialogporten 60/60 (CORS for the custom
origin); `studioctl run` and `app env` on the custom origin; browser sign-in with cookies on `.altinn.test` and the
issuer on the new origin (screenshot `localtest/localtest/docs/origin/`); `--minimal --port 0` → `Origin:
http://local.altinn.cloud:37639`, root 23+3; Aspire minimal with `PublicPort = 0` → port 24054 in the front door,
Storage, the workflow engine and both apps' environments (apps at `host.docker.internal:24055/24056`), root 23+3;
Aspire full at `altinn.test:18000` root 26/26; default origin root 26/26; `env up --host nothere.test` refused with
the hosts line. Found: the released app frontend's `getHostname` accepts only a first label `local` (or four or five
labels), so party selection says "Unknown domain" at `altinn.test`; docs recommend `local.<…>` hosts. The pinned front
door ignores `Localtest__PublicSubdomains` and still names the default dedicated hosts: the new front door reaches a
stack after a pin bump. `--port 0` race: studioctl releases the port seconds before Podman binds it; on a collision the
state kept the port and recovery needed `env down`; phase 3 fixes the recovery. `/etc/hosts` restored.

**Phase 3 (pushed 6fd85eff, verified by the coordinator and merged ff 2026-10-23).** `env up --port 0` keeps the
recorded port only while the front door container runs, else picks a new free port; a port-in-use failure at container
start deletes the state file and fails with a message naming the port and `--port 0` (Podman's "address already in use"
and Docker's "port is already allocated" recognised); decision-table and mocked-client tests. Open edge: after a
collision the containers that did start keep running without a state file until the next `env up` or `env down`.
Coordinator re-ran go build/test/lint. Worktree and branch removed; dev studioctl rebuilt from main.

**Pin bump 2026-10-23 (coordinator): `sha-48648ec` → `sha-6fd85ef`.** The images of the task-39 merge commit carry the
front door's configurable hosts and the fakes' derived Arbeidsflate origin. Proof on the plain pin: default stack with
dialogporten up in 37 s including pulls (18 containers); root 26/26, authentication 34/34, authorization 31/31,
dialogporten 60/60, front door 80/81 (known); the routes listing shows the dedicated hosts; minimal mode with
`--port 0` (35863) up in 6.7 s, four containers, no volume added, root 23/23 through `LOCALTEST_ORIGIN` with 3 named
skips; after both downs only the four `localtest-v2-*` volumes. Old pin images removed (3.6 GB); logs in
`/home/agent/code/.scripts/pin-6fd85ef/`.

## 40. Service names for the components' URLs, pass-through off

**Goal (2026-10-23, owner's decision).** Every component's call to another platform service uses `http://<name>`
resolved by `Microsoft.Extensions.ServiceDiscovery` with the configuration provider only (pass-through off, as the
localtest app does), from the manifest's `envs` or the fakes' settings; what stays on `{{origin}}` is rule 4 of the
conventions (issuers, discovery and `jwks_uri`, redirect URIs, browser links, cookie domains), decided per key in a
table; non-factory and non-.NET clients fixed as task 25's plan flagged; Storage's calls then reach the emulation in
minimal mode (`Host: <service name>` routed like the main host, with a test). Proof with dev builds of every changed
component on `--with everything`, every verified suite, a browser instantiate with a Dialogporten sync, and minimal
mode with `--dev-localtest` and `--dev-storage`.

## 41. `env up` tears down after a failed start

**Goal (2026-10-23, owner: "not very worried, but if it has a simple fix it would be nice").** After a failed
`env up -r localtest-v2` (create or start error, port in use), the containers it created are removed the way `env
down` does, the state file is not left behind, and the original error is returned; a health timeout's behaviour is
decided by the agent and named in the error.

**Outcome (merged 2026-10-23).** `failedstart.go`: a create or start error, a port in use or a cancellation partway
through removes what the start created (containers, probes, the network if this runtime made it; named volumes kept),
deletes the state file and returns the original error (the task-39 port message kept); a health timeout leaves the
containers and the state for inspection and says so (`env logs`, `env down`); cleanup failures are joined. Tests with
an in-memory runtime on the mock client for the three cases. Known: a container that exits at once counts as a start
error and is removed with its logs (devenv's error type is private). Coordinator re-ran go build/test/lint.

## 42. App frontend: platform links from the settings alone

**Draft (2026-10-23, owner's question "does the frontend really need to know if it is local?").** No: with
`PlatformFrontendSettings` the backend gives the frontend every platform URL, so the host checks in the monorepo
frontend (`isLocalEnvironment`, `isRecognizedAltinnHost`, `platformUrlForHost`, the cookie `Secure` rule) are guards
from before the settings and can go: a link exists when its URL is configured, the cookie's `Secure` follows the page's
scheme, and the runners set the Arbeidsflate URLs to the front door's Home when the group is off. The released v4
frontend has no settings; its `getHostname()` must stop throwing for an unrecognised host and treat it as the
platform's own origin (upstream PR to app-frontend-react). Details and the proof plan in the hand-over file.

**Owner decision 2026-10-24 (task 42, PR #21481 open and unreviewed).** The "configured or hidden" rule is replaced:
a platform link without a configured URL stays visible, marked as not working, and a click shows an alert naming the
`PlatformFrontendSettings` key. Reason: no real users in the environments without Arbeidsflate (at22, at24 set no
inbox URL; every environment sets the logout URL), and a broken link exposes a misconfiguration where a hidden one
masks it. Covers every settings-driven link including logout and the "all forms" link from #21436; the backend's
"null hides the link" doc comments change accordingly; one shared component renders both states. The runner-side
defaults (inbox to Home, logout to the platform's logout) stay, so nothing is unconfigured on localtest-v2.

**Task 42 revision (2026-10-24, head 4f865e2a62, verified by the coordinator: focused Vitest 163 and type check).**
`src/App/frontend/src/components/PlatformLink/PlatformLink.tsx`: with a URL a plain anchor; without one the same
anchor with `aria-disabled`, a dashed muted style, a title, a marker href (`#platform-setting-not-configured=<key>`),
a click that prevents navigation and alerts `PlatformFrontendSettings.<key> is not configured in this environment`;
links inside built-in texts get the marker through `platformLinkHref` and a `sharedLanguage` rule. Covers the inbox
(header, receipt, signing panel, party selection), all forms, profile, parties, access management, the roles help and
logout; the hiding from #21436 is gone; the backend's doc comments updated; both states tested per link; a signing
panel `Button asChild` nesting bug fixed on the way. The PR body must be pasted by the owner.

## 43. The apps' Maskinporten clients on localtest-v2

**Goal (2026-10-23, owner's question).** Whether the monorepo backend's and app-lib 8's `MaskinportenClient` and
`AuthenticationTokenResolver` (the `/Home/GetTestOrgToken` short-cut on localtest) need adaptation for localtest-v2,
including a custom origin; both token kinds; proof on the stack with both sample apps and a custom origin; a document
with the decision tables and the recommendation. The apps do not use `Altinn.ApiClients.Maskinporten` (task 15), to be
confirmed.

## 44. Egress domains of apps and the stack

**Goal (2026-10-23, owner's request).** The list of external domain names an app (app-lib 8 and the monorepo backend),
the browser frontends, pdf3, the workflow engine and each stack component call, with the caller, the purpose and
whether the domain is fixed in code, configuration or data; where production egress enforcement would sit and in
what format; a measurement design (OpenTelemetry `server.address` from the clients' spans, DNS and connection capture,
a logging forward proxy, deny-by-default egress on localtest as a later task) with scripts; phase 2 observes on the
full stack through every suite and a browser flow and reconciles observed against static. Document
`localtest/reviews/2026-10-23-egress-domains.md`.

**Owner decisions 2026-10-23.** Task 44: do not inventory app-lib v8; the app side is the monorepo backend and
frontend only (the agent was told). Task 43: the apps' localtest short-cut is fine for now; when the new localtest
becomes the default, apps should use the proper fakes (the real client path against the Maskinporten fake and the
Authentication exchange); the document states what that switch needs and the proof shows the real path works today if
it is cheap (the agent was told).
Task 44 addendum (owner 2026-10-23): the monorepo frontend's domains stay in the inventory, framed for a
Content-Security-Policy (directive per domain, fixed in the bundle, in the settings or in app data; what a CSP for an
app page would look like; whether the index page sets one today); phase 2 captures the browser's requests too.

**Task 40 phase 1 (pushed dc0211bb, verified by the coordinator 2026-10-23).** Finding: `Microsoft.Extensions.
ServiceDiscovery`'s configuration provider returns no endpoints for an unknown host (decompiled 10.8.0 and 9.5.2), so
"configuration only" would break every real-host call; the shared fake `components/shared/fakes/ServiceDiscovery/
LocaltestServiceDiscovery.cs` registers the core, the configuration provider and a "real host" provider (a dot, an IP
or `localhost` passes through unchanged; a single-label name without `services__<name>__http__0` fails at once) and
removes the pass-through factory by type name (the four `AltinnServiceDefaults` hosts have no setting for it). Access
Management's `ResourceRegistryClient` gets a factory client; Dialogporten's 4 and Correspondence's 9 clients that share
one production base URL get `LocaltestPrefixRouting` (moves `{origin}/<prefix>/…` to `http://<service>/<prefix>/…`,
table tested against `PlatformRoutes`); the Arbeidsflate BFF points at `http://localtest:8080` by name. Per-key table
in the agent's report; rule 4 values unchanged (JwtCookie metadata, issuers, redirect URIs, links, cookie domains).
Manifest: no `{{origin}}` left in server-call values; golden +9 lines (Register's PDP endpoint by name in every case,
Receipt's three and the BFF's three in `everything`). Minimal mode: `ServiceNameHostTests` (hosts `localtest`,
`register`, `authorization`, `authentication` answered by the emulation). Checks: every changed component built with
`LOCALTEST`; localtest tests 145, Go clean, Aspire 122 (coordinator re-ran). To watch in phase 2: Authentication's
system-user client now calls the local Access Management; an Events webhook to a single-label non-service host fails
at once; Correspondence's `EdDsaSecurityKeysCacheService` still fetches from the public origin.

**Task 42 phase 1 (2026-10-23).** Frontend branch `feat/app-frontend-settings-only` on the fork (813c0ae8f9): host
checks removed, links from the settings or hidden, cookie `Secure` from the page's scheme, tests rewritten; remaining
host reads are devtools detection, same-host relative links and the app's own URLs. Research runner change
`feat/arbeidsflate-links-fallback` (4b3f46af): the four links only when the `arbeidsflate` group is up, else the inbox
link = the front door's Home; goldens with two new arbeidsflate cases; Aspire 125. Finding: the monorepo's own
studioctl sets no `PlatformFrontendSettings`, so CI's Cypress specs would lose their inbox and logout targets;
decision: the monorepo studioctl sets the inbox and logout defaults to the localtest base URL in the same PR (in
progress). The full Vitest suite could not run on this machine (memory); the focused 212 pass.

**Outcome (merged 2026-10-23).** Nothing changes now. At the default origin both libraries take the service-owner
token from `/Home/GetTestOrgToken` (`ttd`, Storage 200, the monorepo app's 8 workflow callbacks 200). The apps detect
localtest by `GeneralSettings:HostName` being `local.altinn.cloud` or `altinn3local.no`, so **at a custom origin the
short-cut is off: the monorepo app does not start without a stored Maskinporten client (`MaskinportenSettingsStartupCheck`),
and app-lib 8 fails at first use**; task 39's custom host therefore works today only for apps with a client (or the
`local.altinn.cloud` host). The real client path against the fakes already works end to end at both origins (the fake
issues, Authentication exchanges, Storage as the org 200), but always as `digdir`: the client never sends `test=true`
and `ttd` has no organisation number. The switch to the proper fakes when the new localtest becomes the default:
(1) the runners write a local `maskinporten-settings.json` (authority `<origin>/localtest/api/maskinporten/` with the
trailing slash, a client id, a generated RSA JWK; skipped when the developer stored a client; ~60 lines Go, ~40 C#,
golden), (2) Authentication's fakes answer `ttd` for 991825827 in the exchange (~30 lines, a pin bump), (3) the
monorepo resolver drops the `GetTestOrgToken` branch or uses it only without a client (~10 lines), (4) the runners set
`AppSettings__RuntimeSecretsDirectory` for app-lib 8 (it reads that, not `RUNTIME_APP_SECRETS_*`). Open: a developer's
real client would replace the local one and the local exchange answers 401 (two options in the document). Task 15 does
not affect the apps' `MaskinportenClient` (app-lib 8 references the package only for legacy paths). Probe scripts in
`.scripts/task43/`. Worktree and branch removed.

**Phase 1 (pushed ec2a31b2, verified by the coordinator 2026-10-23; `localtest/reviews/2026-10-23-egress-domains.md`,
scripts `localtest/tools/egress/`).** App pod (monorepo backend): `platform.<env>` for every platform API, the OIDC
metadata, the exchange and eFormidling; in-cluster pdf3 (whose relay calls `studio.platform.<env>.altinn.cloud`) and
the workflow engine; `altinncdn.no` for the org list (code); Application Insights; the Maskinporten authority from the
secret; per feature `studioprodsc.blob.core.windows.net`, `api.fiks.ks.no:443` and `io.fiks.ks.no:5671`, Nets, Key
Vault and Entra ID, the app's own host, and the app's own code. Reached only through the platform: ID-porten, e-mail
and SMS, Dialogporten, KRR, Brreg, the population register, subscribers. Browser as a CSP: the app origin,
`altinncdn.no` (font, style, img, connect), `tile.openstreetmap.org` and `cache.kartverket.no` for the Map component,
`data:`/`blob:`, app-named hosts; `IndexPageGenerator` sets no CSP (a nonce would be needed for the inline bootstrap).
pdf3 opens any requested URL; the workflow engine's webhook calls any URI. Stack components: six clients fetch the org
list from `altincdn.no`, the Resource Registry falls back to `raw.githubusercontent.com`, Receipt loads Font Awesome and
Google Fonts, and two self-identified-user paths reach **at22** (Register's SBL bridge, Authentication's Access
Management setting): leaks to a shared test environment. Enforcement today: none for app pods (no NetworkPolicy, apps
in `default`; only pdf3's worker has a Cilium egress policy); options: Cilium `toFQDNs` (needs the DNS proxy,
unconfirmed), plain NetworkPolicy (CIDR only), Azure Firewall FQDN rules (Fiks on 5671 needs a network rule), a proxy
(the Fiks client ignores it). The infra repository could not be read (no `AZURE_DEVOPS_PAT`). Methods for phase 2:
packet capture (DNS plus SNI on the bridge and the uplink, attributed to containers and processes) as ground truth, an
OTLP collector for the app's client spans (`AppSettings__UseOpenTelemetry=true`, which studioctl does not set),
Playwright traces for the browser; the logging proxy rejected (Node, Chromium and AMQP ignore it). Open questions
1–7 in the document (one allow-list per app for egress, CSP and localtest; egress-denied localtest by default; the
platform serving `altinn-orgs.json`; the OTLP collector move; checking webhook and pdf3 URLs; Cilium's DNS proxy; the
at22 paths). Coordinator: typos (after allowing `AKS`), shellcheck clean.

## 45. Egress control: declared hosts enforced on localtest

**Plan (2026-10-23) in `planned-components/egress-control/README.md`.** An `externalHosts` field in
`applicationmetadata.json` (platform origin, own host, pdf3, workflow engine, telemetry and the CDN implied); the
front door as a forward proxy for the app (the runners set the proxy variables with `NO_PROXY` for the public host),
allowing the implied and declared hosts, refusing the rest with 403 naming the host and the field, and keeping a
per-app inventory (host, port, allowed/refused, counts, classification) served at `localtest/api/apps/{org}/{app}/egress`,
on Home and by `studioctl app egress`; optional OTLP attribution; later the production egress rules and the CSP
generated from the same field. Gaps named: raw sockets (closed by container mode on a closed network) and clients
that opt out of the proxy (flagged by telemetry). Phases: 1 inventory (observe), 2 enforcement with `--strict`,
3 attribution, 4 upstream (model, schema, Studio, deploy). Open questions 1–5 for the owner, the first being the
default for apps without the field (recommendation: observe until the field is in the schema).

**Task 40 phase 2 (pushed e2a9b82c, verified by the coordinator and merged ff 2026-10-23).** Fifteen changed
components as dev builds (prebuilt one by one, then one `env up --with everything` in 59 s), every suite at its
expected count: root 26, Register 14, Profile 52, Authentication 34, Authorization 31, Resource Registry 43, Events
27, Notifications 64, Dialogporten 60, adapter 39, Receipt 27, Arbeidsflate 59, shared 12, workflow-engine 3,
localtest app 65/67 (the "service off" check cannot hold with everything on, plus the bundle check), Correspondence
78/78 after the fix. Bug found by the migration: Correspondence's dialog-token JWKS fetch used the synchronous
`HttpClient.Send`, which bypasses the service-discovery handler (async path only) and went to DNS as
`dialogporten-webapi:80`; the pass-through had hidden it (05988923). Browser flow on the default stack with a
Dialogporten sync (dialog `Awaiting`, one transmission, three activities). Minimal mode with `--dev-localtest` and
`--dev-storage`: root 23+3, two browser instantiates, Storage's `decision` and `parties/lookup` calls logged going to
`http://localtest:8080`, its outbox dropping syncs with "adapter not running". Observations: Authentication's
system-user client could not be exercised (no suite reaches it); an Events webhook to a single-label non-service host
fails at the resolver with no delivery row (Events README corrected); Correspondence's upstream key cache still
fetches from the public origin (nothing reads it). The agent freed 14 GB (old pin images, unrelated images, caches).
Side effect to fix on arrival: the agent had removed the `local.altinn.cloud` entries from `/etc/hosts`
(restored with `env hosts add`). Coordinator re-ran go build/test/lint, localtest tests 145, Aspire 122.

**Owner decisions 2026-10-23 (task 45).** Missing declaration = observe, present = enforce (no `--strict`). The
proxy attributes the calling app by proxy credentials in the runner-set proxy URL (to verify .NET sends them), a
container app also by address. DNS: the front door as the resolver for container-mode apps (records per app, NXDOMAIN
for undeclared names), host-wide observation only for process apps. The stack's components call nothing external but
`altinncdn.no`, so the stack network gets a firewall (host and the CDN only), the at22 paths are removed first. Plan
updated (sections 1b, 3a–3c, phases 3b and 3c).
Addendum (owner 2026-10-23): Events is different, its webhook deliveries are data-driven egress; the `events`
container stays open and observed, not blocked (the workflow engine's webhook command likewise).

## 46. Egress inventory (egress control phase 1)

**Brief drafted 2026-10-23** from `planned-components/egress-control/README.md`. A forward proxy in the localtest
container (`localtest/localtest/Egress/`, CONNECT tunnelling and plain proxied requests, its own port published by
both runtimes and recorded), attribution by the user info of the runner-set proxy URL (fallback: a port per app if
.NET does not send `Proxy-Authorization` from the environment), a per-app in-memory inventory (host, port, scheme,
counts, first/last seen, classification platform/implied/declared/undeclared), `GET/DELETE
localtest/api/apps/{org}/{app}/egress`, a Home section with the `externalHosts` line to add, `studioctl app egress`,
the Aspire surface; nothing refused. Proof: the app-lib 8 sample with a scratch call to the CDN and example.com, the
monorepo app with a browser instantiate, task 44's capture as the cross-check, the custom origin once.

**Task 42 phase 2 and close (2026-10-24).** Proof on the stack with the monorepo app and the frontend from the branch
through `--dev-frontend`: with the Arbeidsflate group every link follows the configured URLs and "back to inbox" opens
the dialog in Arbeidsflate; without it every inbox link lands on Home; screenshots on the fork's
`evidence/app-frontend-settings-only` branch, linked from the PR body; at the custom origins the monorepo app refused
to start for task 43's Maskinporten reason (known), the runner's environment there is correct. Coordinator's decisions,
implemented: the research runners set `PlatformFrontendSettings__LogoutUrl=<origin>/authentication/api/v1/logout` on
every localtest-v2 run (also minimal mode); the party selection's inbox and profile links and the logout menu are
hidden without a URL (third commit on the fork branch, unit tests for both states). Runner branch
`feat/arbeidsflate-links-fallback` (inbox link to Home without the group, the four links with it, the logout URL;
goldens with two arbeidsflate cases; Aspire 125) verified by the coordinator and merged. Open: the v4
app-frontend-react host rule (separate upstream PR, not tasked).

**Pin bump 2026-10-24 (coordinator): `sha-6fd85ef` → `sha-e2a9b82`** (task 40's service names in the images).
`--with everything` up in 50 s with pulls; root 26, authentication 34, authorization 31, dialogporten 60,
correspondence 78, notifications 64, register 14 (at the origin; a first run against the suite's old default address
was the coordinator's mistake), events 27 (two timing checks failed once under load, passed on the rerun), front door
79/81 (known); minimal 23+3. Also fixed on main (af28e1d8): the front-door wait no longer overwrites its remembered
state with its own cancellation, which made `TestWaitForAppSaysWhatTheFrontDoorLastHad` fail about one run in three
under load and would have hidden the provisioning error from a timed-out `studioctl run`.

**Phase 2 (pushed 13326070, verified by the coordinator and merged ff 2026-10-24).** Capture (DNS plus SNI on the
bridge and the uplink, attributed per container and process) and the OTLP collector from before `env up` with
`--with everything` on `sha-e2a9b82`, through the suites (root 26, authentication 34, authorization 31, dialogporten
60, correspondence 78; Events 24/30, Notifications 61/64 and Register 11/14 failed on expired tokens and the
registered app during long VM suspensions, unrelated to egress), the monorepo app under `studioctl run` with
`AppSettings__UseOpenTelemetry=true` (the project-reference build ships no bundle, so the browser flow used main's
built `dist` through `--dev-frontend`), a Playwright-traced flow through upload, submit, receipt, PDF, the Receipt page,
the Arbeidsflate inbox and the dialog, and five idle minutes. Observed per process: the app → the public origin, the
pdf and workflow-engine subdomains, `altinncdn.no`, **`data.ssb.no`** (from `Altinn.Codelists`, which also defaults to
`ws.geonorge.no` and `www.bring.no`: an app using it needs them on its production list); the browser on the app page →
the app origin and `altinncdn.no`; on Receipt and Arbeidsflate → `altinncdn.no`, `use.fontawesome.com`,
`use.fortawesome.com`, `fonts.googleapis.com`, **`fonts.gstatic.com`**; pdf3 → `altinncdn.no`; Authentication, the
Resource Registry, the adapter, GraphQL and the BFF → `altinncdn.no`; the three Dialogporten hosts →
**`usage-tracking.masstransit.io`** at start-up (MassTransit's telemetry, nothing switches it off); the workflow engine
and every other component → nothing outside the stack. No unnamed connections, no IPv6, no QUIC. The two at22 paths
were deliberately not exercised (they would reach a shared test environment and create a user there). Capture script
fixes (pollers survive a failed call, final snapshot on stop, the bridge capture waits for the network). Capture files
in `localtest/reviews/egress/` (52 KB). Follow-ups for the plan (task 45): MassTransit telemetry off in the
Dialogporten fakes; the at22 paths removed; the firewall's allow-list is `altinncdn.no` only (the Receipt page's font
hosts are the browser's, not the stack's).

## 47. Compare altinn-apiclient-maskinporten#92 with our change

**Goal (2026-10-24).** Another contributor opened [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92)
(draft, 2026-10-23) for the same purpose as task 15's branch. Side-by-side comparison (settings, resolution,
audience, test org, interface, tests, README, versioning), whether #92 serves localtest-v2's consumers as it is,
the differences that matter, and a recommendation (support #92 with a review comment, keep ours, or merge the two)
with a draft comment the owner may post. Document `localtest/reviews/2026-10-24-maskinporten-pr92.md`. Task 15's own
PR is not opened yet.

**Outcome (merged 2026-10-24).** #92 covers task 15 as it stands and uses the same setting names (`TokenEndpoint`,
`TokenExchangeEndpoint`), so the consumer follow-up applies unchanged (each consumer keeps `Environment=test` or sets
`AssertionAudience`, and needs a real throwaway RSA key). #92 adds the interface members with default implementations
(no break; ours would break direct implementers), has a public issue (#91) with a second consumer, and documents what
a mock must accept. Ours is better on four small points (empty values as unset; absolute http(s) validation naming the
setting; `&test=true` when the exchange URL has a query, where #92 sends `?x=1?test=true`; the audience without
`Environment`), offered as non-blocking suggestions in a draft review comment. Both suites pass (#92: 12, ours: 25); a
probe against a localtest-shaped server shows #92 works over plain http. Nothing was posted. The reference clone has a
local branch `pr-92`. (2026-10-26: #92 merged and released as `v10.2.0`; see section 15.)

## 48. Text parameters as React elements (explore)

**Goal (2026-10-24).** Whether the app frontend's text parameters can be React elements, so the two platform links
inside built-in texts (missing roles, no valid parties) can take a `PlatformLink` element instead of #21481's string
marker and parser rule. Time-boxed: a small PR on the fork if the substitution has a type-safe answer that survives
`langAsString` and improves on the marker; otherwise a notes file and no push.

**Outcome (2026-10-24, no PR).** Feasible: an opt-in element parameter becomes a placeholder span before parsing and
a parser callback renders the element in place (prototype passed marked and DOMPurify); `ValidLangParam` already
accepts `ReactNode`, `langAsString` flattens an element to its text. Costs: a second uncached parse path, the link
classes and `target`/`rel` lost for that link, and the texts themselves must change (three keys in nb, nn, en, the
link text moved into new keys, including `party_selection.seeing_this_preference` where the link sits mid-sentence).
Blast radius: 44 `params=` sites, 9 already pass elements that are flattened today, 16 `langAsString` calls; app text
overrides ignore `params` entirely, so they never received the URL. Verdict: about 30 `PlatformLink`-specific lines
removed against a new parameter kind in the shared language code; the marker is the smaller change for #21481; revisit
only if more in-text component links appear.
Addendum (owner 2026-10-25): `PlatformLink` should not need both the setting and the URL; `href` becomes optional
and is derived from the setting (the helpers keep the templating and the access-management redirect), kept only as
an override for the computed receipt and header links; `platformLinkHref` likewise. Agent resumed, same PR.
Done 2026-10-25 (head d81f8a38fb, verified by the coordinator: focused Vitest 99, type check): `PlatformLink` takes
`setting` only and reads the URL through a map to the `urlHelper` functions; `href` is an optional override for the
computed inbox and dialog links; `platformLinkHref(setting, url?)` likewise; three commits kept.
Done 2026-10-25 (head b0e5555fc5, verified by the coordinator: focused Vitest 103, type check): `href` gone;
`PlatformLink` is a discriminated union, the inbox setting takes optional `partyId` and `dialogId` (dialog template,
party switch through the access-management redirect, or both), every other setting refuses them at compile time;
no caller computes a URL; `platformLinkHref(setting)` for the non-inbox text links.

## 49. localtest-v2 "up" from the containers, not the state file

**Origin (2026-10-25).** Re-review of [altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394)
after the env-runtime refactor ([altinn-studio#21483](https://github.com/Altinn/altinn-studio/pull/21483)). `env up`'s
conflict check and the default `-r` already come from the running containers (`Env.Active`, `envtypes.ResolveRuntime`),
and a failed start removes the state file (task 41). Elsewhere, the existence of
`generated/localtest-v2/state.json` still means "localtest-v2 is up", so after a reboot or a `docker rm` a stale file
changes what other commands do.

**The readers, under `localtest/studioctl/src/cli/` (non-test):**

- `internal/studioctlserver/client.go:1004` (`buildStartConfig`): turns on `Localtest__RegisterApps` and turns off the
  host bridge.
- `internal/cmd/app/run.go:172` (`nativeAccessTokenSigning`): gives the app the test certificate and binds it to
  `0.0.0.0`.
- `internal/cmd/app/run.go:275` (container mode): puts the app on the stack's network.
- `internal/cmd/run_frontdoor.go:67`, `:101`: the registration wait and the `--dev-frontend` route.
  `internal/localtestv2/state.go:69` (`CurrentTopology`) is the origin they use.
- Inside the runtime (`env.go`, `minimal.go`, `origin.go`), the file is read as the started configuration (groups,
  origin, mode). That use is correct and stays.

**Goal.** The state file is the record of the configuration the stack was started with, never the evidence that it
is running.

- **studioctl-server's start config:** decided from whether localtest-v2's containers run. `env up` and `env down`
  already restart or reconcile the server, so the decision is only made at those points and at a plain server start.
  The agent picks between a container check there (it needs a container client) and keeping the file but removing it
  wherever the runtime finds no containers. Name the trade-off in the outcome.
- **`studioctl run`:** with a state file but no answer from the front door, stop at once with a one-line fact and the
  command that fixes it (`env up -r localtest-v2`, or `env down -r localtest-v2` to forget the stack). Today it waits
  for the whole startup timeout.
- **Container mode and the app environment:** the same check, so a stale file neither binds the app to `0.0.0.0` nor
  puts it on a network that does not exist.

**Proof.** Unit tests with a stale file and no containers for each reader. On the stack: `env up -r localtest-v2`,
remove its containers with `docker rm -f` (leaving the file), then `studioctl run` and `studioctl env up -r localtest`
both behave as if localtest-v2 were down. Update `localtest/studioctl/README.md` ("What the localtest-v2 change adds")
and studioctl's `AGENTS.md`.

## 50. Front-door registration off the discovery loop

**Origin (2026-10-25).** Re-review of altinn-studio#21394, point C (the same finding as point 7 of the first review).

**Problem.**
- `studioctl-server/Discovery/AppRegistry.cs:214` awaits `FrontDoorRegistration.Reconcile` inside the registry's
  refresh, one app at a time, through the `front-door-registration` HTTP client with a 2-minute timeout
  (`ServiceCollectionExtensions.cs:36`).
- A slow or hanging front door therefore stalls discovery, the status endpoint's view of the apps and the
  `CompleteReadyStarts` notices for every app, including apps that have nothing to do with localtest-v2.

**Goal.**
- Registration runs beside discovery: a background worker fed by the registry's changes, one registration in flight per
  app.
- A slow front door delays only that app's registration.
- The ordering the comment at line 213 protects is kept: on localtest-v2, an app's start is complete only once the
  front door has provisioned it, because Storage knows the app only after that. So the start waiter for an app waits for
  that app's registration outcome, not for the whole refresh. With registration off (`Localtest:RegisterApps` false),
  nothing changes.
- The retry and re-registration rules stay as they are: a failed registration, a Dialogporten sync that failed, an app
  that restarted, an app the front door no longer lists, and a `DELETE` for an app that went away.

**Proof.**
- `FrontDoorRegistrationTests`, extended:
  - a hanging front door does not delay discovery of a second app or the status endpoint
  - a start completes after its registration succeeds
- On the stack: `studioctl run` with two apps while the front door is paused (`docker pause`), then unpaused.

## 51. Monorepo-ready references in the studioctl change

**Origin (2026-10-25).** Re-review of altinn-studio#21394, point D: about 170 added lines in the PR mention this
repository or `ivarne-agent`, five comments cite this repository's task numbers (21e, 25, 27a, 34, 40), and
`internal/localtestv2/manifest/README.md` links to `../../../../../../CONVENTIONS.md`, which does not exist in the
monorepo.

**Goal.** The vendored studioctl source under `localtest/studioctl/src/` reads correctly in the monorepo.

- No task numbers in code comments or docs.
- No relative links that leave `src/`.
- The conventions the code depends on (manifest rules, ports, service discovery, certificate, volumes) are stated in
  the manifest README or in studioctl's README, and `localtest/CONVENTIONS.md` here points to them instead of the
  other way round.
- What must still name this repository (the image registry in `manifest.json`, the test key pair's origin in
  `certs.go`, the Aspire package in the golden tests) uses absolute GitHub URLs, and each is listed in one "Not
  mergeable yet" note in `localtest/studioctl/README.md`, so the monorepo PR can track them.
- No behaviour change. The golden files only change if a comment inside them changes.

**Proof.** A grep of `localtest/studioctl/src` for task numbers and relative links leaving `src/` finds none, and every
remaining mention of this repository is on the list. `go test ./...`, `make lint` and Prettier pass. Sync into
altinn-studio#21394 afterwards, as in `localtest/studioctl/UPSTREAM.md`.

## 52. Monitoring in localtest-v2: OpenTelemetry collector and Grafana

**Origin (2026-10-25).** The user asked to bring the OpenTelemetry and Grafana containers into localtest-v2. Today
`studioctl env up -r localtest --monitoring` has them and localtest-v2 has no `--monitoring` flag. The Aspire host
(tasks 20, 21, 30, 37) needs none of this, because Aspire has its own dashboard and OTLP endpoint, so this task is about
studioctl only.

**What exists (vendored studioctl at `eae0a7ea65` plus our change, under `localtest/studioctl/src/`):**

- **Five containers**, specified in `cli/internal/cmd/env/localtest/components/monitoring.go`. Their images are pinned
  in `cli/internal/config/images.go` (`MonitoringImages`), and each one can be overridden with `STUDIOCTL_IMAGE_*`:
  - VictoriaMetrics, VictoriaTraces and VictoriaLogs, each keeping one day of data under `/tmp`;
  - `otel/opentelemetry-collector-contrib` 0.161.0, which publishes ports 4317 (gRPC) and 4318 (HTTP) on the host,
    with the host alias `otel.local.altinn.cloud`;
  - `grafana/grafana` 12.4.8, with anonymous Admin access, the pinned VictoriaLogs plugin, and served from the sub-path
    `/grafana/`.
- **The configuration files** are in `Runtime/localtest/infra/` and already ship with studioctl's localtest resources:
  - `otel-collector.yaml` copies the router in Altinn's environments;
  - `grafana-datasources.yaml` uses the same datasource UIDs as the shared Grafana, so dashboards carry over;
  - `grafana-dashboards.yaml` and `grafana-dashboards/` hold the app, aspnetcore, aspnetcore-endpoint and
    workflow-engine dashboards.
  - The collector exports to the hard-coded names `localtest-victoria-*`, and the datasources point at those names
    too.
- **The old localtest serves Grafana itself.** Its own proxy routes `/grafana/` through the topology's `grafana`
  component (`topology.yaml`, `pathPrefix: /grafana`). Its Home page shows a Grafana link only when that route exists
  (`InfraController.Grafana`).
- **Apps on localtest-v2 already point at the collector.** `app env` sets
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel.<public host>:4317` for every run, including localtest-v2 (see the golden
  file `app-environment.golden.json`). Nothing listens there on localtest-v2, so the setting has no effect today.
  - It also has no effect on most apps: app-lib only uses OpenTelemetry when `AppSettings:UseOpenTelemetry` is true.
    Otherwise it uses Application Insights, which needs a connection string, so nothing is sent. studioctl does not set
    `UseOpenTelemetry`. Some test apps, such as `component-library`, set it in their own `appsettings.json`.
- **The components and OTLP**, as the vendored sources read today:
  - Workflow engine: it exports OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Our manifest turns it off with
    `EngineSettings__EnableTelemetry=false`. The old runtime sets `OTEL_SERVICE_NAME=workflow-engine-app` and a 15 s
    metric interval.
  - Dialogporten: it reads the standard `OTEL_EXPORTER_OTLP_*` variables (`Digdir.Library.Utils.AspNet`).
  - Notifications: traces only, through `OtelSettings__Endpoint`.
  - Storage, Register, Profile, Authentication, Authorization, Events, Access Management, Correspondence, Receipt and
    the adapter: Application Insights or Azure Monitor only, with no OTLP path.
  - The front door (the `localtest` container): no telemetry at all.

**Design to decide before approval.**

- **Where the containers are declared (the user's question, 2026-10-25: can telemetry be a group that Aspire knows to
  ignore?).** Yes, if two conditions are met.
  The group approach is chosen.
  - **Condition 1: the manifest can describe the containers (decided 2026-10-26).** Both loaders reject unknown fields
    (`DisallowUnknownFields` in `manifest.go`, `JsonUnmappedMemberHandling.Disallow` in
    `ManifestJsonContext.cs`). A container has only `registry`, `tag`, `port`, `health`, `envs`, `volumes` and
    `dependsOn`. The old runtime configures the stock upstream images with bind-mounted files, command arguments and
    env variables. It builds no images, and neither does this task: all five images stay upstream, pinned by tag and
    digest.
    - *Files:* a new optional container field `files`, which maps a path in the container to a file or folder kept
      beside `manifest.json` under `monitoring/`. Both runtimes mount it read-only; studioctl writes the files out
      first, as it does for the certificate. The images run as non-root users (the collector as UID 10001), so the
      files must be readable by everyone.
      - The collector's config is mounted at the stock image's default path `/etc/otelcol-contrib/config.yaml`. The
        image's default command already points there, so it needs no arguments.
      - Grafana gets its two provisioning files and its dashboards folder, at the same paths as in the old runtime.
        Its other settings are `GF_*` env variables in `envs`: sub-path, `GF_SERVER_ROOT_URL={{origin}}/grafana/`,
        anonymous Admin, and the pinned VictoriaLogs plugin through `GF_PLUGINS_PREINSTALL_SYNC`, which Grafana
        downloads at start, as it does today.
      - The files are the old runtime's `infra/` files. The only change is the Victoria host names, which become the
        manifest's container names, or come from `${env:…}` in the collector config.
    - *Arguments:* none expected. The Victoria containers run on their defaults: data in the working directory and
      inside the container, so it goes with `env down`; the old runtime's `/tmp` path and 1-day retention add nothing.
      Their flags can only be read from env variables after the argument `-envflag.enable`. If a default does not
      work (for example, a data folder the image's user cannot write), an `args` field is acceptable (decided
      2026-10-26). Aspire has `WithArgs`, and devenv's container resource has `Command`.
    - *Host ports:* none. Native apps send OTLP over HTTP through the front door, on the path prefix `/otlp` of the
      main host, which is routed to `otel-collector:4318` with the prefix removed. There is no subdomain (decided
      2026-10-25). `app env` then sets `OTEL_EXPORTER_OTLP_ENDPOINT={{origin}}/otlp` and
      `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`. app-lib's plain `AddOtlpExporter()` takes both from the
      environment and adds `/v1/<signal>` to the path; verify that on the stack. gRPC through the front door would
      need h2c on port 8000, so it is not used. As a result, `--host` and `--port` work with no extra hostname setup.
  - **Condition 2: Aspire ignores the group explicitly (decided 2026-10-25).** The group is an ordinary manifest
    group with no marker. studioctl's `everything` includes it. The Aspire host names it in its own code as a group
    it ignores, because Aspire's dashboard does the same job. For example, `LocaltestManifest.MonitoringGroup` sits
    beside `DefaultGroup` and `MinimalGroup` and is excluded the same way. Today Aspire's `OptionalGroups` is "every
    group except `default` and `minimal`". `everything` enables all of them, and `GroupMethodsTests` requires a typed
    `With<Group>` method for each one. The ignored group therefore comes out of `OptionalGroups`, out of Aspire's
    `everything` and out of the method test, and `WithGroup("monitoring")` fails with a message pointing at the
    Aspire dashboard. A new group that Aspire does not know still fails the method test, so ignoring a group is
    always a deliberate decision. In studioctl, the group is turned on by `--with monitoring` or `--with everything`.
    There is no `--monitoring` flag: localtest-v2 turns every group on the same way (decided 2026-10-25).
  - **One variable for where telemetry goes (decided 2026-10-26).** The manifest gets no telemetry field. Every
    component, the front door and the apps send OTLP exactly when the standard `OTEL_EXPORTER_OTLP_ENDPOINT` is set,
    and send nothing when it is not (tasks 53 and 54). When `otel-collector` runs, studioctl's runner adds to every
    container, as it adds `services__`:
    - `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`;
    - `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`. Task 53's audit (2026-10-26) found that Dialogporten fails to start without
      the protocol variable and drops traces and metrics over `http/protobuf`, and that the Arbeidsflate backend sends
      over gRPC whatever the variable says. Inside the network gRPC needs no front door, so the containers use it.
      Only native apps go through the front door's `/otlp` over `http/protobuf` (task 54), and the .NET SDK adds the
      `/v1/<signal>` paths correctly.
    - `OTEL_SERVICE_NAME=<container name>`, unless the container's `envs` set it. The workflow engine keeps the
      runner's `workflow-engine` (2026-10-26): its dashboard reads `$service` from the engine's metrics, so it needs no
      `workflow-engine-app` (the old runtime's and Altinn's name), and without the group the engine gets no `OTEL_*`.
    Aspire gives its containers the same variables, pointing at its dashboard, so the components' telemetry reaches it
    with no extra work.
  - **Not in minimal mode (decided 2026-10-26).** Grafana and the Victoria stack stay out of minimal mode, and minimal
    mode keeps taking no optional group. Task 55 gives minimal mode a small OTLP viewer of its own.
  - **Not chosen:** the studioctl runtime adding the five containers in Go, outside the manifest. That would mean a
    second place that declares containers, plus host ports 4317 and 4318 again.
  - **Not chosen:** research images with the config files built in (2026-10-26). Mounting files keeps the stock
    images and needs no image builds or tag bumps.
  - **Not chosen:** `grafana/otel-lgtm`, a single container with a collector, Prometheus, Tempo, Loki and Grafana. It
    is simpler, but it does not match the Victoria stack Altinn runs or the shared Grafana's datasource UIDs, so the
    dashboards would not carry over.
- **Where Grafana is reached (decided 2026-10-25): under the sub-path `/grafana/` on the main host,** as the old
  localtest does. It gets no subdomain.
  - The prefix needs `new("/grafana", "grafana")` in `localtest/localtest/Proxy/PlatformRoutes.cs`. When the group is
    off, the existing 503 for a component that is not running names the group.
  - Grafana's `GF_SERVER_ROOT_URL` is `{{origin}}/grafana/`, so `--host` and `--port` (task 39) work.
  - A Home-page link is shown when the route is live.
- **Apps and components** follow tasks 53 and 54. The workflow engine's fix (altinn-studio#21515, 884b8ed755) is in
  `tt02`, and the manifest no longer sets `EngineSettings__EnableTelemetry` (2026-10-26). Task 53 lists the rest.

**Goal.**

- `studioctl env up -r localtest-v2 --with monitoring` starts the
  five containers on the stack's network. The Aspire host never starts them.
- `env down` and `env reset` remove them. `env status` lists them.
- An app run against the stack sends traces, metrics and logs that show up in Grafana's Metrics, Traces and Logs, and
  the app dashboard works without being edited.
- The workflow engine's traces join the app's traces in a single trace, and the workflow-engine dashboard shows data.
- Without the group, nothing changes: same containers, same environment variables, same golden files.
- The `README.md` line "Not in localtest-v2 yet: the monitoring stack" is updated, and the manifest README's "What the
  runner adds" section names the monitoring containers.

**Proof.**

- Unit tests:
  - the resource graph with and without monitoring;
  - both manifest loaders accept the new group field;
  - Aspire's `OptionalGroups`, `everything` and the method test leave the group out, and `WithGroup("monitoring")` fails
    with a clear message;
  - studioctl's `everything` includes `monitoring`;
  - the `app env` golden file gets a `native-localtest-v2-monitoring` case;
  - a front-door route test for `/grafana` that answers 503 when Grafana is not running.
- On the stack:
  - `env up -r localtest-v2 --with monitoring`;
  - `studioctl run` of a test app with `UseOpenTelemetry` (`component-library`, or a scratch copy under
    `/home/agent/code/apps`);
  - open `http://local.altinn.cloud:8000/grafana/` with `playwright-cli`, and screenshot a trace that spans the app,
    Storage's span as seen from the app's client, and the workflow engine, plus the app dashboard;
  - `env down`, then check that no monitoring container remains;
  - then `env up` without the flag, and diff `docker ps` and `app env` against an earlier run.
- Memory: the five containers' resident memory, measured with `docker stats` and added to task 38's numbers, since
  this machine has 7.8 GB.
- Do not port to altinn-studio#21394 or any other PR until the user says so (decided 2026-10-25).

**Done: the workflow engine (2026-10-26, commit f8f4148f, branch `task52-engine-otel`).**
- The engine's `tt02` image is altinn-studio#21515's merge (label `org.opencontainers.image.revision` 884b8ed755,
  created 12:31 UTC). The manifest drops `EngineSettings__EnableTelemetry=false`; the golden file, the manifest and
  studioctl READMEs and the component README follow. Aspire's tests pass unchanged (151).
- The research images move to `sha-38fbbba` (`bump-tag.sh`): the images run of 38fbbbae, the last green run on main,
  which contains the front door's `/grafana` and `/otlp` routes (52) and its OpenTelemetry (54, 4faf77e7). Every one of
  the 20 tags exists.
- On the stack (`env up -r localtest-v2 --with monitoring`, pinned images, 18 containers): the engine gets
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`, `grpc` and `OTEL_SERVICE_NAME=workflow-engine`.
  - `ttd/testapp` (app-lib 8.12.15) never calls the engine: app-lib drives process/next through it only from
    9.0.0-preview, which targets net11 (not installed). One instance was created and ended through `process/next`
    without the engine, and webhook workflows went to the engine's API instead, through the front door.
  - An enqueue with a client `traceparent` is one trace: the front door's server and forwarding spans, then the
    engine's `POST /api/v1/{namespace}/workflows/`, `Engine.EnqueueWorkflow` and its children. The engine runs the
    workflow in a trace of its own (`WorkflowHandler.Handle`) with a span link to the enqueue span; each webhook's
    `GET` continues through the front door (`GET ttd/testapp`, `GET storage`). The app's own server span is not in it
    (app-lib starts a new trace, task 54's note), nor is Storage's (no OTLP until task 53's batch 2).
  - The Workflow Engine dashboard shows data unchanged: `$service` is `label_values(... "service.name")` of the
    engine's health metric, so `workflow-engine` works and no `OTEL_SERVICE_NAME=workflow-engine-app` was added.
    Not carried over from the old runtime: `OTEL_METRIC_EXPORT_INTERVAL=15000` (the SDK's 60 s applies) and the
    dashboard's Grafana links (`EngineSettings__Dashboard__GrafanaUrl`, `...GrafanaTracesDatasourceUid`).
  - Without the group: no `OTEL_*` on the engine, no export lines in its log, and a 40 s capture of SYN and UDP
    packets in its network namespace during an enqueue shows only the front door's call to 9090 and the webhook to the
    host gateway's 8000, nothing to 4317/4318.
  - Screenshots: `/home/agent/code/.handover/task52-engine/`.

**Done: the collector starts first, metrics every 5 s, the engine's Grafana links (2026-10-26, commits 569ef9a4 and
8ca809a0, branch `task52-collector-first`).**
- While `otel-collector` runs, every container outside the monitoring group depends on it besides its manifest
  `dependsOn` (`telemetryDependsOn` in `monitoring.go`, runner code, not manifest data): devenv starts it once the
  collector's readiness probe passed and stops it before the collector. No cycle: the collector's dependencies are the
  Victoria stores, which are in the group (a test builds the graph and orders it). The readiness (TCP to 4318) covers
  4317: the collector starts its receivers after exporters and processors, and the OTLP receiver opens its gRPC
  listener before the HTTP one (`otlpReceiver.Start`, collector v0.161.0); three cold starts of the pinned image with
  a 5 ms probe of both ports saw both open in the same round. Golden file: only the `everything` case changes (22
  containers gain the dependency); Aspire's `GoldenComparisonTests` leaves the runner's collector dependency out, as it
  does the monitoring envs (151 tests pass).
- Part 2 (the user's 5 s, not the brief's 15 s): `OTEL_METRIC_EXPORT_INTERVAL=5000` on every container that gets the
  OTLP variables (own `envs` win); the engine's manifest `envs` get `EngineSettings__Dashboard__GrafanaUrl=
  {{origin}}/grafana` and `...GrafanaTracesDatasourceUid=altinn-studio-traces`, unconditionally (without the group the
  links lead to the front door's "not running" for Grafana; Aspire gets them too). `localtest/README.md`'s "faster than
  production" table has the row (15 s in the engine's kustomize base, pdf3's worker and the Designer chart).
- On the stack (pinned images, studioctl from the worktree under a short `STUDIOCTL_HOME`):
  - `env up -r localtest-v2 --with monitoring`: 31.2 and 31.5 s before, 32.4, 31.5, 28.7 and 30.8 s after; Grafana's
    plugin download (27-30 s) is the critical path either way. The components start about 2 s later (first at +2.2 s
    instead of +0.1 s, last at +10.4 s instead of +9.0 s, from the first container's start).
  - Order (`podman events`, one run): Victoria stores 629.5-629.7, collector started 630.9, its probe container exited
    631.19, then the front door at 631.76 and every component after it. Before the change the front door and Postgres
    started first and the collector at +3.3 s, after pdf3.
  - `env down`: every component, the front door and Grafana died before the collector (633.0-635.6 vs. 635.9), then
    the Victoria stores; before, the collector died first.
  - No container logged an export failure, before or after (the race of task 53's pdf3 line did not reproduce in three
    runs before the change; pdf3 is pulled on every start, which usually puts it after the collector).
  - Metrics every 5 s: raw samples of the last minute in VictoriaMetrics, every series per `service.name`, all twelve
    services that send metrics at a median and mode of 5.0 s (the front door also has 0 s pairs, several points per
    export).
  - The engine's `/dashboard/config` returns both settings; its dashboard's "Engine trace in Grafana" link on a
    webhook workflow opens Grafana Explore on the workflow's trace (`WorkflowHandler.Handle`, 10 spans). Screenshots:
    `/home/agent/code/.handover/task52-collector-first/`.
  - Without the group (`env up` default): 13 containers, no `OTEL_*` variables, 11.0 s.

**Done: the engine dashboard's trace links follow the runtime (2026-10-26, commits 06032322 and 83ad35d6, branch
`task52-trace-links`).**
- studioctl: `EngineSettings__Dashboard__GrafanaUrl` and `...GrafanaTracesDatasourceUid` leave the engine's manifest
  `envs`. The runner adds them beside the OTLP variables (`engineTraceLinks` in `monitoring.go`) while the collector
  and Grafana run, with the public origin (`--host`/`--port`); the container's own `envs` win. Golden file: only the
  default, minimal and custom-origin cases change (the pair is gone); `everything` keeps it, now from the runner.
  Aspire's `GoldenComparisonTests` counts the pair among the runner's telemetry variables (`_telemetryVariables`).
  A test holds the UID to `monitoring/grafana-datasources.yaml`.
- Aspire: `EngineSettings__Dashboard__TraceUrlTemplate=<dashboard>/traces/detail/{traceId}` on `workflow-engine`
  (`DashboardTraceLinks`): the `aspire-dashboard` resource's `https` endpoint, else `http`, read on the localhost
  network (Aspire's own dashboard link reads the same endpoint; `ASPNETCORE_URLS` or an allocated port). The route is
  in the 13.6.2 dashboard binary; not set without a dashboard. `DashboardTraceLinksTests` (4, 155 tests in all).
  - A scratch AppHost (Aspire 13.6.2, Podman, `ASPNETCORE_URLS=http://localhost:18888`) with the same code on a
    busybox container: the container got `http://localhost:18888/traces/detail/{traceId}`, the host's address, not
    `host.docker.internal`. The route answered: without a dashboard session it redirects to
    `/login?returnUrl=/traces/detail/<id>`, after the token login it shows the trace page ("not found" for an unknown
    id).
- On the stack (pinned images, engine `tt02` = 884b8ed755, studioctl from the worktree under a short `STUDIOCTL_HOME`):
  - `--with monitoring`: the engine has the pair; `/dashboard/config` returns
    `{"grafanaUrl":"http://local.altinn.cloud:8000/grafana","grafanaTracesDatasourceUid":"altinn-studio-traces"}`; an
    enqueued webhook workflow's card draws "Engine trace in Grafana", which opens Grafana Explore on the workflow's
    trace (`workflow-engine: WorkflowHandler.Handle`).
  - Default (13 containers): no `EngineSettings__Dashboard__*` and no `OTEL_*` on the engine, `/dashboard/config`
    returns `{}`, and no card has a Grafana link (0 `a[href*=grafana]`), including the workflow from the run before.
  - Screenshots: `/home/agent/code/.handover/task52-trace-links/`.
- **Not verified:** the Aspire link in the engine's dashboard. The `tt02` image ignores `TraceUrlTemplate` until the
  upstream engine change is in it; then an AppHost run with a webhook workflow shows it: `/dashboard/config` returns the
  template and the card's trace link opens `<dashboard>/traces/detail/<traceId>`.
- **Found here, done since (below):** the package's containers had no OTLP exporter, so the engine's own spans did
  not reach the Aspire dashboard.

**Done: every platform container sends its telemetry to the Aspire dashboard (2026-10-26, commit 3a9c9b6d, branch `task52-aspire-otlp`,
brief `/home/agent/code/.briefs/task52-aspire-otlp.md`; the user's decision of 2026-10-26).**
- Mechanism (Aspire 13.6.2 source, `OtlpConfigurationExtensions`): `WithOtlpExporter()` on every localtest container
  (full and minimal) sets `OTEL_EXPORTER_OTLP_ENDPOINT` (the dashboard's OTLP/gRPC endpoint, resolved for the
  container's network: `http://host.containers.internal:18889` under Podman), `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`,
  `OTEL_EXPORTER_OTLP_HEADERS=x-otlp-api-key=...`, `OTEL_RESOURCE_ATTRIBUTES=service.instance.id=...` and, in
  Development only, 1 s batch delays. The package sets `OTEL_SERVICE_NAME=<manifest name>` and
  `OTEL_METRIC_EXPORT_INTERVAL=5000` over Aspire's; manifest `envs` win (`DashboardTelemetry`).
- Reaching the host: with the container tunnel off (Podman 5.7: its image fails with `bufio.Scanner: token too long`)
  DCP proxies the dashboard's endpoints on loopback, and for `0.0.0.0` only on the interfaces present when it starts,
  not the containers' bridge. With the tunnel off the package turns the dashboard's OTLP endpoints unproxied on
  `0.0.0.0` (`HostWideOtlp`, a subscriber after Aspire's dashboard one); trade-off: the OTLP port is open on the
  machine's network, behind the API key. TLS: the README's HTTP OTLP endpoint needs no certificate; the https default
  relies on Aspire appending the dev certificate to `SSL_CERT_DIR` (not tried).
- Found: the API key changed every run, so every restart recreated all 18 persistent containers (25 s instead of 3 s).
  The workaround of commit 3a9c9b6d (`KeepOtlpApiKey`, a key in `<DataDirectory>/otlp-api-key`) was removed after the
  cause was found (below).
- Tests: `DashboardTelemetryTests` (9); `GoldenComparisonTests` leaves each runner's telemetry variables out on both
  sides and compares protocol, service name and interval with studioctl's `everything` (171 tests in all).
- Proof under Podman (pins `sha-609c646`), screenshots and the Telemetry API's listings in
  `/home/agent/code/.handover/task52-aspire-otlp/`:
  - Full sample (18 containers, both apps): all three signals from Access Management, Authentication, Authorization,
    the adapter, Events, the front door, Notifications, Profile, Register, Resource Registry, Storage, the engine and
    both apps; Dialogporten's three hosts send all three but appear as two instances each (traces and metrics with an
    instance id of their own, the Serilog logs with Aspire's); pdf3 metrics and start-up traces, no logs (stdout).
  - One `POST ttd/testapp/instances`: one trace, 161 spans in eight resources (app, front door, Authentication,
    Register, Authorization, Profile, Access Management, Storage). An engine enqueue through the front door: one trace
    (front door, `POST /api/v1/{namespace}/workflows/`, `Engine.EnqueueWorkflow`), opened at
    `<dashboard>/traces/detail/<id>`; the workflow's own trace `WorkflowHandler.Handle` (10 engine spans).
  - Minimal sample: front door, Storage, engine and both apps with all three signals; the app request 112 spans in
    three resources; the engine's enqueue and workflow traces as above.
  - Restart after the key fix: all 18 containers reused, healthy in 3 s, telemetry still arriving.
  - Memory (`free -m` used, beside section 37's 3080 and 4254 MB): minimal with both apps 3789 MB, full 5508 MB; both
    with Chromium, a 0.58 GB idle compiler server and other sessions' processes on the machine, so not comparable one to
    one; the dashboard process 325 MB with the full stack's telemetry.
- Not verified: the https OTLP endpoint with a trusted dev certificate; Docker and Docker Desktop (the tunnel-on path
  is Aspire's own, unchanged); the engine image's own dashboard link (the `tt02` image ignores the template).

**Done: the OTLP API key's real cause, and the workaround removed (2026-10-26, branch `task52-aspire-otlp-key`, brief
`/home/agent/code/.briefs/task52-aspire-otlp-key.md`).**
- Upstream (2026-10-26, checked on `microsoft/aspire` `main` a7ea8cd89, brief
  `/home/agent/code/.briefs/aspire-otlp-key-warning.md`): already fixed for Aspire 17 by
  [microsoft/aspire#20135](https://github.com/microsoft/aspire/pull/20135) (merged 2026-10-14, milestone 17.0, in no
  release yet). Generated values go to a secrets file per environment that is read back in every environment; a probe
  test on `main` kept the key in Production and Development. The planned upstream warning PR was dropped, and so were
  the package's own warning and `CreateBuilder` wrapper. Until then the samples' `apphost.run.json` is the fix. On the
  move to Aspire 17: check that a Production run reuses the persistent containers, and update the README's notes.
- Cause: not the file-based AppHost but its environment. `dotnet run apphost.cs` without a launch profile runs in
  Production. Aspire 13.6.2 (`DistributedApplicationBuilder`, `UserSecretsManager.GetOrSetSecret`) generates
  `AppHost:OtlpApiKey` when configuration has none and writes it to the AppHost's user secrets
  (`apphost-<sha256 of the path>`, the .NET 10 SDK's implicit `UserSecretsId` for file-based apps) in any environment,
  but `HostApplicationBuilder` adds user secrets to the configuration only in Development, so in Production each run
  generates a new key and overwrites the saved one. The same holds for a persisted proxyless port
  (`DcpExecutor`, `Resources:<resource>:<endpoint>:port`: saved, not read back), so `PublicPort = 0` changed every run too.
  `aspire run` without a run.json defaults to Development (`ConfigureSingleFileRunEnvironment`); Aspire's single-file
  template ships an `apphost.run.json` with `DOTNET_ENVIRONMENT=Development`.
- Measured on a minimal file-based AppHost (one persistent busybox with `WithOtlpExporter()`, Podman): Production, as
  is, with `#:property UserSecretsId=<guid>`, with `#:package Altinn.App.Aspire` from a local feed, with `#:project`, and
  on Aspire 13.6.4: a new key and a new container every run. Development (environment variable or `apphost.run.json`):
  the saved key read back, the container reused; the first Development run after Production recreates it once (Development
  also adds Aspire's 1 s batch delays). An unproxied endpoint without a port: a new port every run in Production, the
  same port in Development. The user's hypotheses 1 to 3 do not hold.
- Change: `apphost.run.json` and `apphost-minimal.run.json` beside the samples (Development), the README's [Use] tells
  package users to do the same; `KeepOtlpApiKey`, its call and its two tests removed.
- Proof on the full sample (2026-10-26, Podman 5.7, pins `sha-609c646`, `~/.altinn-app-aspire/otlp-api-key` deleted
  first, the worktree's AppHost with no user secrets yet), evidence in `/home/agent/code/.handover/task52-aspire-otlp-key/`:
  - Run 1 (`dotnet run apphost.cs`, Development from `apphost.run.json`: the containers carry Aspire's
    Development-only `OTEL_BSP_SCHEDULE_DELAY=1000`): 18 containers created, healthy after 25 s, both apps provisioned
    34 s after `dotnet run`; one key in all 18 (`full-run1-otlp-headers.txt`, SHA-256 prefixes), the same as
    `AppHost:OtlpApiKey` in the AppHost's user secrets (`full-user-secrets.txt`); no `otlp-api-key` file written.
  - Run 2: all 18 containers reused (IDs and created times identical, `full-run*-containers.txt`), healthy after 3 s,
    both apps provisioned 10 s after `dotnet run`, the same key. Telemetry reaches the new dashboard from the reused
    containers: an app request (201) and the Telemetry API's resources list every platform service and both apps with
    traces, logs or metrics as before (`full-run*-telemetry-resources.txt`).
  - Minimal sample once: four containers healthy after 10 s, both apps provisioned 15 s after `dotnet run`, one key in
    all four, the front door, Storage, the engine and both apps with all three signals.
- Not verified: a machine that ran the workaround has a stale `~/.altinn-app-aspire/otlp-api-key` (harmless, unused)
  and its containers are recreated once, on the first run with the user-secrets key; `aspire run` (CLI not installed).

## 53. The standard OTLP variables in every component

**Origin (2026-10-26).** From the discussion of task 52. The user's rule: "everything should just take the same
environment variable that describes where to ship the OTEL".

**The rule.**
- Every component sends traces, metrics and logs over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The
  per-signal endpoint variables do not turn exporting on (decided 2026-10-26, as in altinn-studio#21515 after review). It uses
  `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc` or `http/protobuf`) and `OTEL_SERVICE_NAME`, as the OpenTelemetry
  specification defines them.
- Without the variable, a component sends nothing and logs no export errors.
- No component-specific switch is needed: no `EngineSettings__EnableTelemetry` and no `OtelSettings__Endpoint`.
- It covers every component, eventually all of them (decided 2026-10-26), including the ones that only send to
  Application Insights today. A component this task cannot finish stays listed here as open work, not as an
  exception.
- It becomes a convention: the manifest README's conventions and `localtest/CONVENTIONS.md` state it, and a new
  component follows it from the start.
- Once this holds, the manifest needs no telemetry settings. studioctl (task 52) and Aspire only set the standard
  variables.

**Where each component stands** (audit of the vendored sources, 2026-10-26, branch `task53-otlp-audit`; paths below
`localtest/components/<component>/vendor/` unless stated). "Today" is the localtest build: every Azure Monitor /
Application Insights connection string is empty there, so nothing is exported anywhere, with or without
`OTEL_EXPORTER_OTLP_ENDPOINT`, unless the row says otherwise. Facts that hold for every .NET row, checked in the
OpenTelemetry .NET 1.19.1 exporter and by `Otlp.Tests`:
- A hard-coded `service.name` (`AddService(...)` or `AddAttributes(service.name)`) is applied after the SDK's
  environment detector and wins, so `OTEL_SERVICE_NAME` is ignored by every component that sets its own name (all of
  them but Dialogporten). The helper re-adds the detector after the component's resource.
- Without `OTEL_EXPORTER_OTLP_PROTOCOL` the .NET SDK uses `grpc` (the specification says `http/protobuf`).
- An endpoint set in code (`options.Endpoint = ...`) turns off the `/v1/<signal>` suffix for `http/protobuf`; only an
  endpoint from the variable gets it.
- `UseOtlpExporter()` throws next to any signal-specific `AddOtlpExporter`, and when called twice.

| Component | OpenTelemetry today (builder, instrumentation) | Without / with the variable | Azure Monitor / Application Insights | Where the change goes |
| --- | --- | --- | --- | --- |
| Storage | `src/Storage/Program.cs:218-247` traces (ASP.NET Core, HttpClient, Npgsql, `RequestFilterProcessor`) and metrics (ASP.NET Core, Kestrel, HttpClient meters, own meter); logs `:92` (`ILoggingBuilder.AddOpenTelemetry`); name `platform-storage` (`:213-216`, `AddAttributes`); OpenTelemetry 1.19.1, no OTLP package | nothing / nothing | `:249-252` Azure Monitor exporters when the Key Vault connection string is set; Key Vault is `#if !LOCALTEST` (`:143-147`), so never in localtest | `#if !LOCALTEST` / `#else AddLocaltestOtlp(config)` around `:249-252` **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `Program.cs:249-258`; build verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: trivial, [`localtest/reviews/otel/storage.md`](localtest/reviews/otel/storage.md) |
| Events | `src/Events/Program.cs:142-168` traces (own sources, ASP.NET Core, HttpClient, Npgsql) and metrics; logs `:131`; name `platform-events` (`:143`, `AddService`); 1.19.1, no OTLP package | nothing / nothing | `:335-362` exporters when `ApplicationInsights:ConnectionString` is set; the fakes force it empty (`fakes/LocaltestEventsExtensions.cs:95`) | around the call at `:170` **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `Program.cs:170-176`, forced connection string dropped; build verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: trivial, [`events.md`](localtest/reviews/otel/events.md) |
| Profile | `src/Altinn.Profile/Program.cs:125-150` traces (own source, ASP.NET Core, HttpClient, EF Core) and metrics; logs `:114`; name `platform-profile` (`:127`); 1.19.1, no OTLP package | nothing / nothing | `:235-262` exporters when `ApplicationInsights:ConnectionString` is set; forced empty (`fakes/LocaltestProfileExtensions.cs:84`) | around the call at `:152` **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `Program.cs:152-158`, forced connection string dropped; build verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: trivial, [`profile.md`](localtest/reviews/otel/profile.md) |
| Receipt | `src/backend/Altinn.Receipt/Program.cs:168-188` traces (ASP.NET Core, HttpClient) and metrics; logs `:64`; name `platform-receipt` (`:162-165`); 1.19.1, no OTLP package | nothing / nothing | `:190-193` exporters when the Key Vault secret was read (`:110-134`, only with `kvSetting:SecretUri`, unset; the Key Vault call `:105` is not `#if`-guarded) | around `:190-193` **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `Program.cs:190-199`; build, upstream tests (19/19) and a process run against a fake collector verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: trivial, [`receipt.md`](localtest/reviews/otel/receipt.md) |
| Correspondence | `src/Altinn.Correspondence.Integrations/OpenTelemetry/DependencyInjection.cs:26-59` traces (Hangfire source, ASP.NET Core, HttpClient), metrics (Npgsql metrics only, no Npgsql tracing) and `WithLogging()`; name `altinn-correspondence` (`:23`); 1.18.0, no OTLP package | nothing / nothing | `:61-83` exporters when `GeneralSettings:ApplicationInsightsConnectionString` is set; forced empty (`fakes/LocaltestCorrespondence.cs:120`) | the fakes compile into the API project only (`localtest.build.targets:12-15`), so not in `DependencyInjection.cs`: an additive `#if LOCALTEST` call after `src/Altinn.Correspondence.API/Program.cs:65` (`ConfigureOpenTelemetry`), or a `#if !LOCALTEST`/`#else` there if the rule is read strictly **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): the additive `#if LOCALTEST` after `ConfigureOpenTelemetry` (`Program.cs:66-71`), OTLP 1.18.0; build verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: trivial, [`correspondence.md`](localtest/reviews/otel/correspondence.md) |
| Dialogporten adapter | none in localtest: `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs:55-58` registers OpenTelemetry only when `ApplicationInsights:Enabled`, which the fakes force to `false` (`fakes/LocaltestAdapterExtensions.cs:78-82`; `ConfigureTelemetry` throws without a connection string). In production `UseAzureMonitor` (`:220-245`) brings ASP.NET Core and HttpClient instrumentation itself; name `platform-dialogporten-adapter` | nothing / nothing | `UseAzureMonitor` at `:235` | `#if !LOCALTEST` / `#else` around `:55-58`; the `#else` builds its own pipeline (ASP.NET Core and HttpClient instrumentation, 1.15.x arrive with `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0; name `platform-dialogporten-adapter`) and calls the helper **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `ServiceCollectionExtensions.cs:56-65`, `fakes/LocaltestAdapterTelemetry.cs`, OTLP 1.15.3, forced `ApplicationInsights:Enabled` dropped; build and a process run against a fake collector verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: small, [`dialogporten-adapter.md`](localtest/reviews/otel/dialogporten-adapter.md) |
| Notifications | `components/api/src/Altinn.Notifications/Program.cs:148-190` traces (ASP.NET Core, HttpClient, Npgsql, Wolverine, background source) and metrics; logs `:139`; name `platform-notifications` (`:148-151`); 1.19.1 with `OpenTelemetry.Exporter.OpenTelemetryProtocol` already referenced | nothing / nothing. Traces only with `OtelSettings:Endpoint` (`:277-284`, endpoint set in code, so no `/v1/traces` for `http/protobuf`); forced empty (`fakes/LocaltestNotificationsExtensions.cs:93`) | `:286-310`, `ApplicationInsights:ConnectionString` forced empty (`:92` of the fakes) | around the call at `:192` (its signal-specific `AddOtlpExporter` sits in the function the `#else` skips, so `UseOtlpExporter` does not clash); then drop the forced `OtelSettings:Endpoint` **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): `Program.cs:192-199`, forced `OtelSettings:Endpoint` and connection string dropped; build verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). Upstream: small, [`notifications.md`](localtest/reviews/otel/notifications.md) |
| Authorization | not on `AltinnHost`: `Altinn.Authorization.ServiceDefaults` 5.5.0 is referenced but unused for the host. No traces, no logs pipeline; one meter (`DecisionTelemetry`, `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs:300-301`) registered without a provider outside the Application Insights block. OpenTelemetry 1.15.3 with the OTLP exporter and ASP.NET Core/HttpClient instrumentation arrive transitively through ServiceDefaults | nothing / nothing | classic SDK: `ConfigureLogging` `:93-120` (`ClearProviders`, then `AddApplicationInsights` when the Key Vault key was read) and `:278-298` (`AddApplicationInsightsTelemetry`, plus a metrics-only OpenTelemetry pipeline with Azure Monitor). Key Vault only with all four `kvSetting` values (`:157-180`) | `#if !LOCALTEST` / `#else` around `:278-298`: the `#else` adds traces (ASP.NET Core, HttpClient), metrics (`DecisionTelemetry`) and the helper (it adds logs), name `platform-authorization`. **Done** (`7c1e0dd4`, branch `task53-otel-auth`): `fakes/LocaltestAuthorizationTelemetry.cs`, registered only with the variable; build and a process run against a fake collector verified, image `sha-609c646` and the smoke test with its OTLP span check passed on the stack (see **Proven on the stack** below). Upstream: small, [`localtest/reviews/otel/authorization.md`](localtest/reviews/otel/authorization.md) |
| Authentication, Access Management, Register, Resource Registry | `AltinnHost.CreateWebApplicationBuilder(name, ...)` (`AuthenticationHost.cs:68`, `AccessManagementHost.cs:69`, `RegisterHost.cs:68`, `ResourceRegistryHost.cs:45`) runs `AddAltinnServiceDefaults`: decompiled 5.5.0 and 6.0.0 (identical here), `ConfigureOpenTelemetry` = logs (`WithLogging`), metrics (runtime, ASP.NET Core, Kestrel, HttpClient), traces (ASP.NET Core with a health filter, HttpClient, a tail-sampling processor); Npgsql tracing through `AddAltinnPostgresDataSource` (ServiceDefaults.Npgsql). Name from `AltinnServiceResourceDetector`: `service.name` = the name passed (`authentication`, `access-management`, `register`, `resource-registry`), `service.namespace` `altinn` | nothing / **exports already**: `AddOpenTelemetryExporters` checks `builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]` and adds the signal-specific `AddOtlpExporter` for logs, metrics and traces, so the protocol and the `/v1/<signal>` paths come from the standard variables. `OTEL_SERVICE_NAME` is ignored (the detector's name wins) | `else if (EnabledServices.ApplicationInsights)`: `UseAzureMonitor` only without the variable and only with `ConnectionStrings:ApplicationInsights`, `ApplicationInsights:ConnectionString` or `ApplicationInsights:InstrumentationKey`. So nothing to switch off. Access Management disables it (`:69`) and adds its own `UseAzureMonitor` (`:88-108`) only outside Development with `ApplicationInsights:InstrumentationKey` | no vendor change. `AddLocaltestOtlpResource(builder.Configuration)` (not `AddLocaltestOtlp`, which would throw next to their exporters) from each existing fakes hook: `AuthenticationHost.cs:293-295`, `AccessManagementHost.cs:168-170`, `RegisterHost.cs:249-251`, `ResourceRegistryHost.cs:191-193`. **Done** (`7c1e0dd4`, branch `task53-otel-auth`); builds verified, Authentication and Register also run against a fake collector, images `sha-609c646`; the smoke tests of Authentication, Register and the Resource Registry passed with their OTLP span checks, Access Management's span found by hand (see **Proven on the stack** below). Upstream: trivial per host, one small ServiceDefaults change for all four, [`localtest/reviews/otel/`](localtest/reviews/otel/README.md) |
| Workflow engine | `src/Runtime/workflow-engine/src/WorkflowEngine.Telemetry/Extensions/ServiceCollectionExtensions.cs:24-244`, traces (own source, HttpClient, ASP.NET Core, optional EF Core/Npgsql), metrics (views, runtime, HttpClient, ASP.NET Core), logs; name `WorkflowEngine` via `AddService` (`:33-39`) in this vendored copy, so `OTEL_SERVICE_NAME` is ignored here; altinn-studio `main` already lets it win (#21189, `AddEnvironmentVariableDetector` after `AddService`), so a re-vendor fixes the name | `AddOtlpExporter` unconditionally (`:105`, `:226`, `:239`) whenever `EngineSettings:EnableTelemetry` is true (`WorkflowEngineBuilderExtensions.cs:62-87`): without the variable it exports to the SDK default `http://localhost:4317` (grpc), so the manifest sets `EngineSettings__EnableTelemetry=false` (`studioctl/src/cli/internal/localtestv2/manifest/manifest.json:171`). With the variable: all three signals from the standard variables | none | upstream (monorepo), not `#if LOCALTEST`: gate the three exporters on the variable (or `UseOtlpExporter()` behind it), let `OTEL_SERVICE_NAME` win, then drop the manifest switch |
| Dialogporten (WebApi, GraphQL, Service) | `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs:25-137` traces (HttpClient, Npgsql, the hosts add ASP.NET Core and more) and metrics; logs through Serilog's OTLP sink (`:139-193`, `Serilog.Sinks.OpenTelemetry` 4.2.0); name `OTEL_SERVICE_NAME`, else the application name (`:49`) | nothing (console logs) / all three signals. But: (1) `OTEL_EXPORTER_OTLP_PROTOCOL` must be `grpc` or `http/protobuf`; unset throws at start (`:36-41`, `:178-186`). (2) traces and metrics set the endpoint in code (`:104-108`, `:127-131`), so with `http/protobuf` they post to the bare endpoint without `/v1/traces` / `/v1/metrics`; only `grpc` works for them. The Serilog sink appends `/v1/logs` and reads `OTEL_SERVICE_NAME` (decompiled). (3) With the variable the logs go to OTLP instead of the console (`:144-154`). (4) Metrics go to Azure Monitor instead when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (`:117-132`) | metrics only, see (4) | follows the rule for `grpc`; `http/protobuf` needs a `#if !LOCALTEST`/`#else` in `OpenTelemetryExtensions.cs` (no endpoint in code under LOCALTEST), or the stack sends `grpc` to these hosts **Done** (`e64a2a3e`, `3973509e`, branch `task53-otel-platform`): the `#else` in `OpenTelemetryExtensions.cs:106-112`, `:134-139` leaves endpoint and protocol to the SDK, so `http/protobuf` works too; library build and a scratch exporter check verified, image `sha-609c646` (commit ef213725) and the smoke test with its OTLP span check passed on the stack (`--with everything`, 2026-10-26, see **Proven on the stack** below). studioctl sends `grpc` anyway. Upstream: trivial, [`dialogporten.md`](localtest/reviews/otel/dialogporten.md) |
| Arbeidsflate BFF | `packages/bff/src/instrumentation.ts:139-206`, NodeSDK with traces and metrics (HTTP, ioredis, Fastify, GraphQL, pg); logs through `pino-opentelemetry-transport` 4.0.2 (`packages/node-logger/src/index.ts:104-134`); name `bff` (`packages/bff/src/config.ts:61`, resource attribute) | nothing / traces, metrics and logs (`config.ts:74-79`, `enabled: !!OTEL_EXPORTER_OTLP_ENDPOINT`). But traces and metrics use the **grpc** exporters only (`instrumentation.ts:6-7`, `:147-156`); `OTEL_EXPORTER_OTLP_PROTOCOL` is read and logged, not used. The logs keep going to the console as well | none in the BFF (the frontend's browser analytics are out of scope) | follows the rule for `grpc`; `http/protobuf` would need a patch (`patches/`). Not verified: whether `OTEL_SERVICE_NAME` beats the resource attribute in the NodeSDK, and which protocol the pino transport picks **Checked** (branch `task53-otel-platform`): follows the rule on the stack, where studioctl sends every container `grpc` to the collector's 4317; no localtest change. Upstream: small (pick the exporter by protocol), [`arbeidsflate.md`](localtest/reviews/otel/arbeidsflate.md) |
| The front door (`localtest/localtest/`) | none (`Localtest.csproj`: YARP and service discovery only; native AOT) | nothing / nothing | none | task 54: its own code, so a direct call after `WebApplication.CreateSlimBuilder` (`Program.cs:23`) with ASP.NET Core and HttpClient instrumentation and the helper; check the AOT publish with the OpenTelemetry packages |
| pdf3 | not vendored under `components/` (image from altinn-studio); not audited in this pass | | | verify separately |

**Work list, in batches.** Each batch: copy `components/shared/fakes/Otlp/LocaltestOtlp.cs` into the component's
`fakes/Otlp/`, add the package where missing, the `#else` call, the README and `vendor/UPSTREAM.md` lines, the smoke
check of the **Proof** below, one image rebuild at a time and `bump-tag.sh`.
1. **Notifications.** Done in `e64a2a3e` (`task53-otel-platform`; image and smoke run done on `sha-609c646`). `Program.cs:192` in `#if !LOCALTEST`/`#else`; remove `OtelSettings:Endpoint` from
   `fakes/LocaltestNotificationsExtensions.cs:93`. Package present (1.19.1). Risk: low.
2. **The Azure Monitor group: Storage, Events, Profile, Receipt, Correspondence, the adapter.** Done in `e64a2a3e` (`task53-otel-platform`; images and smoke runs done on `sha-609c646`; Dialogporten's `http/protobuf` `#else` too). Files: `Program.cs` of
   each (lines in the table); for Correspondence `src/Altinn.Correspondence.API/Program.cs:65` (not the Integrations
   project); for the adapter `Common/Extensions/ServiceCollectionExtensions.cs:55-58`. Add
   `OpenTelemetry.Exporter.OpenTelemetryProtocol` to each `localtest.build.targets` at the host's OpenTelemetry version
   (1.19.1; Correspondence 1.18.0; the adapter 1.15.3 or the version its Azure Monitor package resolves). None of these
   use central package management. Risk: low for the five with a pipeline; medium for the adapter, which has no pipeline
   in localtest and gets one (instrumentation, its health filter processor, the name).
3. **The Authorization family.** Done in `7c1e0dd4` (`task53-otel-auth`; images and smoke checks done on `sha-609c646`). Authentication, Access Management, Register, Resource Registry: one line each in the
   fakes hook (`AddLocaltestOtlpResource`), no vendor change; the four already export with the variable, the line only
   makes `OTEL_SERVICE_NAME` count. Register's hook lives in its own project (`fakes/Altinn.Register.Localtest/`), which
   references only `Altinn.Register.Core`, so it needs an OpenTelemetry package reference at the host's version. Authorization: `Program.cs:278-298` in
   `#if !LOCALTEST`/`#else`, the `#else` building traces and metrics plus the helper (packages arrive transitively at
   1.15.3; the helper's tests run at 1.15.3). Risk: low for the four; medium for Authorization (first OpenTelemetry
   pipeline in that host, and its `ClearProviders` logging setup).
4. **Anything else.** The workflow engine: done (altinn-studio#21515 in `tt02`, `EngineSettings__EnableTelemetry`
   dropped from the manifest, commit f8f4148f). Dialogporten and the BFF: decide `grpc` for the components
   (the collector's 4317 on the network), or `#else`/patch them for `http/protobuf`. The front door with task 54. pdf3
   to be checked. Risk: the engine change waits on review upstream; the protocol decision affects task 52's
   studioctl variables.

**Changes to the assumptions above.** The Authorization family needs no exporter and no Application Insights switch,
only the name. Every runner must set `OTEL_EXPORTER_OTLP_PROTOCOL` explicitly: Dialogporten refuses to start without it
and the .NET default (`grpc`) differs from the specification's. With `http/protobuf` Dialogporten's traces and metrics and
the BFF's traces and metrics do not arrive, so the components' variable should be `grpc` to the collector unless those
two are changed; task 54's `{{origin}}/otlp` with `http/protobuf` suits the apps and the helper's components but not
these.

**Shape.**
- One shared helper, `localtest/components/shared/fakes/Otlp/LocaltestOtlp.cs` (built on `task53-otlp-audit`, tests in
  `components/shared/Otlp.Tests/`): `AddLocaltestOtlp(configuration)` calls `UseOtlpExporter()` on the OpenTelemetry
  builder the component already has and lets `OTEL_SERVICE_NAME` win over a hard-coded name when the variable is set;
  otherwise it registers nothing. `AddLocaltestOtlpResource(configuration)` is the naming half alone, for hosts that
  export by themselves (the ServiceDefaults hosts).
- Each component calls the helper from a `#else` branch where it would otherwise configure Azure Monitor or
  Application Insights. That follows the repository's `#if !LOCALTEST` rule.
- Production behaviour is unchanged.
- Add the instrumentation a component lacks (ASP.NET Core, HttpClient, Npgsql) only where it has none. A service that
  is missing from a trace is worth knowing about, but it is not a reason to rewrite its telemetry.

**Proof.**
- Each component's `smoke-test.sh` gets one check: with the variable pointing at a throwaway collector container, a
  request creates a span with the right `service.name`.
- Without the variable, the container's log shows no export errors.
- On the stack after task 52: `--with everything --with monitoring`. One form submission produces a single trace in
  Grafana, from the app through Storage, the PDP and the workflow engine.
- Images are rebuilt one at a time (the repository rule), and their tags are bumped with `bump-tag.sh`.

**Proven on the stack (2026-10-26, branch `task53-stack-proof`).**
- Images: the `images` run of 609c6466 (run 40328265724, green, all 20 tags present), pinned with `bump-tag.sh
  sha-609c646` (ef213725). studioctl installed from the worktree under `STUDIOCTL_HOME=/home/agent/lt-home` (a symlink),
  no `--dev-*`.
- One run of `env up -r localtest-v2 --with everything` (27 containers; 4.1 GiB available after start, 2.0 GiB with the
  app and Chromium; the five monitoring containers 0.64 GiB at the end). Every container but the five monitoring ones had
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`, `grpc` and `OTEL_SERVICE_NAME=<container name>`.
- Smoke tests, all green with their OTLP span check: the root `smoke-test.sh` (Storage, 27), Events (28), Register (15;
  it needs the stack URL as its argument), Authentication (35), Authorization (32), Resource Registry (44), Notifications
  (65), Profile (53), Receipt (28), Correspondence (79), Dialogporten (61, `dialogporten-webapi`), the adapter (40); also
  Arbeidsflate (59). Access Management (no smoke test): `GET accessmanagement/api/v1/authorizedparties` with a
  `traceparent` gave its server span and five Npgsql client spans in that trace, under the front door's `GET
  access-management`.
- Signals per `service.name` at the end of the run (about 25 minutes; spans and log records from VictoriaTraces and
  VictoriaLogs over 3 h, metric series from VictoriaMetrics' series API, all through Grafana's datasource proxy):

  | Component (manifest name) | Spans | Metric series | Log records |
  | --- | ---: | ---: | ---: |
  | storage | 1285 | 893 | 24 |
  | events | 2241 | 583 | 2563 |
  | profile | 890 | 1146 | 2028 |
  | receipt | 20 | 575 | 142 |
  | correspondence | 302 | 1371 | 605 |
  | dialogporten-adapter | 123 | 985 | 29 |
  | notifications | 10136 | 834 | 5346 |
  | authorization | 267 | 868 | 84 |
  | authentication | 566 | 724 | 128 |
  | access-management | 1384 | 622 | 3318 |
  | register | 374 | 666 | 75 |
  | resource-registry | 660 | 680 | 1026 |
  | workflow-engine | 35 | 279 | 12 |
  | dialogporten-webapi | 1691 | 1325 | 2792 |
  | dialogporten-graphql | 121 | 801 | 217 |
  | dialogporten-service | 543 | 463 | 1126 |
  | arbeidsflate-bff | 82 | 248 | 14 |
  | localtest (front door) | 2318 | 4624 | 4 |
  | pdf3 | 0 | 9 | 0 |
  | testapp (the app, process) | 384 | 829 | 871 |

  Every component of the table above sends all three signals under its manifest name. Not every signal everywhere:
  pdf3 (not vendored, not audited) sent only nine Go runtime metric series; no PDF was generated, so no spans, and no log
  record arrived (it logs to stdout); its first trace export failed (`name resolver error`) because it starts before
  the collector, which no container waits for. The `arbeidsflate` container (nginx) gets the variables and sends nothing.
  Postgres and Redis get them and ignore them. Low log counts (Storage, the front door) are the components' log levels.
- One form submission of `ttd/testapp` (app-lib 8.12.15, `studioctl run` with `OTEL_EXPORTER_OTLP_ENDPOINT=
  http://local.altinn.cloud:8000/otlp`, `http/protobuf`, `AppSettings__UseOpenTelemetry=true`; logged in on the front
  door's Home page, form filled and sent in Chromium): one trace, 128 spans in 5 services, rooted at the app's `PUT
  {org}/{app}/instances/.../process/next`: the app's internal and client spans, the front door's `GET/PUT/POST/PATCH
  storage` and `POST/GET authorization` with their forwarding spans, Storage's server and Npgsql spans (lock, data,
  `process/instanceandevents`), Authorization's four `POST authorization/api/v1/decision` and `GET .../parties`, and
  Access Management's `GET .../authorizedparties` called by the PDP. Screenshot:
  `/home/agent/code/.handover/task53/grafana-trace-form-submission.png`. As in task 54, the front door's span for the
  incoming PUT is in a trace of its own (app-lib starts a new trace).
- Without the monitoring group (`--with everything --without monitoring`, 22 containers): no container has an `OTEL_*`
  variable; no log has an exporter line (only the ServiceDefaults hosts' resource-detector lines at start, the BFF's
  "OpenTelemetry disabled" and pdf3's "Telemetry exporter disabled"); a 150 s capture of SYNs and UDP in the network
  namespaces of Storage and Authentication during six smoke tests showed no lookup of `otel-collector` and nothing to
  4317 or 4318. The smoke tests (root, Events, Authentication, Authorization, Profile, Receipt) pass and skip the OTLP
  check; Events failed "third event registered" once on the freshly started stack and passed on the next run (27/27),
  not related to OTLP.
- Console noise (the platform agent's observation): over `grpc`, which every container gets, the exporters log nothing
  on the console. The same Receipt image on the stack network with `http/protobuf` logged
  `System.Net.Http.HttpClient.Otlp{Metric,Log}Exporter.{LogicalHandler,ClientHandler}` at Information, 4 events and 8
  lines per export, 40 lines in 5 idle minutes (metrics every 60 s, logs when there are any), none of them in the
  exported logs. Fixed in the shared helper (ea60bbf9): `AddLocaltestOtlp` filters `System.Net.Http.HttpClient.Otlp`
  below Warning, with a test that reproduces the lines without it (OpenTelemetry 1.19.1, 1.18.0, 1.15.3), copied to all
  14 copies; the front door, Register's fakes project and Receipt build. Not in the images until the next build on main.
  The ServiceDefaults hosts (`AddLocaltestOtlpResource` only) do not get the filter.
- Open: pdf3 (audit, and whether to make the components wait for the collector); the filter in the images.
- pdf3's logs (2026-10-27, brief `/home/agent/code/.briefs/pdf3-otel-log-level.md`): pdf3 exports only WARN and above
  over OTLP, and that stays (the user: INFO logs add nothing next to spans). A setting `PDF3_OTEL_LOG_LEVEL` was built
  and dropped, unopened. Proven on the way (evidence `/home/agent/code/.handover/pdf3-otel-log-level/`): one PDF is one
  trace from the app's `PdfGeneratorClient.GeneratePdf` through the front door to pdf3's `POST /pdf` and its
  `pdf.session.*` children. Possible span metadata, not built: `altinn.app`/`altinn.service_owner` are empty in
  localtest (derived from the host), no instance ID, no page count.

## 54. Apps and the front door on the same OTLP variable

**Origin (2026-10-26).** The other half of task 53's rule: the apps and the localtest container itself.

**Apps.**
- app-lib uses OpenTelemetry only when `AppSettings:UseOpenTelemetry` is true. Otherwise it uses Application Insights,
  which sends nothing locally without a connection string.
- `app env` always sets `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel.<host>:4317`, even when nothing listens there.
- **Goal:** when a collector runs (task 52's monitoring group, or task 55's viewer in minimal mode), `app env` sets
  - `OTEL_EXPORTER_OTLP_ENDPOINT={{origin}}/otlp`,
  - `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`,
  - and `AppSettings__UseOpenTelemetry=true` as a default that the app's own settings or the shell can override.
- When no collector runs, `app env` sets none of these, so it stops pointing apps at an address with nothing behind it.
- Container-mode apps get the in-network address. The golden file gains the cases with and without a collector.
- Open: whether app-lib itself should switch to OpenTelemetry whenever `OTEL_EXPORTER_OTLP_ENDPOINT` is set. That is
  a product change with an issue of its own, and the studioctl default does not wait for it.

**The front door.**
- The `localtest` container has no telemetry today. Under task 53's rule it gets:
  - ASP.NET Core and HttpClient instrumentation, which includes YARP's forwarding;
  - the OTLP exporter;
  - the emulated services' calls in minimal mode as spans of their own, under their component's name, so a trace in
    minimal mode still shows "Register" and "Authorization".
- The front door also forwards `/otlp` to the collector (task 52) or to the viewer (task 55). Its own forwarding of
  OTLP is not traced, so it does not feed on itself.

**Proof.**
- `app env` golden cases.
- A front-door test that a forwarded request carries `traceparent` and creates a span.
- On the stack: a `studioctl run` with monitoring shows the app's spans and the front door's spans in one trace.

**Done: the front door (2026-10-26, branch `task54-frontdoor-otel`).**
- `localtest/localtest/Telemetry/`: OpenTelemetry 1.19.1 with ASP.NET Core and HttpClient instrumentation (1.19.0),
  registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, through a copy of the shared helper (the front door's Docker
  build context is its own folder, so it cannot reference `components/shared/`). `service.name` `localtest` unless
  `OTEL_SERVICE_NAME` is set. The AOT publish has no new warnings; the image grew from 38.5 MB to 41.4 MB.
- A forwarded request is a server span named after the route (`GET storage`, `POST ttd/testapp`) and the forwarder's
  client span with `peer.service`; `traceparent` goes to the destination.
- Not traced: `/otlp` (it would feed on itself) and `/grafana` (the observer's own traffic, front-door-only spans), the
  monitoring group's routes; `/health`; `/app-frontend/`. Metrics still count them.
- Minimal mode: a span per emulated call from the source `Localtest.Emulation`, named after the component and the
  endpoint pattern (`Register GET /register/api/v1/parties/{partyId:int}`), with `peer.service`; one resource, so
  `service.name` stays `localtest`.
- The shared helper: a per-signal endpoint alone no longer turns it on (it turned on all three signals, and the ones
  without an endpoint went to `localhost:4317`; a test of that sent spans to the real collector while the stack was up);
  both test projects clear inherited `OTEL_*` variables.
- Seen on the stack: app-lib's incoming server span (`POST {org}/{app}/instances`) starts a new trace instead of
  continuing the front door's span that forwarded the request; the app's own outgoing calls do join the front door.
  Not investigated.
- Open: the apps half above (`app env`); a minimal-mode trace on a stack (minimal mode runs no collector until task 55).

## 55. Telemetry files in minimal mode

**Origin (2026-10-26).** The user: Grafana and its stack should not be in minimal mode, but minimal mode should have
some telemetry. The purpose is to give agents something to debug failed tests with; humans use the default mode with
the full setup (task 52).

**Considered and dropped.**
- The standalone Aspire dashboard: built for people in a browser, and unclear whether agents can query it or whether it
  can be served under a sub-path.
- An OTLP receiver in the front door: our own code to maintain.
- SQL: the collector has no SQLite exporter, and ClickHouse is a server. DuckDB over the files was also dropped.

**Decision (the user, 2026-10-26).**
- Minimal mode runs the stock collector (`otel-collector`, the image already pinned for `monitoring`) with only the
  `file` exporter, which was confirmed with `components` in 0.161.0.
- It writes OTLP JSON Lines files on the host, which agents read with plain `jq`.
- It keeps traces, logs and metrics. Metrics only need to be bounded for a minimal stack that stays up for days:
  the first 10 minutes or the last hour would both do, so size-based rotation that keeps about the last hour is
  enough.
- Reasonable size limits apply.
- The files are cleared on `env down`.

**Design.**
- **Container name:** the container is named `otel-collector`, so the runner's existing rule applies unchanged: the
  OTLP variables, the 5 s interval and the implicit dependency while the collector runs.
- **Config:** it has its own config file next to `monitoring/otel-collector.yaml`, with the same receivers and
  processors (the same attributes as the full setup) and the file exporter instead of the Victoria exporters.
- **Location:**
  - Files land in a host folder under the data directory, split per service and signal where the exporter allows
    it.
  - Rotation is by size, with a few backups.
  - `env down` deletes the folder, and `env up` starts empty.
- **Metrics:** bounded by size-based rotation, holding roughly the last hour.
- **Always on in minimal mode, if cheap:** an agent should not have to restart the stack to get telemetry after a
  test fails.
- **Default mode and Aspire:** unchanged.

**Proof.**
- `env up -r localtest-v2 --minimal` and one `studioctl run` request.
- One documented `jq` command shows that request's spans and log records from the app, the front door, Storage and
  the workflow engine.
- The metrics files stay bounded, and the time window they hold is stated.
- The limits hold.
- `env down` removes the files.
- The collector's memory and the change in start-up time are measured and added to task 38's numbers.

**Changes to the decision (the user, 2026-10-26, during the work).**

- Metrics: "only the first 10 minutes" was a suggestion to bound the files; keeping about the last hour is as good. So
  no OTTL time filter: the metrics files are bounded by size-based rotation like the others, which keeps the newest
  data.
- Names: the two collectors have different configs and defaults, so minimal mode's is `otel-files`, not a second
  `otel-collector`. The runner's rules that were keyed on the name `otel-collector` now follow a manifest field,
  `otlpReceiver`, and the front door's `/otlp` targets the role `otlp`, not a container (option 1 of the trade-off
  reported to the coordinator; no stopgap alias).
- The `/otlp` 503 keeps naming `monitoring`: minimal mode always runs its receiver, so only the default stack without
  the group has none.

**Done (2026-10-26, branch `task55-minimal-telemetry-files`, commits 575088d7, 8626bdba, 35e6fb7c and the docs commit after them; merged as main `673facf4`..`ae45f069` and pinned `sha-ae45f06` the same day; brief
`/home/agent/code/.briefs/task55-minimal-telemetry-files.md`).**

- **Manifest.** A container field `otlpReceiver` (`{"grpcPort": 4317}`, optionally `"output": "/telemetry"`, a path
  where studioctl mounts a host folder read-write) marks the stack's OTLP receiver. `otel-collector` has it (no
  output); `otel-files`, the same pinned `otel/opentelemetry-collector-contrib` 0.161.0 with only the `file` exporter,
  has it with `/telemetry` and is a root of the `minimal` group, with no dependencies. `Parse` refuses two receivers in
  the `minimal` group's selection or in the other groups' together (`errReceivers`), and checks the port and output
  path. Both collectors read one `monitoring/otel-processors.yaml` through the collector's `${file:…}` provider, so the
  attributes match. Found on the stack: the provider expands the included file's text, comments too, so a
  `${file:…}` in its header comment failed the start ("too many recursive expansions"); a test keeps `${` out.
  Why a field and not runner code or per-mode overrides: the receiver's role (OTLP variables, dependency, `/otlp`
  announcement) is the same for both, only the name and config differ, and the field states it where the containers
  are declared; per-mode `files`/`dependsOn` would have added a second override concept to both loaders for one
  container.
- **Runner** (`telemetry.go`, was `monitoring.go`). While a receiver runs, every container but the receiver, its
  dependency closure and the monitoring group gets `OTEL_EXPORTER_OTLP_ENDPOINT=http://<receiver>:<grpcPort>`, `grpc`,
  `OTEL_SERVICE_NAME`, the 5 s interval and the implicit dependency on the receiver; the receiver is announced as
  `services__otlp__http__0` (the old `services__otel-collector__http__0` special case is gone); the engine's Grafana
  pair still needs Grafana running, so it stays with `monitoring`. `otel-files` runs as the developer's uid:gid (and
  `keep-id` under Podman, `envstack.RuntimeUser`) with `<data>/infra/localtest-v2/telemetry` read-write at
  `/telemetry`. `env up` empties the folder unless `otel-files` already runs (a converged stack keeps its open files);
  the teardown of a stack whose state is minimal (down, reset, Ctrl+C, a mode switch, a failed start) deletes it;
  other modes leave it alone. The teardown selection now includes the `minimal` group, whose `otel-files` no other
  group starts. Golden file: `services__otel-collector__http__0` becomes `services__otlp__http__0` in `everything`;
  the minimal case gains `otel-files`, the OTLP variables and the dependency.
- **Front door.** `/otlp` → service `otlp` (`PlatformRoutes.OtlpService`), group `monitoring` for the 503 (comment
  states the assumption); route and telemetry tests updated; the smoke test checks an export through `/otlp` when
  `otlp` has an address (minimal mode) and the 503 naming the group when not.
- **Aspire.** The loader reads `otlpReceiver`; `StackSpec` never starts a receiver (minimal mode stays four
  containers); `GoldenComparisonTests` leaves studioctl's receivers and `services__otlp__http__0` out on both sides,
  and compares minimal mode's shared telemetry variables with studioctl's minimal snapshot instead of `everything`;
  `NoOtlpReceiverRunsInEitherMode`.
- **Files** (`monitoring/otel-files.yaml`). `/telemetry/<service.name>/{traces,logs,metrics}.jsonl`, `group_by` on
  `service.name` (checked in the 0.161.0 source and on the stack: a name with `/` makes nested folders, records
  without the attribute are dropped). With `rotation` set the exporter writes every batch at once (`flush_interval`
  does not apply), so a request shows up within about 10 s (the sender's 5 s batch, the collector's 5 s batch).
  Rotation at 10 MB with two backups (`<signal>-<UTC time>.jsonl`): at most 30 MB per service and signal.
  Metrics at 25 MB with two backups (75 MB): measured on an idle minimal stack with the app (`rates-idle.txt`,
  `metrics-size-t*.txt`), metrics grow by 7.1 MB an hour (Storage), 14.3 (the app), 31.2 (the engine) and 69.9 (the
  front door, after its smoke test had put every route in its histograms; cumulative temporality resends every series
  each 5 s), so 75 MB hold about an hour for the front door and two to ten for the rest. Traces and logs grow mostly
  with requests; idle, the app's logs grow by 6.6 MB an hour (studioctl-server's 5 s polls of its metadata), Storage's
  traces by 3.5, the rest by less. Not done: an OTTL time filter (dropped with the user's change), and the contrib
  image's `interval` processor, which would cut metrics to one export a minute but is a deviation the user did not ask
  for (an option if the front door's rate matters).
- **Guide.** The bundled app-development skill (`agents/_skills/app-development/SKILL.md` since altinn-studio#21512,
  which ships with studioctl and is where an agent working on an app looks) has "Telemetry files in minimal mode": where the files
  are, four `jq` commands (one trace's spans and log records, errors in the last N minutes, a trace's slowest spans,
  the app's requests with their trace IDs). studioctl's README describes the files and points there; the manifest
  README has "OTLP receivers" and the runner's changes; the front door's README the `otlp` role.

**Proof on the stack (2026-10-26, studioctl from the worktree under `STUDIOCTL_HOME=/home/agent/lt-home`, pinned
images `sha-609c646` except the front door, built from the worktree with `--dev-localtest`; rootful Podman 5.7).**
Evidence in `/home/agent/code/.handover/task55/`.

- `env up -r localtest-v2 --minimal --dev-localtest`: five containers; `otel-files` runs as `1000:1000`, the folder
  is `agent`'s (0755 folders, 0644 files); the front door has `services__otlp__http__0=http://otel-files:4318` and
  `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-files:4317`.
- `ttd/testapp` (app-lib 8.12.15) with `studioctl run` and the OTLP variables of the guide; an instance created and
  its process moved (`PUT process/next`, 200). The guide's trace command on the app's process/next trace: 75 app
  spans and 57 app log records, 24 front-door spans (forwarding and the emulated Authorization calls) and 23 Storage
  spans (server and Npgsql) in one listing (`trace-process-next.tsv`). The workflow engine is not in it: this app-lib
  never calls the engine (task 52's finding), so the engine is shown with a direct enqueue through the front door:
  the front door's 2 spans and the engine's 4 in one trace (`trace-engine-enqueue.tsv`). The app's server span starts
  a new trace (task 54's note), so the client's traceparent only finds the front door's 2 spans
  (`trace-process-next-frontdoor-incoming.tsv`); the guide says to use the app's trace ID.
- Errors: with Storage stopped for one request the errors command lists the front door's and the app's error spans
  and the app's two Error log records with the trace ID (`errors-last-2-min.txt`); the slowest spans of the
  process/next trace (`slowest-process-next.txt`). The guide's commands were run verbatim from the skill file
  (`guide-verbatim-output.txt`).
- The front door's smoke test in minimal mode: the new check "OTLP/HTTP under /otlp reaches the running receiver
  (http://otel-files:4318)" passes; 87 ok, 6 fail, all test-data related (the app's registration replaced the
  baseline users, so `1337` is unknown; `USER_SELECT=20001.520100 OWN_PARTY=520001`) or the dev image without
  `FRONTEND_DIST` (`smoke-minimal.txt`).
- Rotation, forced with a temporary 1 MB limit (the host copy of `otel-files.yaml` edited, the container restarted,
  eight listings 30 s apart, `rotation-forced-1mb.txt`): the existing files were rotated on the first write, the front
  door's metrics rotated about every 50 s and the engine's every 2 minutes, and from the third rotation on the oldest
  backup was deleted, so each service's metrics stayed at the current file and two backups of about 1 MB (the 9.8 MB
  file from before the restart went too). The guide's trace command still found the whole process/next trace with its
  log records in a rotated `logs-…-size.jsonl`. Backups are named `<signal>-<UTC time>-size.jsonl`.
- `env down` of the minimal stack removed `telemetry/` and left `certs/` and `files/` (`env-down-minimal.txt`). With a
  marker folder in `telemetry/`, `env up --with monitoring` and its `env down` left it alone; the next
  `env up --minimal` started with an empty folder (`monitoring-check.txt`, `minimal-pinned-check.txt`).
- Start time and memory (task 38): `env up -r localtest-v2 --minimal --detach` on the pinned images, three runs each,
  alternating, studioctl built from main (9efb6ebd, no collector) and from the branch: 6.0, 5.9, 6.0 s without,
  6.3, 6.3, 6.4 s with, so about +0.4 s (`start-times.txt`; `.scripts/task55/time-up.sh`); everything waits for the
  collector's readiness first. `otel-files` (`podman stats`): 61 MB after the app's requests, 70 MB a few minutes
  later, 88 MB after the smoke test and ten minutes of metrics, 46 MB right after a restart; the four other containers
  together 510-540 MB (`podman-stats-*.txt`). About what the monitoring group's collector alone takes (60-74 MB),
  against 377-520 MB for the whole group; no cost worth an opt-in, so it is always on.
- Default stack with `--with monitoring` (`--dev-localtest`, 18 containers, 36 s): no `otel-files`; the front door
  has `services__otlp__http__0=http://otel-collector:4318`; the engine keeps both Grafana settings; `/otlp` 200,
  Grafana's health 200; the collector mounts `config.yaml` and `processors.yaml`, logs nothing, and a Storage span in
  VictoriaTraces carries `deployment.environment.name=local` without `telemetry.sdk.*` (the shared processors work);
  the front door's smoke test 85 ok, its monitoring section all green (the front door's spans read back from
  VictoriaTraces through Grafana), the one failure `FRONTEND_DIST` (`smoke-monitoring.txt`). The default stack alone
  with the new front door: `/otlp` answers 503 with `component` `otlp` and `group` `monitoring`, and no container has
  an `OTEL_*` variable. The pinned front door (`sha-609c646`) in minimal mode answers `/otlp` with 503 naming
  `otel-collector`, as expected until the pin moves.

**Pinned check (2026-10-26, after the merge ae45f069, pins `sha-ae45f06`, studioctl from the pin worktree, no
`--dev-*`; evidence `/home/agent/code/.handover/task55/pinned/`).** Minimal mode: the front door
`localtest-localtest:sha-ae45f06` and `otel-files` run; `POST /otlp/v1/traces` 200; `ttd/testapp` with the guide's
variables, an instance and `process/next`: the guide's trace command lists 75 app spans, 57 app log records, 24
front-door and 23 Storage spans; `env down` removed `telemetry/`. `--with monitoring`: 18 containers,
`services__otlp__http__0=http://otel-collector:4318`, `/otlp` 200, Grafana 200, the engine's Grafana pair, the front
door's smoke test 85 ok with its monitoring section green (the one failure is `FRONTEND_DIST`, unset for the smoke
test). Default stack alone: `/otlp` 503 with `component` `otlp`, `group` `monitoring`.

**Checks.** Under `localtest/studioctl/src/cli`: `go build ./...`, `go test ./...` and `go test -race -count=1 ./...`
pass, `make lint` 0 issues; the golden file regenerated and reviewed. The front door: `dotnet test tests` 163 passed,
`dotnet build -c Release` 0 warnings. The Aspire package: `dotnet test tests` 170 passed, `dotnet csharpier check .`
clean. typos clean on every changed file; Prettier clean on the studioctl README, the skill and the manifest README
(the front door's README, `localtest/README.md`, `CONVENTIONS.md`, the Aspire README and `TODO.md` had Prettier
differences before; their count is unchanged). The changelog's minimal-mode entry mentions the files.

**For the coordinator after the merge** (the images workflow builds the front door with the `otlp` route; then
`bump-tag.sh` and the golden file): on the pinned front door, `env up -r localtest-v2 --minimal` with a stock studioctl
build of main, then `curl -X POST -H 'Content-Type: application/json' -d '{"resourceSpans":[]}'
http://local.altinn.cloud:8000/otlp/v1/traces` answers 200 (not 503) and a `studioctl run` of `ttd/testapp` with the
guide's variables leaves `localtest/` and `testapp/` folders whose `traces.jsonl` the guide's trace command reads;
`env down`, then `env up -r localtest-v2 --with monitoring`: `/otlp` 200, Grafana's health 200, and the front door's
`smoke-test.sh` monitoring section green (its spans read back from VictoriaTraces); with the default stack alone
`/otlp` is a 503 naming `monitoring`. Until the pin moves, the pinned front door routes `/otlp` to
`services__otel-collector__http__0`, which the runner no longer sets, so `/otlp` answers 503 on both stacks with the
old image (the containers' own gRPC export is unaffected).

**Not verified.** rootless Podman and Docker (this machine runs rootful Podman, where `--user 1000:1000`
alone makes the files the developer's; the `keep-id` path for rootless Podman is `envstack.RuntimeUser`'s, as the
old runtime and container-mode apps use it, but not tried here); macOS and Windows paths of the guide; a session
of hours (the rates are from minutes, and the front door's metric series grow with the routes it serves); the
pinned front door image with the `otlp` route (built locally with `--dev-localtest` only).

## 56. A query page for Storage's instances

**Origin (the user, 2026-10-26).** A page in the front door for looking at Storage's instances: a static HTML form in
plain JavaScript beside the Home pages and a proxy endpoint behind it that renders HTML, with links into the inbox, the
dialog, the app or the receipt while they run. Brief `/home/agent/code/.briefs/storage-instances-query-page.md`.

**Design.**
- `/Home/Instances`: every parameter of Storage's `GET storage/api/v1/instances` by Storage's own name
  (`InstanceQueryParameters`), the repeatable date filters with their operator prefix, and the token (a test user, a
  service owner, a pasted token, or none). The form's state is the address.
- The proxy (`/Home/Instances/Results`, `/Home/Instances/{partyId}/{instanceGuid}`, `…/data/{dataGuid}`) gets the token
  the way `/Home/GetTest…Token` does (shared code, `Home/TestTokens.cs`), calls Storage by service name, and renders the
  list, the instance page and the downloads with the same token.
- Links only while they work: the inbox with the owner's party URN (Register) and the dialog (Arbeidsflate, and the
  adapter for the dialog), the app (registered with the front door) or the receipt (the `receipt` group).
- "Similar instances": same owner, app, owner and app, org, process step, archived and deleted status; no "same
  reference" link.

Details in [`localtest/localtest/README.md`](localtest/localtest/README.md#storage-instances).

**Done (2026-10-26, branch `storage-instances-page`; merged as main `510facd6` and `ddef4331`, pinned `sha-ddef433`).** Route tests in `localtest/localtest/tests/InstancesPageTests.cs`
(16 cases, 179 tests in the project); on the stack with the front door built from the branch, see the front door's
README, "Verified (2026-10-26): the instances page on the stack". Open: the pinned image (the next images run on main,
then `bump-tag.sh`).

**Pinned check (2026-10-26, images `sha-ddef433` from main ddef4331, no `--dev-*`; studioctl from the pins worktree
under a short `STUDIOCTL_HOME`; evidence `/home/agent/code/.handover/storage-instances-page/pinned/`).** With
`--with dialogporten --with receipt --with arbeidsflate` the first `env up` came up healthy (the Arbeidsflate BFF
readiness race seen with `--dev-localtest` did not recur). `ttd/testapp` and four new instances: `/Home/Instances` 200;
the list as the service owner (19) and the test user (11); the instance pages link Inbox (person and organisation
URNs), Dialog (three dialog ids, 200 from Dialogporten's end-user API), Open in the app and Receipt; the attachment
through its link 200 with `hello.txt`, without a token 401. The default stack: only "Open in the app" and "Receipt in
the app"; with the app stopped, none. The front door's `smoke-test.sh`: 83 of 84 (the known `FRONTEND_DIST` check).

## 57. The host's CA bundle for pdf3

**Origin (2026-10-27).** Review item 11 of [`localtest/reviews/workflow-engine.md`](localtest/reviews/workflow-engine.md):
pdf3's Chromium trusts its own NSS store, not the system's, so behind a TLS-intercepting proxy (a corporate network,
this sandbox) it cannot load the app frontend from `altinncdn.no` and PDF generation times out. pdf3's
`docker-entrypoint.sh` already imports `$STUDIO_CA_BUNDLE` (a PEM path) into NSS; neither runner passed it. Brief
`/home/agent/code/.briefs/pdf3-ca-bundle.md`.

**Design.**
- A manifest field `"caBundle": true` (pdf3 only), in the style of `otlpReceiver`; both loaders refuse it with the
  variable in the container's own `envs` or a file or volume at the mount path.
- When the host's `STUDIO_CA_BUNDLE` names a readable file, the runner copies it to
  `<data>/infra/localtest-v2/ca-bundle/<first 16 hex digits of its SHA-256>.pem` (Aspire `<DataDirectory>/ca-bundle/`),
  0644, and mounts the copy read-only at `/etc/localtest/ca-bundle.pem` with `STUDIO_CA_BUNDLE` set to that path. A
  copy, not the file: it is readable by the images' non-root users whatever the original's mode, it lies under the
  home directory Docker Desktop shares (a bundle under `/etc` or `/usr/local` is not), and SELinux relabelling (`:z`)
  touches the copy, never a system file. The hash in the name makes a changed bundle change the container spec, so
  devenv (and Aspire for a persistent container) recreates pdf3.
- Unset, or naming no file: nothing is added, the copy is removed. Set but unreadable: one warning per command, the
  containers start without it.
- `env up` on a healthy environment: the state file records the bundle's hash, and `Changed` compares it, so a new,
  changed or removed bundle converges (only pdf3 is recreated, 5.6 s), an unchanged one says "already running".
- Aspire reads the variable from the AppHost's configuration (its environment), when the model is built; the copy is
  written and the warning logged at `BeforeStartEvent`.
- Golden file: the `everything` case is on a host with a bundle of known content, so its copy's path is fixed; the
  other cases have none. Aspire's `everything` model gets a bundle of the same content, so the parity test compares
  pdf3's mount and variable byte for byte instead of excluding them.
- Other components: not marked. No other image reads `STUDIO_CA_BUNDLE`; the .NET components trust their image's system
  store and the Node BFF `NODE_EXTRA_CA_CERTS`, which a container runtime configured for the proxy provides (this
  sandbox's Podman sets `SSL_CERT_FILE` and `NODE_EXTRA_CA_CERTS` through `containers.conf.d/50-agent-ca.conf` and an OCI
  hook). On the stack no component but pdf3 logged a TLS error, with or without the variable. A developer behind a
  corporate proxy whose container runtime is not configured would need those too; that is a separate question.

**Done (2026-10-27, branch `ca-bundle`; merged as main `f3052353`..`ea0bd3f6`; runner code only, no image change).** Commits: studioctl runner (`caBundle`, the copy, the state hash, tests: read
unset/missing/unreadable, warn once, the copy's name, mode and replacement, only pdf3 gets it, `Changed`, the manifest
validation), the Aspire package (`HostCaBundle`, `StackSpec`, `CaBundleTests`, parity), docs. On the stack (pinned
images `sha-ddef433`, pdf3 `tt02`, studioctl from the branch under `STUDIOCTL_HOME=/home/agent/ca-home`, the scratch
app `ttd/testapp` copy at `/home/agent/code/apps/testapp-pdf` as user 20001, party 520001; evidence
`/home/agent/code/.handover/ca-bundle/`):
- with `STUDIO_CA_BUNDLE=/etc/ssl/certs/ca-certificates.crt`: pdf3 has the copy at `/etc/localtest/ca-bundle.pem`
  read-only and the variable; its NSS database was created at container start with 122 certificates (no manual
  import); `process/next` 200 in 2 s, the PDF embeds Altinn-DIN, -Bold and -Italic (`pdffonts`), rendered in
  `with/with-ca-bundle-page-1.png` (`ttd` has no logo in `altinn-orgs.json`, so the fonts are what comes from the CDN);
- `env -u STUDIO_CA_BUNDLE env up`: only pdf3 recreated, without mount or variable, the copy removed, no NSS
  database; `process/next` 500 "Pdf generation failed" after 30 s, pdf3 logs `net::ERR_CERT_AUTHORITY_INVALID` and the
  `#readyForPrint` timeout;
- `env up` with the variable again: pdf3 recreated with the mount, a PDF in 1 s; a second `env up`: "already running".
Not verified: the Aspire package on the stack (unit and parity tests only), Docker Desktop, SELinux hosts.

**The old `localtest` runtime (2026-10-27, scope added by the user).** Same gap in altinn-studio's studioctl
(`src/cli/internal/cmd/env/localtest/components/pdf.go`): fixed on branch `fix/studioctl-pdf3-ca-bundle` of
`ivarne-agent/altinn-studio` (4082f9ee0e, pushed, PR not opened; handover
`/home/agent/code/.handover/studioctl-pdf3-ca-bundle-pr.md`). A preparation copies the bundle to
`<data>/infra/pdf3/ca-bundle.pem`, pdf3 mounts it read-only at `/etc/localtest/ca-bundle.pem` with the variable;
unset or missing changes nothing, unreadable warns. No hash in the name: that runtime's `env up` returns early on a
running environment anyway, so the variable takes effect on `env down` + `env up`. Proven the same way (evidence
`/home/agent/code/.handover/ca-bundle/old-runtime/`): with the variable `process/next` 200 and the Altinn fonts,
without it 500 after 30 s with `ERR_CERT_AUTHORITY_INVALID`.

## 58. Scripted vendor sync for the components

**Origin (the user, 2026-10-27).** "Start to plan scripted vendor sync for all components (not studioctl)." Brief
`/home/agent/code/.briefs/vendor-sync-plan.md`. The plan, with the inventory, the design and the steps:
[`localtest/docs/vendor-sync.md`](localtest/docs/vendor-sync.md). Takes over task 1 (its check is the `check` command).

**The user's decisions (2026-10-27).**
- Metadata: the block inside each `UPSTREAM.md`.
- Line endings: the check tolerates `\r\n` against `\n` (Register's `.sln` files stay as they are).
- A sync needs an agent to triage it, so syncs are triggered by hand, on demand. No schedule, and no CI for now.
- The tool only stages the sync and prints the summary. The agent finishes it: resolves, builds, verifies and
  commits. There is no `--commit`.
- The Resource Registry is retargeted to altinn-auth.
- Register waits for altinn-auth#4056. **Check that PR at every sync**, and retarget Register once it has landed.
- workflow-engine stops being vendored.
- Modifications that upstream has made unnecessary are removed, for example Correspondence's party-id site after
  upstream #2157.

**Goal.** One tool, `localtest/tools/vendor-sync.py`, that syncs one component, a group or all 15 to a given or the latest
upstream commit, keeps the `LOCALTEST` sites and Arbeidsflate's patches, proves "upstream at the new pin plus our sites
and nothing else", builds what changed, and prints the summary for the commit and the TODO note. Replaces
`localtest/tools/repin-vendor.py` and the hand-written recipes in the `UPSTREAM.md` files.

**Inventory (measured 2026-10-27, details in the plan).** Pins are 6 to 14 days old (the Resource Registry 32); behind the
default branch by 0 (Register, the Resource Registry) to 31 commits (altinn-auth; the monorepo's 183 for workflow-engine,
9 of them in its paths), 0 to 95 changed files in the copied paths. 28 site files with 60 blocks, 3 patches. Draft path
lists reproduce all 15 `vendor/` trees blob for blob. Found: one stray blank line next to a block in 3 site files
(Notifications, Resource Registry, Storage), Register's two `.sln` files committed with CRLF (upstream LF), the Resource
Registry's repository archived at our pin and the code moved into altinn-auth, Authorization's release moved to
`docker-bake.hcl` today, Correspondence's party-id site made removable by upstream #2157, Profile's `main` moving
OpenTelemetry past the exporter version our overrides pin. The two site files upstream changed merge clean.

**Design (summary).**
- Metadata: a `json vendor-sync` block in each `UPSTREAM.md` (repository, branch, commit, date, paths, group, mirrors,
  watch, projects, follow, reminders); the pin bullets and a site table generated between markers; prose without line
  numbers.
- Sites are detected, not declared: any differing file must parse into `#if LOCALTEST … #endif` and
  `#if !LOCALTEST … #else … #endif` blocks, and its production view must equal upstream's blob byte for byte.
- `sync`: blob-level (`ls-tree`, `cat-file`, `update-index --cacheinfo`; never `git archive`), `git merge-file` for site
  files changed upstream with a validation after it, conflicts left marked and reported; groups move together.
- Arbeidsflate: replace the tree, refresh the patches by cherry-picking them in a scratch repository.
- Verification: `check`, a host `dotnet build` with the overrides, the follow-the-host version warning, shared fakes
  identical. Images, smoke tests and the stack: `localtest/docs/verify-after-sync.md`.
- Mirrors: three-way merge into one-to-one mirrors when clean, otherwise report; watched paths reported.

**Steps** (each mergeable, proof in the plan): 1 fix the four drifts; 2 metadata, `check`, `status` (task 1's proof);
3 `sync` on Profile (own repository, one site file with both kinds, a small real change that trips the version check);
4 the directive-heavy components one by one (adapter, Notifications, Events, Correspondence, Storage, Receipt,
Dialogporten); 5 the altinn-auth group; 6 Arbeidsflate; 7 the Resource Registry from altinn-auth; 8 workflow-engine;
9 delete `repin-vendor.py`; 10 optional CI (`check` on pull requests, weekly `status`).

**Open questions** (section 4 of the plan): answered by the user's decisions above.

**Done (2026-10-27, branch `task58-tool`, brief `/home/agent/code/.briefs/task58-tool.md`).** The how-to is now
[`localtest/docs/vendor-sync.md`](localtest/docs/vendor-sync.md).
- Step 1 (56eb34f4): the stray blank line next to a block removed in Notifications' `Program.cs`, the Resource
  Registry's `ResourceRegistryHost.cs` and Storage's `Program.cs`; Register's CRLF `.sln` files stay (tolerated, as is a
  leading BOM: both sides are normalised before comparing).
- Step 2 (8fc8fd49, 21dc1e8d): `localtest/tools/vendor-sync.py`, standard library only, blob-level. `check` (the
  invariant, `file:line` on a violation, removal candidates, groups at one commit, Arbeidsflate's patches apply, the
  `follow` versions after a host build, the shared fakes' copies), `status` (the plan's table, live, with the
  reminders), `render` (the generated pin bullets and site table, laid out as Prettier would). The `json vendor-sync`
  block in the 14 `UPSTREAM.md` files; the prose lost its line numbers. 39 unit tests
  (`python3 -m unittest discover -s localtest/tools`). Proof: `check --all` 14 `ok` and the shared fakes; a one-line
  edit in Events' `OutboundService.cs` failed with `…/OutboundService.cs:20: production view differs from upstream
  (upstream line 20)`; an added well-formed block passed (a stale-table warning until `render`); `status --all`
  reproduced the plan's table (altinn-auth one commit further, 32) and printed Register's reminder.
- Step 3 (a5f8fba6, d9da6d60): `sync profile` staged `d1f9871..d0cd92d` (5 csproj files, NuGet updates); `check`
  passed; the host build passed and `check` warned `pins OpenTelemetry.Exporter.OpenTelemetryProtocol 1.19.1, the host
  resolves OpenTelemetry 1.20.0`; the fixes commit moved the exporter to 1.20.0. The vendored commit's message is the
  tool's summary. Profile synced d1f9871..d0cd92d (2026-10-22, 1 commit, 5 files, 0 sites merged clean).
  **Verified on images (2026-10-27, 20:07-20:20, after the coordinator released the lock):** `podman build` of the
  Profile image (32 s; OpenTelemetry and the OTLP exporter 1.20.0 in its `deps.json`); `env up -r localtest-v2
  --dev-profile` with the worktree's studioctl in an isolated home, 13 containers in 19 s; root smoke test 26 `ok`, 0
  `FAIL`, 1 skip (the OTLP check); `components/profile/smoke-test.sh` 52 `ok`, 0 `FAIL`; no `fail:`/`crit:` in
  Profile's log; with `--with monitoring` the Profile suite 53 `ok` including `OTLP: a server span from service.name
  profile`. The images of the three components with step 1's blank-line fix build (Notifications 35 s, the Resource
  Registry 46 s, Storage 52 s, exit 0). Evidence `/home/agent/code/.handover/task58-tool/`. After the merge:
  the images workflow's `sha-<commit>` images and the pinned check (verify-after-sync 5.2, `bump-tag.sh`): done, see
  **Pinned check** below.
- `sync` merges a site file with `git merge-file`, and when that conflicts re-anchors the blocks where upstream left
  their neighbours alone; a conflict stays unmerged in the index (stages 1-3). `sync --repository … --paths …
  [--group …]` moves a component to another repository (for the Resource Registry), and a site whose two branches hold
  the same code or whose replaced code upstream changed is reported.
- Step 4 (d1cafcc4): **removed** `localtest/components/workflow-engine/` (the vendored copy of `447e53e`, its
  `Dockerfile`, `.dockerignore`, `localtest.build.props`/`.targets` and README); the stack runs the monorepo's `tt02`
  image. Its image test is now `localtest/smoke/workflow-engine.sh` (the dotnet mode dropped); references updated.
- Step 5 (fc46a4c7, 2d4a9530): `localtest/tools/repin-vendor.py` deleted; every `UPSTREAM.md`'s Syncing section is a
  pointer to the tool plus the component's own notes. altinn-auth#4056 is an issue (open), not a pull request; the
  reminder links it as one.
- Step 6 (71d77172): the docs.
- **Next task:** done as the follow-up below.

**Pinned check (2026-10-27, 20:22-20:31, images `sha-5f13eea` from main 5f13eea3, images run 40567145923; no
`--dev-*`; studioctl from the `pin-5f13eea` worktree under a short `STUDIOCTL_HOME`; evidence
`/home/agent/code/.handover/pin-5f13eea/`).** `bump-tag.sh sha-5f13eea` pinned 20 images; the golden diff moves only
the tags (`sha-ddef433` to `sha-5f13eea`). Static: `go build`/`go test ./...` all `ok`, `make lint` 0 issues, golden
regeneration clean; studioctl-server 1015 passed, 13 failed (the known `V8Tov9UpgradeTargetFrameworkTests`), devenv
`ok` except `pkg/runtimes/kind`'s two monitoring tests (known); the Aspire package 0 warnings, 178 passed, CSharpier 46
files clean, both AppHosts build. Minimal mode: 5 containers in 14 s; root 23 `ok`, 4 skips, 0 `FAIL`;
`localtest/smoke-test.sh` 92 `ok` and the known `FRONTEND_DIST` failure; telemetry folders `localtest`, `storage`,
`workflow-engine`; `env down` removed `telemetry/`. Default stack: 13 containers in 27 s (with pulls); root 26 `ok`, 1
skip; the front door 83 of 84 (`FRONTEND_DIST`); Profile 52, Notifications 64, the Resource Registry 43, Authentication
32, Authorization 31, Events 27, Register 14, the adapter `--off` 7 (Storage's outbox), shared all ok; no `FAIL`.
`--with monitoring` (19 containers, 54 s): root 27, Profile 53, Notifications 65, the Resource Registry 44, each with
its OTLP server span (`profile`, `notifications`, `resource-registry`, `storage`), no `FAIL`.

**Follow-up (2026-10-27, branch `task58-followup` from main `8a41f851`, brief by the coordinator; evidence
`/home/agent/code/.handover/task58-followup/`).** The user's decisions: the Resource Registry moves to altinn-auth, and
modifications upstream made unnecessary go.
- **The Resource Registry retargeted** (55351c8e vendored, 78fca0e1 fixes): `sync resource-registry --repository
  Altinn/altinn-auth --group altinn-auth --paths src/apps/Altinn.ResourceRegistry/{src,Dockerfile,Directory.Build.props,
  Directory.Build.targets,Directory.Packages.props,stylecop.json,.editorconfig} global.json`, at the group's commit
  `3d51810`: no group move was needed (altinn-auth `main` has three later Resource Registry commits, 32 files: the RDF
  contact point, a paging fix, and the Maskinporten scope-prefix validation (`bfa6fdf8`), which adds feature management
  and a Samarbeidsportalen HTTP client to `ResourceRegistryHost.cs`; none the stack needs now. **The next altinn-auth
  group sync** must check whether that client needs a localtest setting or fake. `docker-bake.hcl` and the `Dockerfile`
  are unchanged on `main`). 20 upstream commits on the
  paths (the archived repository's last ones, its import into altinn-auth `30a6fc91`, then the BOM removal, the warning
  clean-up, NuGet updates, creation/update timestamps, the policy-rights return type); 26 files updated, 6 added, 7
  removed. What moved: the archived repository's `src/Directory.Build.props`, `.targets`, `Directory.Packages.props` and
  `src/stylecop.json` are now the vertical's own `src/apps/Altinn.ResourceRegistry/Directory.*` and `stylecop.json` (they
  do not import altinn-auth's `src/Directory.*`, so those are not copied); `Settings.StyleCop` is gone, the vertical's
  `.editorconfig` (the StyleCop severities) is new; `global.json` is altinn-auth's (SDK 10.0.107, `latestMajor`). The site
  file `ResourceRegistryHost.cs` merged clean (blocks moved by one line, upstream added a schema filter). The `Dockerfile`
  mirror is unchanged: upstream's is byte for byte the archived one. The metadata watches `docker-bake.hcl` and
  `containers/` (the bake file publishes nothing at the pin) and drops the retarget reminder. Fixes: the fake
  `LocaltestFilePolicyRepository.TryAcquireBlobLease` returns `Task<string?>` (CS8613 after upstream made the interface
  nullable); the overrides' comment, the Dockerfile header, the README and `UPSTREAM.md` name the vertical. Host build 0
  errors, 0 warnings (10 before).
- **Correspondence** (1fa7aee0 vendored, 05ffe2b4 the removal): synced `6dcf561..d2dc912` (#2153 the forwarded template,
  #2157 `urn:altinn:partyid:{id}` from `CreateDecisionRequest` and `CreateUserContactPointDecisionRequest`; 7 files, no
  site file, no migration, no raw `Set-Cookie`). Task 7b's `#if !LOCALTEST` block in `XacmlRequestFactory.cs` removed (the
  file is upstream's again) with its reminder; no fake depended on it. Host build 0 errors, 24 warnings (as before).
  `planned-components/correspondence/party-ids.md` is closed.
- **The other blocks.** `check --all` and `status --all` report no removal candidate. No site file changed upstream between
  the pins and `main` but Notifications' `Program.cs` and the adapter's `StorageDialogportenDataMerger.cs` (`status
  --all`; the adapter's change is outside its link blocks and merges clean), and every block's reason still holds at its
  pin (each component README names it), so none was removed beyond Correspondence's. Kept, by kind: the hooks that wire
  the fakes and the configuration (Access Management, Authentication, Authorization, Correspondence, the adapter,
  Dialogporten's three hosts, Events, Notifications, Profile, Receipt, Register, the Resource Registry, Storage); the
  service-discovery registrations (no upstream registers a configuration-only resolver); the OTLP blocks of task 53
  (Authorization, Correspondence, the adapter, Dialogporten, Events, Notifications, Profile, Receipt, Storage: no pinned
  upstream reads `OTEL_EXPORTER_OTLP_ENDPOINT`, and the upstream fixes in `localtest/reviews/otel/` are not merged); the
  http and host sites (Authentication's redirect target, Correspondence's ID-porten retriever, Dialogporten's URL
  validators, Events' webhook and source checks and app domain, Receipt's and Storage's request URLs, the adapter's
  links and app URIs); Access Management's database step and factory-built Resource Registry client; the adapter's
  Wolverine local queue and acks. The workflow engine's OTLP work (altinn-studio#21515) needed no change here: it is not
  vendored, and the manifest already dropped `EngineSettings__EnableTelemetry` (task 52). **Becomes removable at a
  later sync, not done (no other component synced):** Notifications' `main` (`09fa8030`, #1930) calls
  `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; at its next sync the OTLP replacement in its
  `Program.cs` shrinks to the service name (upstream keeps `platform-notifications` over `OTEL_SERVICE_NAME`), or goes if
  the stack accepts that name. Register still waits for altinn-auth#4056 (open).
- **Verified (2026-10-27, 20:40-20:55, under the lock, isolated studioctl home from the branch, `STUDIO_CA_BUNDLE`
  set).** Static: both host builds above, `check --all` 14 `ok` and the shared fakes, the tool's unit tests ok, shellcheck
  of the changed suite silent, typos only the old hits in the changed files. `env up -r localtest-v2
  --dev-resource-registry`: 13 containers in 42 s with the image build; root 26 `ok`, 1 skip (OTLP); the Resource Registry
  43 `ok`, 1 skip; Authorization 31 `ok`, 1 skip; shared "all ok"; no `FAIL`. `--with correspondence --dev-correspondence
  --dev-resource-registry=…`: 82 s with both builds; Correspondence 78 `ok`, 1 skip (OTLP), including app-lib's calls on
  party 500006 and the recipient's path on 501337 without the site; root 26 `ok`. Arbeidsflate's suite not run: it does
  not call Correspondence, whose browser download path is in Correspondence's own suite. Not run: `--with monitoring`,
  `CORRESPONDENCE_RESTART=1`, the other components' suites (their images are the pins). Teardown: `env down`, server down,
  both `--dev` images and their build layers removed, the home removed, the lock released. After the merge: the images
  workflow, then the pinned check (verify-after-sync 5.2) before `bump-tag.sh`: done, see **Pinned check** below.
- **Pinned check (2026-10-27, 20:57-21:10, images `sha-1d08adb` from main 1d08adb4, images run 40572353069; no
  `--dev-*`; studioctl from the `pin-1d08adb` worktree under a short `STUDIOCTL_HOME`, `STUDIO_CA_BUNDLE` set; evidence
  `/home/agent/code/.handover/pin-1d08adb/`).** `bump-tag.sh sha-1d08adb` pinned 20 images; the golden diff moves only
  the tags (`sha-5f13eea` to `sha-1d08adb`). Static: `go build`/`go test ./...` all `ok`, `make lint` 0 issues, golden
  regeneration clean; studioctl-server 1015 passed, 13 failed (the known `V8Tov9UpgradeTargetFrameworkTests`), devenv
  `ok` except `pkg/runtimes/kind`'s two monitoring tests (known); the Aspire package 0 warnings, 178 passed, CSharpier
  46 files clean, both AppHosts build. Minimal mode: 5 containers in 17 s (with pulls); root 23 `ok`, 4 skips, 0 `FAIL`;
  `localtest/smoke-test.sh` 92 `ok` and the known `FRONTEND_DIST` failure; telemetry folders `localtest`, `storage`,
  `workflow-engine`; `env down` removed `telemetry/`. Default stack: 13 containers in 34 s (with pulls); root 26 `ok`, 1
  skip; the front door 83 of 84 (`FRONTEND_DIST`); the Resource Registry 43, Authorization 31, Profile 52,
  Authentication 32, Events 27, Notifications 64, Register 14, the adapter `--off` 7, shared all ok; no `FAIL`. `--with
  correspondence --with dialogporten` (19 containers, 41 s): Correspondence 78 `ok`, 1 skip (OTLP); then Events 20 `ok`
  and 8 `FAIL` (late processing: "stored once", the deliveries, `registeredTime` ages 63 s), 27 `ok` on the rerun, no
  `fail:` or `crit:` in the last 100 lines of its log (the fresh-stack flake of verify-after-sync 6.7); the adapter 39,
  Dialogporten 60 (both again 39 and 60 after the Events rerun, in 6.7's order); root 26. `--with correspondence --with
  monitoring` (21 containers, 49 s): root 27, the Resource Registry 44, Correspondence 79, with the OTLP server spans
  from `storage`, `resource-registry` and `correspondence`; no `FAIL`. Not run: `CORRESPONDENCE_RESTART=1`, the receipt
  and Arbeidsflate groups, 4.4 to 4.6. Teardown: `env down`, server down, the home removed, the lock released; 13 GB
  free, so the older `sha-5f13eea` images stay.

## 59. Upstreaming the localtest modifications

The user's request (2026-10-27): one TODO document per service for pushing our changes back upstream, taken gradually,
the easy fixes first and the structural changes after. The documents are in
[`localtest/upstreaming/`](localtest/upstreaming/README.md): one file per vendored service plus
`authorization-utils.md`, each with phase 1 (easy fixes), phase 2 (structural changes) and "Stays local", every
directive site, fake and patch accounted for. Totals at `1d08adb4`: 58 easy and 57 structural items, 7 already merged
upstream. Progress is tracked in those files' checkboxes, not here. Nothing is opened upstream until the user picks the
services; the user opens the pull requests.

## 60. Database users and passwords owned by studioctl and Aspire

The user's decision (2026-10-28): "As long as studioctl and aspire owns the defaults it's fine for now (meaning it can
be static). The standard passwords should not be hard coded in docker images or repos. I think setting up users and
databases belong in the localtest repo."

- The passwords live in one place that studioctl and Aspire share: the manifest (`manifest.json`, embedded in both).
  Static values are fine. (Superseded a day later by task 62: each runner generates the passwords per installation
  and the manifest holds only `{{password:<role>}}`.) No password in an image: not in the Postgres init scripts, not in the fakes, not in the
  Dockerfiles. The init scripts read them from the Postgres container's environment and fail when one is missing.
- Each component's users and database are created by its own localtest init script (`postgres-init/NN-*.sh`), not by
  an upstream script: Storage's `10-storage.sh` stops running the vendored `infra/postgres_init.sql`, and the Postgres
  image no longer copies it.
- The services get the same values through the manifest (`PostgreSqlSettings__…Pwd`, connection strings, or the
  fakes' options), overriding whatever their upstream `appsettings.json` holds.
- Task 59 is unaffected: the Storage item "Make `infra/postgres_init.sql` independent of the superuser's name" stays
  in phase 1 and its PR (fork branch `storage-postgres-init-role`) is still to be opened (the user, 2026-10-28; opened
  as [altinn-storage#1178](https://github.com/Altinn/altinn-storage/pull/1178) the same day). Once
  localtest creates Storage's users itself, that item no longer removes anything here.

**Done (2026-10-29):** merged as main `d7e15e9e` and `e089ff84`, pinned `sha-e089ff8` (pinned check under section 61). The Aspire run (sample AppHost, the changed images built locally and selected with `Tag = "sha-t60local"`; a `Tag` that does not start with `sha-` floats and is pulled, so a local tag never starts, which is why the first attempt created no container): on the package's existing pre-task-60 volume and on a fresh one, 18 containers healthy, both apps provisioned, root 26, the front door 83 + `FRONTEND_DIST`, Profile 52, the Resource Registry 43, Authorization 31, Authentication 34, the adapter 39, Dialogporten 60; Notifications 64 on the rerun; Events under Aspire 27 + 1 (6.7) on the fresh volume's first run, more delivery failures on reruns after the Dialogporten suites, no database error (evidence `aspire-run-outcome.txt`).

## 61. Remove the OTLP resource workaround from the ServiceDefaults hosts

The user's decision (2026-10-28): "remove the workaround in the four hosts, test and push the stack". Authentication,
Access Management, Register and the Resource Registry called `AddLocaltestOtlpResource` (task 53) so that
`OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` would win over `AltinnServiceResourceDetector`. It changes nothing
that shows: each host already names itself what studioctl and Aspire set as `OTEL_SERVICE_NAME` (the manifest name),
and Aspire's dashboard links no container's telemetry to its resource either way (its container `service.instance.id`
is the full DCP name, which never matches); the Traces, Logs and Metrics pages show the same plain names with and
without it (evidence `/home/agent/code/.handover/aspire-instance-id-check/`). Accepted consequence: `service.instance.id`
is the container's host name (the library's `Environment.MachineName`), and `OTEL_RESOURCE_ATTRIBUTES` no longer
overrides the resource in these four hosts.

- The call goes from each hook, with the hosts' `fakes/Otlp/` copies (Register: `fakes/Altinn.Register.Localtest/Otlp/`,
  its two OpenTelemetry package references and their `follow` entries in `vendor/UPSTREAM.md`). The shared helper stays:
  `AddLocaltestOtlp` calls it (until task 63's follow-up inlined the rule in each component and deleted the shared
  folder).
- Upstreaming: both phase-1 items of `localtest/upstreaming/authorization-utils.md` move to "Not needed", and the four
  hosts' `OTEL_SERVICE_NAME` items go (nothing left to remove); phase 1 totals 52. The fork branches
  `otel-service-name-env` and `otlp-and-azure-monitor` of `ivarne-agent/altinn-authorization-utils` stay unopened.

Verified 2026-10-28 on local images of the four (`--dev-` flags, `env up -r localtest-v2 --with monitoring`, 18
containers; evidence `/home/agent/code/.handover/otlp-resource-workaround-removal/`, the query script `otlp-check.sh`
there): one request with a known `traceparent` per host gave its server span; over 15 minutes, through Grafana's
datasource proxy, under `service.name` = the manifest name and `service.instance.id` = the container's host name:

| Host | Spans | Metric series | Log records |
| --- | --- | --- | --- |
| `authentication` | 308 | 670 | 31 |
| `register` | 100 | 345 | 23 |
| `access-management` | 1253 | 427 | 2634 |
| `resource-registry` | 372 | 561 | 599 |

Smoke tests: root 27 `ok` (with the OTLP check), `localtest/smoke-test.sh` 85 `ok` and the known FRONTEND_DIST failure
(verify-after-sync 6.1), Authentication 33, Register 15, Resource Registry 44, each with its OTLP span check. Static:
the four host builds 0 errors, `vendor-sync.py check --all` ok (shared fakes identical), its 39 unit tests, `Otlp.Tests`
10 of 10.

**Pinned check (2026-10-29, 07:05-07:40, images `sha-e089ff8` from main e089ff84 (tasks 60 and 61), images run
40802133116; no `--dev-*`; studioctl from the task 60 worktree under a short `STUDIOCTL_HOME`, `STUDIO_CA_BUNDLE` set;
evidence `/home/agent/code/.handover/task60-db-passwords/pinned/`).** `bump-tag.sh sha-e089ff8`; the golden diff moves
only the tags. Static: `go test ./...` all `ok`, the Aspire package 178 passed. Existing volumes: default stack up in
24 s, root 26. After `env reset`: default stack 21 s; root 26 (1 skip), the front door 83 + `FRONTEND_DIST`, Events 27
(on the rerun; the first run "third event registered", 6.7), Notifications 64, Profile 52, the Resource Registry 43,
Authorization 31, Authentication 32, Register 14, the adapter `--off` 7, shared 13. `--with dialogporten --with
correspondence`: Events 27, the adapter 39, Dialogporten 60, Authentication 34, Correspondence 78. `--with dialogporten
--with arbeidsflate`: Arbeidsflate 59 (`--with arbeidsflate` alone starts no adapter, so its inbox checks fail: 53 + 6,
as its README says). Task 61's checks, `--with monitoring`: root 27, Authentication 33, Register 15, the Resource
Registry 44, and `otlp-check.sh`: `authentication`, `register`, `access-management` and `resource-registry` export
traces, metrics and logs under their own `service.name`. Minimal mode: root 23 (4 skips), the front door 92 +
`FRONTEND_DIST`. No other `FAIL`.

## 62. Generated database passwords per installation

The user's principles for database initialization (2026-10-28):

1. Localtest owns the initialization of the database and the creation of the users.
2. Platform services that run Yuniql or other migration software at startup do so in localtest too (localtest does
   not run their migrations for them).

Ideally every Postgres installation gets new, random passwords. The user's decision (2026-10-28): "store them in json
and use `Password` as the superuser password for recovery".

- Each runner keeps the role passwords in a JSON file in its own data directory, owner-only: studioctl under its
  `data/generated/localtest-v2/`, the Aspire package under `AltinnLocaltestOptions.DataDirectory`. The two runners
  have separate volumes (`localtest-v2-*` and `altinn-localtest-*`), so they do not share the file. A missing password
  is generated (random, at least 128 bits) and saved before the stack starts.
- The superuser has the fixed password `Password`, owned by the runners (not in an image). It is the recovery path: when
  the JSON file is lost or a volume is older than the file, the runner connects as the superuser and sets every role's
  password to the file's value before the services start, so no volume reset is ever needed for passwords.
- Task 60's mechanism stays: the Postgres container and the services get the values through their environment; the
  manifest describes which variable carries which role's password instead of holding the values.

**Done (2026-10-29):** main `ea619ad5` (runners, manifest, docs) and `3e586ebb` (the init scripts read the passwords
with `\getenv`), pinned ``sha-3e586eb``; brief `/home/agent/code/.briefs/task62-generated-db-passwords.md`, evidence
`/home/agent/code/.handover/task62-generated-db-passwords/`.

- **Manifest:** no role password; `{{password:<role>}}` in `envs`, resolved by both runners. The `postgres` variables
  whose value is exactly a role's placeholder are the roles (13); every other value with the placeholder is a consumer.
  The superuser `postgres` has the literal `POSTGRES_PASSWORD` `Password` (the workflow engine connects as it). Both
  readers check the placeholders (role name, carried by `postgres`, carried once, a literal superuser).
- **Files:** studioctl `data/generated/localtest-v2/secrets/postgres-passwords.json`, the Aspire package
  `<DataDirectory>/secrets/postgres-passwords.json`: role to password, 192 random bits as 48 hex digits, directory 0700,
  file 0600, atomic write, missing roles filled before anything starts, stored ones kept, unusable values refused.
  studioctl records a digest in its state file, so `env up` converges a running stack after the file changed.
- **Reconciliation:** after Postgres's health check and before its dependants start (studioctl: part of the readiness
  probe; Aspire: part of Postgres's health check), `exec -i` of `psql` as the superuser over the Unix socket (the image's
  `pg_hba.conf` trusts local connections, initdb's default; verified), with a script on stdin that reads each password
  from the container's environment with `\getenv`, sets the superuser's and every existing role's, and keeps the
  statements out of the server log and `pg_stat_statements`. The same script and command in both runners, compared
  through the golden file.
- **No password in argv:** the init scripts now use `\getenv` (they passed `psql -v password=…`, visible in `ps` on the
  host during a fresh volume's init); studioctl's Podman CLI client passes container environments in an owner-only
  `--env-file`. Remaining: under Aspire, DCP itself runs `podman create … -e KEY=VALUE` (the containers' environment), so
  the values are in that command line for its lifetime; the package could move the password variables to an owner-only
  `--env-file` of its own (with a digest variable so a changed file still recreates the containers). **Known gap,
  left as is (owner 2026-10-29: "Leave it as a known gap in TODO.md")**: the values are random per installation, work
  only against the local Postgres and are in the containers' environment anyway; the exposure is to other local users
  who can list processes but cannot reach the user's Podman.
- **Tests:** Go: the manifest's roles, placeholders, checks and script; the password file (generation, modes, kept and
  filled, refused files, digest); the runner's reconciliation (after the probe only for Postgres, the script on stdin,
  masked errors, the digest as a change); the Podman env file. Aspire: the same file and manifest cases, studioctl's
  roles, script and command from the golden file, the health check's reconciliation, golden comparison with the
  golden passwords. Go `ok`, lint 0 issues; Aspire 198 passed, CSharpier clean.
- **Proven on studioctl:** (c) the volume from before the task (superuser `postgres`/`postgres`, static role
  passwords): no reset, the 13 roles and the superuser set, the file's passwords log in over TCP and the old ones are
  refused, root 26, Events 27, Notifications 64, Profile 52, the Resource Registry 43, Authorization 31. (a) a fresh
  volume with the new Postgres image and a new file: the init scripts create the roles with the file's passwords, root
  26, the front door 83 + `FRONTEND_DIST`, Events 27, Notifications 64, Profile 52, the Resource Registry 43,
  Authorization 31, Authentication 32; `--with dialogporten --with correspondence`: Events 27, the adapter 39 (rerun; the
  first run lost one dialog lookup), Dialogporten 60, Correspondence 78; `--with dialogporten --with arbeidsflate`:
  Arbeidsflate 59. (b) the file deleted between runs: 13 new passwords, the same volume, reconciled, root 26, Events 27,
  Profile 52, Notifications 64, the Resource Registry 43, Authorization 31; and deleted while running: `env up`
  converged and reconciled, root 26.
- **Proven under Aspire** (sample AppHost, the new Postgres image and the pinned rest under a local `sha-` tag): (a) fresh
  volumes, 18 containers healthy in 61 s, both apps provisioned, root 26, the front door 83 + `FRONTEND_DIST`,
  Notifications 64, Profile 52, the Resource Registry 43, Authorization 31, Authentication 34, the adapter 39,
  Dialogporten 60, Events 26 + 2 (the 6.7 "third event registered" flake seen under Aspire in task 60's run too); (b) the
  file deleted: new passwords, Postgres recreated on the same volume and reconciled, root 26, Profile 52, Notifications
  64, the Resource Registry 43, Dialogporten 60; (c) the roles and the superuser set back to the old static values on
  the stopped stack: the next start reconciled them, root 26, Profile 52, the Resource Registry 43.
- **(d):** no password in any container's log, in `podman inspect`'s `Path`/`Args`/`Cmd`/`Entrypoint`, in studioctl's
  logs or state file, in the AppHost's console or in the evidence; in `ps` samples every 0.2 s during studioctl's starts
  none after the init-script fix (the pinned image's init scripts showed them in `psql -v`); under Aspire only in DCP's
  `podman create` lines. They are in the containers' environment (`podman inspect` `Config.Env`), as accepted.
- **Pinned check (2026-10-29, images `sha-3e586eb` from main 3e586ebb, images run 40840486674; no `--dev-*`; a new
  password file on fresh volumes; evidence `pinned/`):** the golden diff moves only the tags; Go `ok`, Aspire 198. Default
  stack (47 s with pulls): root 26, the front door 83 + `FRONTEND_DIST`, Events 27, Notifications 64, Profile 52, the
  Resource Registry 43, Authorization 31, Authentication 32; `--with dialogporten --with correspondence`: Events 27,
  the adapter 39, Dialogporten 60, Correspondence 78; minimal: root 23 (4 skips). No `FAIL`.

## 63. A directive instead of a small fake

The user's decision (2026-10-29): "It looks like we have the wrong balance between #if !LOCALTEST and custom overriden
services. Anywhere a Fake class only requries 3-4 line changes with a preprocessor directive, should be in the
directive, and the fake should be deleted."

It replaces the inherited rule "everything else goes in `fakes/`" (now in the rules above) and, for this case, "report
before a further site": a directive site that replaces a fake needs no report first.

- Inventory every class under `localtest/components/*/fakes/` and `components/shared/fakes/`: could a `#if !LOCALTEST` /
  `#else` / `#endif` (or an additive `#if LOCALTEST`) block of about 3-4 lines in the vendored code, counting the DI
  registration it replaces, have the same effect? Not candidates: real behaviour (stores, controllers, inspection
  endpoints, seeders, token issuing, the Maskinporten fake), blocks inside blocks, many files, and classes from packages
  that are not vendored.
- Convert each candidate (production branch byte-identical, `vendor-sync.py check --all`), delete the fake and its
  registration, `render` the site tables; list the borderline cases (5-8 lines, or a new site file for a tidy fake) with
  a recommendation instead.
- Docs: the rule in this file, `planned-components/mvp-vendoring-model.md`, `localtest/README.md`, the component READMEs,
  `UPSTREAM.md` prose and `localtest/upstreaming/*.md` items naming a deleted fake.

**First part pinned (2026-10-29):** main `6e1b1105`..`27e07fb5`, images run 40844475916, pinned `sha-27e07fb`. Six
directive sites replaced seven fakes (289 lines): Authorization's `NoOedRoleAssignments` and
`LocaltestPolicyRetrievalPoint` (and `NoopEventsQueueClient` without a site: the audit log is forced off),
Dialogporten's two `LocaltestTokenIssuerCache` copies, the Resource Registry's `LocaltestApplicationsClient` and
Correspondence's `LocaltestResourceManager` (the IP restrictions job is no longer registered). Inventory and evidence in
`/home/agent/code/.handover/task63-directives-over-small-fakes/`. Local images (`--dev-*`): root 26, Authorization 31,
Resource Registry 43, Correspondence 78, Events 27, the adapter 39, Dialogporten 60, Authentication 34. Pinned check (no
`--dev-*`): default stack root 26 (1 skip), the front door 83 + `FRONTEND_DIST`, Authorization 31, Resource Registry 43;
`--with dialogporten --with correspondence`: Events 27, the adapter 34 + 5 FAIL on the first run (the service-owner read
of the new dialog), 39 on two reruns, Dialogporten 60, Authentication 34, Correspondence 78; minimal root 23 (4 skips).
Go `ok`, the Aspire package 198. Then the user widened the scope to the shared fakes (below).

**Shared fakes (the user's addition, 2026-10-29, verbatim):** "Convert the shared fakes to directives too if they are
sufficiently small and don't live in a dependency." Correspondence's copy of `Cookies/` became a `#if !LOCALTEST` block
in upstream's `ExpireCookie` (`Auth/DependencyInjection.cs`: `Secure = false`, `SameSite = Lax`); Authentication keeps
its copy (twelve sites, task 21b's decision). `Maskinporten/` and `ServiceDiscovery/LocaltestServiceDiscovery.cs` wrap
packages, `LocaltestPrefixRouting` is a handler with the front door's table: kept. `Otlp/` is borderline (about six
lines inlined into each of six `Program.cs` sites, and the tested shared implementation lost): kept, as is every
`LocaltestPublicSigningKeyProvider` (it replaces `Altinn.Common.AccessToken`'s class). No shared file lost its last
copy, so the copy check is unchanged.

**Done (2026-10-29):** main `4840c5e9` (the cookie directive) on top of the first part, images run 40847216554, pinned
`sha-4840c5e`. Local image: Correspondence 78 (with the restart's expiry check). Pinned check (no `--dev-*`, fresh
`self install` under a short `STUDIOCTL_HOME`, `STUDIO_CA_BUNDLE` set; evidence `pinned2/`): golden diff only the tags,
Go `ok`, the Aspire package 198; default stack root 26 (1 skip), the front door 83 + `FRONTEND_DIST`, Authorization 31,
Resource Registry 43; `--with dialogporten --with correspondence`: Events 19 + 9 FAIL on the run straight after the
converge (registered times 62-64 s old, verify-after-sync 6.7) and 27 on two reruns, the adapter 39, Dialogporten 60,
Authentication 34, Correspondence 78; minimal root 23 (4 skips). Static: the four changed hosts build with 0 errors
(Debug and Release; one new warning, CS0162 after the early return in `OedRoleAssignmentWrapper`),
`vendor-sync.py check --all` ok with the shared copies identical, `Cookies.Smoke` 7 ok, `Otlp.Tests` 10. No component
here has unit tests of its own.

**Follow-up (the user, 2026-10-29, verbatim):** "Inline the borderline cases, and make a task for upstream changes to the
token generator package". Access Management's `LocaltestTokenGenerator` became two blocks in the vendored
`Altinn.Authorization.Integration.Platform/TokenGenerator.cs` (`TestTool`'s primary constructor takes the production
`IAccessTokenGenerator`; `CreatePlatformAccessToken(issuer, app)` returns its token first); the lib is compiled into
Access Management only. Events' `LocaltestEventsQueueClient` became a `NotSupportedException` at the top of each of the
four queue getters in `EventsQueueClient.cs` (`Enqueue*` answers `Success = false` with it, as the fake did). The OTLP
helper is inlined: the six `Program.cs` sites (Storage, Events, Profile, Notifications, Receipt, Correspondence) and the
two telemetry fakes (Authorization, the adapter) carry the endpoint check, `UseOtlpExporter()` after the environment
detector and the `HttpClient.Otlp` filter; its nine copies are deleted, `Otlp.Tests` compiles the front door's
`LocaltestOtlp.cs`, and `vendor-sync.py` needed no change (its copy check walks whatever `shared/fakes/` holds). The
upstream change is task 64, also an item in `localtest/upstreaming/access-management.md` (phase 1 total 52).

**Follow-up done (2026-10-29):** main `c42c24b6` (and its three parents), images run 40860589985, pinned `sha-c42c24b`.
Static: the nine changed hosts build with 0 errors in Debug and Release (new warnings: CS0162 once in `TokenGenerator.cs`,
four times in `EventsQueueClient.cs`), `vendor-sync.py check --all` ok, `Otlp.Tests` 10, `Cookies.Smoke` 7, Go `ok`, the
Aspire package 198. Local images, one `--dev-*` per `env up` with `--with monitoring` (evidence `followup/`): root 27
and Authorization 32 and the Resource Registry 44 with the new Access Management (its refresh re-imported 158 role
assignments from the Register with the locally signed token, no test-tool call in its log), Authorization 32, root 27
(Storage), Events 28, Notifications 65, Profile 53, Receipt 28, Correspondence 79, Events 28 / the adapter 40 /
Dialogporten 61, each suite's OTLP span check passing under the manifest name. Pinned check (no `--dev-*`, evidence
`pinned3/`): `--with monitoring`: root 27, the front door 85 + `FRONTEND_DIST`, Authorization 32, Resource Registry 44,
Events 28, Notifications 65, Profile 53, Authentication 33; `--with monitoring --with receipt`: Receipt 28;
`--with monitoring --with dialogporten --with correspondence`: Events 22 + 7 FAIL on the run straight after the converge
(6.7; the same on `sha-4840c5e` before the queue change) and 28 on two reruns, the adapter 40, Dialogporten 61,
Correspondence 79, Authentication 35; Grafana shows spans, metric series and log records under `access-management`,
`authorization`, `storage`, `events`, `notifications`, `profile`, `correspondence` and `dialogporten-adapter` (Receipt:
its suite's span check passed, the 15-minute count afterwards found one log record and no spans); default without
monitoring root 26, minimal root 23 (4 skips).

## 64. Local platform access tokens in the Integration.Platform token generator

**Superseded by [task 66](#66-a-test-tool-token-generator-mock-in-the-front-door) (2026-10-29).** The owner asked "Can
localtest just use a mock of the online token generator?" and chose the mock in the front door. The plan
(`/home/agent/code/.handover/task64-token-generator/plan.md`) stays as history; a local signing source may come back as
an optional idea for developers who run Access Management with `dotnet run`. The text below is the task as it was.

The user's request (2026-10-29, verbatim): "Inline the borderline cases, and make a task for upstream changes to the
token generator package".

An upstream change in [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth),
`src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/`:

- What (plan, option B, chosen by the owner 2026-10-29: `/home/agent/code/.handover/task64-token-generator/plan.md`;
  the earlier option A in `plan-option-a.md` beside it): `AddAltinnPlatformIntegrationDefaults`
  (`Extensions/ServiceCollectionExtensions.cs`) chooses the platform access token source: `TokenGenerator.KeyVault` when
  `Token:KeyVault:Endpoint` is set, otherwise `TokenGenerator.TestTool`, which fetches every token from the test tool on
  the internet. Add a third source, `TokenSource.Local`, selected by an explicit `Platform:Token:Local:Enabled=true`,
  allowed only when ServiceDefaults reports local development (`IsLocalDev`, fail-fast otherwise), refused together
  with a Key Vault endpoint, signing with the host's `IAccessTokenGenerator.GenerateAccessToken(issuer, app)` (the
  call `KeyVault.CreatePlatformAccessToken` already makes) with the certificate the client library's
  `AccessTokenSettings:AccessTokenSigningCertificateFullPath` names (no new path setting; the convention
  [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280) keeps), binding that section itself
  in this branch only, and throwing instead of sending a request without a token.
- Why: localtest has no Key Vault and must not call the test tool, so since task 63 two directive blocks in the
  vendored `TokenGenerator.cs` do it (the `TestTool` primary constructor takes the generator, and
  `CreatePlatformAccessToken(issuer, app)` returns its token first). The upstream source removes both, and the dummy
  `Platform:Token:TestTool:*` values Access Management's hook sets only to pass the options validation.
- Size: small: about 40 lines of library code, 150 lines of tests (11 cases in the plan) and a README section.
- Removes: the two `TokenGenerator.cs` blocks in `localtest/components/access-management/vendor/` and the
  `Platform:Token:TestTool:*` defaults in `fakes/LocaltestAccessManagementExtensions.cs` (and its
  `Configure<AccessTokenSettings>` line, once the source binds the section itself).
- Hosts that use the lib: in this repository only Access Management (`Altinn.AccessManagement` and
  `Altinn.AccessMgmt.Core` reference `Altinn.Authorization.Integration.Platform`); in altinn-auth check the other apps
  at the time (none of the vendored Authentication, Authorization or Resource Registry hosts compiles it at the pin
  `3d51810`).
- Related: [altinn-auth#4052](https://github.com/Altinn/altinn-auth/issues/4052) (Mockporten as the token generator);
  the item in [`localtest/upstreaming/access-management.md`](localtest/upstreaming/access-management.md), phase 1.

## 65. Docs audit fixes and Correspondence's Service host

The user's decisions (2026-10-29, verbatim): "Go through docs and code to find any assumptions and principles that have
since changed." (the audit: `/home/agent/code/.handover/audit-stale-principles/general.md` and
`components-and-code.md`, 79 findings: 17 wrong, 52 stale, 10 unclear), then "corresponcence needs dialogporten, so if
it does not that is a bug." and "Go ahead, with the Correspondence change to require dialogporten-service".

- Correspondence depends on `dialogporten-service` as well as the WebApi and Redis (not the GraphQL host or the
  adapter, which it does not use): the WebApi writes each dialog change to an outbox, and the Service host drains it
  and publishes the dialog's events to Events, so without it a correspondence's dialog never has events. The runtime
  starts the Service host after the WebApi is healthy (the hosts migrate at start) and Correspondence after the
  Service host. Correspondence's smoke test checks that its dialog's `dialogporten.dialog.created.v1` reaches Events.
- Every wrong and stale finding fixed in the docs and comments it names, no behaviour change beyond the item above.
  Dated records stay history: a superseded statement that reads as current got a dated note or was corrected. Task
  34's records filed under sections 41, 46 and 47, task 32's and 33's under 33 and 34, task 42's under 46 and task
  15's under 44 moved to their own sections.
- Unclear findings settled from the code and the records; one left for the owner (below).

**Done (2026-10-29):** main `23dc68e3` (manifest, golden, the Go and Aspire tests, the smoke check, the group docs),
then docs commits for the plans, the reviews, the stack docs, `localtest/upstreaming/`, the component READMEs and
`UPSTREAM.md` files, the code comments, and this file. On the pinned images (`sha-c42c24b`, no image change, so no pin),
`make dev-install` from the branch, `env up -r localtest-v2 --with correspondence` started exactly 17 containers: the
13 default plus `redis`, `dialogporten-webapi`, `dialogporten-service` and `correspondence` (no GraphQL host, no
adapter), in 26 s; Correspondence 79 `ok` (the event check included, twice), root 26 `ok` (1 skip, OTLP). With the
Service host stopped by hand the event check failed and the other 78 passed, so the check depends on it. Static: Go
build and test `ok`, `make lint` 0 issues, golden regeneration unchanged after the commit, the Aspire package 198,
CSharpier clean on the package, `Otlp.Tests` 10, `vendor-sync.py check --all` ok and `render --all` unchanged,
shellcheck clean on the changed scripts. The per-finding outcomes: `findings.md` in the evidence folder.

- Upstreaming index after the fixes: phase 1 52 (Profile's #1076 item added, Correspondence's merged party-id item
  deleted per "Marking progress"), done 6.
- Open: whether the rules every brief inherits (top of this file) should also state task 62's database principles
  (localtest owns the init scripts and users; the services migrate at start). `localtest/CONVENTIONS.md` states them as
  rules already; one sentence here if the owner wants them inherited.

## 66. A test-tool token generator mock in the front door

The owner's decisions (2026-10-29, verbatim): "Can localtest just use a mock of the online token generator?" then "go
ahead with the mock in the front door. Make a task so we can configure the url through environment variables"; for
the route "By the way, use /mockporten/api/..." and "I don't need to configure the url front-door answers on"; for
Access Management, after the lib was found to call the test tool at an absolute path, "Upstream fix, keep current",
then "I would like to upstream an environment variable that have the full path", then "Can't we just use the root /api
in front-door for now?". Task 64 is superseded.

- **The mock** (`localtest/localtest/TokenGenerator/`, README "Test tool"): `GET /mockporten/api/GetPlatformAccessToken`
  on the front door, a fixed route (the org `mockporten` cannot be registered as an app), on the public origin and as
  `http://localtest:8080/mockporten/…` inside the network; for now the same handler also at the root,
  `GET /api/GetPlatformAccessToken` (only that path; nothing else is at `/api/` in either mode). The real tool's
  interface (Altinn/AltinnTestTools `TokenGenerator/GetPlatformAccessToken.cs`, which altinn-auth#4052 plans for
  Mockporten): basic auth required (`401` with a Basic challenge without it, `400` when it does not decode), any value
  accepted; `app` required (the real tool's dotted-identifier rule) as `urn:altinn:app`; `ttl` seconds, default 1800
  and no maximum as at the real tool; `org` the issuer, `platform` (default, the real tool's and production's) or
  `ttd`; `env` ignored and logged. The token is what `Altinn.Common.AccessTokenClient`'s `AccessTokenGenerator` writes
  (RS256, `kid` thumbprint, `x5t`; `urn:altinn:app`, `nbf` now minus 5 s, `exp` now plus `ttl`, `iat`, `iss`, `aud`
  `platform.altinn.no`), signed with the shared certificate, answered as `text/plain`.
- **Access Management** fetches its platform access tokens from the mock: task 63's two `TokenGenerator.cs` directive
  blocks are gone and production's `TestTool` runs unchanged. Its settings moved from the fake's code to the manifest
  `envs`: `Platform__Token__TestTool__Endpoint` `http://localtest:8080` (the lib calls `{Endpoint}/api/…`, the root
  route), `__Username` and `__Password` `localtest`, `Environment` `localtest` (the host copies it into the test tool's
  environment), and `__PlatformAccessTokenUrl` the canonical URL (ignored by the lib at the pin); it depends on the front
  door. `Configure<AccessTokenSettings>` stays: its own clients call `IAccessTokenGenerator`.
- **Authorization's `TokenGeneratorSettings:Url`** (the full URL of the online tool, `appsettings.Development.json`) is
  not used locally: its `DevAccessTokenGenerator` is registered in Development and the fake replaces it with the
  production `AccessTokenGenerator` signing with the shared certificate, which production runs too. No other consumer
  of the test tool in the vendored hosts.
- **The upstream change**, pushed to the fork (the owner opens the PR):
  `feat(platform-integration): configure the full URL of the test tool's platform access token endpoint`, an optional
  `Platform:Token:TestTool:PlatformAccessTokenUrl` used as is when set (validation: `Endpoint` or it), 11 unit tests;
  `ivarne-agent/altinn-auth` branch `feat/test-tool-platform-access-token-url`, commit `826df92`, description
  `/home/agent/code/.handover/altinn-auth-upstream/test-tool-platform-access-token-url.md`; the item in
  [`localtest/upstreaming/access-management.md`](localtest/upstreaming/access-management.md), phase 1.
- **Follow-up**, when the upstream change is merged and synced: remove the front door's root `/api/GetPlatformAccessToken`
  route (and its tests and README paragraph) and the root use of `Platform__Token__TestTool__Endpoint`, and check that
  Access Management's log shows its calls at `/mockporten/api/GetPlatformAccessToken`.

**Done (2026-10-29):** main `db670995`..`8023057f`, images run 40887997111, pinned `sha-8023057`. Static: the front door's
tests 206 (25 new), its Release build 0 warnings; Access Management 0 errors in Debug and Release (8 warnings, the
CS0162 gone with the directive); `vendor-sync.py check --all` ok (Access Management 3 site files, 5 blocks); Go `ok`,
`make lint` 0 issues, the Aspire package 198. Local images (`--dev-localtest`, `--dev-access-management`,
`--with monitoring`, evidence `run2/`): Access Management's log shows 20 calls to
`http://localtest:8080/api/GetPlatformAccessToken`, all `200`, then the Register streams `200`; the refresh re-imported
158 role assignments; root 30 (the three new mock checks: no basic auth `401`, the token passes Storage's
`PlatformAccess`, a broken signature `401`), the front door 88 + `FRONTEND_DIST`, Authorization 32, Resource Registry
44, Register 15; minimal: root 26 (4 skips), the front door 95 + `FRONTEND_DIST`. Pinned check (no `--dev-*`, evidence
`pinned/`): golden diff only the tags; `--with monitoring`: Access Management's mock calls `200`, refresh 158, root 30,
the front door 88 + `FRONTEND_DIST`, Authorization 32, Resource Registry 44, Register 15; minimal root 26 (4 skips).

## 67. File-based Maskinporten token source for apps on localtest

The owner's questions and decision (2026-10-29, verbatim): "The production version of apps gets maskinporten keys from a
volume mount that altinn operator manages. Is there any way we can run that code path in localtest?", "Is it feasable
to run the real operator code?" (answer: feasible only with a Kubernetes API, kind or a bare API server, and a sync of
the Secret out to the apps; not recommended by default; a contract test between the operator's writer, studioctl's
writer and the app libraries' reader covers the format drift), then "Add the file based token source to the TODO and
dispatch agents to fix all 4".

The four changes are those of task 43's review, "The switch to the proper fakes"
([`localtest/reviews/2026-10-23-app-maskinporten.md`](localtest/reviews/2026-10-23-app-maskinporten.md)):

1. **The runners write a local client** into the app's secrets directory (`RUNTIME_APP_SECRETS_DIR`, the file
   `RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME` names, the format the operator's `maskinporten-settings.json` and
   studioctl's `appsecrets` writer use) on every run against localtest-v2, unless the developer stored one: authority
   `<origin>/localtest/api/maskinporten/` (trailing slash), client id `<org>-localtest`, a generated RSA key. studioctl
   and the Aspire package alike. (67a)
2. **The exchange makes the app's org**: production Authentication's `exchange/maskinporten` turns the fake
   Maskinporten token for 991825827 into `ttd`, not `digdir`, through the Authentication fakes. (67a)
3. **The monorepo app libraries' `AuthenticationTokenResolver`** takes the real `MaskinportenClient` for `AltinnToken`
   whenever a client is configured, keeping the `/Home/GetTestOrgToken` short-cut only without one (the old localtest).
   A pull request to altinn-studio. (67b)
4. **app-lib 8**: the runners also set `AppSettings__RuntimeSecretsDirectory` for a process run (67a); the same
   resolver change in app-lib-dotnet, a pull request from the fork, which needs a release to take effect (67c).

Also in 67b: a contract test in the monorepo that the operator's Secret writer, studioctl's writer and the app
libraries' reader agree on the file name and format.

~~Open point, decided by default until the owner says otherwise: a client the developer stores (`studioctl app
maskinporten set`, a real test client) wins over the local one, and service-owner requests then fail at the local
exchange (`401`, the real token is not trusted); the runners say so when they see a stored client against localtest-v2.
Alternatives for the owner: a second file for the local exchange, or Authentication's fakes also trusting test
Maskinporten.~~ Settled by the owner (2026-10-29, verbatim): "And also, let the authorization faces accept any
envionments maskinporten keys (test or prod or local) Ideally by config in the manifest." A stored client still wins
over the local one, and now works: the stack's exchange trusts test and production Maskinporten too. No warning.

Key lifetime (the owner, 2026-10-29, verbatim): "If key rotation in localtest is hard, rather make the lifetime really
long." Nothing in the local client carries a validity (a bare JWK: no `x5c`, no dated key id, no expiry that the fake
or the operator's format checks), so the key is generated once per app and kept, and nothing rotates it. Rotation is
out of scope locally by the owner's decision; the operator's rotation stays covered by its own e2e test.

**67a done (2026-10-30)**, commits `2f8a249e`..`d3bbf4a2`, images run 40971523592, pinned `sha-d3bbf4a`. Pinned check
(no `--dev-*`, a fresh `self install`, `STUDIO_CA_BUNDLE` set; evidence `pinned/`): golden diff only the tags, Go `ok`,
the Aspire package 206; default stack root 29, the front door 86 + `FRONTEND_DIST`, Authentication 39, the monorepo
app's exchanged token `ttd` with Storage 200; minimal root 26 (4 skips), its exchange `ttd` for `ttd-localtest` and
`digdir` for another client.

- (1) **The runners write a local client.** studioctl (`internal/appsecrets/localclient.go`, called from
  `BuildDotnetRunSpec`/`BuildDockerRunSpec`, so `run` in both modes and `app env`) and the Aspire package
  (`LocalMaskinportenClient`, on the app resource's `BeforeResourceStartedEvent`, when an allocated public port is
  known) write `maskinporten-settings.json` against localtest-v2 only: authority `<origin>/localtest/api/maskinporten/`
  (follows `--host`/`--port`, `PublicHost`/`PublicPort`), client id `<org>-localtest`, a 2048-bit RSA JWK with all
  twelve fields, generated once per app and reused (rewritten only when the origin moves), owner-only and atomic. A
  developer's client is never overwritten (an unreadable file neither); `show`, `remove` and `doctor` treat the local
  one as no client (recognised by the `-localtest` id and the fake's path), `set` replaces it. Shared golden file
  `internal/appsecrets/testdata/local-maskinporten.golden.json` (a placeholder key: no private key committed).
- (4, runner part) **app-lib 8**: both runners set `AppSettings__RuntimeSecretsDirectory` to the secrets directory
  (a process run) or `/mnt/app-secrets` (a container run) on every run; golden `app-environment.golden.json`.
- (2) **The exchange makes the app's org**, by a directive, not a fake: `OrgIsDigDirAndTestIsTrue(org, test ||
  <client_id ends in -localtest>)` in `AuthenticateMaskinportenToken`. Not the `IOrganisationsService` decorator: that
  would also have made the components' own clients of 991825827 (Dialogporten, the adapter, Correspondence, the shared
  fake's `UseAltinnTestOrg=false` check) `ttd`, where they act as `digdir` by design. Minimal mode's emulated exchange
  has the same rule. An app of another org still gets `ttd` (the fake issues 991825827 for any client id that is not
  nine digits).
- **Trusted Maskinporten instances (the owner's addition).** Production Authentication has exactly two settings
  (`MaskinportenWellKnownConfigEndpoint` and the alternative), validates against the first's keys, then the second's,
  and checks the issuer with `Contains` on both; three need code. Two more blocks in the same method: the token's
  unvalidated `iss` picks one endpoint of `Localtest:Authentication:MaskinportenWellKnownEndpoints` (production's
  `Contains` rule; `fakes/Maskinporten/LocaltestMaskinportenIssuers.cs`), whose keys alone validate it, and the issuer
  check uses that endpoint. The manifest sets the list on the `authentication` container (`__0` the fake on
  `{{origin}}`, `__1` `https://test.maskinporten.no/.well-known/oauth-authorization-server`, `__2`
  `https://maskinporten.no/.well-known/oauth-authorization-server`); without it the fake alone. Egress: Authentication
  now also reaches `test.maskinporten.no` and `maskinporten.no` (task 44's inventory), only when such a token arrives
  (no cache). Offline: only the chosen endpoint is fetched, so the fake's tokens do not depend on the internet ones (by
  construction; the smoke test shows the fake's tokens exchanging after the test and production refusals; a run without
  the internet was not made). A real token becomes the organisation `altinn-orgs.json` (altinncdn.no) names for its
  `consumer` organisation number, exchanged without `test=true` as the apps call it: Digdir's clients `digdir` (so a
  `ttd` app with a Digdir test client acts as `digdir`, as production does without `test=true`), a service owner's own
  client that service owner. Upstreaming item: a list setting (`localtest/upstreaming/authentication.md`).
- **Other components that validate Maskinporten tokens themselves** keep the fake only: Dialogporten's WebApi and
  GraphQL (`JwtBearerTokenSchemas` is a configurable list, but `TokenIssuerCache` fetches every scheme's metadata in one
  initialisation, so one unreachable internet instance would fail every scheme's tokens offline; adding two schemes
  needs a directive there first), Correspondence (one `MetadataAddress` for its Maskinporten scheme, and upstream
  refuses `maskinporten.no` issuers there on purpose, asking for an exchanged token). Access Management's Development
  settings name test Maskinporten as an OIDC provider; not examined. Apps call the platform with exchanged Altinn
  tokens, so the exchange is the one that matters for a developer's client.
- Docs: `localtest/CONVENTIONS.md` ("The local Maskinporten client", "Which Maskinporten tokens the stack trusts"),
  the manifest README, studioctl's `docs/localtest-v2.md` ("Maskinporten"), CHANGELOG and README, the bundled skill,
  the Aspire README, Authentication's README, `vendor/UPSTREAM.md` and `localtest/upstreaming/authentication.md`.
- Proven on the stack (2026-10-30, studioctl from the branch, `--dev-authentication`; evidence
  `/home/agent/code/.handover/task67a/`): the monorepo app `expression-validation-test` (altinn-studio `d8848b118f`)
  and app-lib 8 (`ttd/testapp`, 8.12.15), each with a scratch probe, at the default origin and at `--host
  local.altinn.test --port 18000`: both start; `GetAccessToken` gets the fake's token (`client_id` `ttd-localtest`);
  `GetAltinnExchangedToken` an Altinn token with `urn:altinn:org` `ttd` and Storage 200; instantiation 201 (the
  monorepo app's workflow-engine callbacks to Storage 200). The resolver takes the real client at the custom host
  (`client_id` `ttd-localtest`, `ttd`, Storage 200) and the `/Home/GetTestOrgToken` short-cut at the default origin
  until 67b (monorepo) and 67c (app-lib 8, which needs a release) land. A stored developer client
  (`https://test.maskinporten.no/`, unregistered) stayed byte for byte, without a warning, and the app used it (test
  Maskinporten answered `invalid_grant`, as expected for an unregistered client); `remove` and the next `app env`
  brought the local client back. Root 29, front door 86 + the known `FRONTEND_DIST` failure, Authentication 39 at both
  origins, shared all ok. A real test or production token was not available (no credentials): its acceptance is
  unverified; the refusal of a foreign key under each issuer, each checked against that instance's own live keys, is in
  Authentication's log.

## 68. The Access Management GUI (am.ui) in the stack

The owner's question and request (2026-10-30, verbatim): "Are we running access-management gui?" (answer: no; the stack
runs Access Management's API, the vendored altinn-auth host, and no frontend), then "yes, write it up as a task".

**Today.** The manifest has no container for the GUI. In Altinn it is a separate app, `am.ui.<env>`, from
[`Altinn/altinn-access-management-frontend`](https://github.com/Altinn/altinn-access-management-frontend) (main
`76c599d2` on 2026-10-30). Arbeidsflate's links to it go to production `am.ui.altinn.no`
([`planned-components/dialogporten/arbeidsflate.md`](planned-components/dialogporten/arbeidsflate.md), row 5, patch
0002 keeps them); Access Management's consent links (`https://am.ui.{Hostname}/…`,
[`planned-components/service-discovery.md`](planned-components/service-discovery.md)) name a host the stack does not
serve; studioctl leaves the apps' `AccessManagementChangeAndRedirectUrl` unset (`internal/cmd/app/env.go`,
`localtest/CONVENTIONS.md`). Delegations and roles can be shown only through the API or the seeded test users' roles.

**What upstream ships** (read from the repository, not yet tried):

- One image (`Dockerfile`): a Vite build of the React SPA served from `wwwroot/accessmanagement/` by a .NET 10 BFF,
  `Altinn.AccessManagement.UI`, on port 80. The repository also has `Dockerfile.runner`, not examined.
- The BFF's `PlatformSettings` name the platform APIs it calls: Access Management, Authentication, Authorization,
  Profile, Register, Dialogporten and the Resource Registry, plus `OpenIdWellKnownEndpoint`; the login is the
  `AltinnStudioRuntime` cookie (`JwtCookieName`), so the stack's Authentication cookie on `.local.altinn.cloud` should
  carry over. `GeneralSettings` has `FrontendBaseUrl`, `Hostname` and `AllowedRedirectDomains` (`altinn.no`,
  `altinn.cloud`, which covers `local.altinn.cloud`).
- `ClientSettings` (issuer `amui`, app `access-management-ui`, certificate `JWTCertificate` from Key Vault): it signs
  platform access tokens of its own, which the stack's components must accept (the shared signing certificate, or the
  issuer added to their trusted issuers).
- `MockSettings`: about twenty switches that replace each platform client with canned data (`AccessManagement` is on in
  `appsettings.Development.json`); localtest wants them all off, so the GUI reads the stack's data.
- `FeatureManagement` flags: off in `appsettings.json`, on in Development; to choose.
- Other clients not in `PlatformSettings` (the Altinn CDN, Key Vault, the ID-porten authorisations, the system register):
  to find.

**Work.**

1. **Research** (its own document under `planned-components/access-management-ui/`, the house style of the other
   plans): build the image as is and run it against the stack (`--dev-*` is not available for a container the manifest
   lacks, so a scratch manifest entry or a plain `podman run` on `localtest-v2_network`); list every outbound call, the
   settings each takes, which work against the stack unchanged and which need a directive or a fake under the rules
   above; the token issuer question; the routes it serves and whether they fit under `am.ui.<host>`; memory (the
   heaviest containers of the full stack use about 300 MB each, and the 8 GiB machine had about 2.5 GB available with
   `--with everything` on 2026-10-30); the build time and the image size; whether the SPA hard-codes hosts the way
   Arbeidsflate's `url.ts` does.
2. **The component**, after the owner approves the research: `localtest/components/access-management-ui/` with the
   vendored source, `vendor/UPSTREAM.md`, an image in the images workflow; a manifest container in a new optional group
   (`access-management-ui`, depending on `access-management` and what the research shows); the front door's subdomain
   route `am.ui.<host>` (which also needs `studioctl env hosts add` and the Aspire package's hosts, like `af.<host>`);
   a smoke test (the GUI loads for a test user, lists the user's parties and roles, one delegation round-trip shown in
   the API afterwards).
3. **The links**: Arbeidsflate's to Access Management go to `am.ui.<host>` when the group runs (a change to patch 0002,
   or a directive); Access Management's consent links follow `{Hostname}`; studioctl sets the apps'
   `AccessManagementChangeAndRedirectUrl` and the Aspire package does the same, when the group runs.
4. Docs: the group in studioctl's `docs/localtest-v2.md`, the manifest README, `CONVENTIONS.md` (hosts and links), the
   front door README, the Aspire README, CHANGELOG, and the upstreaming list for anything the GUI needs changed
   upstream.

Open questions for the owner: whether the GUI belongs in `--with everything`; whether the feature flags follow
production (off) or Development (on).
