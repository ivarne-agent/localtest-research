# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/vendor/` | Unmodified copy of Altinn/altinn-register (see `UPSTREAM.md`); later changed only inside `#if !LOCALTEST` blocks |

Fake-only components (authentication, later profile, events, notifications) are written fresh for this layout.
Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a reference for
which behaviours the fakes must provide.

Status: baseline copy of Register only. No `#if LOCALTEST` blocks, no compose file yet.
