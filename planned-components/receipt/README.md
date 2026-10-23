# Plan: the platform receipt in the new localtest

**Superseded 2026-10-23 by task 27:** the studioctl wiring described here (a component entry in `components.go`, a host port, an image spec with a `STUDIOCTL_IMAGE_LOCALTEST_V2_*` override, a route in studioctl-server) is gone: a component is now one entry in manifest v2 without a host port, and its route is in the localtest container's `PlatformRoutes.cs`; Receipt is the optional group `receipt`, not in the default stack; see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

## Status (2026-10-21)

- **Implemented** as task 16 (2026-10-21, `f40027a..319a712`) in
  [`../../localtest/components/receipt/`](../../localtest/components/receipt/README.md), host port 8034, route
  `/receipt`. Smoke test 27 ok on 2026-10-21. The studioctl group table (`6b41c96`) and the 503 front door (`adf6ee6`)
  landed from this task.
- Taken: pin `babc0ed`, the two directive sites, the `selfLinks` rewrite fake, the shared certificate mount, no
  provisioning; task 10's smoke step corrected to the adapter's own 200 Markdown receipt.
- Decided otherwise (task 16): **its own optional group `receipt`**, implying nothing, not the default stack
  (`studioctl env up -r localtest-v2 --with receipt`). "Tilbake til innboks" stays as upstream has it, raised upstream
  with task 11. Re-pin on backend changes only; image builds may reach the npm registry.

Superseded statements:

- Recommendation and brief, "vendor the receipt into the default stack" and "component `receipt` in the default
  stack": superseded by the `receipt` group. Without it `/receipt` answers `503` "Component not running".
- Headline findings, Profile as "the Register component's `users/{id}`": production Profile since task 14.
- Headline findings, the adapter's go-to action as "task 10, unapproved": task 10 is done; the click-through from
  Arbeidsflate waits for task 11.
- Brief, "`git subtree`": the vendored copy is a `git archive` with `vendor/UPSTREAM.md`, as every component's.

Research date 2026-10-20 (task 16 of [`../../TODO.md`](../../TODO.md)), written against this repository at `178f3da`.
The questions were: what `Altinn/altinn-receipt` is, who links to `/receipt/{instanceOwnerPartyId}/{instanceGuid}`,
what the stack already provides, and whether to vendor it, redirect to the app's own receipt, or do nothing. There is
one report in this folder.

| Report | What it plans | Effort |
| --- | --- | --- |
| [`receipt.md`](receipt.md) | Pin, routes, authentication, calls and credentials; who links to it and what the 404 costs; dependencies against the stack; directive sites and the one fake; three options; build chain and Dockerfile (scratch build, start and browser run against the stack); studioctl wiring; smoke tests; differences; open questions | 2 to 3 days |

