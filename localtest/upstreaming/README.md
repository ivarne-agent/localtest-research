# Upstreaming the localtest modifications

One file per vendored service listing every change to push upstream so that localtest's modifications of that service
become unnecessary: every `#if !LOCALTEST` / `#else` / `#endif` block and pure `#if LOCALTEST` addition, every fake in
`components/<name>/fakes/`, every Arbeidsflate patch, every build override and every `localtest:` deviation in a
Dockerfile. The end state is the one from the Arkitekturforum deck: each team publishes an image that runs in localtest
unchanged, configured by the manifest alone, and localtest stops vendoring it.

## Two phases

Take each service in two passes, all easy fixes first.

- **Phase 1, easy fixes.** Small, self-contained pull requests a team accepts without a design discussion: OTLP when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set, unused Application Insights packages dropped, a hard-coded URL or scheme made a
  setting, http metadata or URLs allowed in Development, bugs found while vendoring, a package bump.
- **Phase 2, structural changes.** Changes that need agreement with the owning team first: a supported local
  configuration without Key Vault, Azure Blob, Service Bus or Storage queues; service discovery by name; a supported
  local mode instead of a fake; seeding test data; publishing images localtest can pull.

Each item is a checkbox with what to change upstream, why it matters locally, a size (trivial, small, medium, large),
what it removes from localtest (site `file:lines` as in the component's `vendor/UPSTREAM.md` site table, fakes, patches,
build overrides, Dockerfile deviations), the upstream issue or pull request when one exists, and a link to the review or
plan with the detail. Each file ends with "Stays local", the modifications that are localtest's own and should not go
upstream, and a coverage line that accounts for every site, patch, fake, build override and Dockerfile deviation.

Site lines are those of the site tables at the pins in [`../components/*/vendor/UPSTREAM.md`](../components/); a
sync moves them, so take the current lines from the table.

## Marking progress

- When an upstream pull request is opened, add its link to the item's "Upstream" line.
- When it is merged, change `- [ ]` to `- [x]` and add "block to remove at next sync" (or what else is left, such as a
  package bump in the consumer).
- At the sync that takes the change in (`docs/vendor-sync.md`), remove what the item lists, then delete the item and
  update the coverage line. The site table in `UPSTREAM.md` is the record of what is left.
- A new site, fake or deviation added to a component gets an item or a "Stays local" line in the same change.

## Index

"Sites" counts directive sites (patches for Arbeidsflate) at the pin: all of them, and how many are gone once every item
in the file is done. The rest stay as "Stays local" hooks for localtest-only additions (inspection endpoints, test data)
until those move out of the host. `[x]` items are counted in their phase.

