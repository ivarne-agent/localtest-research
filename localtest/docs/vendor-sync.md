# Scripted vendor sync for the components (plan)

**Status: unapproved (2026-10-27), pending the user's review.** [`../../TODO.md`](../../TODO.md) task 58. Nothing here
is implemented, and no vendored tree or pin was changed to write it. studioctl is out of scope: its copy is ordinary
commits on a baseline ([`../studioctl/UPSTREAM.md`](../studioctl/UPSTREAM.md)), not directive sites, and it moves by
re-applying a patch series, not by a merge.

The goal: one tool that syncs one component, a group, or all 15 to a given or the latest upstream commit, keeps the
`LOCALTEST` sites (and Arbeidsflate's patches), proves the result is "upstream at the new pin plus our sites and nothing
else", builds what it changed, and writes the summary for the commit and the TODO note. What happens after a sync (images,
stack, `bump-tag.sh`) is in [`verify-after-sync.md`](verify-after-sync.md), written in parallel.

## 1. Inventory (measured 2026-10-27)

How it was measured: blobless clones (`git clone --filter=blob:none --no-checkout`) of the 11 upstream repositories
under `/home/agent/code/.reference/upstream/`, the existing `/home/agent/code/altinn-storage` and
`/home/agent/code/altinn-studio` fetched. For each component, a draft of the machine-readable path list (section 2.1)
was checked against the pin: the blob ids of `vendor/` in this repository's index (`git ls-files -s`) against
`git ls-tree -r <pin> -- <paths>`. **All 15 path lists reproduce `vendor/` exactly** (no file missing, none extra),
so the lists are ready to become the metadata. Each site file was then reduced to its production view (section 2.2)
and compared byte for byte with upstream's blob at the pin; the site files upstream changed since the pin were
three-way merged (`git merge-file`) in a scratch folder. Ages are from the pin's commit date.

