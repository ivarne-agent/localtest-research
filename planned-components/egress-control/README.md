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
| **2. Enforcement** | `externalHosts` read from the provisioned metadata; allowed/refused; 403 with the message; `--strict`/observe switch per open question 1; the Aspire surface | A declared host passes, an undeclared one is refused and shown with the line to add; an app without the field behaves as decided |
| **3. Attribution** | OTLP/HTTP receiver in the front door, spans joined to the rows, the runners turning the app's telemetry on | The feature name beside each host |
| **4. Upstream** | The field in the monorepo's model and schema; Studio's editor; the deploy generating the egress rules | The monorepo PRs; a production-like proof in a test cluster |

Container mode (task 34) is independent but makes phase 2 complete: with the app in a container on a closed network,
the proxy is the only exit and raw sockets are blocked too.

## 7. Open questions for the owner

1. **Default for an app without the field:** refuse everything undeclared (breaks every existing app that reaches a
   third party until it declares), or observe only until the field exists, with `--strict` for teams that want the
   production behaviour now? Recommendation: observe by default in phase 1 and 2, flip the default once the field is in
   the schema and Studio can edit it.
2. **The implied list:** is `altinncdn.no` acceptable as implied, or should the platform serve the org list (task 44
   question 3) so apps have no CDN egress at all?
3. **Ports and wildcards** in the declaration: ports only where not 443; one-level wildcards only?
4. **Should the declaration also carry the browser's hosts** (one field with a `browser` flag) or stay server-side only?
5. **Proxy for the components?** The stack's own components also reach the internet (six org-list fetches, two at22
   paths); the same proxy could observe them, but their egress is the platform's concern, not the app's. Out of scope
   unless wanted.
