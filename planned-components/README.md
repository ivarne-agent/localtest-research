# Planned components

Plans for replacing localtest emulations with vendored production code. One subfolder per component.
Each plan states the goal, the vendoring approach, where local modifications live, the test-data mapping and
the rollout order. Background research is in [`../localtest-components/`](../localtest-components/).

| Component | Status | Plan |
| --- | --- | --- |
| All (MVP model) | sketch | [`mvp-vendoring-model.md`](mvp-vendoring-model.md): compose root, one folder per component with Dockerfile + vendored subtree + build props/targets + adjacent fakes |
| All: studioctl | plan | [`studioctl-integration.md`](studioctl-integration.md): what changes in the monorepo, the images, studioctl and studioctl-server to replace today's localtest with this stack; [`studioctl-integration-options.md`](studioctl-integration-options.md): the alternatives at each decision point |
| Register | MVP implemented in [`../localtest/components/register/`](../localtest/components/register/) | [`register/README.md`](register/README.md) (plan), [`register/production-register-analysis.md`](register/production-register-analysis.md) (analysis with scratch build) |

## Conventions for vendored production code

- Production source is brought in with `git subtree` (non-squashed, the monorepo's existing practice in the
  root `Makefile`), so upstream history is preserved and `git subtree pull` merges later upstream changes.
- Every local modification to a vendored file sits inside a preprocessor block with the **production code
  first and unchanged**:

  ```csharp
  #if !LOCALTEST
      // production code exactly as upstream has it
  #else
      // localtest replacement
  #endif
  ```

  Keeping the upstream lines in the `#if !LOCALTEST` branch means an upstream change to those lines merges
  as a normal diff hunk against the same text, instead of a conflict against deleted code. Additions that
  have no production counterpart use a bare `#if LOCALTEST … #endif` block. Never edit a vendored line in
  place.
- `LOCALTEST` is defined in `LocalTest.csproj` (`<DefineConstants>$(DefineConstants);LOCALTEST</DefineConstants>`).
  Vendored projects that localtest compiles get the same define from a `Directory.Build.props` in the
  localtest tree, not from edits to the vendored `.csproj` files.
- Files that localtest should not compile at all (Postgres persistence, import jobs, Aspire host) are
  excluded by glob from the localtest-side project file rather than deleted, so the subtree stays complete.
- Localtest-only implementations (in-memory persistence over the test-data model, auth policies) live
  outside the vendored directory, under `src/Runtime/localtest/src/<Component>/`, and are wired in from the
  `#else` branches or from localtest's own `Startup.cs`.
| Authentication | MVP implemented in [`../localtest/components/authentication/`](../localtest/components/authentication/) | [`authentication/production-authentication-analysis.md`](authentication/production-authentication-analysis.md) (analysis with verified run); [`authentication/localtest-app-identity.md`](authentication/localtest-app-identity.md) (plan: fake ID-porten, Maskinporten and `/Home` move to the localtest app) |
| Authorization (PDP) | MVP implemented in [`../localtest/components/authorization/`](../localtest/components/authorization/) | [`../localtest-components/authorization.md`](../localtest-components/authorization.md) |
| Storage | MVP implemented in [`../localtest/components/storage/`](../localtest/components/storage/) | [`../localtest-components/storage.md`](../localtest-components/storage.md) |
| Dialogporten, adapter, Arbeidsflate | plan (five strands, 2026-10-20) | [`dialogporten/README.md`](dialogporten/README.md): index, cross-strand decisions and order; reports for the shared prerequisites, the Resource Registry, Dialogporten, the adapter and Arbeidsflate |
| Notifications | plan (research 2026-10-20) | [`notifications/README.md`](notifications/README.md): index and headline findings; [`notifications/notifications.md`](notifications/notifications.md): consumer contract, production path (Azure Service Bus, not Kafka), build chain verified in a scratch build, options A/B/C (recommends vendoring the orders API host with capture fakes), directive sites, fakes, test-data mapping, inspection API, smoke tests |
