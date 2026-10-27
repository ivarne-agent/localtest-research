# Vendored copy of studioctl from Altinn/altinn-studio

- Repository: https://github.com/Altinn/altinn-studio
- Commit: [`35c9f5dc83`](https://github.com/Altinn/altinn-studio/commit/35c9f5dc83)
  (2026-10-27): the head of [altinn-studio#21483](https://github.com/Altinn/altinn-studio/pull/21483) (the shared env
  stack and the Runtime/Env interface, the host's CA bundle in `stack` and devenv's `cabundle` split and validation,
  branch `refactor/studioctl-env-shared-runtime`, rebased onto `main` at `23899c2306`, which has the docs
  restructuring of [altinn-studio#21512](https://github.com/Altinn/altinn-studio/pull/21512)), vendored as if it were
  merged. When #21483 merges, sync to the `main` commit that contains it.
- Copied paths, monorepo layout preserved under `localtest/studioctl/` so relative references resolve as upstream:
  - `src/cli`: studioctl (Go CLI), `studioctl-server` (C#), `studioctl-server-tests` and `common/dotnet`.
  - `src/Runtime/devenv`: Go module `altinn.studio/devenv`; `src/cli/go.mod` replaces it with `../Runtime/devenv`.
  - `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common`: `studioctl-server.csproj` compiles its `HostBridge/**`
    and `EnvTopology/**` sources by relative path.
  - `src/Runtime/localtest/infra`: staged into the install by `src/cli/cmd/dev/main.go` during `make dev-install`.
  - `agents/_skills/app-development` (as `localtest/studioctl/agents/...`): the bundled app-development skill, which
    `src/cli/cmd/dev/main.go` reads from `../../agents/_skills/app-development` and `install/dist.go` ships in the
    resources archive. It moved there from `src/cli/resources/agent/skills` in altinn-studio#21512.
- Copied with `git archive` of that commit, no subtree. Baseline commit in this repository: `be07516c`
  ("baseline, unmodified"). It is never amended. Earlier baselines: `3f0dc1e6` (altinn-studio `9b678de2aa`, #21483
  before its rebase onto the docs restructuring; the change on it is `git log 3f0dc1e6..206073cb --
  localtest/studioctl/src`), `8ec94546` (altinn-studio `8e4650664e`, #21483
  before the CA bundle's digest-named copy; the change on it is `git log 8ec94546..94df77d0 -- localtest/studioctl/src`), `890a0366` (altinn-studio `eae0a7ea65`, #21483
  before its CA bundle commits; the change on it is `git log 890a0366..9deb53bc -- localtest/studioctl/src`), `9a6609eb` (altinn-studio `fd53bd173b`, #21483
  before its review fixes; the change on it is `git log 9a6609eb..b9e79879 -- localtest/studioctl/src`), `58c58a6b` (altinn-studio `e45c348eac`,
  2026-10-25; the interface itself was developed on it in `9d4f10b7` with both runtimes, then upstreamed without
  localtest-v2), `24562cba` (altinn-studio `b908931519`,
  2026-10-23; the change on it is `git log 24562cba..e56ab475 -- localtest/studioctl/src`) and `3ecc3fb` (altinn-studio
  `89b1d7bc70`, 2026-10-20; `git log 3ecc3fb..a1a5df42 -- localtest/studioctl/src`).
- Everything after the baseline is our change to upstream: the localtest-v2 runtime. There is no `#if LOCALTEST`
  here: this code is meant to be upstreamed as-is, so changes are ordinary commits. The first commit after the
  baseline (`f6a6249e`) re-applies the whole change as altinn-studio#21394 has it on the new base (`d9163a934c`)
  (`git log --oneline be07516c..HEAD -- localtest/studioctl`). localtest-v2 passes the CA bundle on
  through #21483's shared `stack` code. localtest-v2 implements the interface in
  `internal/cmd/env/localtestv2/runtime.go`. [`README.md`](README.md) "What the
  localtest-v2 change adds" describes the result file by file.
- Upstream, the change is a stack of branches on `ivarne-agent/altinn-studio`, each re-authored from this tree as a few
  logical commits:
  1. `refactor/studioctl-env-shared-runtime` ([altinn-studio#21483](https://github.com/Altinn/altinn-studio/pull/21483)):
     the env-runtime refactor and the Runtime/Env interface, which the pin above includes.
  2. `feat/studioctl-localtest-v2-stack` ([altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394)):
     the localtest-v2 change on the refactor, re-authored from this tree as a few logical commits and rebased with
     #21483 onto `35c9f5dc83` (head `d9163a934c`, 20 commits). At its head, `diff -r` of the copied paths against
     `localtest/studioctl` is empty.

  Later studioctl commits here go on top of branch 2.

## Regenerating the upstream patch

From the root of this repository, the whole change since the baseline, with paths already relative to the monorepo
root (`src/...`); `--binary` is needed for the `.pfx` asset:

```sh
git diff --binary --relative=localtest/studioctl be07516c HEAD -- localtest/studioctl/src localtest/studioctl/agents \
  > studioctl.patch
git -C <monorepo> checkout -b <branch> 35c9f5dc83
git -C <monorepo> apply --index < studioctl.patch
```

On the pinned commit the patch applies without a three-way merge; on a newer `origin/main` use `apply -3`, which needs
the monorepo's own blobs and has them there.

## Syncing to a newer upstream commit

The procedure used for `24562cba` (task 32, 2026-10-23), from the root of this repository:

1. Save the change: `git diff --binary <old-baseline> HEAD -- localtest/studioctl/src localtest/studioctl/agents > ours.patch`.
2. Replace the vendored tree with upstream's at the new commit and commit it unmodified, as the new baseline:

   ```sh
   git rm -rq localtest/studioctl/src && rm -rf localtest/studioctl/src
   git rm -rq localtest/studioctl/agents && rm -rf localtest/studioctl/agents
   git -C <monorepo> archive --format=tar --prefix=localtest/studioctl/ <new-commit> \
     src/cli src/Runtime/devenv src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common src/Runtime/localtest/infra \
     agents/_skills/app-development | tar -x -C .
   git add -A localtest/studioctl/src localtest/studioctl/agents && git commit -m "studioctl: vendor ... at <new-commit> (baseline, unmodified)"
   ```

   Check that `git diff --stat <old-baseline> HEAD -- localtest/studioctl/src` equals the monorepo's
   `git diff --stat <old-pin> <new-pin> -- <the copied paths>` (add `localtest/studioctl/agents` to the first).
3. Re-apply the change: `git apply -3 --index ours.patch`. This repository has the blobs of both sides (the old
   baseline and our files), so a conflict becomes markers, not a rejected hunk. Resolve in favour of upstream's
   structure with our behaviour kept, and commit as one "re-apply" commit.
4. Rebuild and test (`go build ./... && go test ./...` and `make lint` in `src/cli`; `dotnet test
   studioctl-server-tests/Studioctl.Tests.csproj`), then update the pin and the baseline above.

For `58c58a6b` (2026-10-25) the re-apply conflicted where the refactor moved code: `internal/cmd/env.go`, the
localtest runtime's `env.go`, and the moved `log_streamer.go` and `status.go`, which `apply -3` cannot follow. Those two
were excluded from the patch (`--exclude`) and ported to `stack/log_streamer.go` and `env/status.go` by hand.

For `8ec94546` (2026-10-27) only `AGENTS.md` and `CHANGELOG.md` conflicted, where #21483 added the CA bundle; both
sides were kept. localtest-v2's own CA bundle code (task 57) was then replaced by the shared code.

For `3f0dc1e6` (2026-10-27) the re-apply had no conflict: #21483 had taken the digest-named copy from this tree,
so the change re-applied unchanged.

For `be07516c` (2026-10-27) this repository's own patch was not used. #21483 was rebased onto `main` with the docs
restructuring (altinn-studio#21512: the `AGENTS.md` files folded into READMEs, the skill moved to
`agents/_skills`), and #21394 was re-applied there upstream, with its docs moved to `src/cli/docs/localtest-v2.md`,
`src/cli/README.md` and the skill, and `internal/cmd/app/env.go` merged with #21481's inbox and logout defaults. The
baseline added the skill's path, and the re-apply is the monorepo's
`git diff --binary 35c9f5dc83 d9163a934c -- <the five paths>`, applied with `--directory=localtest/studioctl`.
Before it, `diff -r` of this repository's tree against #21394's previous head `7f548efa73` was empty, so nothing of
ours was left out of the upstream branch; afterwards `diff -r` against `d9163a934c` was empty.

For `24562cba` the re-apply had no conflict: of the 57 changed files only `CHANGELOG.md` and
`internal/cmd/run.go` had also changed upstream, in other places.