Upstream pin: `Altinn/altinn-receipt` [`babc0ed`](https://github.com/Altinn/altinn-receipt/commit/babc0ed23483e8c3fb62dad4da903c6a88024914)
(`main`, 2026-10-20).

## Headline findings

- **Small, stateless, and every dependency is already in the stack.** One ASP.NET host on .NET 10 with a webpack-built
  React bundle. It calls Storage (instance), Register (owner party) and Profile (the Register component's
  `users/{id}`) with the user's `AltinnStudioRuntime` cookie, adds a `platform`/`receipt` platform access token for
  Register and Profile, and sends unauthenticated users to Authentication's `authentication?goto=`. The browser reads
  Storage's application metadata and texts and altinncdn.no's org list. There is no database, queue or blob store.
- **Verified in a scratch run against the running stack.** With two directive sites and one 40-line fake, the host
  built, started and rendered the receipt for an archived `ttd/testapp` instance as user 1337 in Chromium: party, user,
  app title, sender and receiver, and a downloadable attachment ([screenshot](scratch-receipt-1337.png)). A user
  without rights gets 403, an unknown instance 404. Without a cookie the redirect goes through Authentication and back.
- **Two production host names are hard-wired.** The 401 redirect's `goto` is `https://platform.{Hostname}…`: one
  `#if !LOCALTEST` site. Attachment links come from Storage's `selfLinks.platform`
  (`https://platform.local.altinn.cloud/…`, which does not resolve): a DI-only `IStorage` decorator rewrites them to
  the local origin. The platform access token needs the shared certificate bind-mounted at
  `/accesstoken/accesstokencredentials.pfx`, as Events and Notifications already have it.
- **Nothing links to `/receipt/` in today's stack.** The only producer is the Dialogporten adapter's go-to action for
  archived instances (task 10, unapproved), which a person clicks only in Arbeidsflate (task 11). The app frontend
  (monorepo and `app-frontend-react`), app-lib v8 and v9, and the old localtest never link to it. The legacy
  `app-localtest` once proxied `/receipt/` to a developer-run receipt on port 5060; that was dropped in
  [altinn-studio#17839](https://github.com/Altinn/altinn-studio/pull/17839). The 404 costs testers nothing today.
- **Correction to task 10:** the adapter's transmission receipt
  (`storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}`) is computed by the adapter from Dialogporten,
  Storage, Register and the CDN, not by the receipt app. Task 10's smoke step should expect 200 Markdown, not "a 404
  from the missing receipt app".

## Recommendation

**Option A: vendor the receipt into the default stack,** on host port 8034 (assumed: 8033 is reserved by the brief)
with proxy prefix `/receipt` (no collision with the localtest app, the platform prefixes or app routes). It needs no
provisioning and uses about 67 MB RSS idle. Effort is **2 to 3 days**. The thin redirect to the app's own receipt
(Option B, 0.5 to 1 day) shows a different page and needs the app running. Doing nothing (Option C) leaves
Arbeidsflate's archived dialogs on a 404 once tasks 10 and 11 land.

*2026-10-21: decided otherwise: the receipt is its own optional group `receipt` (task 16), not part of the default stack.*

## Not verified

No image was built and studioctl was not touched. The route through studioctl-server was stood in for by a scratch
proxy on port 8090. The scratch used SDK 10.0.401 and Node 22 rather than the image's pinned SDK 10.0 and Node 24.21.
Upstream's tests were not run. The adapter's go-to link and Arbeidsflate wait for tasks 10 and 11. Person-owned
instances, self-identified users and the PDF link were not tried. How production mounts the signing certificate
(chart values in altinn-studio-ops) was not seen. Details are in section 8 of the report.

## Proposed brief

### 16. Receipt

**Goal.** `/receipt/{instanceOwnerPartyId}/{instanceGuid}` shows the production platform receipt for an archived
instance, as in production. It is the target of the Dialogporten adapter's go-to action for archived instances (task
10) and of Arbeidsflate's archived dialogs (task 11).

**Plan.** [`planned-components/receipt/receipt.md`](receipt.md). Vendor `Altinn/altinn-receipt`
at `babc0ed` (whole repository, `git subtree`) as `localtest/components/receipt/`.

**Design.**

- Two sites in `src/backend/Altinn.Receipt/Program.cs`:
  - an additive `#if LOCALTEST` hook after `ConfigureServices(…)` calling `LocaltestReceipt.Apply(services, config)`;
  - `#if !LOCALTEST` / `#else` around the status-code page's `goto` URL, using the request's scheme and host instead
    of `https://platform.{Hostname}`.
- `localtest.build.props` defines `LOCALTEST`. `localtest.build.targets` compiles `fakes/**/*.cs` into the host,
  because the fake decorates the host's own `IStorage`.
- One fake: an `IStorage` decorator that rewrites `data[].selfLinks.platform` from `https://platform.{Hostname}/` to
  `Localtest:Receipt:PlatformEndpoint`.
- Configuration by environment only: the OpenID endpoint, `Hostname`, and the four `PlatformSettings` endpoints on
  the public origin.
- The Dockerfile mirrors upstream's three stages (Node 24.21 bundle, SDK 10.0 publish, chiseled runtime as
  `$APP_UID`) with `vendor/` paths, the overrides and `ASPNETCORE_ENVIRONMENT=Development`,
  `ASPNETCORE_URLS=http://+:5060` as `localtest:` lines.
- studioctl (in `localtest/studioctl/src/cli/`):
  - component `receipt` in the default stack, container port 5060, host port 8034, `/health`;
  - the shared certificate bind-mounted read-only at `/accesstoken/accesstokencredentials.pfx`;
  - deps `register`, `authentication`, `storage`;
  - route `{AppHostName, "/receipt"}`;
  - an image spec and `STUDIOCTL_IMAGE_LOCALTEST_V2_RECEIPT`;
  - no provisioning.
- Image workflow: one matrix entry in each list. Optionally, `/receipt/` in the localtest app's `PlatformRoutes.cs`.

**Open.** Report section 7.3:
1. default stack or the `dialogporten` group (recommended: default);
2. the `selfLinks` decorator (recommended: yes);
3. "Tilbake til innboks" (back to the inbox) once Arbeidsflate exists (recommended: live with the start page, and
   raise it upstream);
4. amending task 10's smoke step;
5. the pin cadence;
6. npm registry access for image builds.

**Verification.** `components/receipt/smoke-test.sh` as in report section 7.1:
- health;
- the 302 to `authentication?goto=http://local.altinn.cloud:8000/receipt/…` and back;
- the page, bundle and CSS;
- `users/current` (proves the certificate bind);
- `instances/…?includeParty=true` with party 500000, `isArchived`, and rewritten links that return the attachment's
  bytes;
- the language cookie and the hidden groups;
- 403 for user 1002 and 404 for an unknown guid;
- no access-token warning in the log;
- optionally a Chromium render through the proxy.

`run-platform-regression.sh` must stay green. Add the README, `vendor/UPSTREAM.md` and the `localtest/README.md` rows.
Once tasks 10 and 11 are in, add a check that an archived dialog's go-to URL ends on the receipt.
