# TODO

The open work on the new localtest, gathered from the "Not yet" paragraph of [`localtest/README.md`](localtest/README.md),
the components' "Known gaps", [`localtest/reviews/`](localtest/reviews/README.md) and the plan indexes under
[`planned-components/`](planned-components/). Each task is written so it can become a subagent's brief once approved.

Status values: **unapproved** (not yet discussed, or discussed and not yet released to an agent), **approved**
(design settled, an agent may be dispatched), **deferred** (discussed, parked until something needs it), **won't do** (discussed and dropped),
**in progress** (agent name, worktree, branch), **done** (commit).
Nothing is dispatched while it is unapproved.

Rules every brief inherits: work in a worktree of this repository under `/home/agent/code/.worktrees/<task>/`; a
change to a `vendor/` file is only ever a `#if !LOCALTEST` (production first) / `#else` / `#endif` block, everything
else goes in `fakes/`; a component's Dockerfile mirrors upstream's with `localtest:` deviations; the old localtest in
`altinn-studio/src/Runtime/localtest` is reference only, never a basis; one image build or AOT publish at a time on
this machine (7.8 GB RAM), never next to a Cypress run; extend the component's `smoke-test.sh`; update the component
README and `vendor/UPSTREAM.md`; no `Co-Authored-By` trailers. The studioctl side of a
component (container spec, image pin, proxy prefix, provisioning calls, health) is changed in the vendored studioctl
source under `localtest/studioctl/src/cli/` (ordinary commits on top of the baseline, see
`localtest/studioctl/UPSTREAM.md`), built with `make dev-install` there and verified with
`studioctl env up -r localtest-v2 --dev <component>`; nothing is edited in an altinn-studio checkout.

**Optional component groups (decided 2026-10-20).** `localtest/studioctl/src/cli/internal/cmd/env/localtestv2/groups.go`
becomes a group table: each group has a default (all off) and the groups it implies; `--with x` enables `x` and,
transitively, what it implies; `--without` removes only the named group. Groups: `dialogporten` (Redis, the three
Dialogporten hosts, the adapter from task 10) implies nothing; `receipt` implies nothing; `correspondence` implies
`dialogporten`; `everything` implies every other group. Task 16 lands the table (its first commit, merged ahead of the
rest); 7 and 10 build on it. **Front door for components that are not running (decided 2026-10-20, in task 16's
studioctl step):** `env up` writes every known component's route, enabled or not, and studioctl-server answers `503`
with an RFC 9457 ProblemDetails (`application/problem+json`; a small HTML page when `Accept` prefers `text/html`)
naming the component and group and the exact `studioctl env up -r localtest-v2 --with <group>` command (and
`--with everything`); an enabled component that does not answer gets the same shape pointing at `studioctl env logs`.
A path matching no route still falls through to the localtest app.

