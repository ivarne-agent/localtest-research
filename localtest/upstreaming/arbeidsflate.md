# Arbeidsflate: upstreaming

- Upstream: [Altinn/dialogporten-frontend](https://github.com/Altinn/dialogporten-frontend)
- Pin: `bb74dff` (main, 2026-10-21), [`../components/arbeidsflate/vendor/UPSTREAM.md`](../components/arbeidsflate/vendor/UPSTREAM.md)
- Group: none
- Reminders: a new `secure: true` or `setCookie` in `packages/bff/src` needs patch 0003's treatment; new BFF settings go
  into studioctl's `arbeidsflateBFFComponent` and the README; move the base image digests when upstream's tags move.
- Localtest modifications: no directive sites. Four build-time patches in `patches/` (0001 BFF OIDC discovery scheme,
  0002 SPA cookie domain, 0003 BFF cookies `secure: 'auto'`, 0004 SPA links to the Access Management GUI) on a pristine `vendor/`, the `localtest:` deviations in
  `Dockerfile.bff`, `Dockerfile.frontend` and `.dockerignore`, the image commands `bff-start.sh` and
  `nginx/nginx-start.sh`, our `nginx/nginx.conf`, and `postgres-init/90-arbeidsflate.sh`. No fakes, no build overrides.
- Not applicable: the cross-cutting service discovery and ServiceDefaults items (README,
  [Across services](README.md#across-services)). The BFF is Node: it reaches Postgres, Redis and the platform by alias
  and through the front door (`PLATFORM_BASEURL=http://localtest:8080`), which needs nothing upstream.
- The component README records "nothing is proposed upstream (decided 2026-10-21)"; this list is the proposal should
  that change. Upstream's `origin/main` (15 commits after the pin, checked 2026-10-27) still has every patched line.

Paths in "Removes" are relative to `../components/arbeidsflate/`; patch file paths in it are relative to upstream's
repository root.

## Phase 1: easy fixes

- [ ] **Let `OIDC_PLATFORM_URL` carry a scheme.** `packages/bff/src/config.ts`: add `oidcDiscoveryUrl`, which keeps
      `OIDC_PLATFORM_URL` as is when it starts with `http://` or `https://` and prefixes `https://` otherwise, then append
      `/.well-known/openid-configuration`; read it in `auth/oidc.ts`, `auth/verifyToken.ts` and `azure/HealthChecks.ts`
      instead of the three copies of `` `https://${oidc_url}/.well-known/openid-configuration` ``.
  - Why: Authentication is plain http locally, so the BFF cannot fetch the discovery document and does not start.
  - Size: trivial (backwards compatible: production's values have no scheme).
  - Removes: patch `0001-bff-oidc-discovery-scheme.patch` (`config.ts`, `auth/oidc.ts`, `auth/verifyToken.ts`,
    `azure/HealthChecks.ts`).
  - Upstream: none found. The patch is the pull request's shape (`vendor/UPSTREAM.md`).
  - Detail: [`../../planned-components/dialogporten/arbeidsflate.md`](../../planned-components/dialogporten/arbeidsflate.md),
    site 1 and section 2.1.

- [ ] **Session and `altinnPersistentContext` cookies `secure: 'auto'`.** `packages/bff/src/server.ts` (the
      `@fastify/session` cookie) and `graphql/profile/queries.ts`, `graphql/profile/mutations.ts` (`@fastify/cookie`):
      `secure: 'auto'` instead of `true`. Both libraries then set `Secure` exactly when `request.protocol` is https.
  - Why: over plain http the browser refuses a `Secure` cookie and `@fastify/session` refuses to save a `secure: true`
    session, so `/api/login` loses its `state` and loops.
  - Size: trivial. Production is unchanged: `secure: true` already saves a session only when `request.protocol` is https
    (`trustProxy: true` and the ingress's `X-Forwarded-Proto`), so production already sees https there. The hook in
    `graphql/fastifyHeaders.ts` (`secure = true` on `X-Forwarded-Proto: https`) can then go, or stay as a no-op.
  - Removes: patch `0003-bff-cookies-secure-auto.patch` (`server.ts`, `graphql/profile/queries.ts`,
    `graphql/profile/mutations.ts`).
  - Upstream: none found for this change. `secure: true` came from the security finding
    [dialogporten-frontend#3145](https://github.com/Altinn/dialogporten-frontend/issues/3145) ("Secure Cookie Attribute
    Not Set"), fixed in [dialogporten-frontend#3147](https://github.com/Altinn/dialogporten-frontend/pull/3147); the
    pull request has to show that `'auto'` still sets `Secure` behind the TLS ingress.
  - Detail: [`../reviews/cookies.md`](../reviews/cookies.md), rows 13 and 14 and decision A1.

- [ ] **Cookie domain for the party cookies as a runtime value.** The SPA writes `AltinnPartyId` and `AltinnPartyUuid`
      with `getCookieDomain()` (`packages/frontend/src/auth/url.ts`), which maps the host to a fixed domain and every
      unknown host to `.altinn.no`. Add a runtime value next to `DIALOGPORTEN_STREAM_URL` (for example
      `window.cookieDomain = '$COOKIE_DOMAIN'` in `index.html`, the variable added to `envsubst` in `start.sh`) and return
      it from `getCookieDomain()` when it is set; the host map stays the fallback.
  - Why: on `af.local.altinn.cloud` the SPA writes `Domain=.altinn.no`, which the browser rejects, so a party chosen in
    the inbox never reaches the apps, which read the same cookies on `local.altinn.cloud`.
  - Size: small.
  - Removes: patch `0002-frontend-cookie-domain-localtest.patch` (`packages/frontend/src/auth/url.ts`); studioctl then
    sets `COOKIE_DOMAIN=local.altinn.cloud` on the `arbeidsflate` container. The patch's own rule (`http:` and an `af.`
    prefix) is a localtest convention and not the shape to propose.
  - Upstream: none found. Upstream's merged
    [dialogporten-frontend#3126](https://github.com/Altinn/dialogporten-frontend/pull/3126) only sets the BFF's
    `AUTH_CONTEXT_COOKIE_DOMAIN` per environment. A recognised local host environment in `getEnvByHost()` would also
    move the inbox's links out (Access Management, the info site) off production, but those have no local target, so the
    smaller change is enough (planning note: "five hunks for no test value").
  - Detail: [`../../planned-components/dialogporten/arbeidsflate.md`](../../planned-components/dialogporten/arbeidsflate.md),
    site 5 and section 2.2; [`../reviews/cookies.md`](../reviews/cookies.md), row 15.

- [ ] **The links to the Access Management GUI from configuration or the page's origin.** `packages/frontend/src/auth/url.ts`:
      `getAccessAMUILink()` and `createChangePartyAndRedirect()` map every host `getEnvByHost()` does not know to
      `am.ui.altinn.no`. A runtime value (as the cookie domain item above) or, for an unknown host, `am.ui.` plus the
      page's host without `af.` keeps production as it is.
  - Why: on the stack the menu's access management link, a dialog's "who has access" and the party switch through
    `changeandredirect` went to production (TODO.md task 68).
  - Size: small.
  - Removes: patch `0004-frontend-access-management-links.patch`.
  - Upstream: none found.

- [ ] **Pick the OTLP exporter by `OTEL_EXPORTER_OTLP_PROTOCOL`.** `packages/bff/src/instrumentation.ts` uses the gRPC
      trace and metric exporters whatever the protocol says (it reads and logs `OTEL_EXPORTER_OTLP_PROTOCOL` only). Add
      the `-proto` exporters to `packages/bff/package.json` and choose by the protocol, or let NodeSDK build the
      exporters from the environment and keep only the wrappers (`SafeSpanExporter`, the span-name filter).
  - Why: a collector that takes `http/protobuf` (Aspire, the front door's `/otlp`) would receive gRPC on an HTTP port.
  - Size: small (`instrumentation.ts`, `package.json` and the lockfile).
  - Removes: nothing. studioctl sends every container `grpc` to the collector's 4317, so localtest has no change for it.
  - Upstream: none found.
  - Detail: [`../reviews/otel/arbeidsflate.md`](../reviews/otel/arbeidsflate.md). It also leaves unverified whether
    `OTEL_SERVICE_NAME` wins over the resource's `service.name` `bff`; check that in the same pull request.

## Phase 2: structural changes

- [ ] **Extra CSP sources in the frontend image.** `packages/frontend/nginx.conf` allows `'self'` and `https:` in
      `connect-src` and `img-src`. Make additional sources configurable at start: a placeholder in `nginx.conf` that
      `start.sh` fills with `envsubst` (empty by default), as it already does for `index.html`.
  - Why: the SPA's subscription stream, dialog content and transmission receipts are fetched from the stack's
    `http://local.altinn.cloud:8000`, which `https:` does not cover, so the browser blocks them.
  - Size: small in code; a CSP change the team has to agree to.
  - Removes: the CSP line in `nginx/nginx.conf` and its origin rewrite in `nginx/nginx-start.sh`.
  - Upstream: none found for this. The open
    [dialogporten-frontend#2310](https://github.com/Altinn/dialogporten-frontend/issues/2310) asks whether the CSP
    should move to the application gateway; if it does, this item becomes a gateway setting localtest's front door
    imitates.
  - Detail: [`../../planned-components/dialogporten/arbeidsflate.md`](../../planned-components/dialogporten/arbeidsflate.md),
    site 6.

- [ ] **Run upstream's published images.** Upstream already pushes both images to the public GitHub Container Registry
      (`.github/workflows/workflow-docker-build-and-push.yml`): `ghcr.io/altinn/dialogporten-frontend-node-bff` and
      `ghcr.io/altinn/dialogporten-frontend-frontend`, tagged `<version>-<short sha>` (anonymous pulls answer 200,
      checked 2026-10-27). With the items above merged, they run locally unchanged. Agree with the team that localtest
      pulls them (which tags are stable to pin). The rest is localtest's own work: the front door routes `/api/` on
      `af.<host>` to the BFF (upstream's frontend image serves only the SPA; traefik or the ingress does that split),
      the manifest runs the BFF image once with `MIGRATION_RUN=true` before the server, as production's migration job
      does, and supplies `MASKINPORTEN_JWK` (any key: the fake Maskinporten does not check the signature).
  - Why: localtest builds both images from a vendored copy only to carry the patches; the BFF image is 1.47 GB and
    builds in about a minute and a half.
  - Size: small upstream (nothing, or a tag convention); medium on our side (the front door's host and path route).
  - Removes: `Dockerfile.bff`, `Dockerfile.frontend`, `.dockerignore`, `bff-start.sh`, `nginx/` (our `nginx.conf` with
    its `/api/` location and no HSTS, and `nginx-start.sh` with its resolver) and the vendored copy itself. Upstream's
    `Strict-Transport-Security` then reaches the browser over http, which ignores it (RFC 6797, section 8.1).
  - Upstream: nothing to change for publishing; none found for the rest.
  - Detail: [`../components/arbeidsflate/README.md`](../components/arbeidsflate/README.md), "What changed, and why".

## Stays local

- `Dockerfile.bff` and `Dockerfile.frontend`: the `source` stage (`git apply` of `patches/`) and `COPY --from=source`
  instead of `COPY . .`: the mechanism that carries the patches; gone with the last one.
- `Dockerfile.bff` and `Dockerfile.frontend`: the Node base image, and in `Dockerfile.frontend` the nginx base image,
  pinned by digest: localtest's reproducible builds; gone with the vendored copy.
- `Dockerfile.bff` (install and build) and `Dockerfile.frontend` (install): `NODE_EXTRA_CA_CERTS` when
  `/run/agent/tls/ca-bundle.pem` exists: the agent sandbox's TLS-intercepting proxy; a no-op elsewhere.
- `Dockerfile.frontend`: `pnpm install --filter 'frontend...'`: a build-time saving only (the nginx stage gets the same
  `dist/`); gone with the vendored copy.
- `Dockerfile.frontend`: `COPY nginx/nginx.conf`, `COPY vendor/packages/frontend/start.sh` and `CMD` `nginx-start.sh`;
  `Dockerfile.bff`: `CMD` `bff-start.sh`: the vendoring layout and our image commands; gone with the published images.
- `.dockerignore`: upstream's entries under `vendor/`, the packages' `node_modules` and `dist`, and `smoke-test.sh`: the
  vendoring layout.
- `bff-start.sh`: the generated `MASKINPORTEN_JWK` and the migrations at every start: deployment settings, which
  production's Bicep and migration job supply; with the published images they move to the manifest.
- `nginx/nginx.conf`: the `/api/` location and no `Strict-Transport-Security`; `nginx/nginx-start.sh`: the per-request
  resolver for the BFF's alias: the split upstream's traefik or ingress does; gone with the published images.
- `postgres-init/90-arbeidsflate.sh`: the role and database on the shared Postgres: deployment, which production's
  infrastructure provisions.
- `docs/` (screenshots) and `smoke-test.sh`: localtest's verification.

Coverage: 0 sites, 4 patches, 0 fakes, 0 build overrides, 15 Dockerfile and 3 `.dockerignore` deviations, 2 image
commands, 3 `nginx.conf` deviations, the Postgres init script: all accounted for.
