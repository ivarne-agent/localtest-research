# Vendored copy of studioctl from Altinn/altinn-studio

- Repository: https://github.com/Altinn/altinn-studio
- Commit: [`89b1d7bc7057c54efb1cc47ef5f155c2c75ac55a`](https://github.com/Altinn/altinn-studio/tree/89b1d7bc7057c54efb1cc47ef5f155c2c75ac55a) (main, 2026-10-20)
- Copied paths, monorepo layout preserved under `src/` so relative references resolve as upstream:
  - `src/cli`: studioctl (Go CLI), `studioctl-server` (C#), `studioctl-server-tests` and `common/dotnet`.
  - `src/Runtime/devenv`: Go module `altinn.studio/devenv`; `src/cli/go.mod` replaces it with `../Runtime/devenv`.
  - `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common`: `studioctl-server.csproj` compiles its `HostBridge/**`
    and `EnvTopology/**` sources by relative path.
  - `src/Runtime/localtest/infra`: staged into the install by `src/cli/cmd/dev/main.go` during `make dev-install`.
- Copied with `git archive` of that commit, no subtree. Baseline commit in this repository: `3ecc3fb`
  ("baseline, unmodified"). It is never amended.
- Everything after the baseline is our change to upstream. There is no `#if LOCALTEST` here: this code is meant to be
  upstreamed as-is, so changes are ordinary commits. The first one is the localtest-v2 runtime (`7e3f1cd`, upstream
  commit `43be33f73e`, also branch `feat/studioctl-localtest-v2` on `ivarne-agent/altinn-studio`).

## Regenerating the upstream patch

From the root of this repository, the whole change since the baseline, with paths already relative to the monorepo
root (`src/...`); `--binary` is needed for the `.pfx` asset:

```sh
git diff --binary --relative=localtest/studioctl 3ecc3fb HEAD -- localtest/studioctl/src > studioctl.patch
git -C <monorepo> checkout -b <branch> origin/main
git -C <monorepo> apply -3 --index < studioctl.patch
```

The localtest-v2 output was checked to reverse-apply cleanly on the monorepo branch, so it is byte-identical to the
upstream commit. To upstream a single commit instead of the whole range, use `git diff ... <commit>^ <commit>`.

## Syncing to a newer upstream commit

1. In a monorepo checkout, apply the upstream change in the copied paths here, then update the pin above:
   `git -C <monorepo> diff <old-pin> <new-pin> -- src/cli src/Runtime/devenv src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common src/Runtime/localtest/infra | git apply -3 --directory=localtest/studioctl`
2. Resolve conflicts, rebuild (`go build ./... && go test ./...` in `src/cli`, `dotnet build studioctl-server/studioctl-server.csproj -c Release`)
   and check that `make dev-install` and `env up -r localtest-v2` still work.
3. Commit as one "sync" commit, separate from feature changes.

Not verified: the sync command has not been exercised yet. In particular, `git apply -3` needs the blobs in this
repository's object database to fall back to a three-way merge, which it may not have for the baseline paths; without
them conflicts are rejected hunks instead of markers.
