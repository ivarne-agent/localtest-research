# Syncing a vendored component with upstream

How an agent moves a component's `vendor/` to a newer upstream commit with
[`../tools/vendor-sync.py`](../tools/vendor-sync.py), triages what the tool cannot decide, and finishes the sync by
hand. The tool stages; the agent resolves, builds, verifies and commits. What to run after the commits (images, smoke
tests, the stack) is [`verify-after-sync.md`](verify-after-sync.md).

Built in [`../../TODO.md`](../../TODO.md) task 58 (2026-10-27), from the plan this page replaced, with the user's
decisions: the metadata lives in each `UPSTREAM.md`; line endings are tolerated, not fixed; syncs are triggered by hand
because each needs an agent's triage; the tool never commits; no CI and no schedule; workflow-engine is no longer
vendored. studioctl is out of scope: its copy is ordinary commits on a baseline
([`../studioctl/UPSTREAM.md`](../studioctl/UPSTREAM.md)), moved by re-applying a patch series.

## 1. The model

Each component under `localtest/components/<name>/` keeps upstream's files in `vendor/`, our code in `fakes/`, and the
build overrides (`localtest.build.props` / `.targets`) beside them. The rule the tool proves, `check`:

> `vendor/` is upstream at the pinned commit, file for file and mode for mode, except files with `LOCALTEST` blocks,
> whose **production view** equals upstream's file.

