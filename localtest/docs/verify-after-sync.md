# Verifying the stack after a sync

A checklist for an agent that has just synced part of this repository with upstream and must show that everything
still works. It is ordered from cheap to expensive: run the levels in order and **stop at the first failure** that is
not listed in [Known failures and false alarms](#6-known-failures-and-false-alarms). It assumes a machine like the
agents' sandbox: Podman (with `docker` as a compatibility command), about 8 GB of RAM, perhaps a TLS-intercepting
proxy, other agents' containers beside yours, and no context beyond this repository.

A **sync** is any of:

- a component's vendored source moved to a newer upstream commit (`localtest/components/<name>/vendor`, staged by
  [`tools/vendor-sync.py`](../tools/vendor-sync.py) `sync` and finished by the agent as [`vendor-sync.md`](vendor-sync.md)
  describes; `sync` prints which levels below apply);
- the vendored studioctl re-based ([`studioctl/UPSTREAM.md`](../studioctl/UPSTREAM.md));
- the image pins moved ([`tools/bump-tag.sh`](../tools/bump-tag.sh), [`CONVENTIONS.md`](../CONVENTIONS.md#tags));
- the Aspire package changed ([`aspire/Altinn.App.Aspire/`](../aspire/Altinn.App.Aspire/README.md)).

Every command below uses these variables; set them first:

```sh
WT=/home/agent/code/.worktrees/<task>          # the worktree with the sync
L=$WT/localtest
CLI=$L/studioctl/src/cli
EV=/home/agent/code/.handover/<task>           # evidence (logs, screenshots), outside the repository
mkdir -p "$EV"
```

What "proven" meant for earlier work, with the exact numbers, is recorded in [`../../TODO.md`](../../TODO.md) (search
for "Proven on the stack", "Pinned check" and "root 26") and in each component README's "Verified" section. Where this
page gives a number it names its source; numbers marked **from TODO records, not re-run** were not checked when this
page was written.

## 1. What changed

```sh
git -C "$WT" fetch -q origin
git -C "$WT" diff --stat origin/main...HEAD -- localtest | tail -1
git -C "$WT" diff --name-only origin/main...HEAD -- localtest | cut -d/ -f2-3 | sort | uniq -c
```

| Paths that changed                                                                   | Layer               | Levels to run                                                                                                                   |
| ------------------------------------------------------------------------------------ | ------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `components/<name>/vendor/`, `fakes/`, `Dockerfile*`, `localtest.build.*`            | A component         | 2.1 for that component; 5.1 (`--dev-<container>`); 4.2 and the component's smoke test, on its group (4.3) when it is optional   |
| `localtest/` (the front door's source)                                               | The front door      | 2.3; 5.1 (`--dev-localtest`); 4.1 (the emulation) and 4.2 with `localtest/smoke-test.sh`                                        |
| `postgres/`, `postgres-init/`, `components/*/postgres-init/`, `redis/`               | The shared images   | 5.1 (`--dev-postgres`), after `env reset` (init scripts run only on an empty volume); 4.2                                       |
| `studioctl/src/`, `studioctl/agents/`                                                | studioctl (re-base) | 2.2, 2.4 (parity), 2.5; 3; 4.1, 4.2, 4.4                                                                                        |
| `studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, only `"tag"` values | The pins            | 2.2 (golden), 2.4 (parity); 5.2; 4.1, 4.2 and the groups whose images moved (all research images share one pin, so all of them) |
| `aspire/`                                                                            | The Aspire package  | 2.4; 4.6                                                                                                                        |
| Markdown only                                                                        | Docs                | 2.5                                                                                                                             |

A sync usually touches several layers. Run the union, each level once.

## 2. Static checks, no containers

None of these start a container (except where noted), so they need no lock. Run one build at a time: the machine has
about 8 GB.

### 2.1 Components

Each .NET component builds its host from `vendor/` with the overrides that define `LOCALTEST` and compile `fakes/`
(the same variables the Dockerfile sets). Run from the component folder, not from `vendor/`, so that `vendor/global.json`
(several pin an SDK with `rollForward: disable`) does not apply:

```sh
cd "$L/components/<name>"
DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets \
  dotnet build -c Release <host project>
```

| Component            | Host project (under the component folder)                                                                                                                                     |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| access-management    | `vendor/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement`                                                                                                         |
| authentication       | `vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication`                                                                                                    |
| authorization        | `vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization`                                                                                                               |
| correspondence       | `vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj`                                                                                                       |
| dialogporten         | `vendor/src/Digdir.Domain.Dialogporten.WebApi` (with `-c Debug`, see below), `vendor/src/Digdir.Domain.Dialogporten.GraphQL`, `vendor/src/Digdir.Domain.Dialogporten.Service` |
| dialogporten-adapter | `vendor/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj`                                                                                       |
| events               | `vendor/src/Events/Altinn.Platform.Events.csproj`                                                                                                                             |
| notifications        | `vendor/components/api/src/Altinn.Notifications/Altinn.Notifications.csproj`                                                                                                  |
| profile              | `vendor/src/Altinn.Profile/Altinn.Profile.csproj`                                                                                                                             |
| receipt              | `vendor/src/backend/Altinn.Receipt/Altinn.Platform.Receipt.csproj`                                                                                                            |
| register             | `vendor/src/apps/Altinn.Register/src/Altinn.Register/Altinn.Register.csproj`                                                                                                  |
| resource-registry    | `vendor/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry`                                                                                                         |
| storage              | `vendor/src/Storage/Altinn.Platform.Storage.csproj`                                                                                                                           |

**Expected:** `0 Error(s)`. Warnings come from upstream; compare the count with the same build on `origin/main` rather
than expecting zero (on 2026-10-27: Access Management 10, Authentication 306, Authorization 2, Correspondence 24,
Dialogporten 4/1/1, Resource Registry 10, Storage 4, the rest 0). Each build took 1 to 13 s with a warm NuGet cache.

- **Dialogporten:** the WebApi's Release build runs NSwag after the build, which fails on the host
  (`MSB3073 … dotnet-nswag.dll … exited with code 255`, "Unable to retrieve project metadata"); build it with
  `-c Debug`. The image (5.1) builds it in Release. The three builds also rewrite each host's `packages.lock.json`
  (the service-discovery package changes the dependencies); restore them afterwards:
  `git -C "$WT" checkout -- localtest/components/dialogporten/vendor`.
- **Arbeidsflate** (Node, pnpm) has no host build here; its check is the image build (5.1).
- **Fidelity to upstream:** `python3 tools/vendor-sync.py check --all` (from `$L`; `VENDOR_SYNC_CLONES` names the
  blobless clones, created when missing): every `vendor/` is upstream at its pin plus well-formed `LOCALTEST` blocks,
  the groups share one commit, Arbeidsflate's patches apply, and the copies of `components/shared/fakes/` are
  identical. **Expected:** one `ok` line per component and `ok shared fakes`, exit 0; notes for Register's two CRLF
  `.sln` files (tolerated) and warnings only for `follow` versions after a host build that moved them. How a sync is
  staged and finished: [`vendor-sync.md`](vendor-sync.md).
- Some READMEs record more (upstream's own tests built with the overrides, "Running without Docker"); run them when
  that component changed.

The shared fakes and their tests, no stack needed (the third part of `components/shared/smoke-test.sh` needs the
stack, 4.2):

```sh
cd "$L/components/shared"
dotnet run --project Cookies.Smoke      # every line "ok", exit 0
dotnet test Otlp.Tests                  # Passed: 10 (2026-10-29; the front door's LocaltestOtlp.cs since task 63)
```

### 2.2 studioctl

```sh
cd "$CLI"
go build ./... && go test ./...                         # every package "ok"
make lint                                               # "0 issues." (downloads golangci-lint into bin/ once)
STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/ ./internal/cmd/app/ ./internal/appsecrets/
git -C "$WT" status --porcelain -- localtest/studioctl  # empty: the golden files are unchanged
dotnet test studioctl-server-tests/Studioctl.Tests.csproj
(cd ../Runtime/devenv && go build ./... && go test ./pkg/...)
```

**Expected** (2026-10-27): `go test` all `ok` in about 8 s; lint `0 issues.`; golden regeneration leaves no diff. A
golden diff after a pin bump or a manifest change is expected: review it (only the changed values move) and commit it.
studioctl-server's tests: `Failed: 13, Passed: 1015, Skipped: 3` — the 13 are known (6.2). devenv: every package `ok`
except `pkg/runtimes/kind`'s two monitoring tests (6.3). devenv's `test/e2e/container` (run by `go test ./...` in
devenv, about 70 s) starts containers: run it only under the lock (3.5).

**For a studioctl re-base**, also UPSTREAM.md's check that the vendored tree equals the upstream branch head (#21394's
head, or the `main` commit it merged in):

```sh
M=/home/agent/code/altinn-studio; HEAD_UP=<upstream commit>; T=$(mktemp -d)
mkdir "$T/up" "$T/ours"
git -C "$M" archive "$HEAD_UP" src/cli src/Runtime/devenv src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common \
  src/Runtime/localtest/infra agents/_skills/app-development | tar -x -C "$T/up"
git -C "$WT" archive HEAD:localtest/studioctl src agents | tar -x -C "$T/ours"
diff -r "$T/up" "$T/ours" && echo identical
```

**Expected:** `identical` (it was for `d9163a934c` on 2026-10-27). Then the baseline's diff stat against the monorepo's,
as in [`studioctl/UPSTREAM.md`](../studioctl/UPSTREAM.md#syncing-to-a-newer-upstream-commit), step 2.

### 2.3 The front door

```sh
cd "$L/localtest"
dotnet test tests            # Passed: 179 (2026-10-27)
dotnet build -c Release      # 0 Warning(s), 0 Error(s): the AOT and trim analysers run on every build
```

### 2.4 The Aspire package and parity

```sh
cd "$L/aspire/Altinn.App.Aspire"
dotnet build src -c Release
dotnet test tests -c Release     # Passed: 178 (2026-10-27); the parity with studioctl's golden files is part of it
dotnet csharpier check .         # "Checked 46 files", exit 0
cd ../samples/app/AppHost && dotnet build apphost.cs && dotnet build apphost-minimal.cs   # 0 Error(s); ASPIRE010 is a warning
```

This is what [`manifest-parity.yml`](../../.github/workflows/manifest-parity.yml) runs on a pull request (`go build`
and `go test` in `$CLI`, `dotnet test tests -c Release` here). The parity tests read studioctl's golden files, so run
them after 2.2's regeneration.

### 2.5 Scripts, workflows, spelling, formatting

```sh
cd "$L"
git ls-files '*.sh' | grep -v '/vendor/\|^studioctl/' | xargs shellcheck -x -S warning   # no output, exit 0
actionlint "$WT"/.github/workflows/*.yml                                                 # no output
bash tools/bump-tag.sh; echo "exit $?"        # "… usage: bump-tag.sh sha-<commit>", exit 1: changes nothing
cd "$WT"
git diff --name-only origin/main...HEAD | grep -v '/vendor/\|^localtest/studioctl/src/' | xargs -r typos
git diff --name-only origin/main...HEAD -- '*.md' | grep -v '/vendor/' | xargs -r npx --yes prettier@3 --check
```

**Expected:** shellcheck and actionlint silent; typos silent on the files the sync changed (the whole tree has about
130 hits in dated files and upstream text, so check only changed files). Prettier: several long documents
(`localtest/README.md`, `CONVENTIONS.md`, `TODO.md`) already differ from Prettier's output; a file the sync changed
must not differ more than it did on `origin/main`. Behind the proxy, npm needs
`NODE_EXTRA_CA_CERTS=/etc/ssl/certs/ca-certificates.crt`.

## 3. Machine setup

### 3.1 Before anything starts

```sh
free -g; df -h /                           # about 4 GB available is enough for the default stack and one group
podman ps -a --format '{{.Names}}\t{{.Status}}'   # note what is not yours; never touch it
pgrep -af studioctl-server                 # other homes' servers; leave the ones you did not start
ls -la /home/agent/code/.locks/            # see 3.5
```

Containers named `storage_*` (another agent's Storage development setup) and anything not named `localtest-v2-*`
belong to someone else. Never run `podman rm -a`, `podman system prune`, `podman volume prune` or `podman network
prune`. Remove only what you created, by name.

### 3.2 An isolated studioctl install

Build and install the studioctl of the worktree into its own home, so that the normal install
(`~/.local/bin/studioctl`, home `~/.config/altinn-studio`) stays as it was. The home must be a **short** path:
studioctl-server listens on a Unix socket under it, and a path over 107 characters fails `env up` with "invalid
argument". Point a short symlink at a scratch folder. `self install` (like `make dev-install`) **stops every
localtest-v2 environment** on the machine, so take the lock (3.5) first.

```sh
cd "$CLI"
go run ./cmd/dev dist      # build/studioctl and build/studioctl-resources-<os>-<arch>.tar.gz, about 10 s
mkdir -p <scratch>/home && ln -sfn <scratch>/home /home/agent/<task>-home
export STUDIOCTL_HOME=/home/agent/<task>-home NO_COLOR=1
STUDIOCTL_RESOURCES_ARCHIVE=$PWD/build/studioctl-resources-$(go env GOOS)-$(go env GOARCH).tar.gz \
  build/studioctl self install --path "$STUDIOCTL_HOME/bin"
S=$STUDIOCTL_HOME/bin/studioctl
"$S" --version             # v0.1.0-preview.0 (the dev build); the normal install reports its release
```

**Expected:** "Installation completed"; `$STUDIOCTL_HOME` holds `bin/studioctl`, `bin/studioctl-server` and `agent/`
(the bundled skills). Keep `STUDIOCTL_HOME` exported in every shell that runs `$S`, and use `$S`, never the `studioctl`
on `PATH`. For a studioctl re-base the install itself is a check: the bundled app-development skill must be there
(`find "$STUDIOCTL_HOME/agent" -name SKILL.md`).

### 3.3 Host names

```sh
"$S" env hosts status -r localtest-v2      # every name "managed 127.0.0.1"
sudo "$S" env hosts add -r localtest-v2    # only when a name is missing
```

The names: `local.altinn.cloud` and its `pdf.`, `workflow-engine.`, `app-frontend.` and `af.` subdomains. A custom
origin (`--host`) needs a line of your own in `/etc/hosts` ([README](../README.md#run-it), "Another origin").

### 3.4 Behind a TLS-intercepting proxy

```sh
export STUDIO_CA_BUNDLE=/etc/ssl/certs/ca-certificates.crt   # a PEM file with the proxy's CA
```

before every `env up`. Without it pdf3 cannot load the app frontend from `altinncdn.no` and PDF generation fails (6.4).
The .NET and Node components trust what the container runtime gives them; this sandbox's Podman already provides it.
Image builds behind the proxy are covered in the Dockerfiles themselves (for example `Dockerfile.bff` exports
`NODE_EXTRA_CA_CERTS` for its install step).

### 3.5 One stack at a time, and the lock

The machine runs one stack: memory is the limit (2.7 GB used with the 13 default containers idle; 4.1 GiB available
after `--with everything` started, 2.0 GiB with an app and Chromium on top: TODO.md tasks 38 and 53), and both runtimes
(studioctl and an Aspire AppHost) need port 8000. Agents share the machine through a lock file. Wait for it, then take
it atomically:

```sh
until (set -o noclobber; echo "<task> ($WT), since $(date -Is)" > /home/agent/code/.locks/stack-busy) 2>/dev/null; do
  sleep 30
done
```

Hold it from the first `self install` or `env up` until your containers are down (7.2). Also run image builds (5.1)
and Cypress (4.5) under it, never beside another build or a Cypress run. Watch memory between levels:
`free -g` and `podman stats --no-stream --format '{{.Name}} {{.MemUsage}}'`.

## 4. Stack levels

Run the smoke tests from `$L`, with the stack's origin in the default (`http://local.altinn.cloud:8000`; for another
origin set `LOCALTEST_ORIGIN`). Save each run's output: `./smoke-test.sh 2>&1 | tee "$EV/<level>-root.txt"`. Count with
`grep -c '^ok' <file>` and `grep '^FAIL' <file>`. Each script exits non-zero when a check fails.

Times and memory below are this machine's (4 cores, 8 GB, arm64, rootful Podman 5.7), with the images already pulled
unless stated.

### 4.1 Minimal mode and its telemetry files

```sh
"$S" env up -r localtest-v2 --minimal 2>&1 | tee "$EV/minimal-up.txt"
"$S" env status | tee "$EV/minimal-status.txt"
./smoke-test.sh 2>&1 | tee "$EV/minimal-root.txt"
sleep 15   # the collector writes within about 10 s
ls "$STUDIOCTL_HOME/data/infra/localtest-v2/telemetry/"*/ | tee "$EV/minimal-telemetry.txt"
"$S" env down -r localtest-v2
ls "$STUDIOCTL_HOME/data/infra/localtest-v2/telemetry" 2>&1   # No such file or directory
```

**Expected:**

- `env up`: five containers (`localtest`, `postgres`, `storage`, `workflow-engine`, `otel-files`), about 6 s (task 55);
  `env status` says "running (minimal mode)" and lists the emulated services (register, profile, authentication,
  authorization, access-management, events, resource-registry, notifications, pdf3).
- Root smoke test, about 1 s: "minimal mode: …" first, then 23 `ok`, no `FAIL`, exit 0, and four `skip` lines: the
  three checks that need real authorization decisions and the OTLP span check (no `monitoring` group). Without
  Podman the Storage log check is skipped too.
- Telemetry: a folder per `service.name` (`localtest`, `storage`, `workflow-engine`) with `traces.jsonl`, `logs.jsonl`
  and `metrics.jsonl`, written within about 10 s of a request. How to read them: the bundled skill
  ([`SKILL.md`](../studioctl/agents/_skills/app-development/SKILL.md#telemetry-files-in-minimal-mode)).
- `env down` deletes the `telemetry/` folder.
- Memory: the five containers about 0.64 GB by `podman stats` (Postgres 249 MB, Storage 148, the engine 128,
  `otel-files` 81, the front door 33).

Also useful in minimal mode: `localtest/smoke-test.sh` (one check per emulated service; 92 `ok` and the one known
`FAIL`, 6.1, on 2026-10-27) and
`components/register/smoke-test.sh http://local.altinn.cloud:8000` (the emulated Register).

### 4.2 The default stack and the root smoke test

```sh
"$S" env up -r localtest-v2 2>&1 | tee "$EV/default-up.txt"
"$S" env status | tee "$EV/default-status.txt"
./smoke-test.sh 2>&1 | tee "$EV/default-root.txt"
localtest/smoke-test.sh 2>&1 | tee "$EV/default-frontdoor.txt"
```

Then, for a changed component, its own smoke test (4.3 lists them with the group they need). Run the suites with no
app registered (6.6).

**Expected:**

- `env up`: 13 containers, `env status` lists them `running`; 17 s with the images present (2026-10-27), 43 s with
  pulls (TODO.md task 27).
- Root smoke test, about 3 s: 26 `ok`, no `FAIL`, exit 0, one `skip` line (the OTLP span check; with the `monitoring`
  group it runs and makes 27).
- `localtest/smoke-test.sh`, about 6 s: 83 `ok` and one `FAIL`, `app-frontend bundle with CORS (needs FRONTEND_DIST)`
  (6.1), exit 1.
- Memory: the 13 containers 1.7 GB by `podman stats`. Release the .NET build servers that section 2 left behind
  before the stack levels, when no other agent is building: `dotnet build-server shutdown` (on 2026-10-27 the
  compiler server and three MSBuild nodes held 1.5 GB, and `free -m` showed 1.5 GB available with the default stack).

After `env reset -r localtest-v2 --yes` (needed when an init script changed), the first `env up` runs every init script
and migration: 21.6 s, then root 26 of 26 (task 27a).

### 4.3 The optional groups and the component smoke tests

**From TODO records, not re-run** for this page. Start a group on top of the default stack (`env up` converges a running
stack to the new selection), run its suites, and go back with `--without <group>` or `env down` before the next group
if memory is short. `--with everything --with monitoring` was 27 containers with 4.1 GiB available after start
(task 53).

| Group (`env up -r localtest-v2 …`) | Suites, from `$L`                                                                                                                                                                                                                                                      | Last recorded result                                                                                                                                                         |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| none (default)                     | `components/{authentication,authorization,events,notifications,profile,resource-registry}/smoke-test.sh`, `components/register/smoke-test.sh http://local.altinn.cloud:8000`, `components/dialogporten-adapter/smoke-test.sh --off`, `components/shared/smoke-test.sh` | authentication 32 (34 with `dialogporten`), authorization 31, events 27, notifications 64, profile 52, resource registry 43, register 14, adapter `--off` 7, shared "all ok" |
| `--with dialogporten`              | `components/dialogporten-adapter/smoke-test.sh` **before** `components/dialogporten/smoke-test.sh` (6.7)                                                                                                                                                               | adapter 39, dialogporten 60                                                                                                                                                  |
| `--with correspondence`            | `components/correspondence/smoke-test.sh` (`CORRESPONDENCE_RESTART=1` adds the restart)                                                                                                                                                                                | 78 (task 27a)                                                                                                                                                                |
| `--with receipt`                   | `components/receipt/smoke-test.sh`                                                                                                                                                                                                                                     | 27                                                                                                                                                                           |
| `--with arbeidsflate`              | `components/arbeidsflate/smoke-test.sh`                                                                                                                                                                                                                                | 59 (task 21b)                                                                                                                                                                |
| `--with monitoring`                | every suite above adds one OTLP span check; root 27                                                                                                                                                                                                                    | task 53, 2026-10-26 (counts in TODO.md section 53)                                                                                                                           |

Counts are from 2026-10-21 to 2026-10-26 (README "Tests" and TODO.md tasks 21, 27 and 53); they grow when a suite
gains a check, so a different count is a reason to read the suite's history, a `FAIL` is a failure. Run Events before
the adapter and Dialogporten suites (6.7). The workflow engine's own test (`smoke/workflow-engine.sh`, the image studioctl
runs; the engine is not vendored) starts its own Postgres container: run it under the lock.

### 4.4 An app with `studioctl run`, through to a PDF

The sample app `localtest/aspire/samples/app/App` (`ttd/testapp`, app-lib 8.12.15, net8.0) does not create a PDF as
committed. Copy it to a scratch folder and turn PDF creation on, as earlier agents' `testapp-pdf` did:

```sh
APP=<scratch>/testapp-pdf; mkdir -p "$APP"
cp -r "$L/aspire/samples/app/App" "$L/aspire/samples/app/global.json" "$APP/"
rm -rf "$APP/App/bin" "$APP/App/obj"      # left by 2.4's sample build, if it ran
python3 -I - "$APP/App/config/applicationmetadata.json" <<'EOF'
import json, sys
p = sys.argv[1]; m = json.load(open(p))
for d in m["dataTypes"]:
    if d["id"] == "model":
        d["enablePdfCreation"] = True
m["dataTypes"].append({"id": "ref-data-as-pdf", "allowedContentTypes": ["application/pdf"], "maxCount": 0, "minCount": 0})
json.dump(m, open(p, "w"), indent=2)
EOF
cd "$APP" && "$S" run -d 2>&1 | tee "$EV/app-run.txt"
curl -s http://local.altinn.cloud:8000/localtest/api/apps | tee "$EV/app-registered.json"
```

Then one instance as the app's test user 20001 (party 520001, from its `testData.json`), moved to the end, and the PDF
downloaded:

```sh
B=http://local.altinn.cloud:8000; A=$B/ttd/testapp
TOKEN=$(curl -sf "$B/Home/GetTestUserToken?userId=20001&authenticationLevel=2" | tr -d '"')
ID=$(curl -sf -X POST "$A/instances?instanceOwnerPartyId=520001" -H "Authorization: Bearer $TOKEN" \
  | python3 -I -c 'import json,sys; print(json.load(sys.stdin)["id"])')
curl -s -o /dev/null -w 'process/next %{http_code} in %{time_total} s\n' -X PUT "$A/instances/$ID/process/next" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{}'
PDF=$(curl -sf "$A/instances/$ID" -H "Authorization: Bearer $TOKEN" \
  | python3 -I -c 'import json,sys; print(next(e["id"] for e in json.load(sys.stdin)["data"] if e["dataType"] == "ref-data-as-pdf"))')
curl -sf "$A/instances/$ID/data/$PDF" -H "Authorization: Bearer $TOKEN" -o "$EV/receipt.pdf" && file "$EV/receipt.pdf"
cd "$APP" && "$S" stop
```

**Expected** (2026-10-27, default stack, `STUDIO_CA_BUNDLE` set): `run -d` reports "App ready" in about 5 s;
`GET localtest/api/apps` lists `ttd/testapp` with provisioning `"state":"succeeded"`, `"hasTestData":true`;
`process/next 200` in 1.5 s; `file` says "PDF document, version 1.4, 1 page(s)". A 500 after 30 s is pdf3 without the
CA bundle (6.4). While the app is registered its `testData.json` replaces the Register's seed, so stop it before any
smoke suite (6.6): the root suite run with the app registered gave 15 `ok` and 11 `FAIL`, and 26 `ok` after `stop` and
the PDP refresh.

### 4.5 The platform regression (Cypress)

**From TODO records, not re-run.** [`run-platform-regression.sh`](../run-platform-regression.sh): the six app-frontend
specs that once failed because of the platform, 44 tests in about six minutes, 44 of 44 on 2026-10-21 (README,
"Tests"). Needs an altinn-studio checkout with the frontend built (`MONOREPO`, default `altinn-studio` beside this
repository's checkout; `src/App/frontend/dist`) and the three apps (`frontend-test`, `signering-brukerstyrt`,
`process-transition-test`). Under localtest-v2 start them with `studioctl run --dev-frontend` and serve the built
frontend with `Access-Control-Allow-Origin`, then `./run-platform-regression.sh --no-apps`; with the released CDN
bundle every spec fails ([README](../README.md#run-it), "Cypress"). Never next to an image build. Output in
`$L/.run/cypress/platform-regression.log`.

### 4.6 The Aspire samples

**From TODO records, not re-run.** `env down` the studioctl stack first (both need port 8000), then in
`$L/aspire/samples/app/AppHost`:

```sh
ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
ASPNETCORE_URLS=http://localhost:18888 ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:18889 \
ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:18887 dotnet run apphost.cs     # or apphost-minimal.cs
```

Recorded: the default stack plus `dialogporten`, 18 containers, platform healthy 22 s after `dotnet run` (4 s when the
containers are reused, 32 s on empty volumes), the app provisioned 11 to 40 s after start, about 1.9 GB for the
containers; the minimal AppHost's four containers healthy in 8 s. Run the smoke suites against it as the
[README](../README.md#tests) says ("Against an Aspire-hosted stack": deprovision the app and refresh the PDP first):
root 26, authentication 34, dialogporten 60, authorization 31, `localtest/smoke-test.sh` 80 of 81 (task 27b). Stop with
SIGINT to the process group; the persistent containers stay, so remove them and the `altinn-localtest-*` volumes with
the dashboard's `reset` command (7.2). Details: the package README's "Run under Podman" and "Minimal mode".

## 5. Images

### 5.1 Locally built images, before the images workflow

The images workflow builds only on a push to `main`, so a branch's component change is tested with an image built
here: `--dev-<container>[=<path>]` builds a Dockerfile (or a folder holding one, default the current folder) and runs
it as `localtest-v2-dev-<container>` instead of the pin.

```sh
cd "$L/components/storage" && "$S" env up -r localtest-v2 --dev-storage                 # 46 s (task 27a)
cd "$L/components/dialogporten" && "$S" env up -r localtest-v2 --with dialogporten --dev-dialogporten-webapi=Dockerfile.webapi
cd "$L/localtest" && "$S" env up -r localtest-v2 --dev-localtest
cd "$L" && "$S" env up -r localtest-v2 --dev-postgres=postgres/Dockerfile
```

Build one image at a time, under the lock. The Arbeidsflate BFF image is the heaviest (1.47 GB, whole-workspace
install). A `--dev-` image can also be built on its own first: `podman build -t localtest-storage:dev .` in the
component folder. Then
run the levels of section 4 that the table in section 1 names, with the same `--dev-` flags on every `env up`.

### 5.2 The pinned check, after `bump-tag.sh`

Once the sync is merged, [`images.yml`](../../.github/workflows/images.yml) builds every research image as
`sha-<first 7 of the commit>`:

```sh
gh run list -R ivarne-agent/localtest-research -w images -L 3
gh run watch <run id> -R ivarne-agent/localtest-research       # must end "completed success"
"$L/tools/bump-tag.sh" sha-<commit>                              # "Pinned 20 images to sha-…"
cd "$CLI" && STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/ && git -C "$WT" diff --stat
```

**Expected:** the golden diff changes only the image tags; then 2.2 and 2.4 pass, and levels 4.1 and 4.2 (and the
groups of 4.3 whose images changed) pass on the pinned images **without** any `--dev-` flag, after a fresh
`go run ./cmd/dev dist` and `self install` (the manifest is embedded in studioctl). Earlier records of this step:
TODO.md, "Pinned check" in sections 55 and 56.

## 6. Known failures and false alarms

Do not chase these; do report them.

1. **`localtest/smoke-test.sh`: `/app-frontend/`** fails on every stack: the check needs the built frontend mounted
   (`FRONTEND_DIST`), which neither runtime does. 83 of 84 is a pass.
2. **studioctl-server's 13 `V8Tov9UpgradeTargetFrameworkTests`** fail (`app upgrade v9` moves apps to .NET 11, which this
   machine lacks). They fail the same way on an unmodified altinn-studio checkout of the pinned commit
   (`/home/agent/code/.handover/sync-35c9f5d/studioctl-server-failed.txt`).
3. **devenv's `pkg/runtimes/kind`**: `TestGraph_IncludeMonitoring` and `TestMonitoringOverlaysRender` fail with
   `marker ".altinn-studio-root" not found`: they need the monorepo's root, which the vendored copy does not have.
4. **pdf3 behind a proxy without `STUDIO_CA_BUNDLE`**: `process/next` answers 500 "Pdf generation failed" after 30 s,
   pdf3's log (`"$S" env logs -c pdf3`) has `net::ERR_CERT_AUTHORITY_INVALID` and the `#readyForPrint` timeout. Set
   the variable (3.4) and `env up` again; only pdf3 is recreated.
5. **The Arbeidsflate BFF's readiness after a slow front door**: with `--dev-localtest` (the front door built while
   the rest starts), `env up --with arbeidsflate` failed once with `localtest-v2-arbeidsflate-bff not ready: not
healthy after 3m0s: http://arbeidsflate-bff:80/api/readiness`; the next `env up` came up healthy, and on the pinned
   images it did not recur (TODO.md section 56). `env up` leaves the containers for inspection: read
   `"$S" env logs -c arbeidsflate-bff`, then run `env up` again.
6. **User 1337 unknown, or the seed's parties missing, while an app is registered**: a registered app's
   `testData.json` replaces the Register's seed (when every registered app posted test data, only theirs is loaded),
   so the suites' users (1337, 1003, …) do not exist. `"$S" stop` the app (studioctl-server registers a running app
   again, so a `DELETE` by hand does not stick), then
   `curl -X POST http://local.altinn.cloud:8000/authorization/api/localtest/refresh` for the PDP's one-minute party
   cache. In minimal mode with an app registered, `localtest/smoke-test.sh` fails six such checks (task 55).
7. **Order of the Dialogporten-related suites**: the adapter's suite straight after Dialogporten's answered 404 once
   (Dialogporten's last step deletes every dialog); Events straight after the adapter fails "stored once". Order:
   Events, the adapter, Dialogporten. Events also failed "third event registered" once on a freshly started stack and
   passed on the rerun (task 53).
8. **`env up` on a healthy, unchanged stack** says "already running" and does nothing, so it does not pick up a new
   studioctl's manifest `files`; `env down` first when you installed a new build.
9. **`self install` stops every localtest-v2 environment**, also another agent's: only under the lock.
10. **An orphaned `studioctl-server`** from another home (`pgrep -af studioctl-server`) is someone else's; it does not
    block your stack.
11. **pdf3 with the monitoring group**: its first trace export fails (`name resolver error`) because it starts before
    the collector; pdf3 sends nine metric series and no logs (task 53). Not a failure.
12. **The register suite's default URL** is a standalone Register (`http://127.0.0.1:5020`); against the stack pass
    `http://local.altinn.cloud:8000`.

## 7. Evidence and cleanup

### 7.1 Evidence

Save to `$EV` (`/home/agent/code/.handover/<task>/`), never into the repository: each level's `env up`, `env status`
and suite output (as in section 4), the static checks' summaries, `podman stats --no-stream` and `free -m` where memory
matters, and screenshots (`playwright-cli --browser chromium`) for anything checked in a browser. Name files by level
(`minimal-root.txt`, `default-root.txt`).

### 7.2 Teardown

```sh
"$S" stop --all                                        # apps started with run -d ("No matching apps running.")
"$S" env down -r localtest-v2                          # "Environment stopped"; minimal mode's telemetry/ too
podman ps -a --format '{{.Names}}' | grep '^localtest-v2-'   # nothing
"$S" server down                                       # "studioctl-server shutdown requested."
pgrep -af studioctl-server                             # none from your home (others' are not yours to stop)
```

- **Volumes:** `env down` keeps the `localtest-v2-*` volumes (the default stack's data). Delete them with
  `"$S" env reset -r localtest-v2 --yes` only when you created them or the sync needs empty volumes; they are shared by
  every studioctl home on the machine. An Aspire run: the dashboard's `reset` command removes its containers and the
  `altinn-localtest-*` volumes.
- **`--dev-` images:** `podman rmi localhost/localtest-v2-dev-<container>` for the ones you built (`podman images`
  lists others' too: remove only yours).
- **The isolated home:** `rm /home/agent/<task>-home` (the symlink) and its scratch folder; the scratch app folder.
- **The lock**, last: `rm /home/agent/code/.locks/stack-busy`, only when you created it.
- Check the normal install is untouched: `ls -la ~/.local/bin/studioctl ~/.config/altinn-studio` shows the same
  times as before, and nothing of yours runs (`podman ps -a`, `pgrep -af studioctl`).

### 7.3 What to report

- The sync's commits and what changed (section 1's output), and the levels that change required.
- Per level: the commands, the counts (`ok`, `FAIL`, `skip`), the time, and the path of the evidence.
- Every failure: listed in section 6 (name the item) or new (the output and what you found).
- What was not run, and why (memory, time, a level this page marks "from TODO records").
- Confirmation of the cleanup: no containers, no server, the lock released, the normal install untouched.
