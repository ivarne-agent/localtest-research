# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/vendor/` | Unmodified copy of Altinn/altinn-register (see `UPSTREAM.md`); later changed only inside `#if !LOCALTEST` blocks |
| `components/authentication/` | Fake-only component, starting from an unmodified copy of today's localtest (see `ORIGIN.md`) |
| `components/common/` | Shared sources today's localtest compiles by relative path; goes away with the proxy |

Status: baseline copies only. No `#if LOCALTEST` blocks, no compose file yet.
