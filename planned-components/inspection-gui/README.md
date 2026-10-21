# Plan: the inspection GUI of the new localtest

## Status (2026-10-21)

- **Deferred**: task 8 (2026-10-20), pending the owner's decision after task 17
  ([`../admin-running-services/`](../admin-running-services/README.md)), whose recommendation is to build this GUI as
  planned with the admin frontend's instance vocabulary. Nothing is built; the questions of section 8 are open.
- Since the plan: the Correspondence group (task 7), Receipt (task 16), production Profile (task 14) and the adapter
  (task 10, the re-sync input) exist, so the page list and the endpoint inventory are behind the stack.
  `storage/api/localtest/instances` (step 8a) still does not exist.

Superseded statements:

- Brief, design, "a component that is off: `404 text/plain` ... = not part of this environment" and the verification's
  "or the text 404 when the group is off": studioctl-server answers `503` problem details ("Component not running",
  with `component` and `group`) for a group that is off and `503` "Component not answering" instead of the `502`
  (task 16). The report's section 5 already records this.
- Headline findings, "the re-sync button ... arrives with task 10": task 10 is done.

Research date 2026-10-20 (Europe/Oslo), task 8 of [`../../TODO.md`](../../TODO.md), written against `origin/main` at
`178f3da` plus the Dialogporten 9b and Authentication OIDC branches being merged that day. The question: one page in
the localtest app over the components' `api/localtest/` endpoints, replacing the old localtest's StorageExplorer and
debug pages for what developers actually look at. Which endpoints exist, which are missing, how the page is built
under the localtest app's Native AOT constraints, and what the first version contains. One report in this folder.

| Report | What it plans | Effort |
| --- | --- | --- |
| [`inspection-gui.md`](inspection-gui.md) | Inventory of every localtest endpoint in the stack (with live payloads), the old localtest's pages and what they covered, the developer jobs with their endpoints and gaps, the Storage decision, the re-sync and Dialogporten actions, three architecture options, the first version's pages, missing endpoints in priority order, effort, risks, open questions | First version 8 to 9 days; follow-ups about 6 days |

## Headline findings

- **Most of the data is already there.** Register (users, parties, roles, apps with merge conflicts), Events (events,
  deliveries, subscriptions, trace log, an SSE stream), Notifications (inboxes, messages) and Dialogporten (dialogs,
  sync, caches, delete) have anonymous `api/localtest/` endpoints; the Resource Registry's and Storage's application
  reads are anonymous in production. 24 `api/localtest/` endpoints in six components, plus 17 under the localtest
  app's own `localtest/api/`, inventoried in section 1.
- **Storage is the gap that matters.** "Did my instance get stored, in which task, with which data" was the old
  Storage Explorer's main use and has no token-free answer today. **Decision: a fake
  `storage/api/localtest/instances` in Storage's `fakes/`** reading the production repositories, not the production
  listing with an org token (which needs a token per org, the CDN-backed exchange and several calls, and hides
  hard-deleted instances).
- **The PDP's decisions are not recorded.** The old localtest wrote every decision to an audit folder, the only local
  answer to "why was I denied". A decision log in the Authorization fakes is the most useful follow-up.
- **Under studioctl the localtest app does not know the route table.** studioctl-server owns the routes and its status
  API is on a Unix socket; `localtest/api/proxy/apps` is empty and `upstreams` shows compose-era names. The first
  version probes each component through the front door (three states: on, not part of this environment, not
  answering) and takes the running apps from the Register; an environment endpoint is a studioctl follow-up.
- **The re-sync button needs no new endpoint.** Production `syncDialog/simple` with an org token from
  `/Home/GetTestOrgToken` (scope `altinn:storage/instances.syncadapter`); it arrives with task 10.
- **Shapes are inconsistent** (`{ items, next }`, poll cursors, plain arrays). The GUI handles each; new endpoints use
  `{ items, next }` with `after` and `limit`.

## Recommendation