| Component | Upstream | Pin (commit date, age) | Behind the default branch: commits / touching the paths | Files changed in the paths (M/A/D) | Sites: files, blocks (addition/replacement) | Site files changed upstream | Mirrored files changed upstream |
| --- | --- | --- | --- | --- | --- | --- | --- |
| access-management | Altinn/altinn-auth | `3d51810` (10-19, 8 d) | 31 / 11 | 34 (29/3/2) | 3, 5 (2/3) | none | none |
| authentication | Altinn/altinn-auth | `3d51810` (10-19, 8 d) | 31 / 1 | 21 (14/0/7) | 2, 2 (1/1) | none | none |
| authorization | Altinn/altinn-auth | `3d51810` (10-19, 8 d) | 31 / 8 | 46 (16/30/0) | 1, 3 (2/1) | none | release moved to `docker-bake.hcl` + `containers/Dockerfile.api` (10-27) |
| arbeidsflate | Altinn/dialogporten-frontend | `bb74dff` (10-21, 6 d) | 15 / 15 (whole repo) | 95 (76/14/5) | 3 patches, no directives | the 3 patches apply to `main` unchanged | none of the 5 mirrored; `package.json`, `pnpm-workspace.yaml`, `packages/bff/src/config.ts` (UPSTREAM.md steps 3–4) |
| correspondence | Altinn/altinn-correspondence | `6dcf561` (10-16, 11 d) | 2 / 2 | 7 (7/0/0) | 3, 6 (4/2) | none, but `d2dc912` (#2157) passes `urn:altinn:partyid:` to the PDP request factory, which is what the `XacmlRequestFactory.cs` site works around | none |
| dialogporten | Altinn/dialogporten | `ec333e4` (10-20, 7 d) | 10 / 5 | 31 (24/7/0) | 5, 7 (3/4) | none | the 3 host Dockerfiles (aspnet 10.0.13, sdk 10.0.402); a three-way merge into ours conflicts in all 3 |
| dialogporten-adapter | Altinn/altinn-dialogporten-adapter | `86364b8` (10-19, 8 d) | 2 / 2 | 2 (2/0/0) | 4, 10 (4/6) | `StorageDialogportenDataMerger.cs`: merges clean | none |
| events | Altinn/altinn-events | `1a4a80c` (10-19, 8 d) | 6 / 2 | 2 (2/0/0) | 3, 7 (3/4) | none | none |
| notifications | Altinn/altinn-notifications | `a4aa959` (10-20, 7 d) | 12 / 9 | 83 (63/17/3) | 1, 4 (2/2) | `Program.cs`: merges clean | none |
| profile | Altinn/altinn-profile | `d1f9871` (10-19, 8 d) | 2 / 1 | 5 (5/0/0) | 1, 4 (3/1) | none | none |
| receipt | Altinn/altinn-receipt | `babc0ed` (10-20, 7 d) | 1 / 1 (whole repo) | 1 (1/0/0) | 1, 4 (2/2) | none | `Dockerfile` (the SDK tag): merges clean into ours |
| register | Altinn/altinn-register | `f6b5c60` (10-13, 14 d) | 0 / 0 (no commit since 10-13) | 0 | 1, 1 (1/0) | none | none |
| resource-registry | Altinn/altinn-resource-registry, **archived** | `a342241` (09-25, 32 d) | 0 in the archived repository; it lives on in altinn-auth | in altinn-auth `src/apps/Altinn.ResourceRegistry/src/`: 32 differ, 10 added, 2 removed | 1, 2 (2/0) | the host file merges clean against altinn-auth's | (moved) |
| storage | Altinn/altinn-storage | `9e72a9f` (10-19, 8 d) | 6 / 4 | 2 (2/0/0) | 2, 5 (2/3) | none | `Dockerfile` (both base tags): the merge into ours conflicts |
| workflow-engine | Altinn/altinn-studio | `447e53e` (10-16, 11 d) | 183 / 9 | 62 (53/6/3) | none | — | none |

Totals: 28 site files with 60 blocks (31 pure `#if LOCALTEST` additions, 29 `#if !LOCALTEST` / `#else` replacements; no
`#elif`, no `#if !LOCALTEST` without `#else`, no nesting), 3 patches, no edited csproj. Every .NET component injects its
fakes the same way: `localtest.build.props` / `.targets` beside `vendor/`, selected by `DirectoryBuildPropsPath` /
`DirectoryBuildTargetsPath` in the Dockerfile, define `LOCALTEST` for vendored projects and compile `fakes/` in
(Register through its own fakes project). Every component's Dockerfile mirrors upstream's with `localtest:` lines
(3 to 16 per component).

**Irregularities a script must handle**, all found by the measurement above:

- **The invariant is broken by one blank line in three files.** The production view of
  `notifications/.../Altinn.Notifications/Program.cs` (after line 45), `resource-registry/.../ResourceRegistryHost.cs`
  (after line 193) and `storage/.../Storage/Program.cs` (after line 79) has one blank line more than upstream: a blank
  line was added next to a block instead of inside it. The other 25 site files reduce to upstream's blob byte for byte.
- **Register's two `.sln` files** (`Altinn.Register.sln`, `Altinn.Register.Contracts.sln`) are committed here with CRLF;
  upstream's blobs are LF. Register was copied with `rsync` of a checkout whose `.gitattributes` turns `*.sln` into CRLF
  on checkout. The same trap is why Arbeidsflate needed `git hash-object --no-filters`, and `git archive` (which every
  `UPSTREAM.md` and `repin-vendor.py` use) applies the same `eol`/`text` conversions and drops `export-ignore` paths. The
  tool must read and write **blobs** (`git ls-tree`, `git cat-file blob`, `git update-index --cacheinfo`), never a checkout
  or an archive, and compare index blob ids, not working-tree bytes. Two vendored trees carry their own `.gitattributes`
  (arbeidsflate, receipt) that apply inside this repository too.
- **Modes**: 14 executable files and one symlink (`arbeidsflate/vendor/AGENTS.md`); `repin-vendor.py` copies with
  `shutil.copyfile`, which loses both. 219 tracked paths under `components/` contain a space (Authentication's
  `TokenClientAuthType .cs` among them): `-z` everywhere.
- **Whole-repository copies** (arbeidsflate, receipt): the path list is `.`; a top-level entry upstream deletes is then
  handled, which receipt's manual `$(git ls-tree --name-only <new>)` list misses.
- **Partial folders** (workflow-engine): `UPSTREAM.md`'s repin command passes whole folders and then deletes `tests/`,
  `docs/` and the rest again by hand; the explicit path list (27 entries) avoids that.
- **One upstream, several components**: Authentication, Authorization and Access Management share `Altinn/altinn-auth`
  at one pin and share libraries (`Api.Contracts`, `ABAC`, `PEP`); they must move together.
- **Repository moves**: the Resource Registry's repository was archived on 2026-09-25 (at our pin) and the code lives in
  `Altinn/altinn-auth` `src/apps/Altinn.ResourceRegistry/` with its own `Directory.Build.*` and `Directory.Packages.props`
  there, active (commits 10-20 to 10-27). `altinn-register` has had no commit since 10-13; altinn-auth has a 17-file
  skeleton `src/apps/Altinn.Register/` (no source yet; altinn-auth#4056). A sync must be able to change the repository and
  the path list, and the Resource Registry would then join the altinn-auth group.
- **Package versions that follow the host**: `localtest.build.targets` pins packages "at the host's version" (the OTLP
  exporter 1.19.1 in Storage, Profile and others). Profile's `main` moves `OpenTelemetry.*` to 1.20.0; a scratch build of
  Profile at `main` with the overrides succeeds but resolves `OpenTelemetry` 1.20.0 with the exporter
  at 1.19.1. Register's fakes project follows `Directory.Packages.props` the same way.
- **Dockerfile mirrors are of three shapes**: one-to-one (most), one local file per upstream host (Dialogporten's three),
  and one local file assembled from three upstream files (Access Management: `containers/Dockerfile.aspnet-builder`,
  `-runtime` and the app's `containers/Dockerfile.api`). A three-way merge into ours (base: upstream's file at the pin,
  theirs: at the new commit) is clean for Receipt and conflicts for Storage and Dialogporten, where a `localtest:`
  comment sits next to the changed `FROM` line.
- **Release definitions move**: Authorization's image is built from `docker-bake.hcl` and `containers/Dockerfile.api`
  since today (`0a905aa`, `6228127`); our `../Dockerfile` follows `src/apps/Altinn.Authorization/Dockerfile`, which the
  release no longer uses.
- **workflow-engine** is vendored but not built by `images.yml`; the stack runs the monorepo's `tt02` image. Its copy only
  serves `smoke-test.sh` and a `--dev` build.
- **Invocation drift**: the adapter's `UPSTREAM.md` runs the script from inside `vendor/` with `.` as target, the others
  from the component folder; the line numbers in prose are checked on different dates (the Resource Registry's say
  "line 46", "line 191" for blocks 46-49 and 191-193).

## 2. Design

One Python 3 script with the standard library only, `localtest/tools/vendor-sync.py`, which replaces
`repin-vendor.py` (its file-by-file loop is the seed of `sync`). Three commands:

- `status [<component>|<group>|--all]`: read only; the inventory table above, as Markdown or `--json`.
- `check [<component>|<group>|--all]`: read only; the invariant (2.4), exit 1 with the file and line on a violation.
  This is task 1.
- `sync <component>|<group>|--all [--to <commit>] [--commit] [--no-build]`: moves the pin. Without `--to`, the head of
  the default branch (`git ls-remote`).

Upstream clones: blobless, in `--clones <dir>` (default `$XDG_CACHE_HOME/localtest-vendor/<owner>/<repo>`; on this
machine `/home/agent/code/.reference/upstream/`), created or fetched on each run. Cheap: the 11 clones together are
37 MB, altinn-auth clones in 1.6 s; blobs are fetched only for the files a command reads.

### 2.1 Metadata

A fenced JSON block at the top of each `vendor/UPSTREAM.md`, under the title, with the info string `json vendor-sync`;
the prose stays. This keeps the decision of 2026-10-20 (the pin stays in `UPSTREAM.md`, no separate pin file) while
making it machine-readable, and keeps the component folder self-contained for the move into the monorepo
([`../../planned-components/studioctl-integration.md`](../../planned-components/studioctl-integration.md) section 1).
JSON because the standard library reads and writes it (`tomllib` cannot write) and a CI step in any language reads it.

```json vendor-sync
{
  "repository": "Altinn/altinn-profile",
  "branch": "main",
  "commit": "d1f9871c40bf00eda949338957ce4259e4ae5a24",
  "date": "2026-10-19",
  "paths": ["src", "Dockerfile", ".dockerignore", "docker-compose.yml", ".editorconfig", "stylecop.json"],
  "group": null,
  "local": "directives",
  "mirrors": [{ "upstream": "Dockerfile", "ours": "../Dockerfile", "merge": true }],
  "watch": [],
  "projects": ["src/Altinn.Profile/Altinn.Profile.csproj"],
  "follow": ["OpenTelemetry.*"],
  "reminders": []
}
```

- `paths`: upstream paths, relative layout preserved under `vendor/`; `.` for the whole repository. The 15 lists measured
  in section 1 are in the appendix.
- `group`: components with the same group share `repository` and `commit` and sync together (`altinn-auth`).
- `local`: `directives` or `patches` (Arbeidsflate, with `"patches": "../patches"`).
- `mirrors`: our files that mirror an upstream file; `merge: true` for one-to-one mirrors (section 2.5).
- `watch`: upstream paths outside `paths` whose change needs a human (Authorization's and Access Management's
  `docker-bake.hcl` and `containers/`).
- `projects`: the .NET host projects to build with the overrides (2.4); empty for Arbeidsflate.
- `follow`: package id patterns that `localtest.build.targets` (or Register's fakes project) pins at the host's version.
- `reminders`: one-line manual checks printed after every sync, taken from today's prose: Authentication's and
  Correspondence's `Set-Cookie` grep, Correspondence's two `#if !LOCALTEST` branches to compare word for word and the
  migration count of its smoke test, Arbeidsflate's `secure: true` grep and BFF environment, Notifications' Yuniql
  migrations on a fresh volume, Register's move to altinn-auth.

Two parts of `UPSTREAM.md` become generated, between `<!-- vendor-sync:begin pin -->` / `end` and
`<!-- vendor-sync:begin sites -->` / `end` markers: the Repository/Commit bullets (link, date, "re-pinned from … on …"),
and a table of the sites (file, lines, kind, the first line of the `LOCALTEST` branch). The prose keeps the why of each
site, named by file and call, and loses its line numbers, which today are checked by hand on different dates. The
"Syncing" section shrinks to the tool's command line and the component's manual steps.

### 2.2 Directive sites: detection and the merge

**Detection.** A site file is a file under `vendor/` whose text has a line `#if LOCALTEST` or `#if !LOCALTEST`
(whitespace allowed, a trailing `//` comment allowed). The parser (prototyped for this plan, 40 lines) walks the
file's lines with their line endings, tracks nested `#if`/`#endif`, and accepts exactly two shapes: `#if LOCALTEST … #endif`
(an addition) and `#if !LOCALTEST … #else … #endif` (a replacement). `#elif`, a second `#else`, or `#if !LOCALTEST`
without `#else` is an error. The **production view** of a file drops every addition and keeps only the first branch of
every replacement, without the directive lines. The site list is derived, never declared: any file that differs from
upstream must be a site file, and its production view must equal upstream's blob byte for byte (BOM and line endings
included). That is stronger than `diff -rq` allowing the site files, and it proves the `!LOCALTEST` branch is upstream's
text, so the production side compiles exactly when upstream does; no build without `LOCALTEST` is needed.

**Merge.** Per file in `paths`, from the trees at the old and the new commit (blob ids, so unchanged files cost
nothing):

| Upstream | Site file? | Action |
| --- | --- | --- |
| unchanged | either | nothing |
| changed, added | no | write the new blob, with its mode |
| deleted | no | remove |
| changed | yes | `git merge-file --diff3 -L localtest -L <old> -L <new> <ours> <old blob> <new blob>`, then validate |
| deleted or renamed (`git diff -M`) | yes | conflict, reported with the new path if renamed; the file is left as it was |

Validation after a clean merge: the result parses (2 shapes), its production view equals the new blob, and the list of
`LOCALTEST` branch texts is the same as before (no block lost, none changed). A clean textual merge that fails it (a
hunk landed inside a block) counts as a conflict. A conflict leaves the markers in the file, is listed with the block's
old line range, and makes `sync` exit 2 without committing; the rest of the component is still updated so the person
resolving sees the whole new tree. Measured on today's upstreams: the 2 site files upstream changed (Notifications'
`Program.cs`, the adapter's `StorageDialogportenDataMerger.cs`) merge clean, and so does the Resource Registry's host
file against altinn-auth.

**Writing.** Files are written from `git cat-file blob` and staged with `git hash-object -w --no-filters` and
`git update-index --cacheinfo <mode>,<sha>,<path>`, so the index holds upstream's blob ids whatever the vendored
`.gitattributes` or the user's `core.autocrlf` say; Arbeidsflate's hand-written loop in its `UPSTREAM.md` becomes the
general rule. `bin/` and `obj/` are ignored. A group is synced as one unit: every member to the same commit, one commit.

### 2.3 Patch-based components (Arbeidsflate)

`vendor/` is upstream's tree byte for byte (`git write-tree --prefix=…/vendor/` equals the commit's tree once
`UPSTREAM.md` is excluded; the tool checks the blob list instead, so `UPSTREAM.md` may stay where it is). `sync` replaces
the tree from blobs, then refreshes the patches in a scratch repository: commit the old tree, apply each patch as one
commit (`git apply`, the header kept as the commit message), then `git cherry-pick` the patch commits onto a commit of
the new tree. A clean pick regenerates the patch file (`git diff` of that commit, fresh context and line numbers, the
header kept, its "Against Altinn/dialogporten-frontend <sha>" line updated); a conflicting pick is reported per patch with
the conflicting files and leaves that patch file unchanged. The patches carry no `index` lines, so `git apply -3` alone
cannot do this. Today all three patches apply to `main` without change.

### 2.4 Verification the tool does

1. **`check`** on the result: the index blob ids of `vendor/` equal the new tree's, except the site files, whose
   production view equals the new blob; for Arbeidsflate, the patches apply (`git apply --check`) to the new tree.
2. **Build with the overrides** (`--no-build` skips): `dotnet build -c Release` of each of `projects` with
   `DirectoryBuildPropsPath` and `DirectoryBuildTargetsPath` set to the component's files, one at a time, on the host.
   This compiles the `LOCALTEST` branches and the fakes against the new upstream. Measured: Profile with `vendor/src` at
   `main` and its `Program.cs` builds this way in seconds (warm NuGet cache). It is not the image build (the image leaves some vendored
   root `Directory.Build.props` out, the host build finds them), which stays in the verification after the sync.
3. **Versions that follow**: for each `follow` pattern, compare the versions in `localtest.build.targets` with what the
   host resolves (`obj/project.assets.json`) and warn on a mismatch (Profile at `main`: exporter 1.19.1, OpenTelemetry
   1.20.0).
4. **Shared fakes**: each copy of a file of `components/shared/fakes/` is identical to it (all are today, file by file;
   the `ServiceDiscovery/` folders hold one or two of its files on purpose).

No image build, no smoke test, no stack: those need the machine's one-build-at-a-time rule and a running stack, and are
[`verify-after-sync.md`](verify-after-sync.md).

### 2.5 Mirrors and watched paths

For each mirror, the tool prints upstream's diff of the file. With `merge: true` it three-way merges the change into ours
(base: upstream's file at the old pin, theirs: at the new commit) and keeps the result only when clean; on a conflict
our file stays untouched and the hunk is in the summary. Section 1 measured both outcomes. Base-image digests are the
usual change. For `watch` paths a change is only reported, as "needs a decision".

### 2.6 Output

Per component (or group), printed and written to `--summary <file>`:

- `<component>: sync vendor to <short> (<date>)` as the subject, then the old and new pin, the commit count and the
  subjects of the commits that touch `paths` (at most 20, then "and N more"), the files updated/added/removed;
- the site files merged clean, the conflicts with their old block ranges, the blocks whose lines moved;
- the patches refreshed or failing; the mirrors merged, or left with their hunk; the watched paths that changed;
- the build result and the version warnings; the reminders;
- one line for the TODO or README note: `<component> synced <old>..<new> (<date>, N commits, M files, K sites merged
  clean, verify-after-sync pending)`.

With `--commit` the tool commits `vendor/` and `UPSTREAM.md` (and the refreshed patches) alone, with that message,
"separate from localtest changes" as every `UPSTREAM.md` asks; mirrors and fakes are never in that commit. `--json`
gives the same data for CI.

### 2.7 What stays manual, and why

- **Conflicts in site files and refused patches**: where a hook belongs in moved code is a judgement.
- **Whether a site is still needed, or a new one is**: the tool cannot know that upstream fixed the cause. Today's case:
  altinn-correspondence `d2dc912` (#2157, 2026-10-22) passes `urn:altinn:partyid:{id}`, so Correspondence's
  `XacmlRequestFactory.cs` site (task 7b, `planned-components/correspondence/party-ids.md` option (c)) may now be deleted.
- **The fakes and the overrides** after upstream renames and new required settings: the build says what broke; fixing it
  is code, in its own commit.
- **Mirror conflicts, watched paths and repository moves**: Authorization's release now comes from `docker-bake.hcl`,
  which changes what `../Dockerfile` mirrors; the Resource Registry's repository is archived.
- **Images, smoke tests, the stack, and moving the image pin**: a pin is a statement that the stack ran with that build
  ([`../CONVENTIONS.md`](../CONVENTIONS.md#tags)); no script makes it.

### 2.8 How it fits

- **`tools/repin-vendor.py`**: absorbed into `sync` and deleted when the last `UPSTREAM.md` stops naming it. It reads
  through `git archive` (filters, `export-ignore`), loses modes, keeps upstream files that the copy leaves out when a
  path is a whole folder, and checks nothing after merging.
- **The images workflow** ([`../../.github/workflows/images.yml`](../../.github/workflows/images.yml)) builds every
  component on a push to `main` under `localtest/components/**`, so a merged sync produces `sha-<commit>` images with no
  extra step. workflow-engine is not in it.
- **`tools/bump-tag.sh`** moves every research image to one `sha-<commit>` once that build is proven on the stack, then
  the golden snapshot is regenerated. The tool ends its summary with this sequence and the link to
  [`verify-after-sync.md`](verify-after-sync.md); it never runs it.
- **CI (optional, task 1's idea)**: a workflow `vendor.yml` on pull requests and pushes touching `localtest/components/**`
  runs `vendor-sync.py check --all` (fails on a violation; blobless clones cached by `actions/cache`), and a weekly
  schedule runs `status --all` into the job summary (and, if wanted, one issue it opens or updates). Neither writes to the
  repository.

## 3. Steps

Each step is one mergeable change with its proof. Steps 1–3 prove the tool on one component; later steps can run in any
order once 3 is merged.

1. **Fix the four drifts.** Move the blank line into the block in Notifications' `Program.cs`, the Resource Registry's
   `ResourceRegistryHost.cs` and Storage's `Program.cs` (in-rule changes inside the directive convention), and restore
   upstream's LF blobs of Register's two `.sln` files. Proof: the prototype check from this plan reports all 28 site
   files equal to upstream in production view and Register with no other difference; the four images still build
   (`--dev` builds, one at a time).
2. **Metadata and `check`/`status`.** The JSON block in all 15 `UPSTREAM.md` files (the measured path lists; groups,
   mirrors, watch, projects, follow, reminders), the parser, `check` and `status`, and unit tests for the parser
   (addition, replacement, nested `#if`, `#elif` refused, `#if !LOCALTEST` without `#else` refused, CRLF, BOM, a space
   in a path, a symlink). Proof: `check --all` passes; a one-line edit in a vendored file fails naming the file and line;
   an added well-formed block passes (task 1's verification); `status --all` reproduces section 1's table.
3. **`sync` on Profile.** Why Profile: its own repository (no group), an explicit path list, one site file with both
   block kinds, an unchanged Dockerfile mirror, a smoke test, and a real but small upstream change (2 commits, 5 csproj
   files) that exercises the version check (OpenTelemetry 1.20.0 against the exporter pinned at 1.19.1). Proof: `sync
   profile --commit` produces a vendored-only commit whose message is the summary; `check profile` passes; the host build
   passes and warns about the exporter; the follow-up commit moves the exporter and the image passes `smoke-test.sh` per
   [`verify-after-sync.md`](verify-after-sync.md). Profile's `UPSTREAM.md` gets the generated sections and loses its line
   numbers.
4. **The merge path and the directive-heavy components**, one commit pair each: the Dialogporten adapter (10 blocks;
   `StorageDialogportenDataMerger.cs` merged), Notifications (`Program.cs` merged; 83 files), Events (7 blocks),
   Correspondence (6 blocks, then the separate decision on the party-id site), Storage (5 blocks; the Dockerfile conflict
   left for the hand), Receipt (whole repository; the Dockerfile merge), Dialogporten (7 blocks, three hosts). Proof per
   component: `check` passes, the summary names exactly the merged site files, the host build passes; images and smoke
   tests per the verification doc. Includes the mirror merge of 2.5 (proved on Receipt clean, Storage left).
5. **The altinn-auth group.** Authentication, Authorization and Access Management in one sync; Authorization's
   `watch` hit (the release now from `docker-bake.hcl`) is decided by hand. Proof: one commit for the three, `check`
   passes for all three.
6. **Arbeidsflate.** The `patches` mode of 2.3. Proof: `sync arbeidsflate` to `main` refreshes the three patches (their
   "Against" line, fresh context), the blob list equals the commit's tree, a deliberately broken hunk is reported for that
   patch only; both images build and `smoke-test.sh` passes.
7. **Repository moves.** `sync --repository <owner/repo> --paths …` for the Resource Registry from altinn-auth (base: the
   archived repository's blob at the old pin, theirs: altinn-auth's at the new commit; it then joins the group). Proof:
   `check` passes against altinn-auth; the host file merged clean as measured; the smoke test. Register waits for
   altinn-auth#4056.
8. **workflow-engine**, or its removal (open question 5).
9. **Retire `repin-vendor.py`** and the manual sync recipes in the `UPSTREAM.md` files (done per component in steps 3–7;
   this step deletes the script). Proof: `grep -r repin-vendor` is empty.
10. **CI (optional).** `vendor.yml` with `check` and the weekly `status`. Proof: the workflow passes on `main`, fails on a
    branch with a one-line vendored edit, and the scheduled run's summary shows the table.

## 4. Risks and open questions

**Risks.**

- A clean textual merge can still put a hook in the wrong place (a registration moved after our block). The validation
  proves fidelity to upstream, not behaviour; the build and the verification after the sync catch the rest.
- Syncing to the latest commit pulls in unreleased upstream work (Arbeidsflate: 15 commits, two releases; Authorization
  switching its release build the same day). Pinning to releases would be calmer for some.
- A pin that leaves the default branch (a force push, a branch rename) is reported by `status`, not repaired.
- workflow-engine's 183-commit distance is the monorepo's, not the engine's (9 touch its paths); `status` must count
  commits touching `paths`, as the table does.
- Line endings and BOMs: upstreams change them wholesale (altinn-auth `3939549`, "remove BOM from most file-types"); a
  blob-level tool handles it, a checkout-based one does not.
- This plan's TODO entry and the parallel verification doc may both touch `TODO.md`.

**Questions for the user.**

1. **Metadata**: a JSON block inside `UPSTREAM.md` (recommended; the pin stays in `UPSTREAM.md` as decided on 2026-10-20)
   or a separate `upstream.json` beside `vendor/`? And may the prose lose its line numbers in favour of the generated
   table?
2. **Strictness**: fix the three stray blank lines and Register's `.sln` line endings (step 1, recommended), or let the
   check tolerate blank lines next to a block?
3. **Target and cadence**: sync to the default branch's head, or to release tags where upstream has them (Arbeidsflate,
   Receipt's "backend changes only" rule)? Sync all regularly, or on demand only?
4. **Commits**: may the tool commit the vendored part itself (`--commit`), or only stage and print the message?
5. **Repository moves**: re-vendor the Resource Registry from altinn-auth now and add it to the altinn-auth group? Keep
   Register on the frozen `altinn-register` until altinn-auth#4056 lands? Keep vendoring workflow-engine, which no image
   of ours is built from?
6. **CI**: `check` on pull requests and pushes only, or also the weekly `status`, and should that open or update an
   issue? Should the Dockerfile mirrors be compared in CI too (task 1's second open question)?
7. **Correspondence's party-id site**: upstream #2157 makes it removable; drop it in the first Correspondence sync, or as
   its own task?

## Appendix: the measured path lists

Each list reproduces the component's `vendor/` exactly at its pin (2026-10-27): same paths, same blob ids, except the
site files and Register's two `.sln` files. The repository is `Altinn/<name>`.

```json
{
  "access-management": { "repository": "Altinn/altinn-auth", "paths": [".editorconfig", "global.json", "containers/Dockerfile.aspnet-builder", "containers/Dockerfile.aspnet-runtime", "src/Altinn.ruleset", "src/Directory.Build.props", "src/Directory.Build.targets", "src/Directory.Packages.props", "src/Stylecop.json", "src/apps/Altinn.AccessManagement", "src/apps/Directory.Build.props", "src/apps/Directory.Build.targets", "src/libs/Altinn.Authorization.Api.Contracts", "src/libs/Altinn.Authorization.Host", "src/libs/Altinn.Authorization.Integration", "src/libs/Directory.Build.props", "src/libs/Directory.Build.targets", "src/pkgs/Altinn.Authorization.ABAC", "src/pkgs/Altinn.Authorization.PEP", "src/pkgs/Directory.Build.props", "src/pkgs/Directory.Build.targets", "src/pkgs/Directory.Packages.props", "src/pkgs/README.md"] },
  "authentication": { "repository": "Altinn/altinn-auth", "paths": [".editorconfig", "global.json", "src/Directory.Build.props", "src/Directory.Build.targets", "src/Directory.Packages.props", "src/apps/Altinn.Authentication", "src/apps/Directory.Build.props", "src/apps/Directory.Build.targets", "src/pkgs/JWTCookieAuthentication", "src/pkgs/Directory.Build.props", "src/pkgs/Directory.Build.targets", "src/pkgs/Directory.Packages.props"] },
  "authorization": { "repository": "Altinn/altinn-auth", "paths": [".editorconfig", "global.json", "src/Altinn.ruleset", "src/Directory.Build.props", "src/Directory.Build.targets", "src/Directory.Packages.props", "src/Stylecop.json", "src/apps/Altinn.Authorization", "src/apps/Directory.Build.props", "src/apps/Directory.Build.targets", "src/libs/Altinn.Authorization.Api.Contracts", "src/libs/Directory.Build.props", "src/libs/Directory.Build.targets", "src/pkgs/Altinn.Authorization.ABAC", "src/pkgs/Altinn.Authorization.PEP", "src/pkgs/Directory.Build.props", "src/pkgs/Directory.Build.targets", "src/pkgs/Directory.Packages.props", "src/pkgs/README.md"] },
  "arbeidsflate": { "repository": "Altinn/dialogporten-frontend", "paths": ["."] },
  "correspondence": { "repository": "Altinn/altinn-correspondence", "paths": ["src", "Dockerfile", "docker-compose.yml"] },
  "dialogporten": { "repository": "Altinn/dialogporten", "paths": ["Directory.Build.props", ".editorconfig", "global.json", "src/Digdir.Domain.Dialogporten.WebApi", "src/Digdir.Domain.Dialogporten.GraphQL", "src/Digdir.Domain.Dialogporten.Service", "src/Digdir.Domain.Dialogporten.Application", "src/Digdir.Domain.Dialogporten.Domain", "src/Digdir.Domain.Dialogporten.Infrastructure", "src/Digdir.Library.Entity.Abstractions", "src/Digdir.Library.Entity.EntityFrameworkCore", "src/Digdir.Library.Utils.AspNet"] },
  "dialogporten-adapter": { "repository": "Altinn/altinn-dialogporten-adapter", "paths": ["src/Altinn.DialogportenAdapter.WebApi", "src/Altinn.DialogportenAdapter.Contracts", "Directory.Build.props", "global.json", "Dockerfile", ".dockerignore"] },
  "events": { "repository": "Altinn/altinn-events", "paths": ["src/Events", "src/Events.Common", "src/DbTools", "stylecop.json", ".editorconfig", "Dockerfile"] },
  "notifications": { "repository": "Altinn/altinn-notifications", "paths": ["components/api/src", "components/shared/src", "components/api/Dockerfile", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "stylecop.json"] },
  "profile": { "repository": "Altinn/altinn-profile", "paths": ["src", "Dockerfile", ".dockerignore", "docker-compose.yml", ".editorconfig", "stylecop.json"] },
  "receipt": { "repository": "Altinn/altinn-receipt", "paths": ["."] },
  "register": { "repository": "Altinn/altinn-register", "paths": ["src/apps/Altinn.Register", "src/pkgs/Altinn.Register.Contracts", ".editorconfig", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "stylecop.json", "Dockerfile", ".dockerignore"] },
  "resource-registry": { "repository": "Altinn/altinn-resource-registry", "paths": ["src/apps/Altinn.ResourceRegistry/src", "src/apps/Altinn.ResourceRegistry/Dockerfile", "src/Directory.Build.props", "src/Directory.Build.targets", "src/Directory.Packages.props", "src/stylecop.json", "Settings.StyleCop", "global.json"] },
  "storage": { "repository": "Altinn/altinn-storage", "paths": ["src/Storage", "src/DbTools", "Directory.Build.props", ".editorconfig", ".csharpierignore", ".config/dotnet-tools.json", "infra/postgres_init.sql", "Dockerfile"] },
  "workflow-engine": { "repository": "Altinn/altinn-studio", "paths": ["src/Runtime/workflow-engine-app/src/WorkflowEngine.App", "src/Runtime/workflow-engine-app/src/Directory.Build.props", "src/Runtime/workflow-engine-app/Directory.Build.props", "src/Runtime/workflow-engine-app/Directory.Packages.props", "src/Runtime/workflow-engine-app/global.json", "src/Runtime/workflow-engine-app/.editorconfig", "src/Runtime/workflow-engine-app/AGENTS.md", "src/Runtime/workflow-engine-app/README.md", "src/Runtime/workflow-engine-app/Dockerfile", "src/Runtime/workflow-engine-app/Dockerfile.dockerignore", "src/Runtime/workflow-engine/src/Directory.Build.props", "src/Runtime/workflow-engine/src/WorkflowEngine.Core", "src/Runtime/workflow-engine/src/WorkflowEngine.Commands", "src/Runtime/workflow-engine/src/WorkflowEngine.Data", "src/Runtime/workflow-engine/src/WorkflowEngine.Models", "src/Runtime/workflow-engine/src/WorkflowEngine.Resilience", "src/Runtime/workflow-engine/src/WorkflowEngine.Telemetry", "src/Runtime/workflow-engine/Directory.Build.props", "src/Runtime/workflow-engine/Directory.Packages.props", "src/Runtime/workflow-engine/global.json", "src/Runtime/workflow-engine/.editorconfig", "src/Runtime/workflow-engine/.csharpierrc.yaml", "src/Runtime/workflow-engine/.csharpierignore", "src/Runtime/workflow-engine/.config/dotnet-tools.json", "src/Runtime/workflow-engine/AGENTS.md", "src/Runtime/workflow-engine/README.md", "src/common/dotnet/Altinn.Studio.Common"] }
}
```
