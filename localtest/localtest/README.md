# localtest

The localtest app: the stack's front door, and the home of the localtest features that are not a platform component.
A small ASP.NET app published with Native AOT: one 16 MB binary on `runtime-deps`, a 34 MB image, no .NET runtime.
Each feature keeps its own code folder and its own API under `localtest/api/{feature}/`.

| Feature | Folder | API |
| --- | --- | --- |
| Proxy: one origin, `local.altinn.cloud:8000`, split by host name and path prefix onto the platform components and the apps the app runner has registered. Replaced the nginx ingress on 2026-10-19 with the same contract | `Proxy/` | `localtest/api/proxy/` |

| File | What |
| --- | --- |
| `Program.cs` | Composition: dedicated hosts first, then the app-frontend files, routing, the features' endpoints, the catch-all forwarder |
| `LocaltestJsonContext.cs` | Source-generated JSON for every feature (Native AOT has no reflection serializer) |
| `Proxy/PlatformRoutes.cs` | The route table: dedicated hosts, exact paths and path prefixes, and the upstream addresses |
| `Proxy/AppRoutes.cs`, `Proxy/AppRoutesApi.cs` | The app routes and the API the app runner pushes them through |
| `Proxy/Forwarder.cs` | Resolves a request, forwards it with [YARP](https://github.com/dotnet/yarp)'s `IHttpForwarder` (the forwarding core the old localtest also uses), and the transform (original `Host`, cookies without `Secure`) |
| `Dockerfile` | Native AOT publish for the build machine's architecture (`linux-musl-arm64` or `-x64`) |
| `smoke-test.sh` | Against the running stack: the proxy's routing contract and its app route API |

## Proxy

### Routes

In order:

1. **Dedicated hosts** own every path, the localtest app's own endpoints included: `pdf.local.altinn.cloud` → `pdf3`,
   `workflow-engine.local.altinn.cloud` → `workflow-engine`.
2. **`/app-frontend/`**: the built app-frontend (`FRONTEND_DIST`, mounted at `/frontend`) as static files, with
   `Access-Control-Allow-Origin: *` and `Cache-Control: no-cache`. The apps' asset base.
3. **The localtest app's own endpoints**: `GET /health`, and `localtest/api/…` (below).
4. **App routes**: `/{org}/{app}/…` → the registered upstream, path unchanged.
5. **Platform routes** on the main host, path unchanged: `/` and `/Home` (exact) and `/Home/`, `/authentication/` →
   authentication; `/register/`, `/profile/` → register; `/authorization/` → the PDP; `/accessmanagement/` → Access
   Management; `/storage/` → Storage; `/events/` → Events.
6. Anything else: `404` with a hint to start the app with `run-app.sh`.

A prefix requested without its trailing slash (`/storage`, `/ttd/app?x=1`) is redirected with `301` to the slashed
path, query kept, as nginx did. Matching is ordinal and case-sensitive, like the nginx locations.

Upstreams default to the compose services (`register:5020`, `authentication:5101`, `authorization:5070`,
`access-management:5080`, `storage:5010`, `events:5080`, `pdf3:5031`, `workflow-engine:9090`). Override one with
`Proxy__Upstreams__<name>`, for example `Proxy__Upstreams__storage=http://host.docker.internal:5010` to run Storage
from an IDE. `GET localtest/api/proxy/upstreams` lists the effective ones.

### What it does to a request

- **Streams** both ways, unbuffered: server-sent events (`events/api/localtest/stream`) and downloads arrive as the
  upstream writes them; WebSocket upgrades are passed on.
- **Headers**: YARP's default transform (all headers copied, `X-Forwarded-For`, `-Proto` and `-Host` set), plus the
  original `Host`, because the components and apps build their own links from it.
- **Cookies**: the `Secure` attribute is removed from every `Set-Cookie`, because the browser talks plain http to this
  origin while the production services set it (the nginx ingress did the same with `proxy_cookie_flags ~ nosecure`).
- **Limits**: request bodies up to 200 MB; an upstream may stay silent for 5 minutes (PDF generation, big uploads)
  before the request fails; connect timeout 5 seconds. A failed upstream is a `502` naming the route and address.
- Connections to an upstream are recycled every 30 seconds, so a recreated container's new address is picked up.

### App routes (`localtest/api/proxy/`, anonymous)

| Call | What |
| --- | --- |
| `PUT apps/{org}/{app}` with `{ "upstream": "http://host.docker.internal:5005" }` | Route `/{org}/{app}/…` to the app. Org and app are lower-case letters, digits and hyphens; the upstream is an http(s) origin without a path. Replaces an existing route |
| `DELETE apps/{org}/{app}` | Remove the route (`404` if there was none) |
| `GET apps` | Every route with its upstream and registration time |
| `GET upstreams` | The platform upstreams in effect |

`../run-app.sh` registers each app it starts; `../stop-apps.sh` removes them. Routes are saved to
`/data/app-routes.json` (`Proxy__AppRoutesFile`) on the `localtest-data` volume and read at start, so a recreated
container keeps routing the apps that still run. Nothing watches the file, and nothing reloads.

## Build notes

- `PublishAot` with `IsAotCompatible` and warnings as errors: the AOT and trim analysers run on every build. YARP 2.3.0
  publishes without AOT or trim warnings.
- All JSON goes through the source-generated `LocaltestJsonContext`; a new feature adds its types there.
- The catch-all is `MapFallback("{**path}", RequestDelegate)`. The default fallback pattern (`{*path:nonfile}`) skips
  every path that looks like a file (`…/report.pdf`), and with Native AOT the request delegate generator rewrites the
  typed-lambda overload of `MapFallback` and loses an explicit pattern, so the plain `RequestDelegate` overload is used.
- `UseRouting()` comes after the static files: the catch-all matches every path, and the static file middleware does
  nothing for a request that already has an endpoint. A feature's endpoints are mapped before the catch-all and win
  over it by route precedence.
- The AOT publish is memory-hungry; don't run it alongside a Cypress run on a small machine.

## Verified (2026-10-19)

- Side by side with the nginx ingress (port 8001 against 8000, same `Host`): identical status, content type and body
  for the start page, `/Home`, OpenID metadata, Register, Storage, Events, the PDP, app requests and the app-frontend
  bundle; identical `Set-Cookie` headers from the sign-in (no `Secure`); identical answers on the PDF and
  workflow-engine hosts.
- In nginx's place: `./smoke-test.sh` 22 ok, root smoke 23 ok, Events smoke 23 ok, authorization smoke 30 ok.
- Cypress through it, all 13 test apps running: 10 app folders (anonymous-stateless-app, expression-validation-test,
  multiple-datamodels-test, navigation-test-subform, payment-test, process-transition-test, service-task,
  signering-brukerstyrt, signing-test, stateless-app) with 73 tests, all passing, before a machine reboot cut the run
  off in subform-test; subform-test, component-library and frontend-test not yet run.