Static HTML and vanilla ES modules served by the localtest app from `wwwroot/localtest/inspect/`, with a page fallback
for deep links (`/localtest/inspect/events?app=ttd/testapp`); the browser reads the components directly through the
front door (one origin, anonymous). No build step, no third-party code, no change to the AOT publish beyond static
files. Preact and htm copied into `wwwroot` (no CDN) only if a page needs shared client state; a Vite build only if
the stack moves into the monorepo's frontend toolchain. Eight pages: Overview, Users and parties, Apps, Instances,
Events (live through the SSE stream), Notifications, Dialogs, Tools (tokens, PDP decision, refresh buttons). Beside the
start page at `/`, not replacing it. Tested by a section in `localtest/smoke-test.sh` that checks the page and assets
load and every endpoint the GUI reads answers the fields it uses; Playwright later.

## Not verified

Nothing was built: no AOT publish with a `wwwroot`, no mock-up page. The Storage repository calls for the new endpoint
were read, not called. Events, the Notifications messages and the Dialogporten list were empty on the running stack,
so their item shapes are from source. No `POST`, `PUT` or `DELETE` was sent, so the actions (including the re-sync and
the dialog detail with an org token) were not exercised from a browser. Details in section 9 of the report.

## Proposed brief

**Goal.** One GUI in the localtest app, under `/localtest/inspect/`, that shows what the stack did for a developer's
app: test users and parties, running and pushed apps with their resources and policies, instances, events and webhook
deliveries, captured e-mails and SMS, dialogs; with the reset and refresh actions the components offer. Replaces the
old localtest's Storage Explorer, token page and debug endpoints for what developers look at.

**Design (proposed 2026-10-20).** As [`inspection-gui.md`](inspection-gui.md):

- Static HTML plus vanilla ES modules in `localtest/localtest/wwwroot/localtest/inspect/`, served with
  `UseStaticFiles` before routing and a `MapGet("/localtest/inspect/{**path}")` fallback to `index.html` (plain
  `RequestDelegate`); no build step, no CDN, no new JSON types in the localtest app. Data read by the browser from
  the components through the front door.
- Pages, endpoints, actions and refresh as section 5: Overview, Users and parties (log in as through
  `/Home/LogInTestUser`), Apps, Instances, Events (SSE while visible), Notifications (polling with `after`), Dialogs,
  Tools. Filters and ids in the URL. Destructive actions confirmed; "delete all dialogs" typed.
- A component that is off: JSON = on; `404 text/plain` from the localtest app's fallback = not part of this
  environment (with the `--with` hint for optional groups); `502` or network error = not answering (with the
  `studioctl env logs` hint).
  *2026-10-21: superseded by the front door's `503` problem details for an off or silent component (task 16); a text 404 remains only for a path that matches no route.*
- The start page at `/` and the `/Home` contract unchanged, one link added.

**Steps.** 8a: Storage's `GET storage/api/localtest/instances` and `instances/{partyId}/{guid}` in `fakes/` (section 6,
item 1), smoke-tested and documented. 8b: the GUI with the eight pages, the smoke section, READMEs, an image build and
a manual pass with and without `--with dialogporten`. 8c, each its own brief later: Access Management's last refresh,
data download, the PDP decision log, the re-sync button (after task 10), the environment endpoint (studioctl), a
Playwright check, Correspondence (after task 7).

**Open.** Questions 1 to 11 of section 8: beside or replacing `/`; the path; approval of the Storage endpoint;
destructive actions in the first version; the tokens page; the prefill upload; data download and the decision log in
the first version; the environment endpoint's route; language; a built frontend after the monorepo move; the proxy
API's misleading lists under studioctl.

**Verification.** `localtest/localtest/smoke-test.sh`: the page, every asset and a deep link load with the right
content types; every endpoint the GUI reads answers JSON with the fields the GUI uses (Dialogporten: JSON or the text
404 when the group is off). The root `smoke-test.sh`: the Storage endpoint lists the smoke instance with its data
element and returns its instance events. A manual pass through each page with `ttd/testapp` after the root smoke
test, with and without the Dialogporten group.