| Service                                         | Upstream                                        | Phase 1 | Phase 2 | Done `[x]` | Sites: gone / all                                   | Notes                                                                                                                                                               |
| ----------------------------------------------- | ----------------------------------------------- | ------- | ------- | ---------- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Authentication](authentication.md)             | Altinn/altinn-auth                              | 4       | 4       | 0          | 2 / 2                                               | Group `altinn-auth` (one pin with Authorization and Access Management); image not published yet; upstream's local-dev epic altinn-auth#4048 plans Azurite and https |
| [Authorization](authorization.md)               | Altinn/altinn-auth                              | 2       | 3       | 1          | 2 / 3                                               | Group `altinn-auth`; image public since altinn-auth#4739; the next sync follows `containers/Dockerfile.api`                                                         |
| [Access Management](access-management.md)       | Altinn/altinn-auth                              | 3       | 4       | 1          | 3 / 5                                               | Group `altinn-auth`; image public                                                                                                                                   |
| [Register](register.md)                         | Altinn/altinn-register, then Altinn/altinn-auth | 2       | 5       | 0          | 1 / 1 (the hook only once `Controllers/` moves out) | Waits on altinn-auth#4056 (the move into altinn-auth); phase 2 targets altinn-auth                                                                                  |
| [Resource Registry](resource-registry.md)       | Altinn/altinn-auth                              | 4       | 5       | 0          | 2 / 2                                               | Pinned from the archived Altinn/altinn-resource-registry; retarget to altinn-auth at the next sync                                                                  |
| [Storage](storage.md)                           | Altinn/altinn-storage                           | 3       | 7       | 0          | 5 / 5                                               | Self-link base is in the `Storage.Interface` package; signing keys depend on altinn-accesstoken#136                                                                 |
| [Events](events.md)                             | Altinn/altinn-events                            | 5       | 6       | 0          | 6 / 7                                               | Two Postgres bugs found (`trace_log` partitions end with 2026, time defaults assume a UTC server); altinn-events#1149 (Aspire, emulators) is related                |
| [Profile](profile.md)                           | Altinn/altinn-profile                           | 2       | 4       | 0          | 2 / 4                                               | Open altinn-profile#1073 (Aspire host, OTLP) lacks the environment detector                                                                                         |
| [Receipt](receipt.md)                           | Altinn/altinn-receipt                           | 3       | 2       | 0          | 4 / 4                                               |                                                                                                                                                                     |
| [Correspondence](correspondence.md)             | Altinn/altinn-correspondence                    | 9       | 3       | 2          | 6 / 6 (the hook only once inspection moves out)     | Party-id site: altinn-correspondence#2157 merged, block to remove at next sync; image public; draft #2152 overlaps two items                                        |
| [Notifications](notifications.md)               | Altinn/altinn-notifications                     | 4       | 4       | 1          | 3 / 4                                               | OTLP merged (altinn-notifications#1930), the service name item remains; open Aspire mock-stack PRs relate to the transport item                                     |
| [Dialogporten](dialogporten.md)                 | Altinn/dialogporten                             | 5       | 4       | 1          | 7 / 7                                               | Images public; already runs in upstream's own local mode (Development, `LocalDevelopment`)                                                                          |
| [Dialogporten adapter](dialogporten-adapter.md) | Altinn/altinn-dialogporten-adapter              | 6       | 3       | 1          | 10 / 10                                             | No public image; upstream's local mode is the Service Bus emulator                                                                                                  |
| [Arbeidsflate](arbeidsflate.md)                 | Altinn/dialogporten-frontend                    | 4       | 2       | 0          | 3 / 3 patches                                       | No `#if` sites; images public on ghcr; patch 0003 reverses a security fix's `secure: true` for http only                                                            |
| [authorization-utils](authorization-utils.md)   | Altinn/altinn-authorization-utils               | 2       | 1       | 0          | none of its own                                     | The ServiceDefaults package of the four `AltinnServiceDefaults` hosts; see below                                                                                    |
| **Total**                                       |                                                 | **58**  | **57**  | **7**      |                                                     |                                                                                                                                                                     |

The workflow engine is done: its OTLP fix, altinn-studio#21515, is merged, studioctl runs the monorepo's image
unchanged, and it is no longer vendored.

## Across services

Changes that land in one place but remove modifications in several services, or the same change repeated per service.
Agree on each with one team first and reuse the result.

### `Altinn/altinn-authorization-utils` ServiceDefaults

Authentication, Access Management, Register and the Resource Registry build their host with
`AltinnHost.CreateWebApplicationBuilder`. One release of the package and a bump in each host removes their copies of
`fakes/Otlp/` (`AddLocaltestOtlpResource`) and `fakes/ServiceDiscovery/` (`AddLocaltestServiceEndpointProviders`):
OTLP and Azure Monitor exporters independent, `OTEL_SERVICE_NAME` winning over `AltinnServiceResourceDetector`, and an
option for service discovery without the pass-through provider. Details and the per-host items it covers:
[`authorization-utils.md`](authorization-utils.md).

### OTLP in the Azure Monitor services

Storage, Events, Profile, Receipt and Correspondence are OpenTelemetry first already; each needs the OTLP package, one
`UseOtlpExporter()` when the variable is set, and the environment detector after its hard-coded `service.name`
(Notifications has the exporter since altinn-notifications#1930 and needs only the detector). Authorization and the
Dialogporten adapter need their pipeline built first, Dialogporten needs the exporters to read endpoint and protocol
themselves, and the Arbeidsflate BFF needs its exporter chosen by `OTEL_EXPORTER_OTLP_PROTOCOL`. The OTLP item in each
service file; the classification per service is in [`../reviews/otel/README.md`](../reviews/otel/README.md), with the
note on `AddAzureMonitorTraceExporter`'s sampler to carry into every pull request.

### Service discovery by name

The stack gives each container `services__<name>__http__0` for every running service. The four ServiceDefaults hosts
register `AddServiceDiscovery()` already (see above); Authorization, Storage, Events, Profile, Notifications, Receipt,
Correspondence, Dialogporten and the Dialogporten adapter need the same registration after
`WebApplication.CreateBuilder` and remove their `#if LOCALTEST` block and `fakes/ServiceDiscovery/`. Correspondence
(`AltinnOptions:PlatformGatewayUrl`, nine clients) and Dialogporten (`Infrastructure:Altinn:BaseUri`, four clients) also
need a base URL per platform service instead of one shared base, which removes `LocaltestPrefixRouting`. Access
Management's Resource Registry client needs to come from the factory. The "Service discovery" item in each file; the
pattern and inventory: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

### A supported local configuration without Key Vault, Azure Blob and queues

The largest structural theme, and the one that needs a shared decision: what a service does when no Azure resource is
configured. localtest does not run Azurite or the Service Bus emulator; several upstream plans do
([altinn-auth#4048](https://github.com/Altinn/altinn-auth/issues/4048) and its issues,
[altinn-events#1149](https://github.com/Altinn/altinn-events/pull/1149), Notifications' Aspire mock stack), so raise the
difference there.

- **Platform access token keys without Key Vault.** `PublicSigningKeyProvider` in `Altinn/altinn-accesstoken`
  reads Key Vault only; [altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136) (open) asks
  for a local mode. Covers the signing-key items in [Storage](storage.md), [Events](events.md),
  [Profile](profile.md), [Notifications](notifications.md), [Register](register.md),
  [Resource Registry](resource-registry.md), [Authentication](authentication.md), [Authorization](authorization.md) and
  [Access Management](access-management.md).
- **Blob storage.** Storage's data, Correspondence's attachments, and the policy store shared by Authorization, Access
  Management and the Resource Registry (one layout, written by all three). Items in [Storage](storage.md),
  [Correspondence](correspondence.md), [Authorization](authorization.md), [Access Management](access-management.md) and
  [Resource Registry](resource-registry.md).
- **Queues and Service Bus.** Events' delivery, Notifications' transport, the Dialogporten adapter's transport,
  Storage's file scan and outbox, Authorization's audit queue. Items in [Events](events.md),
  [Notifications](notifications.md), [Dialogporten adapter](dialogporten-adapter.md), [Storage](storage.md) and
  [Authorization](authorization.md).

### OpenID metadata and URLs over http in Development

The stack is plain http. Several hosts refuse http metadata or URLs even in Development: items in
[Authentication](authentication.md), [Correspondence](correspondence.md), [Dialogporten](dialogporten.md),
[Dialogporten adapter](dialogporten-adapter.md) and [Events](events.md). The `JWTCookieAuthentication` handler (in
altinn-auth's `src/pkgs/`) ignores `RequireHttpsMetadata` on its per-issuer path, one fix for
[Access Management](access-management.md) and the [Resource Registry](resource-registry.md).

### Cookies with `Secure` on plain http

Authentication and Correspondence hard-code `Secure` (localtest's shared cookie policy in `fakes/Cookies/` removes it
on http), and the Arbeidsflate BFF does through patch 0003. Items in [Authentication](authentication.md),
[Correspondence](correspondence.md) and [Arbeidsflate](arbeidsflate.md); the inventory is
[`../reviews/cookies.md`](../reviews/cookies.md).

### `Altinn.ApiClients.Maskinporten`

The package hard-coded the Maskinporten and exchange hosts; configurable endpoints are merged and released in 10.2.0
([altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92)). What is left is a
bump to 10.2.0 in each consumer, which removes its copy of `fakes/Maskinporten/`: [Correspondence](correspondence.md),
[Notifications](notifications.md), [Dialogporten](dialogporten.md), [Dialogporten adapter](dialogporten-adapter.md),
and [Profile](profile.md) (a prerequisite for its KRR item there).

### Published images

Public, commit-tagged images exist for Authorization and Access Management (`ghcr.io/altinn/altinn-auth/…`),
Correspondence, Dialogporten and Arbeidsflate. Authentication, Register, the Resource Registry, Storage, Events, Profile,
Receipt, Notifications and the Dialogporten adapter have none localtest can pull. The last item of each file.

## Related, out of scope

- **app-lib** ([`../../TODO.md`](../../TODO.md), task 22): honour an explicitly configured localtest environment, and
  take the `keepAlive` cookie's `Secure` from the request, in `Altinn/app-lib-dotnet` and the monorepo's
  `src/App/backend`. Not a vendored service.
- **The monorepo's test apps** ([`../reviews/test-apps.md`](../reviews/test-apps.md)): 8 of 13 policies have a `delete`
  rule the Resource Registry's flattener rejects; the fix is in each app's `policy.xml` in `Altinn/altinn-studio`, not
  in the Resource Registry.