| # | Task | Status | Size | Needs the stack |
| --- | --- | --- | --- | --- |
| 1 | [Vendor sync check](#1-vendor-sync-check) | deferred (2026-10-20) | small | no |
| 2 | [Authentication as an OIDC provider, logout, `pid`](#2-authentication-as-an-oidc-provider-logout-pid) | done (2026-10-20, commits 4b9b5f7..ddd0ecf; code-flow tokens carry `pid`, Dialogporten end-user API 200; pin sha-a3f62a8) | medium | yes |
| 3 | [Local altinncdn.no](#3-local-altinncdnno) | won't do (2026-10-20: the stack may read altinncdn.no) | small | yes |
| 4 | [Maskinporten token endpoint and the shared `IMaskinportenService` fake](#4-maskinporten-token-endpoint-and-the-shared-imaskinportenservice-fake) | done (2026-10-20, commit 798f18e) | small | yes |
| 5 | [Resource Registry component](#5-resource-registry-component) | done (2026-10-20: 5a commits e0cba40..b6f4c47, pin a3422419; 5b commits 9b7278d..aaa099e) | large | yes |
| 6 | [Notifications](#6-notifications) | done (2026-10-20, commits 45e99a1..0b38452, pin a4aa959; follow-up: seed data for SMS numbers and organisation addresses, see section 6) | medium | yes |
| 7 | [Correspondence](#7-correspondence) | done (2026-10-21, commits 94c1acb..e96f904; group `correspondence` implying `dialogporten`, port 8033; 7b done the same day closed the 6-digit party-id gap with one directive site, see `planned-components/correspondence/party-ids.md`; pin sha-735e5d2, proven 2026-10-21) | medium | yes |
| 8 | [Inspection GUI](#8-inspection-gui) | deferred (2026-10-20: possible overlap with Altinn Studio's admin of running services, see task 17; plan in `planned-components/inspection-gui/`) | medium | yes |
| 9 | [Dialogporten core](#9-dialogporten-core) | 9a done (2026-10-20, commits 4ab03f0..76b8787, pin ec333e4); 9b done (2026-10-20, commits 4749bd7..d7827bf, pin sha-d7827bf) | large | yes |
| 10 | [Dialogporten adapter and the Storage outbox](#10-dialogporten-adapter-and-the-storage-outbox) | done (2026-10-21, commits c940fd6..3c9b03f; adapter in the dialogporten group, Storage outbox on in the default stack, one `#if !LOCALTEST` site added in Dialogporten for http URLs; pin sha-3c9b03f, proven 2026-10-21 with `--with everything`) | large | yes |
| 11 | [Arbeidsflate](#11-arbeidsflate) | done (2026-10-21, commits dc1ae37..b136eabf; group `arbeidsflate` implying `dialogporten`, host `af.local.altinn.cloud:8000`, nginx 8037 and BFF 8038, two build-time patches on an unmodified vendor tree, 56-check smoke test, inbox verified in Chromium; 11b done the same day (Correspondence attachments from the inbox, commits 789edcd3..8e510e91, one directive site in Correspondence, see section 11); pin sha-7f21882, proven 2026-10-21 on plain images with `--with correspondence`: root, authentication, correspondence and dialogporten smoke tests pass) | large | yes |
| 12 | [Review leftovers](#12-review-leftovers) | done for the audit findings (2026-10-21, commits 6ba99ee..90c6bc4: Postgres in UTC, two Events directive sites for Dialogporten sources, authorization and root smoke tests fixed, Storage standalone test removed, `reviews/test-apps.md`; still open: HEAD on the localtest app, a WebSocket test, the Events reset not draining queues, Slack deliveries without their event, the Altinn 2 roles stub; pin sha-3143730, proven 2026-10-21 on plain images) | small each | mostly |
| 13 | [studioctl integration leftovers](#13-studioctl-integration-leftovers) | unapproved | medium | yes |
| 14 | [Profile beyond the Register](#14-profile-beyond-the-register) | done (2026-10-21, commits f7b7f50..097b474; production Profile in the default stack on port 8036, `/profile` off the Register, two additive hooks; pin sha-0485d0a, proven 2026-10-21 on plain images) | unknown | yes |
| 15 | [Localtest URLs in the Maskinporten client package](#15-localtest-urls-in-the-maskinporten-client-package) | unapproved | small | no |
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
| 34 | [`studioctl run --mode container` against localtest-v2](#34-studioctl-run---mode-container-against-localtest-v2) | to discuss (owner 2026-10-23: "make a task to make studioctl run --container work"); task 32 refuses the mode up front today | medium | yes |
| 35 | [minimal mode phase 1](#35-minimal-mode-phase-1) | approved (owner 2026-10-23: "draft the brief for phase 1 of the minimal mode and send it to an agent for implementation; the minimal implementations stay isolated in a separate folder"); brief `/home/agent/code/.briefs/task35-minimal-mode-phase1.md`; **done, merged** 2026-10-23 (ff to 6d0adddc): minimal mode proven on the stack, default stack unchanged; follow-ups: remember the mode after `env down`, Aspire `WithMinimal` (spec phase 2), pin bump | large | yes (phase 2) |
| 36 | [Anonymous volumes removed with their containers](#36-anonymous-volumes-removed-with-their-containers) | approved (owner 2026-10-23: "dispatch this task"); brief `/home/agent/code/.briefs/task36-anonymous-volumes.md`; **done, merged** 2026-10-23 (ff to 48648ecc); the Storage and localtest images carry the Dockerfile change at the next pin bump | small | yes (phase 2) |
| 37 | [Aspire minimal mode](#37-aspire-minimal-mode) | approved (owner 2026-10-23: "dispatch the Aspire minimal mode task"); brief `/home/agent/code/.briefs/task37-aspire-minimal.md`; API decision: `builder.AddAltinnLocaltestMinimal()` with no extension that enables another service; **done, merged** 2026-10-23 (ff to 2976956a): `AddAltinnLocaltestMinimal()` proven under Podman on sha-48648ec | medium | yes (phase 2) |
| 38 | [Memory statistics of the stack](#38-memory-statistics-of-the-stack) | approved (owner 2026-10-23: "dispatch an agent to grab some stats"); brief `/home/agent/code/.briefs/task38-memory-stats.md`; **done, merged** 2026-10-23 (`localtest/reviews/2026-10-23-memory.md`, `localtest/tools/memory-stats.sh`) | small | yes |
| 39 | [Configurable public host names and port](#39-configurable-public-host-names-and-port) | approved (owner 2026-10-23: "dispatch"); brief `/home/agent/code/.briefs/task39-configurable-origin.md`; **phase 1 verified, phase 2 running** (2026-10-23; one regression to fix first: the localtest test suite took 17 min) | large | yes (phase 2) |

Suggested order: 21a, 21b, 21c running (stack: 21a, then 21c, then 21b); then 21d and 21e; 8 when decided. 13 stays with the maintainer; 12 is a filler.

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
(watch, restart or `studioctl app sync`), frontend version override and cookie-domain rewriting (port or drop), and
two front-door gaps seen on 2026-10-20: studioctl-server answers 404 for a platform prefix without its trailing slash
(`/storage`), where the localtest app redirects, and for `/app-frontend/`. The maintainer's; not for a subagent until
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

## 33. Minimal in-memory emulation in the localtest image

**Goal (2026-10-23, owner's request).** People complain about the number of containers the default stack starts.
Research how the localtest image could carry a truly minimal emulation of the platform services so that one container
serves an app: a simple Register from our seed data, a PDP that always permits, Notifications in an in-memory list, and
whatever else the app cannot run without (Profile, Authentication's tokens, in-memory Storage, a PDF stub), all in
memory. Reference for what is sufficient: the monorepo's `src/Runtime/localtest` (the old runtime's emulation, 36k
lines, file-backed, MVC) and app-lib's clients. The document decides how the front door picks emulation versus a real
service, how registration provisions into memory, what fidelity is lost, how studioctl and Aspire expose it, reuse
versus rewrite, and a phased plan. Deliverable `planned-components/minimal-emulation/README.md`; no code, no stack.

**Owner decision 2026-10-23 (flag form).** The owner does not like `--dev NAME[=PATH]` and does not see the
`--dev-workflow-engine` clash as a reason: the per-container form `--dev-<container>[=<path>]` comes back in a phase-B
fix commit. `--dev-workflow-engine` stays one flag whose meaning follows the runtime (upstream's hidden host-process
routing under `-r localtest`, a container build under `-r localtest-v2`); `env up` refusing the other runtime's flags
keeps the two meanings apart. The agent was told during phase B.

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
