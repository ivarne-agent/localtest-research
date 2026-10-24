# Egress control: apps declare the domains they call, localtest enforces and records them

Plan, started 2026-10-23 (owner's goal: "block any domain not declared in applicationmetadata"; "collect the inventory
of network access in localtest at runtime, the key is whatever the app's own code calls"). Nothing here is built.
Related: [`../../localtest/reviews/2026-10-23-egress-domains.md`](../../localtest/reviews/2026-10-23-egress-domains.md)
(task 44: the static inventory of what an app, its browser and the stack call, and where production enforcement could
sit), `TODO.md` tasks 34 (container-mode apps), 42 (frontend links from settings), 44.

## 1. Goal

One declaration per app of the external hosts its own code reaches. Localtest enforces it while the app runs
(everything else is refused, visibly) and shows the inventory of what was reached, allowed or refused. Production
egress rules and the browser's Content-Security-Policy are generated from the same declaration later, so an app that
works on localtest works in production and nothing undeclared leaves the cluster.

## 1b. Owner decisions (2026-10-23)

- **Missing declaration = observe; present = enforce.** No `--strict` switch: the field itself is the switch. An app
  without `externalHosts` gets the inventory and no refusals; an app with it (even empty) is refused everything
  undeclared.
- **The stack's own components call nothing external except `altinncdn.no`**, so they are blocked outright by a
  firewall on the stack network (section 3c); the two at22 paths found by task 44 are bugs to remove. **Events is
  different**: its webhook deliveries go wherever subscribers point, so it stays open and observed (section 3c); the
  workflow engine's webhook command likewise.

## 2. The declaration

A field in `applicationmetadata.json`:

```json
"externalHosts": ["api.fiks.ks.no", "io.fiks.ks.no:5671", "*.blob.core.windows.net"]
```

- An entry is a host, optionally with a port; `*.` allows one level of subdomain. Scheme-less: egress is about
  endpoints, not URLs.
- **Implied, never listed:** the platform origin (every platform API, the OIDC metadata, the exchange), the app's own
  host, pdf3 and the workflow engine in the cluster, telemetry ingestion, and `altinncdn.no` (the org list fixed in the
  backend's code). The implied list is owned by the platform and documented in one place; if the platform stops needing
  one of them (the org list served locally, open question 3 of task 44), the list shrinks without touching apps.
- **Absent field** = the app declares nothing of its own. What that means for enforcement is open question 1.
- The model and the JSON schema live in the monorepo (`Altinn.App.Core` `ApplicationMetadata`, the schema Studio
  validates against). Until that upstream change lands, localtest reads the raw file it receives at provisioning and
  tolerates the field; the apps' libraries ignore unknown properties today (to confirm in phase 1).

## 3. Enforcement on localtest

The **front door is a forward proxy for the app**. The runners (`studioctl run`, `AddAltinnApp`) set `HTTPS_PROXY`
and `HTTP_PROXY` on the app process to the front door's proxy port, with `NO_PROXY` for the public host, the app's own
host and `localhost`/`host.docker.internal`. .NET's `HttpClient` honours the variables with no app change, so every
third-party request arrives at the front door as `CONNECT host:port` (https) or a plain proxied request (http).

- Allowed = implied hosts + the app's declared list. Everything else: **403** with a problem-details body naming the
  host, the app and the field (`externalHosts`), which the app's own error then carries, so the developer sees which
  domain to declare.
- The proxy only tunnels: no TLS interception, so it sees the host and port, never the path or the body (https) — enough
  for a host allow-list and for the inventory.
- Per registered app the front door keeps the **inventory** in memory: host, port, scheme, allowed or refused, count,
  first and last seen, the last example request line for plain http; classified as platform, implied, declared, or
  undeclared.

What this does not catch, and how the gap closes:

| Gap | Why | Closing it |
| --- | --- | --- |
| Raw sockets (the Fiks AMQP connection on 5671, SMTP, database drivers) | Not HTTP, ignore proxy variables | Container mode (task 34) with a closed network whose only exit is the front door; or per-process firewall rules on the host (root, later) |
| An `HttpClient` built with its own handler and `UseProxy = false` | Opts out of the system proxy | Observed by the telemetry source (below), flagged "bypassed the proxy" |
| DNS | Not through the proxy | Irrelevant with a proxy: the app resolves nothing it tunnels |

### 3a. How the proxy knows which app is calling

A proxy sees only a TCP source. A process app on the host arrives from the host gateway with an ephemeral port, so the
app is identified by **proxy credentials**: the runner sets `HTTPS_PROXY=http://<org>.<app>:<token>@<front door>:<port>`
and .NET's environment proxy sends them as `Proxy-Authorization` on every CONNECT and proxied request (to verify in
phase 1: `HttpEnvironmentProxy` parses user info from the URL). The token is the registration's secret the front door
already knows per app (or a fresh one handed to the runner in the registration answer), so a request without valid
credentials is refused and never attributed to the wrong app. A container-mode app is also known by its address on the
stack network. Attribution therefore needs no port per app and no process inspection.

### 3b. DNS as a second control point

Every outbound connection to a name starts with a lookup, including the raw-socket cases the proxy misses. Where the
app's resolver can be ours, the lookup is both the inventory and the block:

- **Container-mode app (task 34):** the container's DNS is the front door's resolver (`--dns` on the container, or the
  stack network's own resolver pointed at it). The front door answers platform and stack names itself, forwards declared
  hosts upstream, records every query per app (the source address is the app), and answers **NXDOMAIN** for an
    undeclared name when the declaration is present. An IP literal bypasses DNS, so the proxy and the firewall stay; but
  NXDOMAIN closes the AMQP, SMTP and driver cases the proxy cannot see. The network exists since task 34: container-mode
  apps run on `localtest-v2_apps`, apart from the stack's network, with the front door attached to both
  ([`CONVENTIONS.md`](../../localtest/CONVENTIONS.md#the-runtimes)).
- **Process app on the host:** the app uses the host's resolver; there is no per-process hook without root (a network
  namespace per app, or `LD_PRELOAD`, both out of scope). Observation only, host-wide: a packet capture on port 53
  attributes by time, not by process. So for process mode the proxy is the control point and DNS is a cross-check.
- Both feed the same inventory rows (host, how seen: proxy, DNS, firewall).

### 3c. The stack's components: firewall, not proxy

The components reach the public origin through the host gateway and each other by service name; nothing else is
legitimate except `altinncdn.no` (six org-list fetches, task 44 section 3.5). So the stack network gets a firewall:
forward traffic from the stack bridge is allowed to the host (the gateway) and to `altinncdn.no`'s addresses (resolved
at `env up`, refreshed when a lookup answers), and **dropped** otherwise, with a counter per destination for the
inventory. On Podman that is an `nftables` chain the runner installs with root (`studioctl env up` already needs sudo
for the hosts file; the rule set is removed on `env down`) or a Podman network with no external route plus an explicit
exception; which of the two is cheaper and survives Docker and Aspire is a phase-1 question. Until the org list is
served locally (task 44 open question 3), `altinncdn.no` stays the one hole; after that the stack network is fully
internal. The two self-identified-user paths that reach at22 are removed first (their own small task).

**Events is the exception (owner, 2026-10-23).** Its webhook delivery goes to whatever endpoint a subscriber
registered: data-driven egress that no fixed list can hold, in production as on localtest. So the `events` container
is not behind the block: it keeps its route out, and its deliveries are observed instead of refused, through the same
inventory (the delivery log it already keeps, plus the firewall's counters for its destinations). The workflow engine's
`webhook` command is the same class (any URI from the request) and gets the same treatment. Everything else in the
stack stays fully blocked.

## 4. The inventory surface

- `GET localtest/api/apps/{org}/{app}/egress`: the table above as JSON; `DELETE` resets it.
- The Home page: a section per registered app, undeclared hosts first, with the line to add to `externalHosts`.
- `studioctl app egress [--json]` and the Aspire dashboard (a resource property or a command).
- Optional attribution: when the app exports OpenTelemetry (the runners can set `AppSettings__UseOpenTelemetry=true`
  and the OTLP endpoint; today they set only the endpoint), the front door accepts OTLP/HTTP on its public port and
  joins the client spans' `server.address` to the inventory rows: which feature made the call. Telemetry never blocks.

## 5. Beyond localtest (later, out of the first task)

- **Production egress** generated from the declaration: the deploy pipeline turns `externalHosts` plus the implied list
  into the cluster's mechanism (Cilium `toFQDNs` if the DNS proxy is available, else Azure Firewall FQDN rules; task 44
  section 4). The deploy refuses an app whose declaration is empty and whose code reaches outside? No: the declaration
  is the contract; the cluster enforces it, and the app fails there the way it fails on localtest.
- **CSP**: the frontend's third-party hosts are a different list (the browser's), generated into the index page's
  `Content-Security-Policy` from the implied CDN list plus the hosts an app names in its layouts; task 44 section 3.2.
- **Studio**: the field edited in the app settings UI, validated against the schema.

## 6. Phases

| Phase | Scope | Proof |
| --- | --- | --- |
| **1. Inventory** | The forward proxy in the front door (CONNECT tunnelling and plain http), the runners' proxy variables, the per-app inventory with classification, the API, the Home section, `studioctl app egress`; refuse nothing yet (observe mode) | The sample app with a scratch feature that calls two third-party hosts: both in the inventory as undeclared; the platform calls not in it; the root suite unchanged; task 44 phase 2 cross-checks against its packet capture |
| **2. Enforcement** | `externalHosts` read from the provisioned metadata; allowed/refused; 403 with the message; missing field = observe, present = enforce; per-app proxy credentials; the Aspire surface | A declared host passes, an undeclared one is refused and shown with the line to add; an app without the field behaves as decided |
| **3. Attribution** | OTLP/HTTP receiver in the front door, spans joined to the rows, the runners turning the app's telemetry on | The feature name beside each host |
| **3b. Stack firewall** | The stack network's forward rules (host and `altinncdn.no` only), counters into the inventory, removed on `env down`; the at22 paths removed beforehand | No component log shows a refused connection during every suite; a deliberate `curl` from a component to the internet is dropped |
| **3c. DNS for container-mode apps** | The front door as the container's resolver, queries recorded per app, NXDOMAIN for undeclared names when the field is present; depends on task 34 | A raw-socket call to an undeclared name fails at the lookup and shows in the inventory |
| **4. Upstream** | The field in the monorepo's model and schema; Studio's editor; the deploy generating the egress rules | The monorepo PRs; a production-like proof in a test cluster |

Container mode (task 34) is independent but makes phase 2 complete: with the app in a container on a closed network,
the proxy is the only exit and raw sockets are blocked too.

## 7. Open questions for the owner

1. ~~Default for an app without the field~~ Decided: missing = observe, present = enforce.
2. **The implied list:** is `altinncdn.no` acceptable as implied, or should the platform serve the org list (task 44
   question 3) so apps have no CDN egress at all?
3. **Ports and wildcards** in the declaration: ports only where not 443; one-level wildcards only?
4. **Should the declaration also carry the browser's hosts** (one field with a `browser` flag) or stay server-side only?
5. ~~Proxy for the components?~~ Decided: a firewall on the stack network, `altinncdn.no` the only hole (section 3c).
6. **Proxy credentials or a port per app** for attribution (section 3a): credentials are the recommendation; confirm
   .NET sends them from the environment URL in phase 1.
