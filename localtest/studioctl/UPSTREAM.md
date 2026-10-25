# Vendored copy of studioctl from Altinn/altinn-studio

- Repository: https://github.com/Altinn/altinn-studio
- Commit: [`480110f40e`](https://github.com/ivarne-agent/altinn-studio/tree/refactor/studioctl-env-runtime-interface)
  (2026-10-25): `main` at `819ad7362e` plus two preparation PRs, vendored as if they were merged:
  [altinn-studio#21483](https://github.com/Altinn/altinn-studio/pull/21483) (the shared env stack, `e45c348eac`) and the
  Runtime/Env interface stacked on it (branch `refactor/studioctl-env-runtime-interface`, `480110f40e`). When they
  merge, sync to the `main` commit that contains them; the vendored tree does not change for them.
- Copied paths, monorepo layout preserved under `src/` so relative references resolve as upstream:
  - `src/cli`: studioctl (Go CLI), `studioctl-server` (C#), `studioctl-server-tests` and `common/dotnet`.
  - `src/Runtime/devenv`: Go module `altinn.studio/devenv`; `src/cli/go.mod` replaces it with `../Runtime/devenv`.
  - `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common`: `studioctl-server.csproj` compiles its `HostBridge/**`
    and `EnvTopology/**` sources by relative path.
  - `src/Runtime/localtest/infra`: staged into the install by `src/cli/cmd/dev/main.go` during `make dev-install`.
- Copied with `git archive` of that commit, no subtree. Baseline commit in this repository: `9a6609eb`
  ("baseline, unmodified"). It is never amended. Earlier baselines: `58c58a6b` (altinn-studio `e45c348eac`,
  2026-10-25; the interface itself was developed on it in `9d4f10b7` with both runtimes, then upstreamed without
  localtest-v2), `24562cba` (altinn-studio `b908931519`,
  2026-10-23; the change on it is `git log 24562cba..e56ab475 -- localtest/studioctl/src`) and `3ecc3fb` (altinn-studio
  `89b1d7bc70`, 2026-10-20; `git log 3ecc3fb..a1a5df42 -- localtest/studioctl/src`).
- Everything after the baseline is our change to upstream: the localtest-v2 runtime. There is no `#if LOCALTEST`
  here: this code is meant to be upstreamed as-is, so changes are ordinary commits. The first commit after the
  baseline (`e0e26724`) re-applies the whole change as it stood on the previous baseline (`git log --oneline
  9a6609eb..HEAD -- localtest/studioctl/src`). localtest-v2 implements the interface in
  `internal/cmd/env/localtestv2/runtime.go`. [`README.md`](README.md) "What the
  localtest-v2 change adds" describes the result file by file.
- Upstream, the change is a stack of branches on `ivarne-agent/altinn-studio`, each re-authored from this tree as a few
  logical commits:
  1. `refactor/studioctl-env-shared-runtime` ([altinn-studio#21483](https://github.com/Altinn/altinn-studio/pull/21483)):
     the env-runtime refactor, which the pin above includes.
  1b. `refactor/studioctl-env-runtime-interface`: the Runtime/Env interface, stacked on 1, which the pin includes too.
  2. `feat/studioctl-localtest-v2-stack` ([altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394)):
     the localtest-v2 change on the refactor: the runtime as first reviewed (six commits), then what this repository
     added after that (four commits). At its head, `diff -r` of `src/cli` and `src/Runtime/devenv` against
     `localtest/studioctl/src` is empty.

  Later studioctl commits here go on top of branch 2.

## Regenerating the upstream patch

From the root of this repository, the whole change since the baseline, with paths already relative to the monorepo
root (`src/...`); `--binary` is needed for the `.pfx` asset:

```sh
git diff --binary --relative=localtest/studioctl 24562cba HEAD -- localtest/studioctl/src > studioctl.patch
git -C <monorepo> checkout -b <branch> 480110f40e
git -C <monorepo> apply --index < studioctl.patch
```

On the pinned commit the patch applies without a three-way merge; on a newer `origin/main` use `apply -3`, which needs
the monorepo's own blobs and has them there.

## Syncing to a newer upstream commit

The procedure used for `24562cba` (task 32, 2026-10-23), from the root of this repository:

1. Save the change: `git diff --binary <old-baseline> HEAD -- localtest/studioctl/src > ours.patch`.
2. Replace the vendored tree with upstream's at the new commit and commit it unmodified, as the new baseline:

   ```sh
   git rm -rq localtest/studioctl/src && rm -rf localtest/studioctl/src
   git -C <monorepo> archive --format=tar --prefix=localtest/studioctl/ <new-commit> \
     src/cli src/Runtime/devenv src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common src/Runtime/localtest/infra \
     | tar -x -C .
   git add -A localtest/studioctl/src && git commit -m "studioctl: vendor ... at <new-commit> (baseline, unmodified)"
   ```

   Check that `git diff --stat <old-baseline> HEAD -- localtest/studioctl/src` equals the monorepo's
   `git diff --stat <old-pin> <new-pin> -- <the four paths>`.
3. Re-apply the change: `git apply -3 --index ours.patch`. This repository has the blobs of both sides (the old
   baseline and our files), so a conflict becomes markers, not a rejected hunk. Resolve in favour of upstream's
   structure with our behaviour kept, and commit as one "re-apply" commit.
4. Rebuild and test (`go build ./... && go test ./...` and `make lint` in `src/cli`; `dotnet test
   studioctl-server-tests/Studioctl.Tests.csproj`), then update the pin and the baseline above.

For `58c58a6b` (2026-10-25) the re-apply conflicted where the refactor moved code: `internal/cmd/env.go`, the
localtest runtime's `env.go`, and the moved `log_streamer.go` and `status.go`, which `apply -3` cannot follow. Those two
were excluded from the patch (`--exclude`) and ported to `stack/log_streamer.go` and `env/status.go` by hand.

For `24562cba` the re-apply had no conflict: of the 57 changed files only `CHANGELOG.md` and
`internal/cmd/run.go` had also changed upstream, in other places.
