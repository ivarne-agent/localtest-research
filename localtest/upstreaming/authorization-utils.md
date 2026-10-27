# Altinn.Authorization.ServiceDefaults: upstreaming

- Upstream: [Altinn/altinn-authorization-utils](https://github.com/Altinn/altinn-authorization-utils), package
  `Altinn.Authorization.ServiceDefaults` (`src/pkgs/Altinn.Authorization.ServiceDefaults/src/ServiceDefaults/`). A shared
  library, not vendored: there is no pin of our own; each host restores the version its own build files name.
- Versions the hosts use: [Authentication](authentication.md) 6.0.0 (its csproj overrides the central 5.5.0),
  [Access Management](access-management.md) 5.5.0, [Register](register.md) 5.5.0,
  [Resource Registry](resource-registry.md) 6.0.0 (from the vendored csproj and `Directory.Packages.props` files).
  [Authorization](authorization.md) references 5.5.0 but never calls it. `main` has changed nothing in
  `ServiceDefaults/` since `v6.0.0` but dependency updates and the repository reorganization (checked 2026-10-27), so
  both items below are still open.
- Localtest code it forces: in each of the four hosts the hook calls `AddLocaltestServiceEndpointProviders` and
  `AddLocaltestOtlpResource` from the shared copies `fakes/ServiceDiscovery/` and `fakes/Otlp/` (Register: under
  `fakes/Altinn.Register.Localtest/`, with two OpenTelemetry package references in the fakes project).

A change here is one package release, then a version bump in each host's repository (Register and Access Management
are on 5.5.0). Upstream's own plan for local runs states the ServiceDefaults side in
[altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050) (the configuration contract for LocalDev and
Aspire, ServiceDefaults 6.x); the items below belong there.

## Phase 1: easy fixes

- [ ] **OTLP and Azure Monitor independent.** In `Microsoft.Extensions.Hosting/AltinnServiceDefaultsExtensions.cs`,
      `AddOpenTelemetryExporters` adds the OTLP exporters when `OTEL_EXPORTER_OTLP_ENDPOINT` is set and Application
      Insights only `else if`. Make them two `if`s, so a deployment can send to both while moving from Application
      Insights to a collector.
  - Why: today the variable silently turns Azure Monitor off.
  - Size: small; it changes behaviour only where both are configured.
  - Removes: nothing locally (localtest never has a connection string); it is the production half of the stack's rule.
    Carry the sampler note from [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.
  - Covers: [`authentication.md`](authentication.md), [`access-management.md`](access-management.md),
    [`register.md`](register.md), [`resource-registry.md`](resource-registry.md), the telemetry item of each.
  - Upstream: none found.
  - Detail: [`../reviews/otel/authentication.md`](../reviews/otel/authentication.md), "Upstream change".

- [ ] **`OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` win over `AltinnServiceResourceDetector`.** The detector
      sets `service.name` to the host's name after the SDK's environment detector. Add
      `resource.AddEnvironmentVariableDetector()` after it in both `ConfigureResource` calls of
      `ConfigureOpenTelemetry` (the tracer provider adds the detector a second time), or let the detector skip
      `service.name` when `OTEL_SERVICE_NAME` is set.
  - Why: a runner that names the service (Aspire, studioctl with another name, a manual run) is ignored.
  - Size: small; it changes behaviour only where `OTEL_SERVICE_NAME` is set.
  - Removes: the `AddLocaltestOtlpResource` call and `fakes/Otlp/` in Authentication, Access Management and the
    Resource Registry; in Register `fakes/Altinn.Register.Localtest/Otlp/` and its two OpenTelemetry package
    references.
  - Covers: [`authentication.md`](authentication.md), [`access-management.md`](access-management.md),
    [`register.md`](register.md), [`resource-registry.md`](resource-registry.md), the `OTEL_SERVICE_NAME` item of each.
  - Upstream: none found.
  - Detail: [`../reviews/otel/README.md`](../reviews/otel/README.md), "Across the four ServiceDefaults hosts".

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
