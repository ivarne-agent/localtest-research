# Vendored copy of studioctl from Altinn/altinn-studio

- Repository: https://github.com/Altinn/altinn-studio
- Commit: [`b9089315198c8b947d3ff7c41001334af842a5d8`](https://github.com/Altinn/altinn-studio/tree/b9089315198c8b947d3ff7c41001334af842a5d8) (main, 2026-10-23)
- Copied paths, monorepo layout preserved under `src/` so relative references resolve as upstream:
  - `src/cli`: studioctl (Go CLI), `studioctl-server` (C#), `studioctl-server-tests` and `common/dotnet`.
  - `src/Runtime/devenv`: Go module `altinn.studio/devenv`; `src/cli/go.mod` replaces it with `../Runtime/devenv`.
  - `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common`: `studioctl-server.csproj` compiles its `HostBridge/**`
    and `EnvTopology/**` sources by relative path.
  - `src/Runtime/localtest/infra`: staged into the install by `src/cli/cmd/dev/main.go` during `make dev-install`.
- Copied with `git archive` of that commit, no subtree. Baseline commit in this repository: `24562cba`
  ("baseline, unmodified"). It is never amended. The previous baseline was `3ecc3fb` (altinn-studio `89b1d7bc70`,
  2026-10-20); the history of the change on top of it is `git log 3ecc3fb..a1a5df42 -- localtest/studioctl/src`.
- Everything after the baseline is our change to upstream: the localtest-v2 runtime. There is no `#if LOCALTEST`
  here: this code is meant to be upstreamed as-is, so changes are ordinary commits. The first commit after the
  baseline (`a1a5df42`) re-applies the whole change as it stood on the previous baseline; the commits after it are the
  task-32 review (`git log --oneline 24562cba..HEAD -- localtest/studioctl/src`). [`README.md`](README.md) "What the
  localtest-v2 change adds" describes the result file by file.
- The whole change is also on branch `feat/studioctl-localtest-v2-stack` on `ivarne-agent/altinn-studio`
  ([altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394)), re-authored from this tree as a few
  logical commits, so it can lag behind the latest studioctl commits here.

## Regenerating the upstream patch

From the root of this repository, the whole change since the baseline, with paths already relative to the monorepo
root (`src/...`); `--binary` is needed for the `.pfx` asset:

```sh
git diff --binary --relative=localtest/studioctl 24562cba HEAD -- localtest/studioctl/src > studioctl.patch
git -C <monorepo> checkout -b <branch> b9089315198c8b947d3ff7c41001334af842a5d8
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

For `24562cba` the re-apply had no conflict: of the 57 changed files only `CHANGELOG.md` and
`internal/cmd/run.go` had also changed upstream, in other places.