A block is either an addition, `#if LOCALTEST` … `#endif`, or a replacement, `#if !LOCALTEST` (upstream's text) `#else`
(ours) `#endif`. The production view of a file drops the additions and keeps the first branch of each replacement,
without the directive lines; the `LOCALTEST` view keeps the additions and the `#else` branches. Any other preprocessor
nesting inside a branch is fine. Refused, with the line: `#elif` in a block, `#else` in an addition, a second `#else`,
`#if !LOCALTEST` without `#else`, a block inside a block, any other condition that names `LOCALTEST`, a block without
`#endif`, a conflict marker. Sites are found, not declared: any file that differs from upstream must parse this way.

Tolerated, and reported as a note: a file that differs from upstream only by `\r\n` against `\n` or by a leading UTF-8
byte order mark (both sides are normalised before the comparison; Register's two `.sln` files, committed with CRLF, are
the case today). Everything is read as git blobs (`ls-tree`, `cat-file`, `hash-object --no-filters`), never through a
checkout or `git archive`, so the vendored trees' own `.gitattributes`, `export-ignore` and `core.autocrlf` cannot
change what is compared or written. Paths with spaces, executable bits and symlinks are kept.

### The metadata

A fenced block with the info string `json vendor-sync` under the title of each `vendor/UPSTREAM.md`; the prose stays
around it (an example: [`../components/profile/vendor/UPSTREAM.md`](../components/profile/vendor/UPSTREAM.md)):

| Key                    | Meaning                                                                                                                                                                                   |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `repository`, `branch` | Upstream, `OWNER/NAME`, and the branch whose head `sync` takes                                                                                                                            |
| `commit`, `date`       | The pin and its commit date (written by `sync`)                                                                                                                                           |
| `paths`                | Upstream paths copied with their layout; `["."]` for the whole repository (Arbeidsflate, Receipt)                                                                                         |
| `group`                | Components with the same group share repository and commit and move together (`altinn-auth`)                                                                                              |
| `local`                | `directives`, or `patches` with `patches` the folder of patch files (Arbeidsflate)                                                                                                        |
| `mirrors`              | Our files that mirror an upstream file (`{"upstream", "ours", "merge"}`, `ours` relative to the component folder); with `merge: true` `sync` three-way merges upstream's change into ours |
| `watch`                | Upstream paths whose change needs a decision (a release moved to `docker-bake.hcl`)                                                                                                       |
| `projects`             | The host projects to build with the overrides (path under `vendor/`, or `{"path", "configuration"}`)                                                                                      |
| `follow`               | Packages our build files pin at the host's version: `{"package", "follows", "file"}` (`follows` defaults to the package, `file` to `localtest.build.targets`)                             |
| `reminders`            | One-line manual checks `status` and `sync` print every time (Register's altinn-auth#4056)                                                                                                 |

Two parts of `UPSTREAM.md` are generated between `<!-- vendor-sync:begin pin -->` / `end` and `<!-- vendor-sync:begin
sites -->` / `end`: the Repository/Commit bullets, and the table of sites (file, lines, kind, first line of the
`LOCALTEST` branch). `render` writes them; `check` warns when they are stale. The prose names sites by file and call,
never by line.

## 2. The commands

Run from the repository root (or anywhere: the tool finds the root from its own path). The upstream repositories are
read from blobless clones (`git clone --filter=blob:none --no-checkout`) in `--clones DIR`, `$VENDOR_SYNC_CLONES`, or
`$XDG_CACHE_HOME/localtest-vendor` (`~/.cache/localtest-vendor`), as `<dir>/<owner>/<name>` or `<dir>/<name>`; a
missing clone is created, and blobs are fetched only for the files a command reads. All of them together are about
40 MB. On the agents' machine: `export VENDOR_SYNC_CLONES=/home/agent/code/.reference/upstream`.

| Command                                                                                                  | What it does                                                                                                                                                                                                                                                                                                                                       |
| -------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `status [NAME…\|--all] [--json] [--no-fetch]`                                                            | Read only. Fetches each upstream and prints how far the pin is behind: commits on the branch and those touching `paths`, the files changed (M/A/D), the sites, whether each site file upstream changed merges clean, the mirrors (merge clean or conflict), the watched paths, check errors, removal candidates and the reminders                  |
| `check [NAME…\|--all] [--index]`                                                                         | The invariant, on the working tree (`--index`: on the index). Exit 1 with `file:line` on a violation. Also: groups at one commit, Arbeidsflate's patches apply, removal candidates, stale generated sections (warning), the versions that `follow` the host once it was built (warning), and with `--all` the copies of `components/shared/fakes/` |
| `render [NAME…\|--all]`                                                                                  | Rewrites the generated pin bullets and site table                                                                                                                                                                                                                                                                                                  |
| `sync NAME [--to COMMIT] [--branch B] [--repository OWNER/REPO --paths P…] [--group G] [--summary FILE]` | Stages a new pin; see section 3. Exit 0 when clean, 2 with conflicts, 3 on a refusal                                                                                                                                                                                                                                                               |

NAME is a component (its folder name) or a group. Example, `status --all` on 2026-10-27 after Profile's sync (five of the 14
rows, and one of the reminders that follow the table):

```text
| Component | Upstream | Pin (commit date, age) | Behind the default branch: commits / touching the paths | Files changed in the paths (M/A/D) | Sites: files, blocks (addition/replacement) | Site files changed upstream | Mirrored files changed upstream | Watched paths changed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| authorization | Altinn/altinn-auth | `3d51810` (10-19, 8 d) | 32 / 8 | 46 (16/30/0) | 1, 3 (2/1) | none | none | `src/apps/Altinn.Authorization/containers/Dockerfile.api`; `src/apps/Altinn.Authorization/docker-bake.hcl` |
| notifications | Altinn/altinn-notifications | `a4aa959` (10-20, 7 d) | 12 / 9 | 83 (63/17/3) | 1, 4 (2/2) | `components/api/src/Altinn.Notifications/Program.cs`: merges clean | none | none |
| profile | Altinn/altinn-profile | `d0cd92d` (10-22, 5 d) | 0 / 0 | 0 | 1, 4 (3/1) | none | none | none |
| register | Altinn/altinn-register | `f6b5c60` (10-13, 14 d) | 0 / 0 | 0 | 1, 1 (1/0) | none | none | none |
| storage | Altinn/altinn-storage | `9e72a9f` (10-19, 8 d) | 6 / 4 | 2 (2/0/0) | 2, 5 (2/3) | none | `Dockerfile`: the merge into ours conflicts | none |

- register: reminder: Register waits for altinn-auth#4056 (https://github.com/Altinn/altinn-auth/issues/4056, an issue: move altinn-register into Altinn/altinn-auth; open on 2026-10-27): check it and its linked pull requests at every sync; once the move has landed, retarget with `sync register --repository Altinn/altinn-auth --paths ... --group altinn-auth`
```

## 3. Running a sync

Work in a worktree of this repository started from `main` (`/home/agent/code/.worktrees/<task>/`), one component or
group per sync. Set `VENDOR_SYNC_CLONES` if the clones live elsewhere.

### 3.1 Before

1. `python3 localtest/tools/vendor-sync.py status <name>` (or `--all` to choose): read the commits, the site files
   upstream changed and whether they merge, the mirrors, the watched paths and the reminders. A component behind by 0
   commits touching its paths needs no sync.
2. `check <name>` must pass, and `vendor/` must have no uncommitted change: `sync` refuses otherwise.

### 3.2 Stage

```sh
python3 localtest/tools/vendor-sync.py sync <name>             # the head of the branch; --to <commit> for another
```

What it does, per component (a group: every member to one commit):

- Each upstream file under `paths` that changed is written into the working tree and the index as upstream's blob with
  its mode; a deleted one is removed. Files upstream did not touch stay as they are, including a tolerated CRLF copy.
- Each site file upstream changed is merged: first `git merge-file --diff3` (ours, upstream's old file, the new one);
  when that conflicts, the blocks are **re-anchored** instead: the new file is upstream's, and each block goes back
  where upstream left its neighbours alone (an addition between two lines that are still next to each other, a
  replacement over lines upstream did not change; an insertion at a replacement's edge goes outside it). Either result
  must parse, its production view must equal upstream's new file, and every block's `LOCALTEST` branch must be the same
  as before; otherwise it is a conflict.
- A conflict is left **unmerged in the index** (stage 1 upstream's old file, 2 ours, 3 the new one; a deletion upstream
  has no stage 3), with git's markers in the working tree, so `git status` lists it and `git commit` refuses until it is
  resolved. The rest of the component is still updated.
- Mirrors with `merge: true` are merged into our file when clean and left **unstaged** for review; otherwise the hunk
  is printed. Watched paths that changed are listed.
- Arbeidsflate (`patches`): the tree is replaced, and each patch is cherry-picked from the old tree onto the new one in
  a scratch repository; a clean pick rewrites the patch file (fresh context, its `Against … <commit>` line), a conflict
  leaves it and is reported.
- `UPSTREAM.md`: the metadata moves to the new commit, the pin bullets and the site table are regenerated, staged.
- The summary is printed and written to `.git/…/vendor-sync-summary.txt` (`--summary` for another file): the subject
  `<name>: sync vendor to <short> (<date>)`, the commits that touch the paths, the files, the merges and conflicts,
  removal candidates, sites whose replaced code upstream changed, patches, mirrors, watched paths, reminders, and a
  one-line TODO note. Then the **next steps** for this component: what to triage, which hosts to build, and which
  verification levels apply with which `--dev-<container>` flags (from `images.yml` and studioctl's manifest).

### 3.3 Triage

The tool cannot judge these; the agent does, in this order:

- **Conflicts** (`git status`, "both modified"). Keep every block's `LOCALTEST` branch as it was; outside the blocks
  and inside each `#if !LOCALTEST` branch take upstream's new text exactly. Where a hook belongs in moved code is a
  judgement: read the commit that moved it. `git checkout --conflict=diff3 <file>` restores the markers. `git add` the
  file when done. A site file deleted or renamed upstream (the summary names the new path) means the block moves by
  hand, or goes.
- **"upstream changed the code replaced"** on a merged file: the `#if !LOCALTEST` branch now holds upstream's new text.
  Is our `#else` still right, and is the block still needed?
- **Removal candidates**: a replacement whose two branches hold the same code, or an addition with no code. The tool
  only sees what is in the file; a site that upstream made unnecessary elsewhere (Correspondence's party-id site after
  upstream #2157, removed in task 58's follow-up) is a reminder in the metadata. Removing a site is its own commit, with the smoke test that proves the
  behaviour.
- **Mirrors**: review merged ones (unstaged) and carry printed hunks by hand, keeping the `localtest:` markings.
  Base-image digests are the usual change.
- **Watched paths** and **reminders**: each one is a decision or a grep; record the outcome in the fixes commit.
- **The commits**: new settings, renamed types, new platform calls the fakes must follow.

Then:

```sh
python3 localtest/tools/vendor-sync.py render <name>            # the site table after a resolution
python3 localtest/tools/vendor-sync.py check <name>             # must pass
```

### 3.4 Build

The next steps print the build of each host with the overrides, as [`verify-after-sync.md`](verify-after-sync.md)
level 2.1 does it (from the component folder, so `vendor/global.json` does not apply), for example:

```sh
(cd localtest/components/profile && DirectoryBuildPropsPath=$PWD/localtest.build.props \
  DirectoryBuildTargetsPath=$PWD/localtest.build.targets dotnet build -c Release vendor/src/Altinn.Profile/Altinn.Profile.csproj)
```

Fix the fakes and the overrides for what broke. Then run `check <name>` again: with `obj/project.assets.json` present
it compares the `follow` packages, for example `follow: localtest.build.targets pins
OpenTelemetry.Exporter.OpenTelemetryProtocol 1.19.1, the host resolves OpenTelemetry 1.20.0: move it to the host's
version`. Dialogporten's host builds rewrite each host's `packages.lock.json`: restore them before `check`.

### 3.5 Commit

Two commits, so the vendored part stays reviewable on its own:

```sh
git commit -F "$(git rev-parse --git-path vendor-sync-summary.txt)"   # what sync staged, plus resolved conflicts: vendor/ and UPSTREAM.md
git add <our fixes> && git commit -m "fix(localtest): <name> after the sync to <short>: …"
```

Nothing but `vendor/` and `UPSTREAM.md` may be staged for the first (`git status` before committing). The second holds
the fakes, overrides, mirrors, smoke tests and READMEs. No `Co-Authored-By`.

### 3.6 Verify and report

Run the levels the summary named, in [`verify-after-sync.md`](verify-after-sync.md), under the machine's lock: usually
2.1 (done in 3.4), 2.5, 5.1 with `--dev-<container>` from the component folder, 4.2 with the component's smoke test, and
4.3 when the component is in an optional group. Record in the TODO entry (or the component README's "Verified") the
summary's TODO note with the results. After the merge the images workflow builds `sha-<commit>` images; the pinned check
(verify-after-sync 5.2) comes before `tools/bump-tag.sh` moves the pins.

## 4. Special cases

- **A group** (`altinn-auth`: Authentication, Authorization, Access Management): `sync altinn-auth` or any member
  moves all three to one commit, with one summary; `check` fails when their pins differ.
- **A repository move**: `sync <name> --repository OWNER/REPO --paths <paths in the new repository> [--branch B]
[--group G]`. The base of each merge is the old repository's file at the old pin, theirs the new repository's at the
  new commit, matched by path under `vendor/`; a site file whose path is not in the new tree is a conflict. `--group`
  joins a group, and then `--to` defaults to the group's commit. The commit list is the new repository's last 20 on the
  paths. Done: the Resource Registry to `Altinn/altinn-auth` (task 58's follow-up, 2026-10-27: the archived
  repository's `src/Directory.*` became the vertical's own `src/apps/Altinn.ResourceRegistry/Directory.*`, the one site
  file merged clean). Pending: Register after
  [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056).
- **A new component**: copy upstream's paths blob for blob (or start from an empty `vendor/` and `sync --to <commit>`
  with the metadata in place), write the metadata block and both marker pairs into `vendor/UPSTREAM.md`, `render`, then
  `check`.
- **What the tool never does**: commit, build, start containers, push images, move image pins, or decide whether a
  site is still needed.

## 5. Tests

```sh
python3 -m unittest discover -s localtest/tools        # 39 tests, about 1.5 s, standard library and git only
```

The parser's accepted and refused shapes, CRLF, BOM, the metadata, the generated sections; and `check`, `status`,
`render` and `sync` on throwaway repositories (a blobless clone of a local upstream): a one-line edit failing with file
and line, an added block passing, modes, symlinks, a space in a path, a merge, a re-anchored merge, a conflict left
unmerged, a group, a repository move, Arbeidsflate-style patches refreshed and refused.

## 6. What the plan measured (2026-10-27)

Kept for its findings; `status --all` reproduces the inventory. All path lists reproduced `vendor/` blob for blob.
Three site files (Notifications, Resource Registry, Storage) had one blank line more than upstream next to a block:
removed in task 58. Register's two `.sln` files are CRLF here and LF upstream (copied with `rsync` of a checkout whose
`.gitattributes` converts them): tolerated. Arbeidsflate needed `hash-object --no-filters` for the same reason, and
`git archive` drops `export-ignore` paths: hence blobs only. Authorization's release moved to `docker-bake.hcl` and
`containers/Dockerfile.api` on 2026-10-27 (`0a905aa`, `6228127`; watched). The Resource Registry's repository was
archived at our pin, the code lives in `Altinn/altinn-auth` `src/apps/Altinn.ResourceRegistry/` (its host file merges
clean against it). Correspondence's party-id site was made removable by upstream #2157 (`d2dc912`). Profile's `main`
moved OpenTelemetry to 1.20.0 past the exporter our overrides pinned: the first sync, below. workflow-engine (183
commits behind in the monorepo, 9 in its paths) was vendored but never run by the stack, and was removed.

**The first sync (Profile, 2026-10-27).** `sync profile`: `d1f9871` to `d0cd92d` ("Update nuget non-major dependencies
(#1069)"), 5 csproj files, the site file unchanged upstream; `check` passed; the host build passed and `check` warned
about the exporter; the fixes commit moved it to 1.20.0. Verification: see TODO.md task 58.
