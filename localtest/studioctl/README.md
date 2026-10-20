# studioctl `localtest-v2` runtime (copy of the monorepo change)

The studioctl side of the stack is one commit on `Altinn/altinn-studio`, kept here so this repository holds the whole
picture while the change waits to be upstreamed:

- `0001-feat-studioctl-localtest-v2-runtime.patch`: `git format-patch --binary` of commit `43be33f73e` (2026-10-20),
  parent `89b1d7bc70` on `main`. Also pushed as `ivarne-agent/altinn-studio` branch `feat/studioctl-localtest-v2`.
- `monorepo/`: the 36 files the commit touches, at their monorepo paths, as they are after the commit. For reading
  and grepping; the patch is the thing to apply.

Apply on a fresh altinn-studio checkout:

```sh
git checkout -b feat/studioctl-localtest-v2 origin/main
git am --3way path/to/0001-feat-studioctl-localtest-v2-runtime.patch
```

What the commit adds (details in [`../../planned-components/studioctl-integration.md`](../../planned-components/studioctl-integration.md)):

| Where | What |
| --- | --- |
| `src/cli/internal/cmd/env/localtestv2/` | The runtime: component specs (`components.go`), start/stop/reset (`env.go`), HTTP health polling (`health.go`), topology (`topology.go`), the embedded test certificate (`certs.go`, `assets/localtest-accesstoken.pfx`) |
| `src/cli/internal/cmd/env/runtime.go`, `registry/`, `types.go`, `env.go` | `-r <runtime>` with one runtime at a time, defaulting to the last one started |
| `src/cli/internal/config/images.go` | `ghcr.io/ivarne-agent/localtest-research/localtest-<name>` images pinned by `sha-<commit>` (`tagLocaltestV2`), `--latest` |
| `src/cli/internal/localtestv2/state.go` | The state file both studioctl and studioctl-server read |
| `src/cli/internal/cmd/app/{env,run}.go`, `internal/studioctlserver/client.go` | App environment for the real components (platform token certificate), provisioning call on `studioctl run` |
| `src/cli/studioctl-server/Proxy/` | The front door: routes component prefixes, the PDF and workflow-engine hosts, falls back to the localtest app; keeps `Host`, strips `Secure` |
| `src/cli/studioctl-server/Provisioning/AppProvisioner.cs` | The deploy step: metadata, texts, policy, `testData.json` pushed to the components with the Studio token, Access Management refresh, register `DELETE` on app stop |
| `src/cli/README.md`, `AGENTS.md`, `CHANGELOG.md`, the bundled skill | Docs |

Dev loop in the monorepo: `make dev-install`, then `STUDIOCTL_HOME=build/dev-home build/dev-home/bin/studioctl env up -r localtest-v2`.
`make dev-install` runs `studioctl self install`, which stops every environment and app first.
