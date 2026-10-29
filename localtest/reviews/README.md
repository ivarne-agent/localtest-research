# Reviews of the localtest components

Reviews of how each component is "localtestified": the vendored production code and its fidelity to upstream, the
directive sites, the Dockerfile against upstream's, what the fakes replace, what a developer sees differently from
production, and findings. One file per component, newest status at the top of each. Behaviour that later tasks
changed is marked with a dated "Superseded" note at the top of the review; the reviews themselves are not rewritten.

| Component | Review | State |
| --- | --- | --- |
| Storage | [`storage.md`](storage.md) | Reviewed and fixed 2026-10-19 (re-pin, Dockerfile mirrors upstream) |
| Register | [`register.md`](register.md) | Reviewed and fixed 2026-10-19/20 (Dockerfile, Profile authorization, `userType` at load) |
| Authentication | [`authentication.md`](authentication.md) | Reviewed 2026-10-19; identity fakes moved to the localtest app 2026-10-20 |
| Localtest app | [`localtest-app.md`](localtest-app.md) | Reviewed 2026-10-20; handled except `HEAD` on most endpoints and a WebSocket test |
| Authorization (PDP) | [`authorization.md`](authorization.md) | Reviewed 2026-10-20; all findings handled |
| Access Management | [`access-management.md`](access-management.md) | Reviewed 2026-10-20; all findings handled (two documented as known gaps) |
| Events | [`events.md`](events.md) | Reviewed 2026-10-20; all findings handled (four documented as known gaps) |
| Workflow engine and PDF | [`workflow-engine.md`](workflow-engine.md) | Reviewed 2026-10-20; handled except the state reset (left as decided) |
| Test apps (monorepo `src/test/apps`) | [`test-apps.md`](test-apps.md) | Checked 2026-10-21: 8 of 13 policies fail the Resource Registry's `policy/rules` and `policy/rights` (one template `delete` rule); test data valid. Not fixed here, for upstream |
| Cookies with `Secure` across the stack (task 21b) | [`cookies.md`](cookies.md) | Inventory 2026-10-21, for the owner's review before merge: one cookie policy in two components' `fakes/`, a third Arbeidsflate patch, no new directive site; app routes keep the front doors' stripping (app-lib's `keepAlive`) |
| Egress domains (task 44) | [`2026-10-23-egress-domains.md`](2026-10-23-egress-domains.md) | Phase 1 2026-10-23: static inventory per process (monorepo app, browser as a CSP, pdf3, workflow engine, components), enforcement options, capture scripts in `../tools/egress/`; phase 2 (observed on the stack) done 2026-10-24 |

## Across components

- **Dockerfiles.** Storage, Register, Authentication, Authorization and Access Management mirror the upstream image
  definition (pinned digests, non-root user, the same steps; deviations marked `localtest:`); for Access Management
  that is the release image built through `docker-bake.hcl` from the shared `containers/` files. Events followed on
  2026-10-20 as well, and the workflow engine did until it stopped being vendored (task 58, 2026-10-27: studioctl runs
  the monorepo's `tt02` image).

- **HEALTHCHECK** lines are dead everywhere: podman builds OCI images, which drop them. Health checks are left for the
  studioctl integration. (Both runtimes now check each container's health path from inside the stack's network; see
  [`../CONVENTIONS.md`](../CONVENTIONS.md#port-and-health).)
- **Re-pinning.** Authentication, Authorization and Access Management vendor the same `Altinn/altinn-auth` commit and
  share libraries; they were re-pinned together to `3d518103` on 2026-10-20 with `../tools/repin-vendor.py`.
- **Sync instructions** (`vendor/UPSTREAM.md`) exist for every vendored component.
- **`ASPNETCORE_ENVIRONMENT=Development`** everywhere, for http metadata and local queues: developer exception pages,
  Debug logging (very noisy in Authorization and Events), Swagger.
- **Internet.** Authentication's `exchange/maskinporten` (org code), Storage's Studio and metrics endpoints and pdf3's
  fonts and logo read from `altinncdn.no`. A local copy served by the localtest app would remove that.
- **Profile.** The register component answers Profile's endpoints; Authentication and Authorization use their
  production Profile clients against it. *Superseded 2026-10-21 (task 14): production Profile is its own component
  (`../components/profile/`; no host port since task 27); Authentication, Authorization and Access Management call it, and the
  Register serves no Profile endpoint.*
- **Internet** (above) stays: task 3 (a local altinncdn.no) was decided against on 2026-10-20.
