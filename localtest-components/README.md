# Localtest component research

Research files for the question: *Localtest lags behind the platform components it emulates. Could it
instead use a subtree merge of the real components, and which component would be easiest to replace with
a container started by `studioctl env up`?*

Constraints from the product owner (2026-10-16):

- Register stays in localtest.
- The fake login page (pick the user you want to be) stays in localtest.

## Files

| File | Content |
| --- | --- |
| `summary.md` | Conclusions and recommendation (read this first) |
| `inventory.md` | Component inventory: what localtest contains, size, production home, drift |
| `images-and-repos.md` | Where each service's code lives now and which container images are publicly pullable |
| `storage.md` | Storage strand (localtest copy vs [`Altinn/altinn-storage`](https://github.com/Altinn/altinn-storage)) |
| `authorization.md` | Authorization, Access Management, Resource Registry strand ([`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth)) |
| `authn-profile-register.md` | Authentication, Profile, Register and the test-user data model |
| `events-notifications.md` | Events and Notifications strand (16 Oct; its stub verdict is superseded by `communication-and-inbox.md`) |
| `studioctl-and-app-contract.md` | How studioctl composes containers, the app ↔ platform contract, subtree practice |
| `communication-and-inbox.md` | 2026-10-18: Events, Notifications, Correspondence, Dialogporten and the inspection GUI combined; shared prerequisites, order, open questions (read this first for that topic) |
| `events-vendoring.md` | Vendoring production Events, with a capture API for the GUI |
| `notifications-vendoring.md` | Vendoring production Notifications (API, e-mail and SMS hosts), with e-mail and SMS capture |
| `correspondence-vendoring.md` | Vendoring production Correspondence |
| `dialogporten-arbeidsflate.md` | Dialogporten, the Altinn adapter and Arbeidsflate in localtest |
| `inspection-gui.md` | Design of the GUI where testers see everything the platform sent |

## Sources

- Localtest: [`altinn-studio/src/Runtime/localtest`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest) (monorepo `main` at 447e53eb97, 2026-10-16)
- studioctl: [`altinn-studio/src/cli`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli)
- Production repos, shallow clones under `/home/agent/code/.reference/` (plus [`/home/agent/code/altinn-storage`](https://github.com/Altinn/altinn-storage))
