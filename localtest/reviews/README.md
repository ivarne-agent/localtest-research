# Reviews of the localtest components

Reviews of how each component is "localtestified": the vendored production code and its fidelity to upstream, the
directive sites, the Dockerfile against upstream's, what the fakes replace, what a developer sees differently from
production, and findings. One file per component, newest status at the top of each.

| Component | Review | State |
| --- | --- | --- |
| Storage | [`storage.md`](storage.md) | Reviewed and fixed 2026-10-19 (re-pin, Dockerfile mirrors upstream) |
| Register | [`register.md`](register.md) | Reviewed and fixed 2026-10-19/20 (Dockerfile, Profile authorization, `userType` at load) |
| Authentication | [`authentication.md`](authentication.md) | Reviewed 2026-10-19; identity fakes moved to the localtest app 2026-10-20 |
| Localtest app | [`localtest-app.md`](localtest-app.md) | Reviewed 2026-10-20; most findings fixed the same day |
| Authorization (PDP) | [`authorization.md`](authorization.md) | Reviewed 2026-10-20; Dockerfile, re-pin and sync instructions fixed |
| Access Management | [`access-management.md`](access-management.md) | Reviewed 2026-10-20; Dockerfile (release image), re-pin and sync instructions fixed |
| Events | [`events.md`](events.md) | Reviewed 2026-10-20; findings open (webhooks to `host.docker.internal` now delivered) |
| Workflow engine and PDF | [`workflow-engine.md`](workflow-engine.md) | Reviewed 2026-10-20; findings open |

## Across components

- **Dockerfiles.** Storage, Register, Authentication, Authorization and Access Management mirror the upstream image
  definition (pinned digests, non-root user, the same steps; deviations marked `localtest:`); for Access Management
  that is the release image built through `docker-bake.hcl` from the shared `containers/` files. Events and the
  workflow engine do not yet.

- **HEALTHCHECK** lines are dead everywhere: podman builds OCI images, which drop them. Health checks are left for the
  studioctl integration.
- **Re-pinning.** Authentication, Authorization and Access Management vendor the same `Altinn/altinn-auth` commit and
  share libraries; they were re-pinned together to `3d518103` on 2026-10-20 with `../tools/repin-vendor.py`.
- **Sync instructions** (`vendor/UPSTREAM.md`) exist for Storage, Register and the three altinn-auth components; Events and the workflow engine need them.
- **`ASPNETCORE_ENVIRONMENT=Development`** everywhere, for http metadata and local queues: developer exception pages,
  Debug logging (very noisy in Authorization and Events), Swagger.
- **Internet.** Authentication's `exchange/maskinporten` (org code), Storage's Studio and metrics endpoints and pdf3's
  fonts and logo read from `altinncdn.no`. A local copy served by the localtest app would remove that.
- **Profile.** The register component answers Profile's endpoints; Authentication uses the production Profile client
  against it. Authorization still has its own Profile fake, which could go the same way.
