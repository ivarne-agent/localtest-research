# Vendored copy of Altinn/altinn-access-management-frontend

```json vendor-sync
{
  "repository": "Altinn/altinn-access-management-frontend",
  "branch": "main",
  "commit": "76c599d267c2bb701e75343d07dfe71018285a1a",
  "date": "2026-10-30",
  "paths": ["."],
  "group": null,
  "local": "patches",
  "patches": "patches",
  "mirrors": [{ "upstream": "Dockerfile", "ours": "Dockerfile" }],
  "watch": [
    "backend/src/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI/appsettings.json",
    "backend/src/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI/Program.cs",
    "backend/src/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI.Integration/Configuration/PlatformSettings.cs",
    "backend/src/Altinn.AccessManagement.UI/Altinn.AccessManagement.UI.Integration/Clients",
    "src/resources/utils/pathUtils.tsx",
    "package.json",
    ".yarnrc.yml"
  ],
  "projects": [],
  "follow": [],
  "reminders": [
    "new or renamed `PlatformSettings`, `GeneralSettings` or client base addresses go into the `access-management-ui` envs of studioctl's manifest and the component README's table",
    "a new cookie written with `Secure` outside `Response.Cookies` is not seen by the cookie policy (grep the BFF for `Set-Cookie`)",
    "a new hard-coded host in the SPA (`grep -rn altinn.no src/`) needs patch 0003's treatment if it is a link the stack serves",
    "move the base image digests of `Dockerfile` when upstream's move"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-access-management-frontend
- Commit: [`76c599d267c2bb701e75343d07dfe71018285a1a`](https://github.com/Altinn/altinn-access-management-frontend/tree/76c599d267c2bb701e75343d07dfe71018285a1a) (main, 2026-10-30)

<!-- vendor-sync:end pin -->
- Copied paths: the whole repository, blob for blob (`git ls-tree -r` of the commit, each blob written with its mode and
  staged with `git hash-object --no-filters`), no subtree: the SPA is the repository root and the BFF is under
  `backend/`, and the Yarn workspace needs the root's `package.json`, `yarn.lock`, `.yarn/` and `playwright/package.json`.
- **Pristine.** The commit `access-management-ui: vendor Altinn/altinn-access-management-frontend at 76c599d …, unmodified`
  holds the tree byte for byte: `git rev-parse <that commit>:localtest/components/access-management-ui/vendor` is
  `a26ae39…`, upstream's own tree id for the commit (`git -C <clone> rev-parse 76c599d^{tree}`). Blobs, not a checkout,
  because upstream's `.gitattributes` (`* text=auto`) would normalise its CRLF files and its `.gitignore` ignores six
  tracked files. This file is the only later addition.

## Directive sites: the patches

The SPA's change is TypeScript, which has no `#if`, and `vendor-sync.py` takes one mode per component, so every
localtest change is a unified diff in `../patches/`, applied at image build by the Dockerfile's `source` stage (`git
apply --check`, then `git apply`); `vendor/` itself is never edited. The C# hunks are `#if LOCALTEST` / `#if !LOCALTEST`
blocks (`LOCALTEST` defined by `../localtest.build.props`), so they read as the house's directive sites. Each patch says
what it changes and why in its header.

| Patch | Files | What |
| --- | --- | --- |
| `0001-bff-localtest-hooks.patch` | `backend/.../Altinn.AccessManagement.UI/Program.cs` | Two `#if LOCALTEST` blocks: after `WebApplication.CreateBuilder` the shared service-discovery registration, the shared cookie policy (no `Secure` on plain http) and `AccessTokenSettings` bound; in the JWT cookie options `RequireHttpsMetadata = false` (upstream: Development only) |
| `0002-bff-access-token-certificate.patch` | `backend/.../Altinn.AccessManagement.UI.Core/Services/AccessTokenProvider.cs` | `#if !LOCALTEST` the Key Vault certificate `#else` the stack's shared certificate from `AccessTokenSettings:AccessTokenSigningCertificateFullPath`, for the `amui` platform access tokens |
| `0003-spa-links-follow-origin.patch` | `src/resources/utils/pathUtils.tsx` | On plain http on `am.ui.<host>`: the platform, the start page, the inbox and the GUI's own base follow the page's origin instead of production's hosts |

Nothing is proposed upstream yet; [`../../../upstreaming/access-management-ui.md`](../../../upstreaming/access-management-ui.md)
lists what would remove each patch.

## Upstream files mirrored by our own

| Upstream | Ours |
| --- | --- |
| `Dockerfile` | `../Dockerfile` |

## Syncing

`python3 localtest/tools/vendor-sync.py sync access-management-ui` from the repository root stages the sync and prints
what to triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes. `sync` writes upstream's tree blob for blob, then carries each patch over (a clean cherry-pick rewrites it
with fresh context and its `Against` line; a conflict keeps the old text and is reported). The watched files (the
settings, `Program.cs`, the clients, `pathUtils.tsx`, `package.json`, `.yarnrc.yml`) are reported with upstream's diff:
new settings go into the `access-management-ui` envs of
`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json` and `../README.md`. Build the image
(`podman build -t localtest-v2-dev-access-management-ui .`) and run `../smoke-test.sh`.
