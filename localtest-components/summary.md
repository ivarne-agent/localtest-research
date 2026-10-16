# Summary and recommendation

Question: localtest lags behind the platform components it emulates. Could it use a subtree merge of the real
components instead, and which component is easiest to replace with a container started by `studioctl env up`?
Register and the fake login page stay in localtest.

Research date 2026-10-16. Everything here is from reading code; no real platform container was started against
localtest. Per-strand reports hold the evidence.

## Short answer

1. **The Authorization PDP is the easiest real component to run as a container.** It has no database, its
   image is publicly pullable (`ghcr.io/altinn/altinn-auth/altinn-authorization`), the `decision` endpoint has
   no inbound auth, and it needs only Azurite plus six or seven small "policy information" shim endpoints that
   localtest serves from `testdata`. Estimated 1–2 weeks.
2. **Storage is the component where replacement pays off most, but it is the most expensive.** It carries
   the largest drift and the most hand-porting. Running the real image needs Postgres, Azurite, a fake file
   scanner, a way to push application metadata and texts into Storage, a Key Vault workaround, and someone to
   publish or build the image. Estimated 1–2 weeks for a working path plus two upstream asks.
3. **A subtree merge of platform source is not the right primary tool.** Compiling the real controllers into
   localtest with file-backed repositories moves drift from silent divergence to compile errors but still
   requires re-implementing about 2 300 lines of upstream SQL semantics for Storage, and the altinn-auth
   monorepo (46 MB, 3 400 commits) is not a good subtree source. Where no public image exists (Storage), a
   subtree or pinned clone is useful only as a *build input* so studioctl's dev mode can build the image the
   same way it does for pdf3 and workflow-engine today.
4. **Events, Notifications, Access Management, Resource Registry, Authentication and Profile should stay
   emulated.** For Events and Notifications the real services need Azure Service Bus (emulator plus MS SQL),
   Key Vault-backed tokens and endpoints localtest lacks, while the local value is nil because localtest never
   delivers anything. Access Management cannot learn about any user without Register v2 streams. Authentication
   is tied to ID-porten/Maskinporten. Profile depends on Register v2 and Register stays.

## Why localtest lags

- Storage: 23 commits since the monorepo import touch the Storage copy; since 2026-05 each is a hand-ported
  upstream PR (for example [altinn-studio#21010](https://github.com/Altinn/altinn-studio/pull/21010) mirrors
  [altinn-storage#1136](https://github.com/Altinn/altinn-storage/pull/1136)). The copy records no upstream
  commit, so nobody can tell what is missing. Only 3 of 65 matching files are identical.
- Authorization: forked 2019, refreshed 2023, never synced since. Production changed its role source twice in
  2026 (now Access Management's `policyinformation/roles-and-accesspackages`, keyed by party UUID).
- Notifications: last synced 2024-06-18; current app libraries already call two endpoints localtest lacks.
- Register and Profile: 2019–2020 copies missing three endpoints the current app backend calls.

## Recommended path

**Step 0, cheap and independent of the big decision (days):**
- Add the endpoints apps already call and get 404 for: `notifications/api/v1/future/orders`,
  `orders/{id}/cancel`, `register/api/v1/apps/parties/query`, `profile/api/v1/users/byuuid/{uuid}`,
  `POST profile/api/v1/users`.
- Replace the 4 115-line Notifications copy and the Events copy with small contract-shaped stubs; optionally
  add a localtest page listing sent notifications and published events.
- Give every test user and party a stable `partyUuid` in `testdata` and `testData.json`. Production keys
  authorization by UUID, so this is a prerequisite for any real PDP and aligns the login cookie
  (`AltinnPartyUuid`) with production.

**Step 1, first real container: the Authorization PDP (1–2 weeks):**
- studioctl: `authorization` component on `local.altinn.cloud` with `pathPrefix: /authorization`, image
  pinned to a short SHA, plus an Azurite container (blob containers `metadata` and `delegationpolicies`).
- localtest: serve the PDP's PIP contracts from testdata: Profile `POST internal/user`, Register
  `parties/partylistbyuuid`, Access Management `roles-and-accesspackages`, `getdelegationchanges`,
  `authorizedparties`, Resource Registry access-list endpoints (empty). Upload each app's `policy.xml` to the
  PDP on app discovery with a `studio.designer` token.
- Known costs: policy edits apply after the PDP cache expires (minutes, not 5 s); system-user and instance
  delegation semantics must be expressed as delegation rows; three to six extra HTTP hops on the hottest local
  path, mostly cached.
- Keep the in-process PDP in the image until every supported studioctl has the route (image follows main,
  studioctl may be older; fall-through routing makes this safe).

**Step 2, Storage (1–2 weeks plus upstream asks):**
- Ask the altinn-storage team for a supported local configuration that skips Key Vault and file scan, and for
  a published image (or vendor the source so studioctl dev mode can build it).
- studioctl: `storage` component with `pathPrefix: /storage`, Postgres (shared with workflow-engine or
  separate), Azurite blob + queue, init step for [`postgres_init.sql`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/infra/postgres_init.sql) and the blob container, reset handling.
- studioctl-server or localtest: push application metadata and texts into Storage on app start and change,
  mint the `studio.designer` token, add a fake scanner that marks uploads clean.
- localtest: PDP and Storage Explorer must read instances over HTTP or be dropped (pgAdmin exists already).
- Keep the storage URL authority on localtest because the app's [`LocaltestValidation`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/LocaltestValidation.cs) probes
  `/Home/Localtest/Version` there and shuts the app down on 404.

## Mechanism facts that make this feasible

- `studioctl env up` already composes localtest, pdf3, workflow-engine, its Postgres, Grafana and pgAdmin
  through one manifest; adding a component is an image entry, a component file, a `ComponentID` in four Go
  sites, a [`topology.yaml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/envtopology/topology.yaml) entry, reset handling and the CI image cache list.
- Localtest's [`ProxyMiddleware`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Filters/ProxyMiddleware.cs) is generic: host + longest path prefix, forwarded verbatim, fall-through to
  localtest's own controllers when nothing matches. No app or app-lib configuration changes.
- Caveats: routes match the exact host, so `localhost:5101` (plain `dotnet run`) and `127.0.0.1:8000`
  (studioctl-server probe) bypass the split; a *disabled* route returns 404 instead of falling through, so
  optional components must be omitted rather than disabled; there is no path rewriting.

## Not verified

- No real Storage or PDP container was run against localtest; Key Vault, token validation and Azurite blob
  versioning behaviour are inferred from code.
- Whether private production images exist for Storage, Register, Events (Azure DevOps was unreachable).
- Postgres 18 compatibility of the Storage migrations; PDP startup with empty OED settings; ABAC role-code case
  sensitivity.
