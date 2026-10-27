# Vendored copy of Altinn/dialogporten-frontend

```json vendor-sync
{
  "repository": "Altinn/dialogporten-frontend",
  "branch": "main",
  "commit": "bb74dff2c425f2aff48759ed9c6370e73436545d",
  "date": "2026-10-21",
  "paths": ["."],
  "group": null,
  "local": "patches",
  "patches": "patches",
  "mirrors": [
    { "upstream": "packages/bff/Dockerfile", "ours": "Dockerfile.bff" },
    {
      "upstream": "packages/frontend/Dockerfile",
      "ours": "Dockerfile.frontend"
    },
    { "upstream": "packages/frontend/nginx.conf", "ours": "nginx/nginx.conf" },
    { "upstream": ".dockerignore", "ours": ".dockerignore" }
  ],
  "watch": [
    "packages/frontend/start.sh",
    "packages/frontend/index.html",
    ".node-version",
    "package.json",
    "pnpm-workspace.yaml",
    "packages/bff/src/config.ts",
    "packages/bff/src/auth",
    "packages/bff/src/server.ts"
  ],
  "projects": [],
  "follow": [],
  "reminders": [
    "grep `vendor/packages/bff/src` for `secure: true` and `setCookie`: a new cookie needs patch 0003's treatment",
    "new or renamed BFF settings (`packages/bff/src/config.ts`) go into studioctl's `arbeidsflateBFFComponent` and `README.md`",
    "move the base image digests of `Dockerfile.bff` and `Dockerfile.frontend` when upstream's tags move"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/dialogporten-frontend
- Commit: [`bb74dff2c425f2aff48759ed9c6370e73436545d`](https://github.com/Altinn/dialogporten-frontend/tree/bb74dff2c425f2aff48759ed9c6370e73436545d) (main, 2026-10-21)

<!-- vendor-sync:end pin -->
- Copied paths: the whole repository (`git archive <commit> | tar -x -C vendor`), no subtree. The pnpm workspace needs
  every package the lockfile names (`pnpm install --frozen-lockfile`), and a whole-tree copy is trivially comparable.
- **Pristine.** The commit `arbeidsflate: vendor Altinn/dialogporten-frontend at bb74dff …, unmodified` holds the tree
  byte for byte: `git rev-parse <that commit>:localtest/components/arbeidsflate/vendor` is `637d377…`, upstream's own tree
  id for the commit (`git -C <clone> rev-parse bb74dff^{tree}`). Two things were needed for that: two files upstream's
  `.gitignore` ignores although they are tracked (`.hooks/pre-commit`, `certs/.gitkeep`) were added with `git add -f`, and six
  files upstream stores with CRLF (two SVGs, four k6 CSVs) were added with `git hash-object --no-filters`, because the
  vendored `.gitattributes` (`* text=auto eol=lf`) would otherwise normalise them. This file is the only later addition.

## Directive sites: the patches

TypeScript has no `#if`, so the localtest changes are unified diffs in `../patches/`, applied at image build by the
`source` stage both Dockerfiles share (`git apply --check`, then `git apply`); `vendor/` itself is never edited. Each
patch says what it changes and why in its header; each changed line carries a `localtest:` comment.

| Patch | Files | What |
| --- | --- | --- |
| `0001-bff-oidc-discovery-scheme.patch` | `packages/bff/src/config.ts`, `auth/oidc.ts`, `auth/verifyToken.ts`, `azure/HealthChecks.ts` | `config.oidcDiscoveryUrl`: `OIDC_PLATFORM_URL` keeps a scheme when it has one (Authentication is plain http in the stack); without one it is prefixed `https://` as upstream does in the three places that now read the new value |
| `0002-frontend-cookie-domain-localtest.patch` | `packages/frontend/src/auth/url.ts` | `getCookieDomain()` returns `local.altinn.cloud` on `*.local.altinn.cloud`, the domain Authentication sets the party cookies with, so the inbox's party choice reaches the apps; upstream maps unknown hosts to prod (`.altinn.no`), which the browser rejects |
| `0003-bff-cookies-secure-auto.patch` | `packages/bff/src/server.ts`, `graphql/profile/queries.ts`, `graphql/profile/mutations.ts` | The session cookie (`@fastify/session`) and `altinnPersistentContext` (`@fastify/cookie`) are `secure: 'auto'` instead of `true`: `Secure` only when `request.protocol` is https, so the BFF sets its cookies without it on the stack's plain http and saves a session there; behind TLS unchanged (task 21b, `../../../reviews/cookies.md`). After a re-pin, grep `packages/bff/src` for `secure: true` and `setCookie` for a new cookie |

Nothing is proposed upstream (decided 2026-10-21). Patch 0001 is backwards compatible and is the shape such a pull
request would have.

## Upstream files mirrored by our own

| Upstream | Ours |
| --- | --- |
| `packages/bff/Dockerfile` | `../Dockerfile.bff` (and `../bff-start.sh`, the command) |
| `packages/frontend/Dockerfile` | `../Dockerfile.frontend` |
| `packages/frontend/nginx.conf` | `../nginx/nginx.conf` |
| `.dockerignore` | `../.dockerignore` |
| `packages/frontend/start.sh` | used as is, copied from `vendor/` |

## Syncing

`python3 localtest/tools/vendor-sync.py sync arbeidsflate` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

Here `sync` writes upstream's tree blob for blob (so the CRLF files and the files upstream's `.gitignore` ignores stay
as upstream has them), then carries each patch over: it cherry-picks the patch, applied to the old tree in a scratch
repository, onto the new tree, rewrites a clean one with fresh context and its `Against` line, and reports a conflicting
one, which keeps its old text and needs a hand-made hunk as small as it is now. `check` applies every patch (`git apply
--check`) to `vendor/`.

The mirrored files in the table above and the watched ones (`package.json`, `pnpm-workspace.yaml`, `.node-version`,
`packages/frontend/start.sh` and `index.html`, the BFF's `config.ts`, `auth/` and `server.ts`) are reported with
upstream's diff: new or renamed BFF settings go into studioctl's `components.go` (`arbeidsflateBFFComponent`) and
`../README.md`, cookie or session changes into the patches. Build both images (`podman build -f Dockerfile.bff -t
localtest-arbeidsflate-bff:dev .`, then `Dockerfile.frontend`, one at a time) and run `../smoke-test.sh`.
