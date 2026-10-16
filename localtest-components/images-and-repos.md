# Where the production code and container images live (checked 2026-10-16)

## Repositories

| Service | Live repository | Notes |
| --- | --- | --- |
| Storage | [`Altinn/altinn-storage`](https://github.com/Altinn/altinn-storage) (main, pushed 2026-10-16) | Own repo. 1393 commits, ~6 MB. Local clone [`/home/agent/code/altinn-storage`](https://github.com/Altinn/altinn-storage). |
| Authorization (PDP/PEP) | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/apps/Altinn.Authorization`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization) | Monorepo; [`Altinn/altinn-authorization`](https://github.com/Altinn/altinn-authorization) is now [`altinn-authorization-archive`](https://github.com/Altinn/altinn-authorization-archive) (archived 2026-08). |
| Access Management | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/apps/Altinn.AccessManagement`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement) | [`altinn-access-management`](https://github.com/Altinn/altinn-access-management) is now [`altinn-access-management-archive`](https://github.com/Altinn/altinn-access-management-archive). |
| Resource Registry | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/apps/Altinn.ResourceRegistry`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry) | [`Altinn/altinn-resource-registry`](https://github.com/Altinn/altinn-resource-registry) archived (last push 2026-09-25). |
| Authentication | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/apps/Altinn.Authentication`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication) | [`Altinn/altinn-authentication`](https://github.com/Altinn/altinn-authentication) archived (last push 2026-10-15). |
| Mockporten | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/apps/Altinn.Mockporten`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Mockporten) | Local mock of Maskinporten/ID-porten used by the auth team. |
| Register | [`Altinn/altinn-register`](https://github.com/Altinn/altinn-register) (main, pushed 2026-10-16) | Placeholder in altinn-auth [`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Register); moves in with [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056). |
| Profile | [`Altinn/altinn-profile`](https://github.com/Altinn/altinn-profile) (main, pushed 2026-10-16) | Own repo. |
| Events | [`Altinn/altinn-events`](https://github.com/Altinn/altinn-events) (main, pushed 2026-10-16) | Own repo. |
| Notifications | [`Altinn/altinn-notifications`](https://github.com/Altinn/altinn-notifications) (main, pushed 2026-10-16) | Own repo, [`components/{api,email-service,sms-service,shared}`](https://github.com/Altinn/altinn-notifications/tree/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components). |
| ABAC, PEP, JWTCookieAuthentication | [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth) → [`src/pkgs/*`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs) | Published as NuGet (`Altinn.Authorization.ABAC`, `Altinn.Common.PEP`, `JWTCookieAuthentication`). |
| PDF | [`altinn-studio/src/Runtime/pdf3`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/pdf3) | Already in the monorepo and already its own studioctl container. |
| Old localtest | [`Altinn/app-localtest`](https://github.com/Altinn/app-localtest) (not archived yet, last push 2026-10-02) | Imported into the monorepo in merge commit [`1166612623`](https://github.com/Altinn/altinn-studio/commit/1166612623) (2026-06-04). |

[`altinn-auth`](https://github.com/Altinn/altinn-auth) is ~46 MB with 3426 commits and uses a `repoctl` tool, `conf.json` per vertical, Just,
devenv, and a [`docker-compose.yml`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/docker-compose.yml) with Valkey + Postgres + pgAdmin.

## Publicly pullable container images (anonymous ghcr tag listing)

| Image | Public? | Tags seen |
| --- | --- | --- |
| `ghcr.io/altinn/altinn-auth/altinn-authorization` | yes | git-sha tags (`fb8c898`, …) |
| `ghcr.io/altinn/altinn-auth/access-management` | yes | git-sha tags |
| `ghcr.io/altinn/altinn-auth/accessmanagement` (legacy name) | yes | full-sha tags |
| `ghcr.io/altinn/altinn-auth/authorization-manifests`, `accessmanagement-manifests` | yes | Flux OCI manifests |
| `ghcr.io/altinn/altinn-profile` | yes | git-sha tags |
| `ghcr.io/altinn/altinn-notifications` | yes | `0.0.1`, `latest` (looks stale / experimental) |
| Storage, Register, Events, Resource Registry, Authentication, Mockporten | **not found** under any obvious name | Their GitHub workflows only build images for Trivy scans; production images are built by Azure DevOps (`dev.azure.com/brreg/altinn-studio`, pipelines `altinn-platform/<service>-master`) into a registry we cannot see from here. Not confirmed that no public image exists, only that none was found. |

Consequence: a studioctl container for Storage, Register or Events would have to be built from source
(the Dockerfile in each repo is present and simple), either by a monorepo workflow that publishes a
`ghcr.io/altinn/altinn-studio/localtest-<service>` image, or by asking the owning team to publish to ghcr.
Authorization and Profile images can be pulled today.

The Azure DevOps infrastructure repo was not reachable from this sandbox (`AZURE_DEVOPS_PAT` unset), so
the exact production image names and the deploy pipelines are unverified.
