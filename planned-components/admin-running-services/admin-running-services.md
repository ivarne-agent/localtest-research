# Admin of running services and the inspection GUI: overlap, data path and recommendation

**Superseded 2026-10-23 by task 26:** the route table is no longer studioctl-server's: the localtest container lists it (`GET localtest/api/routes`) and the registered apps (`GET localtest/api/apps`); see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

Research date 2026-10-20 (Europe/Oslo), task 17 of [`../../TODO.md`](../../TODO.md). Written against `Altinn/altinn-studio`
`origin/main` at `9493f65300` (2026-10-20 14:03), `Altinn/altinn-studio-docs` `origin/master` at `dd8bba13`, this
repository at `94a0f93`, and GitHub issues and pull requests read with `gh` the same day. Nothing was built, run or
posted. Monorepo paths below are relative to the monorepo root (`altinn-studio/`); `A/` stands for
`src/Designer/frontend/admin/`, `D/` for `src/Designer/backend/src/Designer/`.

The question: Altinn Studio's administration of running services ("admin av kjørende tjenester": the Designer `admin`
frontend and its backend) and the planned inspection GUI of this stack (task 8,
[`../inspection-gui/`](../inspection-gui/README.md)) both show apps and instances. Do they overlap, and could the admin
frontend be pointed at the localtest stack instead of building a separate page?

Contents:

1. [What the admin frontend does today and what is planned](#1-what-the-admin-frontend-does-today-and-what-is-planned)
2. [Its data path and credentials](#2-its-data-path-and-credentials)
3. [Could it be pointed at the localtest stack?](#3-could-it-be-pointed-at-the-localtest-stack)
4. [Overlap matrix](#4-overlap-matrix)
5. [Recommendation, effort and risks](#5-recommendation-effort-and-risks)
6. [Open questions for the product owner](#6-open-questions-for-the-product-owner)
7. [Not verified](#7-not-verified)

## 1. What the admin frontend does today and what is planned

### 1.1 The application

A React 19 single-page app in the Designer frontend workspace (`A/package.json`; Vite dev server on port 2006 from
`src/Designer/frontend/ports.json`, served under `/admin` by `ADMIN_BASENAME` in
`src/Designer/frontend/packages/shared/src/constants.js`). It is not standalone: it imports `app-shared` (user and
organisation queries, WebSocket hub, consent, PostHog), `@studio/components` (the Designsystemet wrappers) and the
`language` package; all 78 `admin.*` texts are Norwegian only (`language/src/nb.json`; `en.json` has none). The
README is a single heading.

Routes (`A/routes/PageRoutes.tsx`, `A/routes/routes.tsx`, `A/routes/RoutePaths.ts`), all under `/admin/{org}/`:

| Route | Page | What it shows | Calls (`A/features/apps/utils/apiPaths.ts`) |
| --- | --- | --- | --- |
| `/admin/` | `IndexRedirect` | Redirects to `/admin/{user.login}`; the user's own name shows "no org selected" (`NoOrgSelected`) | `designer/api/user/current` |
| `/admin/{org}` (`OrgPageLayout`) | layout | Checks the user is a member of `{org}` in Gitea; opens the alerts WebSocket hub, which invalidates the error-metric queries when an alert for an environment arrives (`WebSocketSyncWrapper`) | `designer/api/user/current`, `designer/api/orgs` |
| `apps` | `Apps`, `AppsTable` | One tab per environment the org has (sorted production, tt02, at22, at23, at24, yt01) with the deployed apps, their version (image tag), a search, and per app the failed-request counts over a chosen range (`TimeRangeSelect`, default 1440 min) linking to the logs | `GET designer/api/v1/admin/applications/{org}`, `GET .../metrics/{org}/{env}/errors?range=` |
| `apps/{env}/{app}/` | `AppsDetails` | **App info** card: environment, app version, app-lib and frontend version, commit (link to Gitea), publish date, published by (`AppInfo`). **Metrics** card: ready pods, failed requests per type, request counts as Chart.js bars (`AppMetrics`). **Instances** card embedded (below) | `.../applications/{org}/{env}/{app}`, `.../metrics/{org}/{env}/app/health`, `.../app/errors`, `.../app` |
| (embedded in the app page; the `Instances` route constant has no route of its own) | `Instances`, `InstancesTable` | Instance list, ten per page with "fetch more" (continuation token): id, created, current task, status tag (unread, active, completed, confirmed, deleted: `InstanceStatus`). Filters bound to the query string: archive reference or instance id, current task (from the process), completed, confirmed, deleted, created more than 14/30/60 days ago. 403 says "missing rights", 404 "unavailable in this environment" | `.../instances/{org}/{env}/{app}?continuationToken&currentTask&isArchived&archiveReference&confirmed&isSoftDeleted&isHardDeleted&createdBefore`, `.../applications/{org}/{env}/{app}/process-metadata` |
| `apps/{env}/{app}/instances/{instanceId}` | `InstanceDetails`, `InstanceDataView` | Status and timestamps (created, completed, confirmed, deleted, last changed); **data elements grouped by data type**, labelled with icon and process task from the app's metadata and BPMN tags (data model, PDF, signature, signee states, payment, attachment), each with created, changed, locked, size, content type, file-scan result; a **Delete** button (soft delete, `confirm()`) | `.../instances/{org}/{env}/{app}/{id}` (GET, DELETE), `.../application-metadata`, `.../process-metadata` |

Not shown in the admin frontend but part of the same feature in the backend and other Designer packages: alert rules
(`D/Controllers/Admin/AlertsController.cs`; Grafana alerts go to Slack and to the org's contact points, which are
edited in the `settings` package, `contact-points` paths in `packages/shared/src/api/paths.js`), an admin audit log
(`AuditLogsController`, owners only), and reports by e-mail (`ReportsController`).

### 1.2 Roadmap and status

- **Epic [altinn-studio#14876](https://github.com/Altinn/altinn-studio/issues/14876) "Administrasjon av publiserte
  tjenester"** (open, label `area/admin`, squad Kjøring), from the roadmap item "Administrasjon av kjørende tjenester"
  (digdir/roadmap#418). Audience: **service owners** (people responsible for operating apps) and Altinn internally.
  Jobs: overview of running apps and instances, control without involving Altinn, finding and fixing errors in running
  instances; access control per role with everything logged. Status: planning and insight done, design and
  development in progress.
- Delivered (closed): list running apps ([#15637](https://github.com/Altinn/altinn-studio/issues/15637)), list
  instances ([#15638](https://github.com/Altinn/altinn-studio/issues/15638)), instance details
  ([#15639](https://github.com/Altinn/altinn-studio/issues/15639), V2 single instance page
  [#16407](https://github.com/Altinn/altinn-studio/issues/16407)), metrics
  ([#16397](https://github.com/Altinn/altinn-studio/issues/16397)), alerts
  ([#16396](https://github.com/Altinn/altinn-studio/issues/16396), [#16676](https://github.com/Altinn/altinn-studio/issues/16676)).
  [#16271](https://github.com/Altinn/altinn-studio/issues/16271) decided a dedicated Storage API for admin
  (`storage/api/v1/studio/instances/`) that leaves out person numbers and other possibly sensitive fields.
- In flight: workflow-engine health in the instance views, a failures view, a drill-down and `resume`/`abandon`
  actions for v9 apps ([#20275](https://github.com/Altinn/altinn-studio/issues/20275),
  [#20204](https://github.com/Altinn/altinn-studio/issues/20204); open PR stack
  [#20233](https://github.com/Altinn/altinn-studio/pull/20233) → [#20234](https://github.com/Altinn/altinn-studio/pull/20234)
  → [#20235](https://github.com/Altinn/altinn-studio/pull/20235) → [#20236](https://github.com/Altinn/altinn-studio/pull/20236),
  all updated 2026-10-20); audited reads of workflow state
  ([#21028](https://github.com/Altinn/altinn-studio/issues/21028)); notification delivery checks for v9
  ([#21262](https://github.com/Altinn/altinn-studio/issues/21262)).
- Ideas and backlog: V3 list ([#19320](https://github.com/Altinn/altinn-studio/issues/19320): more alert types, errors
  per instance, versions in the app list, faster production instance queries, better filters, application logs in
  admin instead of Application Insights), restart a deployed app
  ([#19699](https://github.com/Altinn/altinn-studio/issues/19699)), SLA view
  ([#19387](https://github.com/Altinn/altinn-studio/issues/19387)), status.digdir.no
  ([#16148](https://github.com/Altinn/altinn-studio/issues/16148)), the access model
  ([#15282](https://github.com/Altinn/altinn-studio/issues/15282), open: is `Admin-{env}` the right gate),
  documentation of the access needed ([#21006](https://github.com/Altinn/altinn-studio/issues/21006)).
- Documentation: none yet. The v9 "getting started for service owners" page
  (`altinn-studio-docs/content/altinn-studio/v9/getting-started/serviceowner/_index.nb.md`) lists "Sjekk status på
  tjenester i kjøretidsmiljø" under "For deg som skal vedlikeholde og administrere kjørende tjenester" with an empty
  link; the `Admin-{env}` teams are not in the access-management reference.

Nothing in the issues, the docs or the code mentions localtest, a local environment or app developers as an audience.
The only local reference in the Designer backend is a comment about ttd's org number
(`D/Services/Implementation/EnvironmentsService.cs`).

## 2. Its data path and credentials

### 2.1 Endpoints

Every admin call goes to the Designer backend (`D/Controllers/Admin/`, routes `designer/api/v1/admin/...`), which
calls one of three upstreams per environment:

| Designer endpoint | Authorization in Designer | Upstream call | Upstream credential |
| --- | --- | --- | --- |
| `GET applications/{org}` (`ApplicationsController.GetApplications`) | `[Authorize]` (any signed-in user) | For each environment the org has (`environments.json` intersected with `altinn-orgs.json`, both from altinncdn.no): **runtime gateway** `GET {appCluster}/runtime/gateway/api/v1/deploy/origin/{originEnv}/apps`; the gateway lists Flux `HelmRelease`s labelled for the org (`src/Runtime/gateway/.../HandleListAppDeployments.cs`) | **Maskinporten** client token (`MaskinportenClientForRuntime`, scope `altinn:studio/gateway`), only outside `Development` (`D/TypedHttpClients/RuntimeGateway/ServiceCollectionExtensions.cs`) |
| `GET applications/{org}/{env}/{app}` (`GetApplicationDetails`) | `[Authorize]` | Gateway `deploy/apps/{app}/{originEnv}` (image tag, build id); **Designer's own database** (the succeeded release for that tag, the deployment row: created, created by, environment); **Gitea** `App/views/Home/Index.cshtml` at the release commit (frontend version); the app's `applicationmetadata` (app-lib version) | Maskinporten (gateway); the user's Gitea session (Gitea); none (app) |
| `GET applications/{org}/{env}/{app}/application-metadata`, `.../process-metadata` | `[Authorize]` | **The app itself**: `{appCluster}/{org}/{app}/api/v1/applicationmetadata` and `/api/v1/meta/process` (BPMN parsed in Designer, `D/Services/Implementation/AppResourcesService.cs`) | none (anonymous app endpoints) |
| `GET metrics/{org}/{env}/errors`, `.../app`, `.../app/errors`, `.../app/health` (`MetricsController`) | `MustHaveAdminPermission`: the user is in Gitea team **`Admin-{env}`** of `{org}` (`D/Infrastructure/Authorization/AdminPermissionHandler.cs`) | Gateway `metrics/...`: **Azure Monitor / Log Analytics** queries for failed requests and request counts, **Kubernetes** ready-pod count for health (`src/Runtime/gateway/.../HandleMetrics.cs`, `AzureMonitorClient.cs`, `PodsClient.cs`) | Maskinporten |
| `GET instances/{org}/{env}/{app}`, `GET .../{instanceId}` (`InstancesController`) | `MustHaveAdminPermission` (`Admin-{env}`) | **Platform Storage** `GET {platformUrl}storage/api/v1/studio/instances/{org}/{app}` and `/{instanceGuid}` (`D/TypedHttpClients/AltinnStorage/AltinnStorageInstancesClient.cs`; `PlatformSettings:ApiStorageInstancesUri` in `D/appsettings.json`) | **Designer's platform access token** (issuer `studio`, `dev-studio` or `staging-studio` by host name, app `studio.designer`, signed with Designer's certificate) exchanged at `{platformUrl}authentication/api/v1/exchange/altinnstudio` (`PlatformBearerTokenHandler`, `AltinnAuthenticationClient`), plus an APIM subscription key per environment (`PlatformSubscriptionAuthDelegatingHandler`). Storage requires `urn:altinn:app=studio.designer` (`POLICY_STUDIO_DESIGNER`) |
| `DELETE instances/{org}/{env}/{app}/{instanceId}` | `MustBeOrgOwner` (Gitea `Owners`) | Admin audit log row in Designer's database first, then Storage `DELETE storage/api/v1/studio/instances/{org}/{app}/{guid}` (soft delete, an instance event "soft deleted by app owner via Altinn Studio") | as above |
| `GET alerts/{org}/{env}`; `POST` from the gateway | `Admin-{env}`; the `POST` takes a Maskinporten token from the gateway | Gateway `alerts` → **Grafana** alert rules | Maskinporten |

The user's identity throughout is the Designer session (Gitea OIDC, or Ansattporten behind a feature flag); there is
no per-user platform token. Designer acts towards Storage with its own `studio.designer` identity and towards the
cluster with its Maskinporten client; the user's rights are decided only in Designer, from Gitea team membership.

### 2.2 How environments are found

`EnvironmentsService` reads `GeneralSettings:EnvironmentsUrl` (altinncdn.no `config/environments.json`: name,
`platformUrl`, `hostname`, `appPrefix`) and `OrganizationsUrl` (`altinn-orgs.json`: which environments each org has),
cached an hour. The app cluster URL is one global pattern, `PlatformSettings:AppClusterUrlPattern`
(`https://{org}.{appPrefix}.{hostName}`), applied to every environment. In `dev.` and `staging.` Designer hides
`prod`.

### 2.3 How Designer's own developers run it locally

`src/Designer/compose.yaml` starts twelve services: Designer, Gitea (repositories, an Actions runner and a proxy),
Postgres with migrations and pgAdmin, Redis with Redis Commander, a load balancer, a fake Ansattporten and
**`studio-azure-mock`** (`src/Designer/development/azure-devops-mock/`). The mock answers on port
6161 for everything the admin path needs: `environments.json` (production, tt02, at21, at22, all pointing at the
mock), the gateway's deployment list and details and report metrics, the app's `applicationmetadata` and
`meta/process` (one fixed app), the token exchange (returns a fixed string), and
`storage/api/v1/studio/instances/...` with **1000 random instances** generated at start
(`src/routes/storage.js`). `AppClusterUrlPattern` is `http://studio-azure-mock:6161/apps/{org}/{env}`. Seeded Gitea
teams include `Admin-AT21`, `Admin-AT22`, `Admin-TT02` (`src/Designer/development/data/gitea-teams.json`). The
metric routes other than `report` are not mocked (they fall to the mock's catch-all).

## 3. Could it be pointed at the localtest stack?

### 3.1 What already fits

- **Storage's admin endpoint is in the stack.** The vendored production Storage contains `StudioInstancesController`
  (`localtest/components/storage/vendor/src/Storage/Controllers/StudioInstancesController.cs`): list with the same
  filters, single instance with data elements, soft delete. It needs a bearer token with
  `urn:altinn:app=studio.designer`, which the stack already mints: `GET /Home/GetTestStudioToken` signs a `studio`
  platform access token with the shared test certificate and exchanges it through production `exchange/altinnstudio`
  (`localtest/localtest/Home/HomeApi.cs`; checked by `localtest/smoke-test.sh`). One token covers every org, and the
  list and single reads touch neither altinncdn.no nor the PDP; only the delete looks up the org number
  (`IOrganisationService`, altinncdn.no).
- **The exchange path matches.** Designer exchanges at `{scheme}://{host}:{port}/authentication/api/v1/exchange/altinnstudio`
  of the Storage URL; on the stack that is the front door. The stack's key-provider fakes accept any issuer signed with
  `certs/localtest-accesstoken.pfx`, so Designer would have to sign with that certificate.
- **The app's metadata and process** are on the app route the stack already proxies
  (`http://local.altinn.cloud:8000/{org}/{app}/api/v1/...`).

So the **instance list and instance detail pages** could run against the stack's Storage without a new endpoint. That
is the one real overlap.

### 3.2 What is missing

| Need | Production source | Stack today | What it would take |
| --- | --- | --- | --- |
| An environment entry | altinncdn.no `environments.json`, global `AppClusterUrlPattern` | none | A `localtest` entry (`platformUrl` `http://local.altinn.cloud:8000/`) in a local `environments.json` and `altinn-orgs.json`, and a pattern that yields `http://local.altinn.cloud:8000` for it. The pattern is global, so a Designer cannot mix real environments with a local one without a code change (a per-environment app URL) |
| The app list | Gateway over Flux `HelmRelease`s | Register `api/localtest/apps` (running apps), Storage `applications` (pushed apps); studioctl-server's route table on its Unix socket | A gateway stand-in answering `deploy/origin/{origin}/apps` and `deploy/apps/{app}/{origin}` in the `AppDeployment` shape (org, env, app, buildId, imageTag) from the Register's list |
| App details | Designer's release and deployment rows, Gitea at the release commit | none: a local app was never built or deployed by Designer, and is usually not in Designer's Gitea | Cannot be filled honestly; the card would error or need a local-mode branch in `GetApplicationDetails` |
| Metrics and health | Azure Monitor, Kubernetes pods | none (no Grafana or Log Analytics in `localtest-v2`; studioctl knows whether the app process runs) | A stand-in returning empty metrics, or a local-mode branch in the frontend |
| Alerts | Grafana | none | Not applicable locally |
| Authorization | Gitea team `Admin-{env}` of `{org}`; `Owners` for delete | none | A local Gitea with the developer's org and an `Admin-localtest` team, and a Designer login |
| Designer's signing certificate | Key Vault | `certs/localtest-accesstoken.pfx` | Designer configured to sign with it (setting name not checked) |
| Delete | Storage, org number from altinncdn.no | works if altinncdn.no is reachable | nothing |

### 3.3 Three ways to wire it, and what each costs

- **(A) Designer's compose beside the stack.** A developer runs `src/Designer/compose.yaml` (twelve services, about the
  size of the stack itself), signs in to the local Gitea, has their org and an `Admin-localtest`
  team, and Designer's `EnvironmentsUrl` and `AppClusterUrlPattern` point at a local environment file and the stack.
  A gateway stand-in (a few routes in the azure mock, or a new `api/localtest/` in the stack) answers the app list.
  The admin pages then show the developer's local instances through production Storage code. Cost: about 2 to 3 days
  of configuration and a stand-in, plus a Designer-side change if real environments and the local one must coexist.
  **Useful to Studio's own developers** working on the admin feature (real instances made by real apps instead of
  the mock's random ones, and Storage's real query behaviour), **not to app developers**.
- **(B) The admin SPA inside the stack with a backend-for-frontend.** Build the admin package into a localtest image
  and answer `designer/api/v1/admin/*`, `designer/api/user/current`, `designer/api/orgs` and the WebSocket hub from a
  small service in the stack. The admin package is built from the whole Designer frontend workspace (`app-shared`,
  `@studio/*`, `language`), so the stack would take on a Node build of that workspace pinned to a monorepo commit,
  plus a re-implementation of a Designer API subset that drifts whenever admin changes. It would cover two of the
  inspection GUI's eight pages (Apps, Instances), with production-oriented framing (environments, versions,
  publishers, metrics that are empty). Cost: well over the inspection GUI's 8 to 9 days, for less.
- **(C) Hosted Designer (altinn.studio) reading the developer's machine.** Not possible: Designer calls Storage and the
  app server-side, and the developer's `local.altinn.cloud:8000` is not reachable from Studio's cluster. Excluded.

### 3.4 Does running Designer locally make sense for an app developer?

No. An app developer's local entry point is `studioctl` (`studioctl env up`, `studioctl run`); they never run
Designer, and studioctl talks to the hosted Designer only to sign in and find repositories
(`designer/api/v1/studioctl/auth/*`, `designer/api/repos/search` in `localtest/studioctl/src/cli`). Running Designer's
compose locally means a second set of containers on a machine that already struggles with the stack (task notes:
7.8 GB RAM here), a local Gitea account and org, and team membership, all to see instances that the stack can show
anonymously. The questions an app developer asks locally (which test user, did my event, notification or dialog
happen, why was I denied: [`../inspection-gui/inspection-gui.md`](../inspection-gui/inspection-gui.md) section 3)
are not the questions admin answers (which version runs in which environment, how many requests failed in production,
which instance should a service owner delete).

## 4. Overlap matrix

Rows are the inspection GUI's proposed pages (task 8 plan, section 5.1 of
[`../inspection-gui/inspection-gui.md`](../inspection-gui/inspection-gui.md)) followed by the admin features task 8 has
no page for. "Admin" covers what is on `origin/main` and what is planned in the issues of section 1.2.

| Feature | Inspection GUI (task 8) | Admin frontend (today / planned) | Overlap |
| --- | --- | --- | --- |
| **Overview**: components on, off or not answering; counts | Yes, probes each component through the front door | No equivalent (environments are tabs; planned status.digdir.no card, [#16148](https://github.com/Altinn/altinn-studio/issues/16148)) | None |
| **Users and parties**: test users, parties, roles, access packages, "log in as", reload test data | Yes (Register, Access Management, `/Home/LogInTestUser`) | No; production has no test users | None: localtest only |
| **Apps**: running and pushed apps, their resource, policy XML, PDP decision form | Yes (Register `apps`, Storage `applications`, Resource Registry) | Deployed apps per environment with version, failed-request counts; app page with app-lib and frontend version, commit, publisher, metrics, ready pods | **Partial**: both list apps. Admin's list is deployments (versions, publisher, metrics); the GUI's is the developer's running apps with their test-data status, resource and policy. No field in common beyond `org/app` |
| **Instances**: list, detail with instance events, links to events and dialog; re-sync later | Yes, through a new anonymous `storage/api/localtest/instances` (full `Instance`, party, every deletion state, instance events) | Yes: list with filters (task, completed, confirmed, deleted, age, archive reference), detail with data elements grouped by type and task, soft delete; planned workflow-engine health and drill-down for v9 ([#20204](https://github.com/Altinn/altinn-studio/issues/20204)), errors per instance ([#19320](https://github.com/Altinn/altinn-studio/issues/19320)) | **Real overlap**: same entity, and admin's Storage endpoint is in the stack. Differences: admin's `SimpleInstance` has no party, no data values, no instance events, one app at a time (by design, [#16271](https://github.com/Altinn/altinn-studio/issues/16271)); the GUI needs the party ("which test user") and the events |
| **Events**: events, deliveries, subscriptions, trace log, live stream, reset | Yes (Events `api/localtest/`) | No | None: localtest only |
| **Notifications**: captured e-mail and SMS inboxes, clear | Yes (Notifications `api/localtest/`) | No (production sends; v9 delivery checks are planned as alerts, [#21262](https://github.com/Altinn/altinn-studio/issues/21262)) | None: localtest only |
| **Dialogs**: list, detail, sync, clear caches, delete all | Yes (Dialogporten `api/v1/localtest/`) | No | None: localtest only |
| **Tools**: tokens, PDP decision, cache clears, Access Management refresh | Yes (`/Home/GetTest*Token`, Authorization `api/localtest/`) | No | None: localtest only |
| Workflow engine | A link to the engine's own dashboard (`workflow-engine.local.altinn.cloud:8000`), which developers can use directly | Planned: health column, failures view, drill-down, resume and abandon through the gateway, because service owners cannot reach the dashboard ([#20275](https://github.com/Altinn/altinn-studio/issues/20275)) | **Same need, opposite constraints**: locally the dashboard is open, so the GUI only links to it |
| Deployments, versions, publisher, commit | No (the developer runs a checkout) | Yes | None: admin only |
| Metrics (failed requests, request counts), ready pods, logs links | No | Yes (Azure Monitor, Kubernetes); planned logs in admin | None: admin only |
| Alerts, contact points, reports | No | Yes (Grafana, Slack, e-mail; contact points in `settings`) | None: admin only |
| Audit log of admin actions | No (local, no personal data) | Yes (deletion; planned for state reads, [#21028](https://github.com/Altinn/altinn-studio/issues/21028)) | None: admin only |
| Restart an app | No (studioctl restarts) | Planned ([#19699](https://github.com/Altinn/altinn-studio/issues/19699)) | None |
| Access control | None; everything anonymous on the developer's machine | Gitea teams `Admin-{env}`, `Owners`; open analysis [#15282](https://github.com/Altinn/altinn-studio/issues/15282) | None |

Summary: one page in eight overlaps in substance (Instances), one partly in name (Apps). Six of the GUI's pages
(Overview, Users and parties, Events, Notifications, Dialogs, Tools) have no counterpart in admin and none is planned.
Everything admin adds beyond instances depends on production infrastructure the stack does not have.

## 5. Recommendation, effort and risks

### 5.1 Recommendation

**Build task 8 as planned, don't point the admin frontend at the stack, and borrow admin's instance vocabulary.** In
detail:

1. **Task 8 goes ahead** as briefed in [`../inspection-gui/README.md`](../inspection-gui/README.md): static HTML and
   vanilla ES modules in the localtest app, eight pages, the anonymous Storage inspection endpoint (8a). The audiences,
   the data and the access model differ (section 3.4), and the overlap is one page.
2. **The Instances page follows admin's vocabulary**, so a developer who later opens admin for tt02 sees the same
   concepts: the same status tags (unread, active, completed, confirmed, deleted, from `A/.../InstanceStatus.tsx`),
   the same filters (current task, completed, confirmed, deleted, created before, archive reference or id), and the
   detail's data elements **grouped by data type with the task and kind** (data model, PDF, signature, signee
   states, payment, attachment) derived as `InstanceDataView.getDataTypeType` does from the app's metadata
   (`storage/api/v1/applications/{org}/{app}`, anonymous) and process (`/{org}/{app}/api/v1/meta/process` on the app
   route). The GUI adds what admin deliberately leaves out: party and test user, data values, instance events, every
   org and app in one list. About half a day on top of the 4.5 days of pages.
3. **Keep decision F for Storage** (the anonymous `storage/api/localtest/instances`), and record the production
   alternative this research found, which [`../inspection-gui/inspection-gui.md`](../inspection-gui/inspection-gui.md)
   section 3.5 did not weigh: `storage/api/v1/studio/instances/{org}/{app}` with the token from
   `/Home/GetTestStudioToken` works for every org with one token and no altinncdn.no on read, but it is per app, leaves
   out the party, data values and instance events, and needs a token in the browser. It is a fallback for the
   Instances page if 8a slips, not a replacement.
4. **No shared components now.** Admin's components (`@studio/components`, `app-shared`, i18n) are only usable inside
   the Designer frontend workspace; the GUI's static approach cannot share them without the Node build the plan
   rejected. Reconsider at task 13 (the stack moving into the monorepo), where the workspace and Designsystemet are
   available: then the GUI's pages could be rebuilt with `@studio/components` and admin's instance components
   (`InstanceStatus`, the data-element grouping) extracted into a shared package. That is the only convergence worth
   planning, and it is the inspection GUI's open question 10, now with a concrete reason.
5. **Optional, for Studio's admin developers, not for this stack's roadmap:** a recipe for Designer's local compose
   that adds a `localtest` environment pointing at the stack (way A, section 3.3) would let the admin team develop
   against real Storage data instead of the mock's random instances. Offer it to squad Kjøring; build it only if they
   want it (question 3).

### 5.2 Effort

| Option | Effort | Covers |
| --- | --- | --- |
| Task 8 as planned | 8 to 9 days (first version), follow-ups about 6 days | All eight pages |
| Amendment 2 (admin vocabulary, data-element grouping) | about 0.5 day | Instances page |
| (A) Designer compose beside the stack | about 2 to 3 days of configuration and a gateway stand-in; more if real and local environments must coexist (a per-environment app URL in Designer) | Admin's instance pages for Studio developers; app details, metrics and alerts stay empty or failing |
| (B) Admin SPA in the stack with a backend-for-frontend | more than 10 days, plus upkeep on every admin change | Two of eight pages |

### 5.3 Risks

- **Drift of the shared concepts.** If admin changes its status or filter vocabulary, the GUI's copy goes stale. Low
  impact (labels), and the Storage fields underneath are the same production model.
- **Expectation of convergence.** Product may expect "one tool for local and production". Section 3.4 is the
  argument against; if the product owner wants it anyway, task 13 is the earliest sensible point, and the data path
  would still be different (anonymous `api/localtest/` versus Designer's gated proxy).
- **The production fallback in the browser.** Using the Studio token from the browser (item 3) puts a token that can
  delete any org's instances into page memory; local only, but the reason to prefer the anonymous read-only endpoint.

## 6. Open questions for the product owner

1. Is the inspection GUI (task 8) un-deferred as planned, with the amendments in section 5.1?
2. Should the GUI's Instances page match admin's Norwegian terms (and so be in Norwegian, as admin is), or stay
   English like the start page? This is the GUI's open question 9 with a reason to choose Norwegian for that page at
   least.
3. Does squad Kjøring want a recipe for running Designer's compose against the stack (way A) for their own admin
   development? If yes, as a separate task in the monorepo, not here.
4. Is "one tool for local and production" a product goal? If yes, the decision belongs with the stack's move into the
   monorepo (task 13), together with extracting admin's instance components into a shared package.
5. Should the Storage inspection endpoint (8a) return admin's `SimpleInstance` fields as a subset of its response, so
   shared components could later read either? (Recommendation: return the full production `Instance` as decided; the
   subset is derivable in the browser.)
6. Should the GUI link out to the workflow engine's dashboard only (as planned), or show v9 workflow health per
   instance as admin will ([#20204](https://github.com/Altinn/altinn-studio/issues/20204))? Recommendation: link only;
   the dashboard is reachable locally, which is the very thing service owners lack.

## 7. Not verified

- Nothing was run: no Designer, no admin frontend, no stack call. The claim that `StudioInstancesController` answers in
  the stack with the token from `/Home/GetTestStudioToken` is read from the vendored source and the smoke test's
  check of that token's claim, not called. Whether `exchange/altinnstudio` reaches altinncdn.no was not checked.
- Designer's signing-certificate setting for `AccessTokenGenerator` and whether Designer can be pointed at a different
  certificate locally were not looked up.
- The admin frontend was read, not rendered; the descriptions of pages are from source.
- Effort for ways A and B is an estimate from reading, not from a prototype.
- The roadmap item digdir/roadmap#418 and the Confluence insight documents linked from
  [#14876](https://github.com/Altinn/altinn-studio/issues/14876) were not read. Open PRs #20233 to #20236 were read by
  title only.
