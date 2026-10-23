# The inspection GUI: inventory, gaps, architecture and plan

**Superseded 2026-10-23 by task 26:** the route table, the `503` pages and the app routes moved from studioctl-server into the localtest container (`GET localtest/api/routes`, `GET localtest/api/apps`, which replaced `localtest/api/proxy/apps`), so the sections about studioctl-server owning the routes are history; see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

Research date 2026-10-20 (Europe/Oslo), task 8 of [`../../TODO.md`](../../TODO.md). Written against `origin/main` at
`178f3da`, plus the two branches being merged that day: `origin/task/dialogporten-integration` (Dialogporten's
`api/v1/localtest/` endpoints, task 9b) and `origin/task/authentication-oidc-provider` (the localtest app's
`localtest/api/authentication/`, task 2). Live payloads were read with `GET` from the stack on
`http://local.altinn.cloud:8000` (studioctl `localtest-v2`, Dialogporten group on) at about 20:05; nothing was posted,
put or deleted.

The question: one page in the localtest app that shows what the components' `api/localtest/` endpoints expose and
replaces the old localtest's StorageExplorer and debug pages for what developers actually look at. Which endpoints
exist, which are missing, how the page is built under the localtest app's constraints (Native AOT,
`CreateSlimBuilder`, source-generated JSON, no server-side rendering framework), and what the first version contains.

Contents:

1. [Inventory](#1-inventory)
2. [The old localtest: what developers used](#2-the-old-localtest-what-developers-used)
3. [What developers look at, by job](#3-what-developers-look-at-by-job)
4. [Architecture options](#4-architecture-options)
5. [Page structure of the first version](#5-page-structure-of-the-first-version)
6. [Missing endpoints, in priority order](#6-missing-endpoints-in-priority-order)
7. [Effort and risks](#7-effort-and-risks)
8. [Open questions for the product owner](#8-open-questions-for-the-product-owner)
9. [Not verified](#9-not-verified)

## 1. Inventory

Every localtest-only endpoint in the stack on 2026-10-20, by component, plus the localtest app's own APIs and the
`/Home` helpers the GUI will call. All are anonymous. "Read" is a `GET` with no side effect; "action" changes state.
Paths are relative to `http://local.altinn.cloud:8000/`. "Live" says what a `GET` answered on the running stack.

### 1.1 Component inspection and control endpoints (`{component}/api/localtest/`)

| Component | Endpoint | Kind | Shape | Paging / streaming | Live |
| --- | --- | --- | --- | --- | --- |
| Register | `GET register/api/localtest/test-users` | read | Array of users: `userId`, `partyId`, `partyUuid`, `userUuid`, `ssn`, `displayName`, `userName`, `email`, `language`, `partyType`, `profile`, `customClaims`, `selfRoleCodes`, `parties[]` (own party first: `partyId`, `orgNumber`, `displayName`, `unitType`, `roles`, `roleCodes`, `accessPackages`), `actingFor[]` (adds sub-units, `partyType`, `ssn`, `parentPartyId`) | none (8 users) | 200, 8 users |
| Register | `GET register/api/localtest/organizations` | read | Array: `partyId`, `partyUuid`, `orgNumber`, `displayName`, `parentPartyId`, `unitType` | none | 200, 13 |
| Register | `GET register/api/localtest/apps` | read | `{ source, apps: [{ org, app, status: TestData\|NoTestData, persons, orgs, receivedAt }], conflicts[], warnings[], users, parties }` | none | 200, `apps: []` (no app running) |
| Register | `PUT register/api/localtest/apps/{org}/{app}` (body: `testData.json`) | action (provisioner) | as `GET apps` | | not called |
| Register | `DELETE register/api/localtest/apps/{org}/{app}` | action (provisioner, on app stop) | as `GET apps` | | not called |
| Register | `POST register/api/localtest/apps/refresh` | action | as `GET apps` | | not called |
| Authorization | `GET authorization/api/localtest/decision?userId&partyId&org&app&action[&instanceId&task&authenticationLevel]` | read (posts to the production `decision` endpoint, no state change) | The production XACML JSON response: `response[]` with `decision`, `status`, `obligations` | none | 200, `Permit` for 1337/500000 on `ttd/stateless-app` `read`; 400 problem without parameters |
| Authorization | `POST authorization/api/localtest/refresh` | action | 200, empty | | not called |
| Access Management | `POST accessmanagement/api/localtest/refresh` | action (provisioner step 4) | `RefreshSummary`: `users`, `roleAssignments`, `packageAssignments`, `prunedRoleAssignments`, `prunedPackageAssignments`, `unknownRoleCodes[]`, `unknownPackages[]` | | `GET` is 405: there is no read of the last result |
| Events | `GET events/api/localtest/events?after&limit&org&app&type&subject&alternativeSubject&resource&instance&status` | read | `{ items: [EventSummary], next }`; summary: `sequenceNo`, `id`, `time`, `registeredTime`, `type`, `source`, `subject`, `alternativeSubject`, `resource`, `resourceInstance`, `org`, `app`, `instance`, `status`, `retryCount`, `retryReason`, `idempotencyKey`, `deliveries { total, delivered, captured, failed }`, `cloudEvent` | paged: `after` = sequence number, `limit` 1 to 1000 (100), `next` left out on the last page | 200, `{"items":[]}` |
| Events | `GET events/api/localtest/events/{id}` | read | The CloudEvent, status, trace log and deliveries of one event | none | not read (no event) |
| Events | `GET events/api/localtest/deliveries?after&eventId&endpoint&mode&limit` | read | **Array** of webhook POSTs (request, response or error, duration, captured or sent) | cursor `after` (row id), `limit`; no `next` | 200, `[]` |
| Events | `GET events/api/localtest/subscriptions?consumer` | read | **Array**: `id`, `endPoint`, `sourceFilter`, `resourceFilter`, `subjectFilter`, `typeFilter`, `consumer`, `createdBy`, `created`, `validated`, `includeSubunits` | none | 200, `[]` |
| Events | `GET events/api/localtest/tracelog?after&eventId&limit` | read | **Array**: `id`, `time`, `cloudEventId`, `resource`, `eventType`, `activity` | cursor `after`, `limit` | 200, one row |
| Events | `GET events/api/localtest/stream` | read | `text/event-stream`; `event: change`, `data: { component: "events", entity: event\|subscription\|trace\|delivery\|all, kind: created\|updated\|deleted\|reset, id, at }`; a comment every 15 s | **streaming (SSE)** | 200, `: connected`, through studioctl-server unbuffered |
| Events | `DELETE events/api/localtest/events[?subscriptions=true]` | action | 204 | | not called |
| Notifications | `GET notifications/api/localtest/inboxes` | read | `{ items: [{ address, channel: email\|sms, receiving, messageCount, createdAt, lastMessageAt }] }` | none | 200, 53 inboxes from smoke runs |
| Notifications | `GET notifications/api/localtest/inboxes/{address}` | read | `{ inbox, messages[] }`; 404 for an unknown address; `+` as `%2B` | none | not read |
| Notifications | `GET notifications/api/localtest/messages?orderId&channel&after&limit` | read | `{ items: [Message], next }`; message: `id`, `channel`, `kind`, `address`, `recipient`, `sender`, `subject`, `body`, `contentType`, `attachments`, `orderId`, `orderChainId`, `notificationId`, `creator`, `sendersReference`, `capturedAt`, `outcome`, `outcomeAt` | **poll cursor**: `next` is always present, the last id returned (or `after` when empty), not a "more pages" flag | 200, `{"items":[],"next":0}` (the capture had been emptied) |
| Notifications | `DELETE notifications/api/localtest/messages` | action | 204; inboxes stay with 0 messages | | not called |
| Dialogporten (optional group; branch `task/dialogporten-integration`) | `GET dialogporten/api/v1/localtest/dialogs?party&resource&after&limit` | read | `{ items: [{ id, org, serviceResource, party, status, extendedStatus, externalReference, process, title: { lang: text }, createdAt, updatedAt, deleted, deletedAt }], next }`, soft-deleted included | paged: `after` = offset in creation order, `limit` 1 to 1000 (100), `next` left out on the last page | 200, `{"items":[]}` |
| Dialogporten | `POST dialogporten/api/v1/localtest/sync[?since]` | action (provisioner step 5) | `{ since, subjectResources, subjectResourcesByResource, resourcePolicyInformation, cachesCleared, durationMs }` | | not called |
| Dialogporten | `POST dialogporten/api/v1/localtest/caches/clear` | action | `{ cachesCleared }` | | not called |
| Dialogporten | `DELETE dialogporten/api/v1/localtest/dialogs` | action, destructive (every dialog in the stack) | `{ dialogsDeleted, cachesCleared }` | | not called |
| Storage | none | | | | `storage/api/localtest/instances` is 404 |
| Resource Registry | none (task 5 decided no `refresh`: the cache is gone) | | | | |
| Authentication | none | | | | |
| Workflow engine | none; it serves its own HTML dashboard on `workflow-engine.local.altinn.cloud:8000/` | | | | 200 `text/html` |

### 1.2 The localtest app's own APIs (`localtest/api/{feature}/`) and `/Home`

| Feature | Endpoint | Kind | Shape | Notes for the GUI |
| --- | --- | --- | --- | --- |
| Proxy | `GET localtest/api/proxy/apps` | read | Array of `{ org, app, upstream, registered }` | **Empty under studioctl** (live: `[]`): studioctl-server is the front door and keeps its own app routes; this list only describes the localtest app's own proxy, which `run-app.sh` uses |
| Proxy | `PUT`/`DELETE localtest/api/proxy/apps/{org}/{app}` | action | | `run-app.sh` only |
| Proxy | `GET localtest/api/proxy/upstreams` | read | `{ name: url }` | Under studioctl it shows the compose-era container names (`http://authorization:5070`, ...) for everything but `register` and `authentication`; it is not the route table in effect. The GUI must not present it as such |
| ID-porten | `GET .well-known/openid-configuration`, `GET jwks`, `GET`/`POST authorize` (the picker), `POST token`, `GET endsession` (task 2 branch) | protocol | | Not for the GUI, except a link to the picker |
| Maskinporten | `GET .well-known/oauth-authorization-server`, `GET jwks`, `POST token` | protocol | | Not for the GUI |
| Authentication (task 2 branch) | `GET localtest/api/authentication/token?client&userId&partyId&authenticationLevel&scope` | action (signs in, sets cookies) | Production token response plus `pid` | A "token with `pid`" form on a tools page |
| Authentication | `GET code`, `POST refresh`, `GET callback` | action | | Tests; not for the GUI |
| Home | `GET /` (and `/Home`, `/Home/Index`) | page | Server-built HTML (a `StringBuilder` in `HomeApi.StartPage`): user select `UserSelect` = `userId.partyId`, level, app path | The contract Cypress and app-lib use |
| Home | `POST /Home/LogInTestUser` | action (signs in, sets cookies, redirects) | form or JSON | The GUI's "log in as" buttons post this form |
| Home | `GET /Home/GetTestUserToken`, `/Home/GetTestOrgToken?orgNumber&scopes`, `/Home/GetTestStudioToken` | action (mints and exchanges) | The JWT as `text/plain` | The GUI fetches an org token here for the production endpoints that need one (section 3.6) |
| Home | `GET /Home/Localtest/Version` | read | `8` | |
| Health | `GET /health` | read | `ok` | |

### 1.3 Production read endpoints the GUI can use as they are

Anonymous in production too, and answered live: `GET storage/api/v1/applications` (every pushed app's metadata, 4
apps live), `GET resourceregistry/api/v1/resource/resourcelist` (5 resources: the seed's `altinn_keyrole_access` and
one `app_{org}_{app}` per pushed app), `GET resourceregistry/api/v1/resource/{id}` and `.../{id}/policy` (the app's
XACML as `text/xml`), `GET resourceregistry/api/v1/resource/orgs`, `GET resourceregistry/api/v1/resource/updated`,
`GET accessmanagement/api/v1/meta/info/roles`. With the developer's test-user cookie (same origin):
`GET profile/api/v1/users/current` (who am I signed in as).

Not usable as is: `GET resourceregistry/api/v1/resource/app_ttd_stateless-app/policy/rules` answered 500 (`All matches
in a all must have the same category ruleId urn:altinn:example:ruleid:3`): production's rule parser on that test app's
policy. The GUI shows the policy XML, not the parsed rules.

### 1.4 Shape conventions, as found

There is no single convention yet, and the GUI has to handle each:

- Paged lists: `{ items, next }` with `next` omitted on the last page (Events `events`, Dialogporten `dialogs`); the
  cursor is a sequence number in Events and an offset in Dialogporten.
- Poll cursors: Notifications `messages` always returns `next` (the last id), meant for `after=` polling.
- Plain arrays with an `after` cursor and no `next`: Events `deliveries`, `tracelog`; plain arrays without paging:
  Events `subscriptions`, Register `test-users`, `organizations`.
- Wrapped single objects: Register `apps`, Notifications `inboxes`.

New endpoints should use `{ items, next }` with `after` and `limit` (`next` omitted on the last page), as Events and
Dialogporten do; the existing ones stay as they are (changing them is churn in three components for no user gain).

## 2. The old localtest: what developers used

From `altinn-studio/src/Runtime/localtest/src` (reference only). The navigation bar (`Views/Shared/_Layout.cshtml`)
had seven entries:

| Old page | What it did | Covered in the new stack? |
| --- | --- | --- |
| Login (`/`) | App select (from discovered apps), user select, level, **prefill XML upload** (instantiate with an org token and the uploaded XML), "refresh authentication" | Yes, except the app select (now a free-text `org/app` field) and the prefill upload |
| Pick frontend version | A cookie that pointed the app at another app-frontend build | No; `studioctl run --dev-frontend` and task 13 ("frontend version override ... port or drop") own it. Out of scope here |
| User administration (Tenor users) | Tenor test users from files | No; the Register seed and each app's `testData.json` replace it. Out of scope |
| Tokens (`/Home/Tokens`) | Forms for user, service-owner and system-user tokens | The endpoints exist (`/Home/GetTest*Token`), the page does not. **Cheap to add** (section 5) |
| Storage Explorer (`/Home/StorageExplorer`) | An iframe over `/LocalPlatformStorage/`, a **directory browser of the file store**: instances, data blobs (used to save XML for prefill), instance events, events, notification orders, **PDP decision audit files** (`authorization/`, one JSON per decision with request and responses), instance delegations, locks | Partly. Events and notifications have better views now (`api/localtest/`); **instances, data and decisions have none** (Storage keeps them in Postgres and files on a volume, the PDP logs nothing) |
| Workflow engine | A link to `workflow-engine.local.altinn.cloud:8000` | The dashboard still exists there; only the link is missing |
| Grafana | A link when the topology had a Grafana route | No Grafana in `localtest-v2` |

Debug endpoints: `/Home/DebugUsers/LocalTestUsers[Raw|RoundTrip]` and `ShowTenorUsers` (test data as JSON), now
`register/api/localtest/test-users` and `apps` (with merge conflicts and warnings, which the old one never showed).

What developers actually opened, judged from what the pages offered: the login page, the token page, and the Storage
Explorer for "did my instance and its data get stored" and "save the XML of an earlier instance" (the prefill link
on the start page pointed straight into it). The PDP audit files were the only local answer to "why was I denied".
The rest (frontend version, Tenor users, Grafana) belongs to other tasks or is gone.

## 3. What developers look at, by job

### 3.1 "Did my app's instance, event, notification, dialog happen?"

| Question | Answered today by | Missing |
| --- | --- | --- |
| Which instances does my app have, in what task, archived or deleted, with which data elements? | Nothing without a token. Production `GET storage/api/v1/instances?org=&appId=` needs a service-owner token (section 3.5) | **Storage inspection endpoint** (section 3.5) |
| What is in this data element (the XML for prefill)? | Production `GET storage/api/v1/instances/{p}/{g}/data/{id}` with a token the PDP permits, or the app's own data URL with the user's cookie | A token-free download, follow-up (P2) |
| What happened to this instance, step by step? | Production instance events (`storage/api/v1/instances/{p}/{g}/events`) need a token; the Events component's `events?instance={p}/{g}` shows the app's CloudEvents | Instance events in the Storage endpoint's detail call (P1) |
| Did my app publish events, were they authorized and delivered? | `events/api/localtest/events?org&app`, `events/{id}` (trace log and deliveries), `deliveries`, `subscriptions`, `stream` | Nothing |
| Which e-mails and SMS were sent, to whom, with what text, and did they end `Delivered`? | `notifications/api/localtest/inboxes`, `inboxes/{address}`, `messages?orderId` | Nothing |
| Was a dialog created for my instance, what does it say? | `dialogporten/api/v1/localtest/dialogs?resource=app_{org}_{app}&party=` | Instance to dialog: after task 10 the instance's `dataValues["dialog.id"]` links the two, no endpoint needed. The full dialog (contents, activities, transmissions): production `GET dialogporten/api/v1/serviceowner/dialogs/{id}` with an org token (the 9a smoke test reads a dialog that way) |
| Why was I denied (403 in the app)? | `authorization/api/localtest/decision` re-runs a decision for given parameters; nothing records the decisions apps made | **PDP decision log** (P2), the old audit folder's replacement |

### 3.2 "Who can I log in as, and what parties do they have?"

`register/api/localtest/test-users` answers it completely (own party, other parties with roles, role codes, access
packages, sub-units under `actingFor`), `organizations` lists every organisation, and `apps` shows which app's test
data is loaded, with merge conflicts and warnings (the old localtest silently skipped duplicates). Logging in is
`POST /Home/LogInTestUser` with `UserSelect=userId.partyId`, which the GUI can post from any row. The current session
is `GET profile/api/v1/users/current` with the cookie, plus the readable `AltinnPartyId` cookie. **Nothing missing.**

What Access Management actually granted after its last refresh (which can differ from the seed when a role code or
package is unknown) is only in the `POST refresh` response, which the provisioner reads and drops. A
`GET accessmanagement/api/localtest/refresh` returning the last summary with its time is P2.

### 3.3 "What is registered?"

| What | Answered by | Missing |
| --- | --- | --- |
| Running apps | Register `apps` (the provisioner `PUT`s on discovery and `DELETE`s on app stop, so it is the running set) | The upstream address of each app (studioctl-server's route table is not exposed over HTTP; `localtest/api/proxy/apps` is empty under studioctl). P3 |
| Pushed apps (metadata, data types, texts) | Production `storage/api/v1/applications` (anonymous) | Texts need a token (`applications/{org}/{app}/texts/nb` answered 401); the metadata is enough for v1 |
| Resources and policies | Production `resourceregistry/api/v1/resource/resourcelist`, `resource/{id}`, `resource/{id}/policy` (all anonymous) | Nothing |
| Service owners | `resourceregistry/api/v1/resource/orgs` | Nothing |
| Roles and access packages of the test users | Register `test-users`; Access Management's catalogue `accessmanagement/api/v1/meta/info/roles` | The applied state (above), P2 |
| Which components run, which optional groups are on | Nothing directly. A probe per component (section 5.5) | An environment endpoint, P3 |
| Subjects Dialogporten knows per resource | The `sync` response only | Can wait |

### 3.4 "Reset things"

| Reset | Endpoint | In the GUI |
| --- | --- | --- |
| Events: events, trace log, deliveries (and subscriptions) | `DELETE events/api/localtest/events[?subscriptions=true]` | Button with confirmation; a checkbox for subscriptions. Known gap: Wolverine's queued deliveries can still arrive (Events README) |
| Notifications capture | `DELETE notifications/api/localtest/messages` | Button with confirmation; says the orders stay |
| Dialogs | `DELETE dialogporten/api/v1/localtest/dialogs` | Button with a typed confirmation ("every dialog in the stack"); after task 10 also says the instances' `dialog.id` now point at nothing |
| Dialogporten caches, resource sync | `POST dialogporten/api/v1/localtest/caches/clear`, `POST .../sync` | Plain buttons (not destructive) |
| PDP caches | `POST authorization/api/localtest/refresh` | Plain button |
| Access Management refresh | `POST accessmanagement/api/localtest/refresh` | Plain button, shows the summary it answers |
| Register reload | `POST register/api/localtest/apps/refresh` | Plain button. It rebuilds the store; Access Management only sees it after its own refresh, so the button runs both in that order |
| Storage instances | None, and none proposed: the decision in task 9 is that instances and dialogs are only wiped together, by `studioctl env reset` | The GUI says so and shows the command |

### 3.5 Storage: a fake endpoint or the production listing with a token?

Two ways to list an app's instances:

- **(P) Production `GET storage/api/v1/instances?org=&appId=`** through the front door with an org token from
  `/Home/GetTestOrgToken?scopes=altinn:serviceowner/instances.read`. The token's org comes from the organisation
  number (default 991825827, which `exchange/maskinporten?test=true` turns into `ttd`). For an app of another org
  (`skd/...`) the GUI must look up that org's number in `resourceregistry/.../orgs` and mint a token for it, which the
  exchange resolves against altinncdn.no; otherwise Storage runs a cross-org PDP decision
  (`AuthorizeCrossOrgAccess`), which the app's policy will usually deny. Production applies its hard-delete filter for
  service owners and requires `org` or `appId`, so "all instances of everything, newest first" is several calls. The
  instance's data and events need further token-checked calls.
- **(F) A fake `storage/api/localtest/instances`** in Storage's `fakes/` (a controller in the host assembly, found by
  MVC's own scan as Events' and Notifications' are; no new directive site), anonymous, reading the production
  `IInstanceRepository.GetInstancesFromQuery` and `GetOne` and `IInstanceEventRepository` with no authorization, every
  deletion state included.

**Decision: (F).** Reasons: every other component's inspection data is behind an anonymous `api/localtest/` endpoint,
and the TODO's rule (task 10) is that only production endpoints carry authentication; the GUI then needs no token for
reading anything, so it never depends on altinncdn.no or the exchange to show local data; it covers every org and
every deletion state with one call; it is about 150 lines in `fakes/` reading production repositories, so the shapes
are production's `Instance` model; and tests (smoke tests, Cypress) get the same listing. (P) stays available and
the GUI's instance detail can link to the production URL for developers who want to see what a service owner sees.
pgAdmin or `psql` stays the answer for anything beyond that.

The endpoint's contract is in section 6 (item 1).

### 3.6 The re-sync button and the Dialogporten actions

- **Re-sync per instance (task 10's `syncDialog/simple`)**: production `POST
  storage/dialogporten/api/v1/syncDialog/simple/{partyId}/{instanceGuid}` requires a bearer token with
  `altinn:storage/instances.syncadapter` (adapter plan, section 3: 401 without, 403 with a user token, 202 or 204 with
  the org token). The GUI fetches `/Home/GetTestOrgToken?orgNumber=991825827&scopes=altinn:storage/instances.syncadapter`
  (text), keeps it in memory for its 30 minutes, and posts with `Authorization: Bearer`. **No new endpoint**: the
  production endpoint is the manual re-sync tool by design (adapter plan, section 2.2), and an anonymous localtest twin
  would be a second path to keep in step. The button shows on an instance only when the adapter answers (the
  Dialogporten group is on) and arrives with task 10, not before.
- **Dialogporten**: `sync`, `caches/clear` and `DELETE dialogs` as buttons on the dialogs page (section 3.4); the
  list reads `localtest/dialogs`; a dialog's detail reads production `serviceowner/dialogs/{id}` with an org token
  (`/Home/GetTestOrgToken?scopes=digdir:dialogporten.serviceprovider`). Not verified from the browser; the smoke test
  reads a dialog with the same kind of token. The service-owner search's `ttd`/`digdir` mismatch (TODO task 10) does
  not affect the GUI, which lists through the localtest endpoint.

### 3.7 Which components are on, and the app routes under studioctl

Under studioctl's `localtest-v2` runtime, studioctl-server owns the routes: component prefixes from the bound
topology file `env up` writes, plus `/{org}/{app}` for each discovered app, and everything else falls to the localtest
app. Its status API (`/api/v1/studioctl/status`) is on its Unix socket only, not on the platform port. The localtest
app therefore cannot tell the GUI which components exist or where the apps run, and its own
`localtest/api/proxy/apps|upstreams` describe a proxy that is not in use (live: `[]` and compose-era container
names).

For the first version this does not matter: the GUI probes each component it shows (section 5.5) and takes the
running apps from the Register. An environment endpoint (components, groups, app routes with upstreams) is a
follow-up that needs a studioctl change (either studioctl-server answers a read-only path on the platform port, or the
localtest container gets the bound topology folder mounted read-only, as the old localtest's `InfraController` read
it); see section 6, item 6, and open question 8.

## 4. Architecture options

Constraints: the localtest app is Native AOT (`PublishAot`, `IsAotCompatible`, warnings as errors), built on
`CreateSlimBuilder`, JSON only through `LocaltestJsonContext`, already serves static files (the app-frontend bundle at
`/app-frontend` through `UseStaticFiles` before routing). Everything the GUI reads is on the same origin
(`local.altinn.cloud:8000`) and anonymous, so the browser calls the components directly through the front door; the
localtest app serves files and nothing else for the GUI. The test users' cookies (`AltinnStudioRuntime`,
`AltinnPartyId`) are on the same origin, so the page can call production endpoints as the signed-in user where that
helps. Under studioctl, `/localtest/...` reaches the localtest app (the fallback route, verified: `GET
localtest/api/nothing` is the localtest app's own 404).

| | (a) Static HTML + vanilla JS | (b) Small SPA, no build (Preact + htm) | (c) Built SPA (Vite) |
| --- | --- | --- | --- |
| What ships | `wwwroot/localtest/inspect/`: `index.html`, a few ES modules (`app.js`, one per page, `ui.js` with a ~60-line element helper, table, JSON viewer, `api.js` with fetch, paging, probing), one CSS file | The same, plus Preact and htm (about 15 KB) either from a CDN by exact version or **copied into `wwwroot`** | A `frontend/` project (TypeScript, Vite, npm dependencies); its `dist/` copied into `wwwroot` |
| Build step | None | None | `npm ci && vite build`: a Node stage in the Dockerfile before the AOT stage, or a CI step; a lockfile to maintain |
| AOT impact | None: files only; two endpoints (the page fallback) | None | None at runtime; image build longer, Node in the build |
| Third-party code | None | Two pinned files; a CDN copy needs internet for the page (the stack already needs it for altinncdn.no, but a GUI that is blank offline is worse than a token that fails) | npm tree, Dependabot noise, a supply chain for a dev tool |
| Fits ~8 pages of tables, filters, details, live updates? | Yes, with discipline: one module per page, re-render a page's container on data change, no client-side state beyond the URL | Yes, more comfortably: components and state | Yes, and typed |
| Who can change it | Anyone, in the browser's dev tools, edit and reload | Anyone who reads JSX-like templates | Someone with the toolchain |
| Tests | Smoke check in `localtest/smoke-test.sh`; Playwright later | Same | Unit tests possible; same smoke |
| Fits the old start page | The start page stays server-built; the GUI links to it | Same | Same |

**Recommendation: (a).** The GUI is a read-mostly view over JSON that already exists: tables, a JSON viewer, a few
forms and buttons. Vanilla ES modules handle that without a build or any third-party code, so the localtest image,
its AOT publish and the vendor-sync story stay as they are, and nothing has to be pinned or updated. The trigger to
move to (b) is a page that needs shared client state across views (not expected in the scope below); if it comes,
Preact and htm are copied into `wwwroot` (no CDN), still with no build. (c) is not worth a Node build in an
AOT image for a developer tool; revisit only if the stack moves into the monorepo (task 13), where the frontend
toolchain and the design system already exist (open question 10).

How it is wired in the localtest app (the implementation brief, not done here):

- Files under `localtest/localtest/wwwroot/localtest/inspect/`, published by the web SDK with the binary (the
  Dockerfile's `COPY . .` already brings them into the build). Served by a second `UseStaticFiles()` on the web root,
  before routing, with `Cache-Control: no-cache` like `/app-frontend`.
- Deep links: `MapGet("/localtest/inspect/{**path}")` answers `index.html` for any path without a file extension, so
  `/localtest/inspect/events?app=ttd/testapp` loads the page and the page reads `location`. A plain `RequestDelegate`
  (the AOT request delegate generator rewrites typed lambdas on catch-all patterns, see the localtest README's build
  notes). `/localtest/inspect` without the slash redirects.
- No new JSON types: the localtest app serves files; the browser talks to the components.
- The start page (`/`) gets one link to the GUI; the GUI links back to the start page for logging in. The `/Home`
  contract is not touched.

Testing:

- `localtest/smoke-test.sh` grows a section: `GET /localtest/inspect/` is `200 text/html`; every module and the CSS
  are `200` with a JavaScript or CSS content type (AOT publish drops nothing); a deep link
  (`/localtest/inspect/events?app=ttd/testapp`) answers the same `index.html`; and for each endpoint the GUI reads, a
  `GET` answers JSON with the fields the GUI uses (or, for Dialogporten, either JSON or the localtest app's text 404
  when the group is off). That last part is the real guard: a component changing its inspection shape breaks the GUI
  silently otherwise.
- Later: one Playwright script (`playwright-cli` is on the machine) that opens each page with the stack up and fails
  on a console error or an empty table where the smoke data must show something. Not in the first version.

## 5. Page structure of the first version

One HTML page with a navigation bar; each entry is a path under `/localtest/inspect/`. Times shown in local time
(Europe/Oslo) with UTC in a tooltip. Every list has a filter row bound to the URL's query, so a filtered view is a
link. Every entity id links to its detail.

### 5.1 Pages

| Page and deep link | Reads | Actions | Refresh |
| --- | --- | --- | --- |
| **Overview** `/localtest/inspect/` | One probe per component (5.5); Register `apps`; counts: Events `events?limit=1`, Notifications `messages`, Dialogporten `dialogs?limit=1`, Storage `instances?limit=1` | Links to each page; the start page; the workflow engine dashboard | On load and every 10 s while visible |
| **Users and parties** `/localtest/inspect/users[?user=1337]` | Register `test-users`, `organizations`, `apps` (conflicts and warnings shown); `profile/api/v1/users/current` with the cookie (current session) | "Log in as" per user and party (posts `/Home/LogInTestUser` with `UserSelect`, level, optional app); "Reload test data" (Register `apps/refresh`, then Access Management `refresh`, showing its summary) | On load |
| **Apps** `/localtest/inspect/apps[/ttd/testapp]` | Register `apps` (running), Storage `applications` (pushed), Resource Registry `resourcelist`, `resource/{id}`, `resource/{id}/policy` (XML shown as text) | Open the app (`/{org}/{app}/`); "Decision" form prefilled for the app (Authorization `decision`) | On load |
| **Instances** `/localtest/inspect/instances?app=ttd/testapp&party=500000` and `/localtest/inspect/instances/500000/{guid}` | Storage `instances` (new, section 6 item 1), detail with instance events | Links: the instance's events (`events?instance=`), its dialog (from `dataValues["dialog.id"]` after task 10), its app URL. Re-sync to Dialogporten after task 10 (3.6) | Poll every 5 s while visible |
| **Events** `/localtest/inspect/events?app=ttd/testapp&type=&status=` and `/events/{id}`; tabs for deliveries and subscriptions | Events `events`, `events/{id}`, `deliveries`, `subscriptions`, `tracelog` | "Reset events" (confirm; checkbox "also subscriptions") | **The SSE stream** (`events/api/localtest/stream`): on a `change`, re-read the current view; open only while this page is visible (see risks) |
| **Notifications** `/localtest/inspect/notifications?address=&orderId=&channel=` | Notifications `inboxes`, `inboxes/{address}`, `messages` | "Clear captured messages" (confirm; says orders and inboxes stay) | Poll `messages?after={next}` every 3 s while visible |
| **Dialogs** `/localtest/inspect/dialogs?resource=app_ttd_testapp&party=` | Dialogporten `localtest/dialogs`; detail through production `serviceowner/dialogs/{id}` with an org token | "Sync resources" (`sync`, shows the summary), "Clear caches", "Delete all dialogs" (typed confirmation) | Poll every 5 s while visible |
| **Tools** `/localtest/inspect/tools` | none | Token forms (user, org with org number and scopes, Studio, and the `pid` token of task 2) that open the `/Home` and `localtest/api/authentication` URLs; the PDP decision form; "Clear PDP caches"; "Refresh Access Management" | none |

Out of the first version: data element download (needs item 3 of section 6), the PDP decision log (item 4), the
environment view with app upstreams (item 6), Correspondence (task 7), the inbox reception toggle (Notifications'
planned follow-up; the `receiving` flag is shown read-only).

### 5.2 Actions and confirmation

Every action is a button that shows the response body (or the error) in place. Destructive actions (`DELETE`) use the
browser's `confirm()` naming what is lost; "Delete all dialogs" asks the user to type `delete`, because it cannot be
undone and affects every app. Nothing destructive runs on page load or from a link (no `GET` triggers an action).

### 5.3 Auto-refresh

Events has a stream; the other components do not, and polling is enough at local volumes. Polling and the stream run
only while the page is visible (`visibilitychange`), so a forgotten background tab costs nothing. A stream per
component (Notifications, Storage, Dialogporten) can wait: polling with `after` cursors is cheap. The Events stream
already carries `component: "events"`, so if more streams come they can share the message shape.

### 5.4 Deep links

The URL is the state: page path, filters in the query, detail ids in the path. The page reads `location` on load and
`history.pushState`s on navigation, so links copied from the address bar, from a test log or from a README work.
Examples: `/localtest/inspect/events?app=ttd/testapp`, `/localtest/inspect/instances/500000/{guid}`,
`/localtest/inspect/notifications?address=%2B4799123456`, `/localtest/inspect/dialogs?resource=app_ttd_testapp`.

### 5.5 A component that is off

The GUI probes the endpoint a page reads, not a health URL (studioctl-server routes no health paths on the platform
port). The answers tell three states apart:

- JSON (or problem JSON): **on**.
- `404` with `text/plain` (the localtest app's "No app is registered for this path" fallback, which is what a path
  under a prefix without a route reaches, verified with `/correspondence/...`): **not part of this environment**. For
  Dialogporten the page says "Dialogporten is an optional group: `studioctl env up -r localtest-v2 --with dialogporten`"
  and hides its actions; the Overview shows it greyed out.
- `502` (studioctl-server: "{component} at ... did not answer") or a network error: **not answering**, with
  `studioctl env status -r localtest-v2` and `studioctl env logs --component {name}` as the hint.

**Changed 2026-10-20 (task 16):** studioctl-server now answers a route whose group is off with `503`
`application/problem+json` (`title` "Component not running", extension members `component` and `group`, the `--with`
command in `detail`), and an enabled component that does not answer with `503` "Component not answering" instead of
the `502`; the GUI reads `component` and `group` from that body instead of relying on the text 404, which remains only
for a path that matches no route at all.

The navigation keeps every entry; an off component's page shows only the notice.

## 6. Missing endpoints, in priority order

P1 is needed for the first version; the rest can wait.

1. **P1. Storage**: `GET storage/api/localtest/instances?org=&app=&party=&after=&limit=` → `{ items: [Instance],
   next }`, newest `lastChanged` first, every deletion state, production `Instance` JSON (data elements included),
   read through `IInstanceRepository.GetInstancesFromQuery`; and `GET storage/api/localtest/instances/{partyId}/{guid}`
   → `{ instance, events: [InstanceEvent] }` through `GetOne` and the instance event repository. Anonymous, a
   controller in `fakes/`, smoke-tested in the root `smoke-test.sh`'s instance lifecycle. About 1.5 days.
2. **P2. Access Management**: `GET accessmanagement/api/localtest/refresh` → `{ at, trigger: startup|request, summary:
   RefreshSummary }` (the last run, kept in memory by `LocaltestRefreshService`). 0.5 day.
3. **P2. Storage**: `GET storage/api/localtest/instances/{partyId}/{guid}/data/{dataGuid}` → the blob with its content
   type and file name (`Content-Disposition`), through the production data repository and the file blob fake. The old
   Storage Explorer's "save the XML" use. 0.5 day.
4. **P2. Authorization**: `GET authorization/api/localtest/decisions?after=&limit=` → `{ items: [{ id, at, request,
   response, decision }], next }`, the last 500 decisions posted to the production `authorization/api/v1/decision`,
   captured by a middleware in `fakes/` (an `IStartupFilter`, no directive) into a memory ring buffer; `DELETE` empties
   it. Replaces the old localtest's audit folder: "why was I denied". 1.5 days.
5. **P3. Dialogporten**: an `externalReference` or `instance` filter on `localtest/dialogs`, if task 10's
   `dataValues["dialog.id"]` link turns out not to be enough. Only after task 10.
6. **P3. Environment**: `GET localtest/api/environment` → `{ components: [{ name, prefix, group, on }], groups,
   apps: [{ appId, upstream }] }`, so the GUI shows the real route table and app upstreams. Needs studioctl to expose
   its bound topology to the localtest container (a read-only mount of the topology folder; the file is replaced by
   rename, so a single-file bind would go stale) or studioctl-server to answer it on the platform port. 1 to 1.5 days.
   The probes of section 5.5 cover the first version.
7. **Later, with their tasks**: Correspondence's capture (task 7), the Notifications inbox reception toggle, receipt
   links (task 16). Streams for Notifications, Storage and Dialogporten only if polling turns out to be too slow.

Not missing (decided above): a Storage reset (task 9's decision), an anonymous re-sync twin in the adapter (3.6), a
Resource Registry refresh (task 5), changes to the existing inspection shapes (1.4).

## 7. Effort and risks

### 7.1 Effort

| Piece | Days |
| --- | --- |
| Storage inspection endpoints (item 1) with smoke checks and README | 1.5 |
| GUI shell: static files and the page fallback in the localtest app under AOT, layout and navigation, `api.js` (fetch, paging, probing, the three off states), `ui.js` (element helper, table, JSON viewer, time formatting), deep links | 1.5 |
| Pages: Overview 0.25, Users and parties 0.5, Apps 0.5, Instances 0.75, Events (with the stream) 1, Notifications 0.5, Dialogs 0.5, Tools 0.5 | 4.5 |
| Smoke test section, READMEs (localtest app, the top-level table), one image build and a manual pass under studioctl with and without `--with dialogporten` | 1 |
| **First version** | **8.5 (8 to 9)** |

Follow-ups: Access Management's last refresh 0.5; data download 0.5; the PDP decision log 1.5; the re-sync button
0.5 (after task 10); the environment endpoint 1 to 1.5 (studioctl); a Playwright check 1; Correspondence's page 1
(after task 7). About 6 days in all, each independent.

The Storage endpoint and the GUI shell can be built in parallel; the image builds cannot (one AOT publish or image
build at a time on this machine).

### 7.2 Risks

- **AOT and static files.** `UseStaticFiles` already works for `/app-frontend` from a physical folder; serving
  `wwwroot` from the publish output under `CreateSlimBuilder` is expected to work (the web SDK publishes `wwwroot`
  content items with an AOT publish) but was not built here. Fallback if not: an explicit `PhysicalFileProvider` on
  `{ContentRoot}/wwwroot`, or embedded resources.
- **Shapes drift.** The GUI reads eight components' localtest JSON, owned by different tasks. The smoke section that
  checks the fields the GUI uses is the mitigation; a component README change should mention the GUI.
- **Connection limit.** Browsers allow six HTTP/1.1 connections per host; an open SSE stream holds one. Several tabs
  with the Events page open could stall the app under test on the same origin. Mitigation: the stream only while the
  page is visible, closed on hide.
- **Shared, destructive state.** "Delete all dialogs" and "Reset events" affect every app and every test run in the
  stack, including a Cypress run in progress. Confirmation, and the page names the scope.
- **Token-dependent views** (dialog detail, re-sync) need the exchange, which needs altinncdn.no; they show the
  exchange's error when offline. Everything else is anonymous.
- **Cross-site requests.** The anonymous `POST` actions (cache clears, refresh) are reachable by any web page the
  developer visits as simple cross-origin requests. Pre-existing, local only, not made worse by the GUI; noted, not
  addressed.
- **Times.** The Events component's `registeredTime` was seen two hours behind UTC (Dialogporten README, task 9b);
  the GUI shows times as given and must not hide that.

## 8. Open questions for the product owner

1. Should the GUI replace the start page at `/`, or sit beside it under `/localtest/inspect/` with links both ways?
   Recommendation: beside it. `/` is the login contract Cypress, app-lib and scripts use, and it works.
2. Path and name: `/localtest/inspect/` ("Inspect") or `/localtest/` itself?
3. Approve the Storage inspection endpoint (section 3.5, decision F), the first `api/localtest/` in Storage.
4. Destructive actions (reset events, clear notifications, delete dialogs) in the first version, or a read-only first
   version?
5. A tokens page in the first version (the old localtest had one; the endpoints exist)?
6. The old start page's prefill upload (instantiate with an uploaded XML and an org token): port it, or drop it? Not
   in this plan.
7. Data element download (item 3) and the PDP decision log (item 4): first version or follow-up?
8. The environment endpoint (item 6): through studioctl-server on the platform port, or a read-only mount into the
   localtest container, or not at all?
9. Language: English, like the start page, or Norwegian?
10. When the stack moves into the monorepo (task 13), should the GUI move to a built frontend with Designsystemet,
    or stay as static files?
11. `localtest/api/proxy/apps` and `upstreams` mislead under studioctl (empty and stale). Leave them for
    `run-app.sh`, or have them say they describe the localtest app's own proxy only?

## 9. Not verified

- Nothing was built: no AOT publish with a `wwwroot`, no mock-up page. The wiring in section 4 is read from the
  localtest app's source and README.
- The Storage repository calls in item 1 (`GetInstancesFromQuery` without `org`, `appId` or party, and with every
  deletion state) were read in the interface, not called; the production query may require one of the three, in
  which case the fake endpoint loops over `IApplicationRepository.FindAll` or uses plain SQL as Events does.
- Payloads with data in them were seen only for Register, Notifications' inboxes, Storage's applications and the
  Resource Registry; Events, the Notifications messages and the Dialogporten list were empty at the time, so their
  item shapes are from source and READMEs. `events/api/localtest/tracelog` had a row (18:07 UTC) while `events` was
  empty; not investigated.
- The dialog detail through production `serviceowner/dialogs/{id}` with a `/Home/GetTestOrgToken` token, and the
  re-sync with the `syncadapter` scope, were not called from a browser (no POSTs; task 10 is not built).
- The six-connection limit with an SSE stream through studioctl-server was not measured.
