# Altinn.Authorization.ServiceDefaults: upstreaming

- Upstream: [Altinn/altinn-authorization-utils](https://github.com/Altinn/altinn-authorization-utils), package
  `Altinn.Authorization.ServiceDefaults` (`src/pkgs/Altinn.Authorization.ServiceDefaults/src/ServiceDefaults/`). A shared
  library, not vendored: there is no pin of our own; each host restores the version its own build files name.
- Versions the hosts use: [Authentication](authentication.md) 6.0.0 (its csproj overrides the central 5.5.0),
  [Access Management](access-management.md) 5.5.0, [Register](register.md) 5.5.0,
  [Resource Registry](resource-registry.md) 6.0.0 (from the vendored csproj and `Directory.Packages.props` files).
  [Authorization](authorization.md) references 5.5.0 but never calls it. `main` has changed nothing in
  `ServiceDefaults/` since `v6.0.0` but dependency updates and the repository reorganization (checked 2026-10-27), so
  the item below is still open.
- Localtest code it forces: in each of the four hosts the hook calls `AddLocaltestServiceEndpointProviders` from the
  shared copy `fakes/ServiceDiscovery/` (Register: under `fakes/Altinn.Register.Localtest/`). Until task 61
  (2026-10-28, [`../../TODO.md`](../../TODO.md)) it also called `AddLocaltestOtlpResource` (`fakes/Otlp/`); see "Not
  needed" below.

A change here is one package release, then a version bump in each host's repository (Register and Access Management
are on 5.5.0). Upstream's own plan for local runs states the ServiceDefaults side in
[altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050) (the configuration contract for LocalDev and
Aspire, ServiceDefaults 6.x); the items below belong there.

## Phase 1: easy fixes

None open: the two former items are under "Not needed" below.

## Phase 2: structural changes

- [ ] **An option for service discovery without the pass-through for service names.** `AddAltinnServiceDefaults`
      calls `AddServiceDiscovery()`, which registers the configuration provider and the pass-through provider; the
      pass-through factory is internal to `Microsoft.Extensions.ServiceDiscovery` and has no switch. Add an option (in
      `AltinnServiceDefaultOptions` or configuration, off by default) that registers `AddServiceDiscoveryCore()` with the
      configuration provider and a provider that passes through only real hosts (a dot, an IP address, `localhost`), so
      a single-label name without a `services__<name>__…` entry fails at once.
  - Why: localtest wants an optional group that is off to fail immediately with "no endpoint" rather than through a
    DNS lookup, and removes the internal factory by type name today. Production resolves single-label Kubernetes
    service names through the pass-through, so the default must stay as it is.
  - Size: small in code; needs agreement that the library should carry a local-only option (localtest could also keep
    its fake).
  - Removes: the `AddLocaltestServiceEndpointProviders` call and `fakes/ServiceDiscovery/` in Authentication, Access
    Management and the Resource Registry, and `fakes/Altinn.Register.Localtest/ServiceDiscovery/` in Register.
  - Covers: [`authentication.md`](authentication.md), [`access-management.md`](access-management.md),
    [`register.md`](register.md), [`resource-registry.md`](resource-registry.md), the service-discovery item of each.
  - Upstream: none found; related [altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050).
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), section
    3, "A name with no entry".

No other default forces localtest code in the four hosts. `AltinnHost` reads its settings when the builder is created,
before a host's hook runs, so the hosts run in environment `UNKNOWN` on purpose: `LOCAL` (`Altinn:LocalDev`) would take
platform tokens from the remote test token generator. That costs no code, only the rule that a setting ServiceDefaults
must see comes from the environment.

## Not needed

Both former phase-1 items have no local effect, so localtest does not propose them (the branches `otel-service-name-env`
and `otlp-and-azure-monitor` on the fork `ivarne-agent/altinn-authorization-utils` stay unopened). Evidence:
`/home/agent/code/.handover/aspire-instance-id-check/` (2026-10-28) and TODO.md section 61.

- **OTLP and Azure Monitor independent** (`AddOpenTelemetryExporters`' `else if` made two `if`s): localtest never has
  an Application Insights connection string, so it changes nothing here; a production concern for the owning team.
- **`OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` win over `AltinnServiceResourceDetector`**: each host's own
  `service.name` is already what studioctl and Aspire set as `OTEL_SERVICE_NAME` (the manifest name), and Aspire's
  dashboard links no container's telemetry to its resource either way (its `service.instance.id` for a container is the
  full DCP name). Without it `service.instance.id` is the container's host name, accepted. The four hosts dropped
  `AddLocaltestOtlpResource` and `fakes/Otlp/` in task 61.
