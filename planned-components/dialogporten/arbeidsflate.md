# Arbeidsflate: vendoring plan for `localtest/components/arbeidsflate/`

Research date: 2026-10-20. Read-only against the pinned clones; the running stack was probed over HTTP (sign-in,
discovery, token endpoint, Profile, logout) until it was restarted by another session mid-way (see "Not verified").
Nothing was built; no `pnpm install`. The two patches below were verified with `git apply --check` against a
`git archive` of the pinned commit.

Sibling plans in this folder: [`dialogporten-core.md`](dialogporten-core.md) (the `/dialogporten/` routes, Redis,
CORS), [`shared-prerequisites.md`](shared-prerequisites.md) (Maskinporten token endpoint, Authentication as OIDC
provider, altinncdn), [`resource-registry.md`](resource-registry.md) (`resourcelist`). This plan uses their names
and contracts and lists, in section 3.4, what they still need to add.

## Pins

| Repo | Commit | What |
| --- | --- | --- |
| [`Altinn/dialogporten-frontend`](https://github.com/Altinn/dialogporten-frontend/tree/0011d957ddbd9d77e31fcce842fc41887297942c) | `0011d95` (main, 2026-10-16, release 1.182.1); `origin/main` `73feeb9` (2026-10-20) checked for drift (section 8) | Arbeidsflate: React SPA (`packages/frontend`), Fastify BFF (`packages/bff`) |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810`, the commit vendored in `localtest/components/authentication/vendor/` | Authentication as OIDC provider |
| [`Altinn/altinn-register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) | `f6b5c60`, as vendored in `localtest/components/register/vendor/` | `dialogporten/parties/query`, Profile stand-in |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` | End-user type detection, GraphQL CORS |
| `@fastify/session` 11.1.1 (lockfile) | npm tarball, `lib/fastifySession.js` | Secure-cookie behaviour over plain http |
| `@altinn/altinn-components` 0.76.4 (lockfile) | npm tarball, `dist/global.css` | No external font or image URLs in the CSS |

Local research repo: `localtest/` (the stack this plan must fit), `localtest/components/authentication/`,
`localtest/localtest/` (proxy, fake ID-porten, fake Maskinporten), `localtest/tools/repin-vendor.py`.

## Summary

- Arbeidsflate is a pnpm/turbo monorepo of 740 tracked files (about 6 MB without `.git`, lockfile 516 KB). Two
  production images: the BFF (Node 22, runs TypeScript directly through `@swc-node/register`, the whole workspace
  installed) and the SPA (Vite build served by `nginx:1.25-alpine`, runtime values injected into `index.html` by
  `envsubst`). Vendor the whole tree at the pin, pristine, with `patches/*.patch` applied in the Dockerfiles with
  `git apply --check`. Re-pin is `git archive` plus re-checking the patches.
- Two patches, 4 + 1 hunks, verified to apply: `OIDC_PLATFORM_URL` may carry `http://` (BFF discovery is
  hard-coded `https://` in three files), and the SPA writes its party cookies with domain `local.altinn.cloud`.
  Everything else is configuration or our own files (nginx config, Dockerfiles, compose).
- No patch for the session cookie, but one proxy rule: `@fastify/session` never saves a session when
  `cookie.secure === true` and `request.protocol !== 'https'` (verified in 11.1.1), and the BFF hard-codes
  `secure: true`; without an `X-Forwarded-Proto: https` header the login loops between `/api/cb` and `/api/login`.
  Our nginx in front of the BFF sets that header; the localtest app already strips `Secure` from cookies.
- Host name `af.local.altinn.cloud:8000`, routed as one dedicated host to the SPA's nginx, which proxies `/api/` to
  the BFF (upstream's traefik does the same split). A path prefix is not feasible without about ten more hunks.
- Login is production Authentication's registered-client OIDC flow. The provider code is all vendored and the
  shared-prerequisites plan adds the four missing in-memory repositories, the client seed and the pepper fix. Three
  things verified on the running stack are not in that plan yet: `openid/logout` answers 500 today
  (`OidcProviders:idporten:LogoutEndpoint` is null), the tokens Authentication issues carry no `pid` claim (and
  Dialogporten classifies end users by `pid`), and Authentication's server-side front-channel logout needs
  `af.local.altinn.cloud` resolvable from its container.
- Profile: `users/current` is served by the register component; everything else the BFF asks Profile for
  (settings, favourites, notification settings) is a 404 at the proxy. The inbox works; the profile page partly.
  Maskinporten is needed only for the profile page's username lookup (Register `dialogporten/parties/query`) and for
  the notification log behind a feature flag that defaults to off; the shared plan's token endpoint covers it.
- `/api/init-session` plus `/Home/GetTestUserToken` gives tests a session without the browser flow; the token's
  scopes already include `digdir:dialogporten.noconsent` (verified), which corrects the 2026-10-18 report.
- Effort for this component once the shared prerequisites exist: 3 to 4 days. Risks: whole-workspace `pnpm install`
  in both images (time, disk), the `pid` gap, CSP/CORS details that only show in a browser.

## What the 2026-10-18 report got wrong or stale

| Claim | Now |
| --- | --- |
| "the cookie token has no such scope (`digdir:dialogporten`) today" | Stale. Since the identity rework, every token Authentication issues locally carries `DefaultPortalScopes` = `openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read` (verified by decoding `/Home/GetTestUserToken`). The init-session seam therefore works without a scope change |
| "three OIDC tables (clients, codes, refresh tokens) missing" | Four: `ILoginTransactionRepository` is on the registered-client path twice (also found by the shared-prerequisites strand) |
| "a separate host name is required because `local.altinn.cloud:8000/` is the picker's start page" | The reason is the SPA's and BFF's absolute paths (`/api/*`, `/`, `/assets`), not the start page; section 5.5 |
| "`0002-frontend-local-host.patch`: treat `*.local.altinn.cloud` as `local`" | Wrong target: `local` maps the cookie domain to `app.localhost` and the links to at23. The patch now touches only `getCookieDomain` |
| "P1 Maskinporten token endpoint in the authentication component" | It belongs to the localtest app (`localtest/localtest/Maskinporten/`), where the fake Maskinporten moved on 2026-10-20; the shared plan specifies it |
| Not mentioned at all | the `@fastify/session` secure-cookie behaviour (login loop over http), the logout 500, the missing `pid`, the pepper bug |

## 1. Repository layout and how the images are built

### 1.1 Workspace

| Item | Value | Source |
| --- | --- | --- |
| Package manager | `pnpm@10.34.5` via `packageManager` + corepack (`corepack enable` in both Dockerfiles; corepack downloads pnpm at build time) | [`package.json:24`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/package.json#L24) |
| Node | `>=22.22.0` in `engines`; `.node-version` = `24.15.0` (CI and the frontend image); the BFF image is `node:22-slim` (floating tag). This machine: node 22.23.2, pnpm 10.34.6 | [`package.json:19`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/package.json#L19), [`.node-version`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/.node-version) |
| Workspace | `packages/*`; catalog versions in [`pnpm-workspace.yaml`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/pnpm-workspace.yaml) (`minimumReleaseAge: 1440`, `trustPolicy: no-downgrade`); turbo 2.9.14 | [`turbo.json`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/turbo.json) |
| Packages | `bff` (Fastify 5.12.5, nexus, graphql-tools stitch, typeorm 0.3.31, ioredis, `@fastify/session`, helmet), `frontend` (React 19.2.8, react-router 8.3.0, Vite 8.1.4, `@altinn/altinn-components` 0.76.4, `sse.js`), `bff-types-generated` (graphql-codegen over the BFF schema; `prepare` runs at install), `node-logger` (`@altinn/dialogporten-node-logger`, pino; `prepare: tsc`), `fastify-graphiql`, `embeddable-markdown-html`, `docs` (Astro Starlight, its own image), `e2e` (Playwright against tt02) | `packages/*/package.json` |
| Size | `packages/frontend` 3.1 MB (of which `src/mocks` 732 KB, `tests` 716 KB), `bff` 584 KB, `docs` 580 KB, `CHANGELOG.md` 297 KB, lockfile 516 KB; 740 tracked files | `du`, `git ls-files` |
| Build graph | `turbo build` depends on `build:bff-types-generated` (codegen) and `prepare:@altinn/dialogporten-node-logger`; the BFF's own `build` is `tsc --noEmit` (type check only); the frontend's is `tsc && vite build` | [`turbo.json`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/turbo.json), [`packages/bff/package.json`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/package.json) |
| Runtime | The BFF does not compile: `start` is `./src/app.ts`, whose shebang is `node --import @swc-node/register/esm-register` ([`app.ts:1`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/app.ts#L1)); `@swc-node/register` is a devDependency, which is why the image installs everything. `MIGRATION_RUN=true` makes the same entry point run the TypeORM migrations and exit ([`app.ts:11-16`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/app.ts#L11-L16), [`run-migration.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/run-migration.ts)) | |

### 1.2 Every Dockerfile

| Dockerfile | Builds | Stages and base images | Notes |
| --- | --- | --- | --- |
| [`packages/bff/Dockerfile`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/Dockerfile) (16 lines) | the production BFF image `ghcr.io/altinn/dialogporten-frontend-node-bff:<version>` | one stage `build`: `node:22-slim`, `corepack enable`, `COPY . .` (repo root context), `pnpm install --frozen-lockfile`, `pnpm turbo build`, `ENV PORT=80`, `CMD pnpm --filter bff run start` | The runtime image *is* the build stage: full source and `node_modules` of every workspace package. The migration job runs the same image with `MIGRATION_RUN=true` (`.azure/applications/bff-migration-job/main.bicep`) |
| [`packages/frontend/Dockerfile`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/Dockerfile) (41 lines) | the SPA image `…-frontend:<version>` | `build`: `node:24.15.0-slim`, `ARG BUILD_VERSION`, `pnpm install --frozen-lockfile`, `pnpm --filter frontend run build`; `nginx`: `nginx:1.25-alpine` + `apk add gettext` (envsubst), `COPY dist/ /usr/share/nginx/html/`, `COPY packages/frontend/nginx.conf`, `COPY start.sh`, `CMD /start.sh` | `BUILD_VERSION` only renames the asset files (`assets/[name].<version>.js`, [`vite.config.ts:26-32`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/vite.config.ts#L26-L32)); unset gives hashes. Also used as a dev server (`target: build` in compose) |
| [`packages/docs/Dockerfile`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/docs/Dockerfile) | Starlight docs site | `node:22-slim` build, `nginx:1.25-alpine` | Not vendored as an image; the package stays in the tree for the lockfile (section 1.4) |

CI ([`workflow-docker-build-and-push.yml:22-31`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/.github/workflows/workflow-docker-build-and-push.yml#L22-L31))
builds exactly the first two with context `.`, pushes to `ghcr.io/altinn/dialogporten-frontend-{node-bff,frontend}`
([`ci-cd-main.yml:58`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/.github/workflows/ci-cd-main.yml#L58));
the images are public pulls, a fallback if local builds ever become a problem (but they would not carry the patches,
so this plan builds). Image sizes and build times are not stated anywhere in the repo.
Estimate, not measured: BFF image 1.3 to 2 GB on disk (`node:22-slim` ~75 MB compressed plus the whole workspace's
`node_modules`, which includes Playwright, Astro, vitest and the OpenTelemetry stack), SPA image ~30 MB; first build
of each image 3 to 6 minutes on this machine (install dominates; both images install the same workspace
separately), rebuilds with BuildKit cache under a minute when only sources change.

### 1.3 The SPA's nginx and runtime environment

[`nginx.conf`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf):
`listen 80`, gzip, `try_files $uri $uri/ /index.html`, immutable caching for assets, no-cache for html, HSTS
([L19](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf#L19)),
and the CSP ([L20](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf#L20)):
`default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval' https://js.monitor.azure.com; connect-src 'self'
https:; img-src 'self' data: https:; style-src 'self' 'unsafe-inline' https://altinncdn.no; font-src 'self' data:
https://altinncdn.no; …`. **`connect-src` and `img-src` allow only `'self'` and `https:`**, so every browser call to
`http://local.altinn.cloud:8000` (the subscription stream, dialog content, attachments) is blocked unless our nginx
config says otherwise. No `/api` location: upstream's traefik routes `Host(app.localhost) && PathPrefix(/api)` to the
BFF ([`compose.yml:146`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/compose.yml#L146)).

Runtime values reach the SPA through [`start.sh`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/start.sh):
`envsubst '$APPLICATION_INSIGHTS_INSTRUMENTATION_KEY $APPLICATION_INSIGHTS_DISABLE_DEPENDENCY_TRACKING
$DIALOGPORTEN_STREAM_URL'` over `index.html`, whose inline script sets `window.dialogportenStreamUrl` etc.
([`index.html:10-14`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/index.html#L10-L14),
read in [`config.ts:13-15`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/config.ts#L13-L15)).
Those three are the only runtime variables the SPA has; the environment-dependent links and the cookie domain are
derived from `location.host` at runtime ([`url.ts:52-69`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/auth/url.ts#L52-L69):
`localhost` → `local`, `at.altinn.cloud`/`at23` → `at23`, `yt` → `yt`, `tt.`/`tt02` → `tt02`, anything else,
including `af.local.altinn.cloud`, → `prod`). `index.html` loads the Inter font from `https://altinncdn.no`
([L6](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/index.html#L6)).

### 1.4 What to vendor

`vendor/` = `git archive 0011d95` of the whole repository (every tracked file, no `.git`), pristine. Reasons to take
everything rather than `packages/{bff,frontend,…}`:

- `pnpm install --frozen-lockfile` checks that the lockfile's `importers` (one per workspace package, `docs` and
  `e2e` included) match the projects found by `packages/*`; dropping a package directory would fail the frozen
  install or require a `--filter` deviation from upstream's Dockerfile (not verified either way; taking everything
  needs no verification).
- `.dockerignore` upstream excludes `node_modules`, `build`, `.git`, `*.md`, `dist`, so `CHANGELOG.md` and the docs'
  markdown never enter the build context anyway.
- Tests and mocks (1.4 MB) are inert; `.azure/`, `.github/`, `compose.yml`, `certs/`, `data/`, `oidc-mock/`,
  `maintenance-page/` are small and keep `diff -rq` against `git archive` trivial.

`vendor/UPSTREAM.md`, following the events component's:

```markdown
# Vendored copy of Altinn/dialogporten-frontend
- Repository: https://github.com/Altinn/dialogporten-frontend
- Commit: 0011d957ddbd9d77e31fcce842fc41887297942c (main, 2026-10-16, release 1.182.1)
- Copied paths: the whole tree at that commit (`git archive <commit> | tar -x -C vendor`), no subtree.
- The copy is pristine. TypeScript has no `#if`, so localtest changes are unified diffs in `../patches/`, applied
  by `../Dockerfile*` with `git apply --check` first. `diff -rq vendor/ <git archive>` must be empty except this file.
## Syncing to a newer upstream commit
1. `../../tools/repin-vendor.py <clone> <pin> <new> vendor .` (or re-archive: with no local changes in vendor/ the
   tool only copies, adds and deletes). Note: the tool's `under()` needs the one-line "." special case below.
2. In a scratch export of <new>: `git apply --check -p1 ../patches/*.patch`. A failing patch is regenerated by hand
   (`diff -u`) against the new file; keep each hunk as small as it is now.
3. `git diff <pin> <new> -- packages/bff/Dockerfile packages/frontend/Dockerfile packages/frontend/nginx.conf
   packages/frontend/start.sh` and carry the changes into `../Dockerfile`, `../Dockerfile.frontend`, `../nginx.conf`.
4. Update the commit line, build both images, run `../smoke-test.sh`. Commit the re-pin separately.
```

`localtest/tools/repin-vendor.py` works on a non-.NET tree as it is (it is file-based: `git archive`, byte
comparison, `git merge-file`), with one change: `under(paths, rel)` returns false for the path `"."`, so vendoring the
whole tree needs `if p == ".": return True` (one line) or an explicit list of top-level entries. Its `bin/`/`obj/`
skip is harmless; add `node_modules` to the skipped set so a developer's `pnpm install` inside `vendor/` is never
compared.

Component layout:

```
localtest/components/arbeidsflate/
  Dockerfile              # BFF image; mirrors vendor/packages/bff/Dockerfile, deviations marked "localtest:"
  Dockerfile.frontend     # SPA image; mirrors vendor/packages/frontend/Dockerfile
  .dockerignore           # vendor/node_modules, vendor/**/dist, vendor/.git (none), *.md
  nginx.conf              # ours, derived from vendor/packages/frontend/nginx.conf (section 5.3)
  patches/0001-bff-oidc-discovery-scheme.patch
  patches/0002-frontend-cookie-domain-localtest.patch
  postgres-init/40-arbeidsflate.sh
  maskinporten-client.json   # a committed RSA JWK with kid, base64-encoded into MASKINPORTEN_JWK by compose (test key only)
  vendor/                 # pristine tree + UPSTREAM.md
  README.md, smoke-test.sh
```

`Dockerfile` (BFF), mirroring upstream line for line:

```dockerfile
# localtest: upstream's context is the repository root; here it is the component folder, and the patches are
# applied in a stage of their own so a patch that no longer applies fails the build loudly.
FROM node:22-slim AS source
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY vendor/ .
COPY patches/ /patches/
RUN git apply --check -p1 /patches/*.patch && git apply -p1 /patches/*.patch

FROM node:22-slim AS build
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"
RUN corepack enable

WORKDIR /app
COPY --from=source /app .          # localtest: upstream has COPY . .

RUN pnpm install --frozen-lockfile

RUN pnpm turbo build

ENV PORT=80

EXPOSE $PORT
CMD [ "pnpm", "--filter", "bff", "run", "start" ]
```

`git apply` works outside a repository (it then behaves like `patch -p1`); `node:22-slim` has neither `git` nor
`patch`, hence the `apt-get` in the `source` stage only. `Dockerfile.frontend` gets the same `source` stage and
`COPY --from=source`, then upstream's two stages unchanged except `COPY nginx.conf /etc/nginx/conf.d/default.conf`
from the component folder (`localtest:` line) instead of `./packages/frontend/nginx.conf`. `BUILD_VERSION` is left
unset (hashed asset names).

## 2. Local modifications: what must change for plain http on `local.altinn.cloud:8000`

Classification: **config** = environment variable, **patch** = `patches/*.patch`, **own** = a file of ours
(nginx config, Dockerfile, compose, localtest app), **proxy** = behaviour the localtest app already has.

| # | Site | What breaks locally | Class | Resolution |
| --- | --- | --- | --- | --- |
| 1 | BFF discovery URL `https://${oidc_url}/.well-known/openid-configuration` in [`oidc.ts:165`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L165), [`verifyToken.ts:106`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L106), [`HealthChecks.ts:77`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/azure/HealthChecks.ts#L77) | Authentication is http; the BFF refuses to start (`fetchOpenIDConfig` throws in the plugin) | **patch 0001** | `config.oidcDiscoveryUrl` keeps a scheme when `OIDC_PLATFORM_URL` has one. Unchanged on `origin/main` |
| 2 | Session cookie `secure: true` ([`server.ts:66`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/server.ts#L66)) with `trustProxy: true` ([L39](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/server.ts#L39)) | `@fastify/session` 11.1.1 `onSend`: `isInsecureConnection = cookieOpts.secure === true && request.protocol !== 'https'` → the session is not saved and no cookie is set. `/api/login` stores `state`/`nonce`/`codeVerifier` in the session, so `/api/cb` finds no state and redirects to `/api/login` again: a loop | **own** (nginx) | Our nginx sets `proxy_set_header X-Forwarded-Proto https` on `/api/`; Fastify's `request.protocol` honours it with `trustProxy`. The cookie is then emitted with `Secure`, which the localtest app strips ([`Forwarder.cs:112-125`](../../localtest/localtest/Proxy/Forwarder.cs)). [`fastifyHeaders.ts:100-102`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/fastifyHeaders.ts#L100-L102) sets `secure = true` on the same condition, which is already the value |
| 3 | `HOSTNAME` → `redirect_uri` `${hostname}/api/cb` ([`oidc.ts:264`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L264), [L344](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L344)) | – | **config** | `HOSTNAME=http://af.local.altinn.cloud:8000`; must equal the seeded client's redirect URI byte for byte (exact string match in Authentication) |
| 4 | `PLATFORM_BASEURL`, `DIALOGPORTEN_URL`, `MASKINPORTEN_ISSUER`, `REDIS_CONNECTION_STRING`, `DB_CONNECTION_STRING` | defaults point at at23 / Maskinporten test | **config** | section 7 |
| 5 | SPA host detection → `prod` for `af.local.altinn.cloud` ([`url.ts:52-69`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/auth/url.ts#L52-L69)): links to `info.altinn.no` / `am.ui.altinn.no`, cookie domain `.altinn.no` ([L202-L211](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/auth/url.ts#L202-L211)) | Links out of the inbox go to production info pages (acceptable); the SPA's `document.cookie = AltinnPartyId=…; Domain=.altinn.no` ([`cookie.ts:26-30`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/cookie.ts#L26-L30)) is silently rejected by the browser, so a party change in the inbox is not seen by the apps | **patch 0002** | `getCookieDomain()` returns `local.altinn.cloud` for `*.local.altinn.cloud`, the domain Authentication sets the same cookies with (`GeneralSettings:HostName`). Links stay prod's; a full `localtest` host env would be 5 hunks for no test value |
| 6 | SPA CSP `connect-src 'self' https:`, `img-src 'self' data: https:` ([`nginx.conf:20`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf#L20)) | The subscription stream to Dialogporten and the dialog-content and attachment fetches to `http://local.altinn.cloud:8000` are blocked | **own** (nginx) | `connect-src 'self' http://local.altinn.cloud:8000 https:`, `img-src 'self' data: http://local.altinn.cloud:8000 https:` |
| 7 | SPA stream URL `window.dialogportenStreamUrl` | – | **config** | `DIALOGPORTEN_STREAM_URL` (section 4) |
| 8 | HSTS from nginx ([L19](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf#L19)) and helmet (`fastifyHeaders.ts`) | none: browsers ignore `Strict-Transport-Security` received over http (RFC 6797 §8.1) | **own** | Drop the header in our nginx anyway so a tester who once opened `https://af.local.altinn.cloud` does not get pinned |
| 9 | BFF helmet CSP with `upgrade-insecure-requests`, COEP `require-corp`, CORP `same-origin` ([`fastifyHeaders.ts:26-59`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/fastifyHeaders.ts#L26-L59)) | none: these apply to documents; the BFF serves JSON and redirects, and the SPA calls it same-origin | – | – |
| 10 | BFF CORS origins `https://app.localhost`, `http://localhost:3000` ([`server.ts:45`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/server.ts#L45)) | none: same origin | – | – |
| 11 | `fetch('https://altinncdn.no/orgs/altinn-orgs.json')` ([`organizations/service.ts:60`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/organizations/service.ts#L60)) | Needs internet; on failure returns `[]` (names and logos of service owners missing, no error) | **config-less** | Leave on the internet (the stack's `authentication` exchange has the same dependency today). The shared plan's CDN stub is a one-line patch away (`ALTINN_CDN_URL`) if offline use is ever wanted; not proposed now |
| 12 | Service-resource environment filter picks `at23TestIds` for any non-prod/tt02 platform URL ([`serviceResources/config.ts:20-33`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/config.ts#L20-L33)) | 7 at23 ids excluded, none `app_ttd_*` | – | – |
| 13 | `ENVIRONMENT` enum `dev|test|staging|yt01|prod` ([`config.ts:46`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/config.ts#L46)) | only `prod` changes behaviour (org exclusions) | **config** | leave the default `test` |

Nothing on `origin/main` (section 8) touches any of these sites. Upstream has not made the scheme or the host map
configurable.

### 2.1 Patch 0001, verified with `git apply --check` against `git archive 0011d95`

```diff
localtest: let OIDC_PLATFORM_URL carry a scheme. Upstream hard-codes https:// in front of it in three places;
the localtest stack serves Authentication over plain http on local.altinn.cloud:8000.
Against Altinn/dialogporten-frontend 0011d957ddbd9d77e31fcce842fc41887297942c.

--- a/packages/bff/src/config.ts
+++ b/packages/bff/src/config.ts
@@ -66,6 +66,8 @@ const config = {
   host: env.HOST,
   hostname: env.HOSTNAME,
   oidc_url: env.OIDC_PLATFORM_URL,
+  // localtest: a scheme in OIDC_PLATFORM_URL is kept (plain http); upstream always prefixes https://
+  oidcDiscoveryUrl: `${/^https?:\/\//.test(env.OIDC_PLATFORM_URL) ? env.OIDC_PLATFORM_URL : `https://${env.OIDC_PLATFORM_URL}`}/.well-known/openid-configuration`,
   client_id: env.OIDC_CLIENT_ID,
   client_secret: env.OIDC_CLIENT_SECRET,
   platformBaseURL: env.PLATFORM_BASEURL,
--- a/packages/bff/src/auth/oidc.ts
+++ b/packages/bff/src/auth/oidc.ts
@@ -161,8 +161,8 @@ export const handleLogout = async (request: FastifyRequest, reply: FastifyReply, providerConfig: ProviderConfig) => {
 };
 
 const plugin: FastifyPluginAsync = async (fastify) => {
-  const { client_id, oidc_url, hostname, client_secret } = config;
-  const issuerURL = `https://${oidc_url}/.well-known/openid-configuration`;
+  const { client_id, hostname, client_secret } = config;
+  const issuerURL = config.oidcDiscoveryUrl;
   const providerConfig = await fetchOpenIDConfig(issuerURL);
 
   const handleFrontChannelLogout = async (request: FastifyRequest, reply: FastifyReply) => {
--- a/packages/bff/src/auth/verifyToken.ts
+++ b/packages/bff/src/auth/verifyToken.ts
@@ -102,8 +102,7 @@ const getIsTokenValid = async (
 };
 
 const plugin: FastifyPluginAsync = async (fastify, _) => {
-  const { oidc_url } = config;
-  const issuerURL = `https://${oidc_url}/.well-known/openid-configuration`;
+  const issuerURL = config.oidcDiscoveryUrl;
   const providerConfig = await fetchOpenIDConfig(issuerURL);
 
   fastify.decorate('verifyToken', (allowTokenRefresh: boolean) => {
--- a/packages/bff/src/azure/HealthChecks.ts
+++ b/packages/bff/src/azure/HealthChecks.ts
@@ -74,7 +74,7 @@ const healthCheckList: HealthCheck[] = [
     checkFn: async () => {
       try {
         // todo: change to a URL we can use to check secret id and secret key
-        await axios.get(`https://${config.oidc_url}/.well-known/openid-configuration`, {
+        await axios.get(config.oidcDiscoveryUrl, {
           timeout: 30000,
         });
         return { status: 'ok' };
```

The production value `platform.at23.altinn.cloud/authentication/api/v1/openid` keeps working (no scheme → `https://`),
so this is also the shape of an upstream pull request that would remove the patch.

### 2.2 Patch 0002, verified the same way

```diff
localtest: party cookies written by the SPA get the domain production Authentication uses in the stack
(local.altinn.cloud), so the inbox and the apps share AltinnPartyId/AltinnPartyUuid. Upstream maps every
unknown host to prod (.altinn.no), which the browser rejects on af.local.altinn.cloud.
Against Altinn/dialogporten-frontend 0011d957ddbd9d77e31fcce842fc41887297942c.

--- a/packages/frontend/src/auth/url.ts
+++ b/packages/frontend/src/auth/url.ts
@@ -200,6 +200,9 @@ export const getProfileHelpLink = (language?: string) => {
 };
 
 export const getCookieDomain = () => {
+  if (location.hostname.endsWith('.local.altinn.cloud')) {
+    return 'local.altinn.cloud';
+  }
   const hostMap: Record<hostEnv, string> = {
     local: 'app.localhost',
     at23: '.at23.altinn.cloud',
```

Optional: it can be dropped for a first iteration (the inbox then simply cannot change the apps' party cookie).

## 3. Login: the BFF's OIDC flow end to end

### 3.1 What the BFF does ([`oidc.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts), [`verifyToken.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts))

| Step | BFF | Calls on Authentication (`http://local.altinn.cloud:8000/authentication/api/v1/…`) |
| --- | --- | --- |
| Start-up | `fetchOpenIDConfig(oidcDiscoveryUrl)` twice (oidc and verifyToken plugins); reads `issuer`, `authorization_endpoint`, `token_endpoint`, `end_session_endpoint`; nothing else from the document, no JWKS, no `userinfo` | `GET openid/.well-known/openid-configuration` (verified live: `issuer http://local.altinn.cloud:8000/authentication/api/v1/openid/`, `authorization_endpoint …/authorize/`, `token_endpoint …/token/`, `end_session_endpoint …/openid/logout/`, `jwks_uri`, `response_types_supported: ["token"]` which the BFF ignores) |
| `GET /api/login` ([L361](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L361)) | stores `codeVerifier`, `state`, `nonce` in the session; 302 to `authorization_endpoint` with `response_type=code`, `client_id`, `redirect_uri={HOSTNAME}/api/cb`, `scope=digdir:dialogporten.noconsent openid altinn:portal/enduser altinn:correspondence.forward.check`, `acr_values=idporten-loa-substantial` (`idporten-loa-high` with `?idporten_loa_high=true`), `state`, `nonce`, `code_challenge` S256 ([L343-L353](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L343-L353)) | `GET authorize/?…` → [`OidcServerService.Authorize`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L100): client lookup, [basics](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Services/Validation/AuthorizeRequestValidator.cs#L39-L47) (code, `openid`, PKCE S256, state, nonce, acr in the catalogue: `idporten-loa-substantial` is in both the built-in ID-porten table and the localtest level table), [client binding](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Services/Validation/AuthorizeClientPolicyValidator.cs#L9) (exact `redirect_uri`, scopes ⊆ `AllowedScopes`), login transaction, then session reuse if the browser already has an `AltinnStudioRuntime` cookie with a session of acr ≥ substantial, else redirect to the fake ID-porten's picker with `acr_values` (the picker preselects level 3) |
| picker → `GET upstream/callback` | – | [`HandleUpstreamCallback`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L251): identity from the fake's tokens, Profile lookup, session, cookies (`AltinnStudioRuntime`, `AltinnSession`, `AltinnPartyId`, `AltinnPartyUuid`, all `Domain=local.altinn.cloud`), then for a registered client [`CreateDownstreamAuthorizationCode`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1272) (120 s) and 302 to `http://af.local.altinn.cloud:8000/api/cb?code&state` |
| `GET /api/cb` ([L366](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L366)) | state check (mismatch → `/api/login`); `POST token_endpoint` with `Authorization: Basic client_id:client_secret`, form `grant_type=authorization_code, client_id, code_verifier, code, storedNonceTruth (ignored upstream), redirect_uri` ([L258-L272](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L258-L272)); decodes `id_token` without verifying it: `nonce` (must equal the stored one), `sid`, `pid` (falls back to `sub`), `locale` (absent → `nb`); session gets `access_token`, `refresh_token`, `expires_in`, `refresh_token_expires_in`, `scope`; Redis `idp-sid:{sid}` → session id for 8 h ([L311](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L311)); 302 `/` | `POST token/` → [`OidcTokenController.Token`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcTokenController.cs#L27) → [`TokenService.ExchangeAuthorizationCodeAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L41): client secret (`client_secret_basic` parsed first, [L124-L151](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcTokenController.cs#L124-L151)), code row, PKCE, atomic consume, id token (claims `sub`, `sid`, `iss`, party/user ids, `pid` when the session's external id is a person, `acr`, `nonce`, `aud`, `auth_time`, `scope`, [`ClaimsPrincipalBuilder.cs:51-157`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L51-L157)), access token (same claims, no `nonce`/`aud`), refresh token ([`TryIssueInitialRefreshAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L444)); response `access_token, id_token, token_type, expires_in` (`OidcTokenValidityMinutes` 10 → 600), `scope`, `refresh_token`, `refresh_token_expires_in` (`OidcRefreshTokenValidityMinutes` 30 → 1800) ([L101-L110](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcTokenController.cs#L101-L110)) |
| every request with `verifyToken(true)` (`/api/isAuthenticated`, polled every 30 s by the SPA, [`AuthContext.tsx:14-18`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/components/Login/AuthContext.tsx#L14-L18)) | when the access token expires within 60 s and the refresh token is valid: `POST token_endpoint` `grant_type=refresh_token` with Basic auth ([`verifyToken.ts:8-50`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L8-L50)); keeps the old `id_token`; `/api/graphql` never refreshes (only GraphiQL does) | [`RefreshAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L92): rotation, reuse detection, session must still exist |
| `GET /api/logout` ([L389](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L389)); also what `verifyToken` does on a missing or expired session ([`verifyToken.ts:113-115`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L113-L115)) | without an `id_token` → 401; else destroys the session and 302 `end_session_endpoint?id_token_hint=` ([L150-L161](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L150-L161)); no `post_logout_redirect_uri` | `GET openid/logout/` → [`EndSessionAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L469): the hint's issuer is Authentication's own, so [`ChooseProviderByIssuer`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1461) throws and is caught (warning); the `sid` comes from the `AltinnStudioRuntime` cookie, which the browser sends because this request is on `local.altinn.cloud`; session deleted, refresh families revoked, [`NotifyDownstreamClientsOfLogout`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1504) → a **server-side** `GET {FrontchannelLogoutUri}?sid&iss` ([`OidcDownstreamLogoutClient.cs:53`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Clients/OidcDownstreamLogoutClient.cs#L53), not an iframe); then 302 to the upstream provider's `LogoutEndpoint` with `post_logout_redirect_uri=…/logout/handleloggedout` ([L573-L580](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L573-L580)) |
| `GET /api/frontchannel-logout?iss&sid` ([L168-L199](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L168-L199)) | `iss` must equal `providerConfig.issuer` (it does: `…/openid/`); destroys the session found under `idp-sid:{sid}` | – |

Claims the BFF needs: `id_token.nonce`, `id_token.sid`, `id_token.pid` (or `sub`), and an `access_token` Dialogporten
accepts. The BFF never validates signatures or reads JWKS.

### 3.2 What Dialogporten needs from that access token

Dialogporten's GraphQL classifies the caller with [`GetUserType`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Extensions/ClaimsPrincipalExtensions.cs#L296-L304):
a person **is a token with a `pid` claim**; system user, service owner and self-registered e-mail user otherwise;
anything else is `Unknown` and the request interceptor rejects it with `AUTH_USER_TYPE_UNKNOWN`
([`DialogportenHttpRequestInterceptor.cs:29-55`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Common/DialogportenHttpRequestInterceptor.cs#L29-L55)).
Scope `digdir:dialogporten.noconsent` is in every locally issued token (`DefaultPortalScopes`,
[`GeneralSettings.cs:168`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Configuration/GeneralSettings.cs#L168),
added by [`AddLocalScopes`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L754)
to the session, from which codes and tokens copy their scopes; the BFF's requested `altinn:correspondence.forward.check`
is validated against the client's allowed scopes but does not end up in the token).

### 3.3 What the vendored Authentication has, verified on the running stack (2026-10-20, before it was restarted)

| Probe | Result |
| --- | --- |
| `GET authentication/api/v1/openid/.well-known/openid-configuration` | 200, the document in 3.1 |
| `GET authorize?client_id=localtest-arbeidsflate&…` (full BFF parameter set) | **500**: `OidcServerClientRepository.GetClientAsync` → `Npgsql: No suitable host was found` (the Postgres implementation, no database) |
| `POST token` with Basic auth, `grant_type=authorization_code` | **500**, same cause via `TokenService.AuthenticateClientAsync` |
| `GET openid/logout` with a fresh picker session's cookies | **500**: `ArgumentNullException: uriString` at [`OidcServerService.cs:574`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L574) (`provider.LogoutEndpoint` is null for the localtest `idporten` provider; container log) |
| `/Home/GetTestUserToken?userId=1337&authenticationLevel=3` decoded | `iss …/openid/`, `scope openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read`, `sid`, `sub urn:altinn:party:uuid:…`, `urn:altinn:party:uuid`, `urn:altinn:partyid 501337`, `urn:altinn:userid 1337`, `acr idporten-loa-substantial`, `urn:altinn:authlevel 3`; **no `pid`**, same for user 1003 |
| The fake ID-porten's own id_token for user 1337 (code redeemed directly at `localtest/api/idporten/token`) | carries `pid: 01839012345` |
| `POST profile/api/v1/internal/user` with the SSN | 200, `party.ssn` and `party.partyUuid` present |
| `GET profile/api/v1/users/current` with the token | 200, full `UserProfile` with `party.person` |
| In-memory repositories registered today | `IUnregisteredClientRepository`, `IUpstreamLoginTransactionRepository`, `IOidcSessionRepository` ([`LocaltestAuthenticationExtensions.cs:123-125`](../../localtest/components/authentication/fakes/LocaltestAuthenticationExtensions.cs)) |

### 3.4 What the shared-prerequisites strand must add (exact; aligned with its section 3)

Already in [`shared-prerequisites.md`](shared-prerequisites.md) section 3 and used by this plan as is: the four
in-memory repositories (`IOidcServerClientRepository`, `ILoginTransactionRepository`, `IAuthorizationCodeRepository`,
`IRefreshTokenRepository`, mirroring the Postgres classes under
[`RepositoryImplementations/OidcServer/`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/RepositoryImplementations/OidcServer)),
the client seed `Localtest:Authentication:OidcClients:0` with `ClientId=arbeidsflate-localtest`,
`ClientSecret=arbeidsflate-localtest-secret`, redirect `http://af.local.altinn.cloud:8000/api/cb`, post-logout
`http://af.local.altinn.cloud:8000/`, front-channel `http://af.local.altinn.cloud:8000/api/frontchannel-logout`, the
four scopes, PKCE S256, nonce required, and the pepper fix (`OidcRefreshTokenPepper` must be base64; `localtest-pepper`
is not, verified with Python's strict decoder). This plan's compose uses exactly those ids and URLs.

Not in that plan yet; needed for this component:

1. **Logout endpoint of the upstream provider.** `OidcProviders:idporten:LogoutEndpoint` is unset, so every
   `openid/logout` is a 500 today (verified). Add to the fake ID-porten `GET localtest/api/idporten/endsession`
   (ID-porten's RP-initiated logout: `id_token_hint`, `post_logout_redirect_uri`, `state` → 302 to the
   `post_logout_redirect_uri`, which Authentication sets to `…/authentication/api/v1/logout/handleloggedout`,
   [`LogoutController.cs:95`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/LogoutController.cs#L95)),
   list it as `end_session_endpoint` in the fake's discovery, and set `OidcProviders:idporten:LogoutEndpoint` in
   `LocaltestAuthenticationExtensions.cs` (about 20 lines in `IdPortenApi.cs`, one configuration line). Without it
   Arbeidsflate's logout and every expired-session redirect end in a 500 page.
2. **`pid` in the issued tokens.** The fake's id_token has `pid`; Authentication maps `pid` → `SSN`
   ([`AuthenticationHelper.cs:128`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/AuthenticationHelper.cs#L128)),
   looks the profile up by SSN and overwrites `SSN` with `userProfile.Party.SSN`
   ([`OidcServerService.cs:1806`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1806)),
   derives the session's `ExternalId` as `urn:altinn:person:identifier-no:{SSN}`
   ([L1327-L1337](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1327-L1337)),
   and the token builders emit `pid` only from such an `ExternalId`
   ([`ClaimsPrincipalBuilder.cs:80`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L80),
   [L202](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L202)).
   The issued tokens have no `pid`, so one of these steps loses the SSN in the localtest composition; the most
   likely spot is the Profile response deserialisation of `party.ssn` into Authentication's `UserProfile.Party`
   model (`JsonSerializer.Deserialize<UserProfile>` in `ProfileService.cs:51` with its own options). Not pinned down
   here; it needs a run with a debugger or a log line after `IdentifyOrCreateAltinnUser`. **Until it is fixed, the
   access token the BFF forwards is `Unknown` to Dialogporten and the inbox lists nothing.** The code path is shared
   with the registered-client flow, so the fix is in the authentication component, not here.
3. **Name resolution for the front-channel logout.** Authentication calls the client's `FrontchannelLogoutUri`
   server-side, so the `authentication` container needs `extra_hosts: af.local.altinn.cloud:10.200.0.10` (the
   localtest app) next to the existing `local.altinn.cloud` entry; otherwise the call fails (logged, logout
   continues, the BFF session survives until its own expiry).
4. The seeded client's `RedirectUris` entry must be the exact string the BFF sends:
   `http://af.local.altinn.cloud:8000/api/cb` (section 7 `HOSTNAME`).

## 4. Everything else the BFF and the SPA call

Token legend: **user** = the session's Altinn access token from 3.1; **mp** = Maskinporten → `exchange/maskinporten`
Altinn token ([`maskinporten.ts:110-142`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts#L110-L142),
cached in Redis); **dialog** = the dialog token the GraphQL `dialogById.dialogToken` field carries (Ed25519 JWS from
Dialogporten); **none** = anonymous.

| Caller | Call | Token | Needed for | Served today | Gap and behaviour |
| --- | --- | --- | --- | --- | --- |
| BFF | `POST {DIALOGPORTEN_URL}/graphql` for every stitched query ([`api.ts:22-31`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/api.ts#L22-L31)); the schema itself comes from the npm package `@digdir/dialogporten-schema` 1.120.0, not from the service | user | everything: parties, dialogs, dialog details, search | no (Dialogporten strand: `/dialogporten/graphql` → GraphQL host, path base rewritten) | Hard dependency. Dialogporten's `Altinn` JWT scheme must trust `http://local.altinn.cloud:8000/authentication/api/v1/openid/` (core plan); token needs `pid` (3.4) |
| BFF | `GET {DIALOGPORTEN_URL}/health` | none | `/api/health` only | with the core plan | – |
| SPA | `POST {DIALOGPORTEN_STREAM_URL}` with `Accept: text/event-stream`, `Authorization: Bearer <dialog>` ([`useDialogByIdSubscription.ts:116-129`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/api/hooks/useDialogByIdSubscription.ts#L116-L129)) | dialog | live updates of an open dialog; reconnects with back-off, never blocks the page | with the core plan | HotChocolate serves SSE on `/graphql` itself; `/graphql/stream` is APIM's path. Set `DIALOGPORTEN_STREAM_URL=http://local.altinn.cloud:8000/dialogporten/graphql` unless the core strand adds the `/stream` alias (not verified at runtime). CORS: Dialogporten `GraphQl__Cors__AllowedOrigins__0=http://af.local.altinn.cloud:8000` (core plan 8.2); CSP item 6 |
| SPA | `fetch(content.url, Authorization: Bearer <dialog>)` for `mainContentReference`, receipts, correspondence content ([`MainContentReference.tsx:76-81`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/components/MainContentReference/MainContentReference.tsx#L76-L81)); attachment links are plain navigations | dialog | dialog body for app dialogs | the adapter / app strands decide what those URLs are and whether they accept dialog tokens | CSP item 6; otherwise shows "could not load content" with a retry button |
| BFF | Profile `GET users/current` ([`profile/service.ts:154`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts#L154)) | user | name, e-mail, phone, party on the profile page and in the header | **yes**: the register component's `LocaltestProfileController` (`profile/api/v1/users/current`, verified) | – |
| BFF | Profile `GET users/current/party-groups/favorites` ([L193](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts#L193)), `PUT`/`DELETE …/favorites/{partyUuid}` ([L59](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts#L59), [L133](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts#L133)) | user | favourite parties | no (404 at the proxy's register route) | read: `[]` (soft); add: GraphQL error (the star button fails); delete: swallowed |
| BFF | Profile `PATCH users/current/profilesettings` ([L214](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts#L214)) | user | preselected party, language, show client units, show deleted entities | no | throws → GraphQL error; the party picker still works for the session (the choice is kept in the SPA, not persisted) |
| BFF | Profile `GET users/current/notificationsettings/parties`, `POST users/current/verification/send`, `GET …/verified-addresses`, `PUT …/notificationsettings/…` ([`notifications/service.ts:23-300`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/notifications/service.ts#L23)) | user | profile → notifications page | no | the list returns empty on 404 (soft); edits fail. The profile fake is the register component's; adding these five routes there (~150 lines over an in-memory map) is the right place when the profile page matters |
| BFF | Register `POST register/api/v1/dialogporten/parties/query?fields=user.name` ([`username/service.ts:7`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/username/service.ts#L7)), `POST …/set-username` | mp | the profile page's username field (`partyUsername` query, enabled whenever a party uuid is known, [`useUsername.tsx:27`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/pages/Profile/useUsername.tsx#L27)); editing is behind `profile.enableSetUserName` (default off) | the endpoints: **yes**, vendored Register [`DialogPortenController`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs#L37), policy `InternalOrPlatformAccess` = scope `altinn:register/partylookup.admin` or a platform access token ([`RegisterHost.cs:170-171`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs#L170-L171)); the Maskinporten token: **no** (no token endpoint in the fake Maskinporten) | `getUsername` returns `null` on any failure (soft). With the shared plan's `POST localtest/api/maskinporten/token` and `MASKINPORTEN_SCOPE=altinn:register/partylookup.admin altinn:serviceowner/notifications.create` the exchanged token carries the scope and the query works |
| BFF | Notifications `GET notifications/api/v1/future/log?dialogId=` ([`notifications/service.ts:267`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/notifications/service.ts#L267)) | mp | notification log on a dialog, behind `dialogDetails.enableNotificationLogs` (default **off**, [`featureApi.ts:8-22`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/features/featureApi.ts#L8-L22)) | no Notifications component | not called with default flags |
| BFF | Correspondence `GET correspondence/api/v1/correspondence/{id}/forward/check`, `POST …/forward` ([`correspondence/service.ts:37`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/correspondence/service.ts#L37)) | user | forward-by-e-mail, behind `dialogDetails.enableForwardByEmail` (default off) | no | not called; would return `notAllowed` |
| BFF | Resource Registry `GET resourceregistry/api/v1/resource/resourcelist?includeAltinn2=false&includeApps=true&includeMigratedApps=true` ([`serviceResources/service.ts:77`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/service.ts#L77)), at first use and every 6 h ([`refresh.ts:34-38`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/refresh.ts#L34-L38)) | none | service and organisation filter lists, resource titles in filters | no route today; [`resource-registry.md`](resource-registry.md) | `[]` and a log line (soft): the inbox shows dialogs (their titles come from Dialogporten), the "service" filter is empty |
| BFF | `https://altinncdn.no/orgs/altinn-orgs.json` ([`organizations/service.ts:60`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/organizations/service.ts#L60)), cached 24 h in Redis | none | service-owner names and logos | internet | `[]` when offline (soft). Hard-coded URL; see item 11 |
| BFF | Azure App Configuration (`APP_CONFIG_CONNECTION_STRING`) for feature flags and the alert banner | – | – | – | empty string → defaults, `warn` log per request ([`featureApi.ts:34`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/features/featureApi.ts#L34)); on `origin/main` the banner moved to the provider SDK with the same fallback |
| BFF | Redis (`ioredis`, connects at import, [`redisClient.ts:5`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/redisClient.ts#L5)) | – | sessions (`RevocableRedisStore`, [`server.ts:72`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/server.ts#L72)), `idp-sid:*`, token caches, organisations, service resources | no Redis in the stack | the shared plan's `redis` service; BFF on database 1 (`redis://redis:6379/1`), no password |
| BFF | Postgres via TypeORM: entities `profile`, `group`, `party`, `saved_search` ([`entities.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/entities.ts)); 4 migrations, 90 lines ([`migrations/`](https://github.com/Altinn/dialogporten-frontend/tree/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/migrations)), run from the TS sources ([`data-source.ts:16`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/data-source.ts#L16)) by the same image with `MIGRATION_RUN=true` | – | saved searches, favourite groups, per-`pid` profile row | shared `postgres:16-alpine` (or 18 after the shared plan's bump; TypeORM/pg are indifferent) | `postgres-init/40-arbeidsflate.sh` creates role and database; a one-shot `arbeidsflate-migrate` service runs the migrations before the BFF starts. No reset on start: the rows are keyed by `pid` and harmless across runs; `psql -c 'drop schema public cascade'` in the init script is the reset if ever wanted |
| BFF | OpenTelemetry OTLP exporter | – | – | – | `OTEL_EXPORTER_OTLP_ENDPOINT` unset → disabled |
| SPA | `https://altinncdn.no/fonts/inter/v4.1/inter.css` ([`index.html:6`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/index.html#L6)); `@altinn/altinn-components` CSS has no external `url()` (checked in the 0.76.4 tarball) | – | the Inter font | internet | system font fallback offline |
| SPA | `https://js.monitor.azure.com` (Application Insights), Skyra survey | – | – | – | only with an instrumentation key / the `global.enableSkyra` flag; both off |
| SPA | `/api/features`, `/api/alert-banner`, `/api/isAuthenticated`, `/api/graphql`, `/api/login`, `/api/logout`, `/api/frontchannel-logout` | session cookie | – | the BFF | all relative to the SPA's origin (section 5) |
| SPA | reads cookie `AltinnPartyUuid` at start ([`main.tsx:53`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/main.tsx#L53)) | – | preselected party | Authentication sets it with `Domain=local.altinn.cloud`, `HttpOnly=false`, which the browser sends to `af.local.altinn.cloud` | – |

What the inbox needs to *work* (list dialogs, open one): Dialogporten GraphQL on the user token with `pid`, Redis,
Postgres, the login flow. Everything else degrades softly except the profile page's settings and favourites.

## 5. Hosting shape

### 5.1 Host name `af.local.altinn.cloud:8000`, recommended

The localtest app routes dedicated hosts first ([`PlatformRoutes.cs:24-28`](../../localtest/localtest/Proxy/PlatformRoutes.cs),
[`Program.cs:56-65`](../../localtest/localtest/Program.cs)): one `(host, upstream)` tuple sends every path on that
host to one upstream, with the original `Host`, YARP's `X-Forwarded-*` and the `Secure` strip. Adding Arbeidsflate is
one tuple and one default upstream:

```csharp
["arbeidsflate"] = "http://arbeidsflate:80",          // DefaultUpstreams
("af.local.altinn.cloud", "arbeidsflate"),             // Hosts
```

plus the `localtest` service's network alias `af.local.altinn.cloud` (so containers resolve it to 10.200.0.10 like
`local.altinn.cloud`), the `/etc/hosts` line in `localtest/README.md` (`127.0.0.1 af.local.altinn.cloud`, next to
the pdf and workflow-engine names; Chromium resolves `*.localhost` by itself but not this name), and a line in the
localtest app's `smoke-test.sh` host section. The split between SPA and BFF happens in our nginx, as upstream's
traefik does it: `location /api/ { proxy_pass http://arbeidsflate-bff:80; }`. No change to `Forwarder.cs`.

Two upstreams on one host in the proxy (host + prefix) would also work but is a bigger change for no gain.

### 5.2 Why not `local.altinn.cloud:8000/arbeidsflate/`

A path prefix needs code changes on both sides:

- SPA: `BrowserRouter` without `basename` ([`main.tsx:60`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/main.tsx#L60)),
  absolute `PageRoutes` (`/`, `/inbox/:id`, `/profile`, …, [`routes.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/pages/routes.ts)),
  nine hard-coded `/api/...` fetches and `window.location.assign('/api/login')`, `location.pathname === '/logout'`,
  `<Navigate to="/" />`, `/favicon.ico`; Vite `base` unset.
- BFF: every route is `/api/*`; after login it redirects to `/` ([`oidc.ts:314`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L314)),
  and `redirect_uri` is `${HOSTNAME}/api/cb`, so the proxy would have to strip `/arbeidsflate` and the BFF would
  still send the browser to the root.

About ten hunks across both packages, every one in files that change often. Not worth it; the stack already asks for
host entries.

### 5.3 Our `nginx.conf` (own file, derived from upstream's; every change commented)

```nginx
server {
  listen 80;
  server_tokens off;
  gzip on; gzip_comp_level 6; gzip_min_length 1024; gzip_vary on; gzip_proxied any;
  gzip_types application/javascript text/css image/svg+xml application/json;

  add_header X-Content-Type-Options "nosniff" always;
  add_header Referrer-Policy "no-referrer" always;
  add_header X-Permitted-Cross-Domain-Policies "none" always;
  add_header Permissions-Policy "geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), gyroscope=(), speaker=(), local-network=()" always;
  # localtest: no Strict-Transport-Security (plain http; a browser that once saw it on https would refuse http)
  # localtest: connect-src and img-src also allow the platform origin (stream, dialog content, attachments, logos)
  add_header Content-Security-Policy "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; connect-src 'self' http://local.altinn.cloud:8000 https:; img-src 'self' data: http://local.altinn.cloud:8000 https:; style-src 'self' 'unsafe-inline' https://altinncdn.no; font-src 'self' data: https://altinncdn.no; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self';" always;

  # localtest: upstream's traefik routes PathPrefix(/api) to the BFF; here nginx does.
  location /api/ {
    proxy_pass http://arbeidsflate-bff:80;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    # @fastify/session only saves the session when request.protocol is https (cookie.secure is hard-coded true);
    # the localtest app removes the Secure flag on the way to the browser.
    proxy_set_header X-Forwarded-Proto https;
    proxy_buffering off;
    proxy_read_timeout 300s;
  }

  location / { root /usr/share/nginx/html; index index.html; try_files $uri $uri/ /index.html =404; }
  location ~* \.html$ { root /usr/share/nginx/html; add_header Cache-Control "no-cache, no-store, must-revalidate"; add_header Pragma "no-cache"; add_header Expires "0"; try_files $uri =404; }
  location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ { root /usr/share/nginx/html; add_header Cache-Control "public, max-age=31536000, immutable"; try_files $uri =404; }
}
```

`$host` is `af.local.altinn.cloud:8000` because the localtest app forwards the original `Host`. The BFF does not
use it (it builds URLs from `HOSTNAME`).

### 5.4 Cookies, headers, streams

| Concern | Behaviour |
| --- | --- |
| `arbeidsflate` session cookie | `HttpOnly; Secure; Path=/` (no `SameSite` → browsers default Lax), re-sent on every response (`rolling: true`); `Secure` stripped by the localtest app; Lax is enough: the only cross-origin navigation into `/api/cb` is top-level and same-site (`altinn.cloud` is not a public suffix) |
| Authentication's cookies | `Domain=local.altinn.cloud`, so the browser presents them to `af.local.altinn.cloud` too; the SPA reads `AltinnPartyUuid`, and after patch 0002 writes `AltinnPartyId`/`AltinnPartyUuid` with the same domain |
| `AUTH_CONTEXT_COOKIE_DOMAIN` | the domain of the BFF's own `selectedLanguage`-style cookies written in `profile/queries.ts:38` and `mutations.ts:102`; set to `local.altinn.cloud` |
| HSTS | ignored over http; removed from our nginx; the BFF's helmet HSTS on `/api/` responses is harmless for the same reason |
| CSP | SPA: section 5.3. BFF: helmet on JSON responses, inert; the BFF's HTML error page carries its own nonce CSP |
| SSE | browser → `local.altinn.cloud:8000/dialogporten/graphql` directly (not through the BFF, not through our nginx); the localtest app streams unbuffered |
| `/api/graphiql` | `ENABLE_GRAPHIQL=true` serves GraphiQL at `af.local.altinn.cloud:8000/api/graphiql` for debugging with the browser's session (whether its assets load from a CDN was not checked) |

## 6. The `/api/init-session` test seam

[`oidc.ts:202-244`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L202-L244),
registered only when `ENABLE_INIT_SESSION_ENDPOINT=true` ([L394-L396](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L394-L396);
upstream enables it everywhere but prod, and its k6 tests use it with tokens from the Altinn test token generator,
[`getCookie.js`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/tests/performance/k6/helpers/getCookie.js)):

`POST /api/init-session` `{"token": "<jwt>"}` → decodes the payload (no verification), writes a Redis session
`{token: {access_token, access_token_expires_at (from exp), tokenUpdatedAt}, pid, locale: 'en'}` with TTL = the
token's remaining lifetime, signs a session id with `SESSION_SECRET` the way `@fastify/session` does, and returns
`{cookie: "arbeidsflate=<id>.<sig>", expires}`.

Locally the token is `GET http://local.altinn.cloud:8000/Home/GetTestUserToken?userId=1337&authenticationLevel=3`
(a production sign-in, 30 min, scopes include `digdir:dialogporten.noconsent`; verified). Consequences of the seam:

- `isAuthenticated` is true until the token expires; refresh never happens (`refresh_token_expires_at` is
  undefined → `new Date(undefined) > now` is false), `/api/logout` answers 401 (no `id_token`), and a 30-minute test
  session is the ceiling.
- `pid` in the session is the token's `pid` claim: with today's tokens it is `undefined`, so BFF-owned queries that
  need it (saved searches, profile row) throw `PID is required`; dialogs and parties (proxied with the access token)
  are unaffected. Fixed by 3.4 item 2.

Cypress (the stack's `run-cypress.sh` style): `cy.request('GET', 'http://local.altinn.cloud:8000/Home/GetTestUserToken?userId=1337&authenticationLevel=3')`
→ `cy.request('POST', 'http://af.local.altinn.cloud:8000/api/init-session', {token})` → split the returned
`cookie` on the first `=` → `cy.setCookie('arbeidsflate', value, {domain: 'af.local.altinn.cloud', httpOnly: true})`
→ `cy.visit('http://af.local.altinn.cloud:8000/')`. Playwright: `context.addCookies([...])` then `page.goto`. The
browser flow (picker) is also scriptable, as the root smoke test does with curl, and is what the smoke test below
uses so the OIDC provider path is exercised.

## 7. Compose, environment, volumes, routes, runner, tests, cost

### 7.1 Services (profile `inbox`, with the Dialogporten services)

```yaml
  # Arbeidsflate: the SPA's nginx (the af.local.altinn.cloud host) and the BFF behind its /api/.
  arbeidsflate:
    <<: *platform-host
    build: { context: ./components/arbeidsflate, dockerfile: Dockerfile.frontend }
    image: localhost/localtest-arbeidsflate:dev
    profiles: [inbox]
    environment:
      DIALOGPORTEN_STREAM_URL: http://local.altinn.cloud:8000/dialogporten/graphql
      APPLICATION_INSIGHTS_INSTRUMENTATION_KEY: ""
      APPLICATION_INSIGHTS_DISABLE_DEPENDENCY_TRACKING: "true"
    depends_on: [arbeidsflate-bff]

  arbeidsflate-bff:
    <<: *platform-host
    build: { context: ./components/arbeidsflate, dockerfile: Dockerfile }
    image: localhost/localtest-arbeidsflate-bff:dev
    profiles: [inbox]
    environment: *arbeidsflate-env            # table 7.2
    depends_on:
      arbeidsflate-migrate: { condition: service_completed_successfully }
      redis: { condition: service_healthy }
      authentication: { condition: service_started }
      dialogporten-graphql: { condition: service_started }

  # Runs the TypeORM migrations once (the same image, MIGRATION_RUN=true), as upstream's migration job does.
  arbeidsflate-migrate:
    image: localhost/localtest-arbeidsflate-bff:dev
    profiles: [inbox]
    restart: "no"
    environment:
      MIGRATION_RUN: "true"
      DB_CONNECTION_STRING: postgres://arbeidsflate:arbeidsflate@postgres:5432/arbeidsflate
      SESSION_SECRET: localtest-arbeidsflate-session-secret-32-chars-min
      REDIS_CONNECTION_STRING: redis://redis:6379/1
    depends_on:
      postgres: { condition: service_healthy }
```

`postgres` gets `./components/arbeidsflate/postgres-init/40-arbeidsflate.sh:/docker-entrypoint-initdb.d/40-arbeidsflate.sh:ro`
(role `arbeidsflate` / password `arbeidsflate`, database `arbeidsflate`, idempotent like
[`30-events.sh`](../../localtest/components/events/postgres-init/30-events.sh)). `authentication` gets the
`af.local.altinn.cloud:10.200.0.10` extra host (3.4 item 3). `localtest` gets the network alias. `redis` is the shared
plan's service. The `localtest` service does not `depends_on` the profile's services (compose rejects a dependency
on a disabled profile); the proxy answers 502 for the host when the profile is down. `migrate` imports
`config.ts`, which requires `SESSION_SECRET` ≥ 32 characters even for migrations.

### 7.2 BFF environment

| Variable | Value | Why |
| --- | --- | --- |
| `HOSTNAME` | `http://af.local.altinn.cloud:8000` | `redirect_uri`; must equal the seeded client's |
| `OIDC_PLATFORM_URL` | `http://local.altinn.cloud:8000/authentication/api/v1/openid` | discovery (patch 0001 keeps the scheme) |
| `OIDC_CLIENT_ID` / `OIDC_CLIENT_SECRET` | `arbeidsflate-localtest` / `arbeidsflate-localtest-secret` | the shared plan's seed |
| `PLATFORM_BASEURL` | `http://local.altinn.cloud:8000` | Profile, Register, Notifications, Correspondence, Resource Registry, `exchange/maskinporten` |
| `DIALOGPORTEN_URL` | `http://local.altinn.cloud:8000/dialogporten` | `/graphql`, `/health` |
| `SESSION_SECRET` | any ≥ 32 chars, same in `migrate` | cookie signing ([`config.ts:32`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/config.ts#L32)) |
| `REDIS_CONNECTION_STRING` | `redis://redis:6379/1` | sessions and caches; db 0 is Dialogporten's |
| `DB_CONNECTION_STRING` | `postgres://arbeidsflate:arbeidsflate@postgres:5432/arbeidsflate` | TypeORM; `DB_USE_AAD_AUTH` stays false |
| `PORT` / `HOST` | `80` / `0.0.0.0` | nginx proxies to `arbeidsflate-bff:80` |
| `NODE_ENV` | `production` | as upstream's compose and bicep |
| `ENABLE_INIT_SESSION_ENDPOINT` | `true` | section 6 |
| `ENABLE_GRAPHIQL` | `true` | debugging |
| `AUTH_CONTEXT_COOKIE_DOMAIN` | `local.altinn.cloud` | BFF-written cookies readable by apps and inbox |
| `ENVIRONMENT` | `test` (default) | `prod` would filter orgs |
| `MASKINPORTEN_ISSUER` | `http://local.altinn.cloud:8000/localtest/api/maskinporten/` (trailing slash: the BFF appends `token`) | the shared plan's token endpoint |
| `MASKINPORTEN_CLIENT_ID` | `991825827` | the shared plan's convention "numeric client id = organisation number"; the exchange maps it to `ttd`/`digdir` |
| `MASKINPORTEN_JWK` | base64 of `maskinporten-client.json` (an RSA private JWK **with `kid`**, generated once with `openssl`/`jose`, committed as a test key) | the BFF signs its assertion with it ([`maskinporten.ts:29-33`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts#L29-L33)); the fake endpoint ignores the signature |
| `MASKINPORTEN_SCOPE` | default `altinn:register/partylookup.admin altinn:serviceowner/notifications.create` | Register policy needs the first |
| `PERSON_URN_ENC_KEYS` | default dev key | fine locally |
| `APP_CONFIG_CONNECTION_STRING`, `OTEL_*`, `ALTINN2_*`, `OCP_APIM_SUBSCRIPTION_KEY`, `REGISTER_SUBSCRIPTION_KEY` | unset / empty | defaults; Altinn 2 is never called by this version |
| `DISABLE_PROFILE` | not read by `config.ts` at this pin (compose leftover) | – |

Dialogporten side (core strand): `GraphQl__Cors__AllowedOrigins__0=http://af.local.altinn.cloud:8000`.

### 7.3 Volumes, routes, runner

- Volumes: none for the component (the SPA is static, the BFF state is in Postgres and Redis). Build contexts are
  the component folder.
- Proxy: section 5.1 (one host tuple, one upstream, one alias, one hosts line).
- `run-app.sh` / studioctl: **no change**. Nothing is pushed to Arbeidsflate; it reads Dialogporten, which the adapter
  fills from Storage. The app frontend's "go to inbox" link is the shared plan's section 7.

### 7.4 `components/arbeidsflate/smoke-test.sh` (shape, bash like the root one)

1. Wait for `http://af.local.altinn.cloud:8000/api/health` → `status: ok` with `postgresql`, `redis`, `oidc`,
   `dialogporten` all `ok`.
2. `GET /` → 200, body contains `window.dialogportenStreamUrl = 'http://local.altinn.cloud:8000/dialogporten/graphql'`
   and no `$DIALOGPORTEN_STREAM_URL`; `Content-Security-Policy` contains `http://local.altinn.cloud:8000`; no
   `Strict-Transport-Security`; one `assets/*.js` → 200.
3. Sign-in through the picker, as the root smoke test does: `GET /api/login` → 302 to
   `local.altinn.cloud:8000/authentication/api/v1/authorize/?client_id=arbeidsflate-localtest…` → 302 to the picker
   → POST the picker (`UserSelect=1337.501337`, level 3) → 302 `upstream/callback` → 302
   `af.local.altinn.cloud:8000/api/cb?code&state` → 302 `/`; the jar now holds `arbeidsflate` **without `Secure`**.
4. `GET /api/isAuthenticated` → 200 `{"isAuthenticated":true}`.
5. `POST /api/graphql` `{ parties { party name isCurrentEndUser } }` → 200 with Sophie Salt; `{ dialogs(...) }` →
   200, a list (the adapter strand's fixture gives at least one dialog after an app instance is created);
   `dialogById(id)` → `dialogToken` present.
6. `GET /api/logout` → 302 to `…/openid/logout/?id_token_hint=` → 302 (not 500); afterwards `isAuthenticated` → 401.
7. Seam: `GetTestUserToken` → `POST /api/init-session` → cookie → `isAuthenticated` 200.
8. Negative: `/api/cb?state=wrong` → 302 `/api/login`; `GET /api/graphql` without a cookie → 401/redirect.

Browser verification (not scripted in the first iteration): open `http://af.local.altinn.cloud:8000/`, sign in through
the picker, see the inbox, open a dialog created by an app, change party, log out. Record it with `playwright-cli` for
the pull request.

### 7.5 Size and memory (estimates, not measured)

| Container | Image on disk | RSS | Basis |
| --- | --- | --- | --- |
| `arbeidsflate-bff` | 1.3 to 2 GB | 150 to 300 MB | Node 22 + Fastify + schema stitching of a large schema + TypeORM; OTel SDK is imported but not started |
| `arbeidsflate` (nginx) | ~30 MB | < 10 MB | `nginx:1.25-alpine` + ~5 MB dist |
| `arbeidsflate-migrate` | shares the BFF image | one-shot | – |
| `redis` | shared plan | ~10 MB | – |

The BFF image's size is the one real cost on this 5.5 GB-free machine: two full `pnpm install`s of the same workspace
(BFF and SPA images) plus BuildKit cache. Mitigation if needed, as a `localtest:` deviation:
`pnpm install --frozen-lockfile --filter bff... --filter frontend...` in the respective image (pnpm accepts filtered
frozen installs; not verified here), or a `pnpm deploy --prod` runtime stage for the BFF (would need `@swc-node/register`
moved to dependencies or a `tsc` build: a bigger deviation).

### 7.6 Effort and risk

| Item | Effort | Risk |
| --- | --- | --- |
| Component: vendor copy, two Dockerfiles, nginx, patches, compose, init script, README, smoke test | 2 days | low: the patches are verified, the Dockerfiles are upstream's plus one stage |
| Proxy host + alias + hosts line + localtest smoke section | 0.5 day | low |
| Debugging the browser flow against Authentication (cookies, acr upgrade, logout chain, CORS, CSP) | 1 to 1.5 days | medium: only visible in a browser; depends on 3.4 |
| Profile endpoints in the register fake (favourites, settings, notification settings) if the profile page is in scope | 1 day | low; optional |
| Total for this component | **3 to 4 days** | after the shared prerequisites (OIDC repositories, seed, pepper, logout endpoint, `pid`, Maskinporten token endpoint, Redis) and the Dialogporten routes exist |

Risks outside the estimate: the `pid` loss (3.4 item 2) blocks the whole inbox until found; upstream churn in
`oidc.ts`/`verifyToken.ts` is low (unchanged in the 5 commits since the pin) but the SPA's `url.ts` changes with
every environment addition; `pnpm install` network and disk.

### 7.7 Open questions for the product owner

1. Host name `af.local.altinn.cloud` (one more `/etc/hosts` line) is assumed; a path prefix costs about ten patch
   hunks. Confirm.
2. Is the profile page (favourites, settings, notification settings, username) in scope? If yes, the register
   component's Profile fake grows by five routes; if no, the inbox alone needs nothing from Profile beyond
   `users/current`.
3. Should the stack keep the internet dependencies (altinncdn.no org list and font) or take the shared plan's CDN stub
   with a third one-line patch (`ALTINN_CDN_URL`)?
4. Propose patch 0001 upstream (a scheme in `OIDC_PLATFORM_URL` is backwards compatible) so the patch count drops to
   one?
5. Build locally (patched) or pull `ghcr.io/altinn/dialogporten-frontend-*` and accept an unpatched BFF behind a TLS
   terminator? This plan builds.

## 8. Upstream between `0011d95` and `origin/main` `73feeb9` (5 commits)

`git diff HEAD origin/main --stat`: 23 files. `alertBannerApi.ts` moves from `AppConfigurationClient` to the
`@azure/app-configuration-provider` loader with a 10 s refresh and the same "no connection string → defaults" path
(plus a spec); `@altinn/altinn-components` 0.76.4 → 0.76.5 (lockfile); a mock story and docs for cross-agency
dialogs; `useAlertBanner.ts` +3 lines; `.graphql` removed and `graphql.config.yml` adjusted for the VS Code IDE
plugin; `CHANGELOG`, release 1.182.2. None of the files this plan patches, configures or mirrors (`config.ts`,
`oidc.ts`, `verifyToken.ts`, `HealthChecks.ts`, `url.ts`, both Dockerfiles, `nginx.conf`, `start.sh`, `index.html`,
`server.ts`, `fastifyHeaders.ts`) changed; both patches apply to `origin/main` unchanged. Pinning `73feeb9` instead is
free.

## Not verified

- No image was built and no `pnpm install` was run: build time, image size, memory, whether corepack can download
  pnpm in the build sandbox, whether `git apply` in the `source` stage behaves as expected in the image (it was run
  on this machine's git against an export of the pinned tree), whether filtered frozen installs work.
- The HotChocolate SSE path (`/graphql` vs `/graphql/stream`) and the CORS preflight from `af.local.altinn.cloud`
  were read from code, not exercised; the core strand owns the route.
- The registered-client branch of `upstream/callback` and the token endpoint were not run end to end (both 500 today
  without the client repository); the reading matches the shared-prerequisites strand's.
- Where exactly the `pid` is lost between the fake ID-porten's id_token and Authentication's session (3.4 item 2).
- The stack was restarted by another session while probing (`/health` → connection refused); the last probes
  (picker-flow cookie decode for a second user) did not complete. All results above were obtained before that.
- The BFF's GraphiQL asset source, `fastify-graphiql`'s CDN use.
- Browser behaviour of `Domain=local.altinn.cloud` cookies on `af.local.altinn.cloud` (standard, but not opened in a
  browser here).

## Summary for the orchestrator

Arbeidsflate vendors as a pristine `git archive` of `dialogporten-frontend@0011d95` (whole tree, 740 files, ~6 MB)
under `components/arbeidsflate/vendor/`, with two verified patches in `patches/` applied by a `source` stage that
runs `git apply --check` before `git apply`; the two Dockerfiles mirror upstream's (`node:22-slim` BFF running TS
through swc, `node:24.15.0-slim` → `nginx:1.25-alpine` SPA) with the context switched to the component folder, and
our own `nginx.conf` does what upstream's traefik does (`/api/` → BFF) plus the CSP widened to
`http://local.altinn.cloud:8000`, no HSTS, and `X-Forwarded-Proto: https` because `@fastify/session` 11.1.1 never
writes its `Secure`-only cookie over http (login would loop). Host `af.local.altinn.cloud:8000` is one tuple in
`PlatformRoutes.Hosts`, one alias and one `/etc/hosts` line; a path prefix would need ~10 more hunks. Login is
production Authentication's registered-client OIDC flow with client `arbeidsflate-localtest` as specified by
shared-prerequisites; that strand still needs to add (a) a fake ID-porten end-session endpoint and
`OidcProviders:idporten:LogoutEndpoint` (today `openid/logout` is a 500, verified), (b) the missing `pid` claim in
every token Authentication issues (verified absent; Dialogporten classifies end users by `pid`, so the inbox is empty
until fixed; the fake ID-porten does emit `pid`, the loss is inside Authentication's identity step), and (c)
`af.local.altinn.cloud` in the authentication container's `extra_hosts` for the server-side front-channel logout.
Profile `users/current` is served by the register component; favourites, settings and notification settings 404
(soft for the inbox, broken on the profile page); Maskinporten is only needed for the profile page's username and a
flag-gated notification log. The init-session seam works with `/Home/GetTestUserToken` tokens (scopes verified).
Effort 3 to 4 days after the prerequisites; main risks are the `pid` gap, whole-workspace `pnpm install` in two
images (disk, time) and browser-only details (CSP, CORS, cookies). Upstream's 5 newer commits touch nothing this plan
depends on.
