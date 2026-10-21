# Plan: Notifications in the new localtest

## Status (2026-10-21)

- **Implemented** as task 6 (2026-10-20; `TODO.md` gives `23e3884..72b1781`, on `main` `45e99a1..0b38452`) in
  [`../../localtest/components/notifications/`](../../localtest/components/notifications/README.md), default stack.
  Smoke test 64 ok on 2026-10-21 (against production Profile since task 14).
- Taken: Option A, the orders API host with capture fakes, two directive sites (one additive, one `#if !LOCALTEST`
  around `AddWolverineServices`), the send result applied after the claim transaction commits.
- Decided (task 6, questions in section 6.14): every captured message `Delivered`; the capture modelled as inboxes per
  address or number with a `receiving` flag; no number normalisation; no failure simulation and no reserved flag then;
  the condition client keeps the shared Maskinporten fake; Correspondence does not share the GUI.
- Later: task 18 put every seed number in `+47` form and added `reserved` and organisations' official addresses
  (the follow-up in section 6 of `TODO.md`); task 14 moved the contact-point lookups to production Profile.

Superseded statements:

- Headline findings and section 6.7, "Profile contact-point lookups ... belong in the Register component": superseded
  by task 14; production Profile (`components/profile/`) serves them, with production semantics (unit contact points
  are only addresses users registered).
- Section 6.8, the seed numbers (`87654321`, normalised to `+47` by the fake): no normalisation (task 6); the seed is on schema v3 with valid `+47` numbers and official organisation addresses (task 18).
- Section 6.11, compose: read the studioctl runtime component.
- "Not verified": the migrations ran on Postgres 18 and the claim and result path is exercised by the smoke test.

Research date 2026-10-20 (task 6 of [`../../TODO.md`](../../TODO.md)), written against the stack at commit `5449dfc`.
The question: vendor the production `Altinn/altinn-notifications` with fakes, or build a fake-only service, and
does Kafka decide? One report in this folder.

| Report | What it plans | Effort |
| --- | --- |
| [`notifications.md`](notifications.md) | Consumer contract (app-lib v1 and v2, Correspondence, Authentication, Access Management), how production processes an order, the build chain (verified with a scratch build and a scratch start of the host), three options, directive sites, fakes, Postgres, Dockerfile, studioctl wiring, test-data mapping, inspection API, smoke test list | 6 to 7 days |

## Headline findings

- **There is no Kafka.** The pinned repository has no occurrence of it. Queues are Azure Service Bus through Wolverine,
  reached from the orders API host through one registration call and three publisher classes. The Email and SMS
  hosts are separate processes behind the queue (their old repositories are archived and merged into the monorepo)
  and are not needed locally.
- **Recommendation: Option A.** Vendor `components/api` and `components/shared` with two `Program.cs` sites (one
  additive, one `#if !LOCALTEST`), capture fakes in place of the Service Bus publishers, every would-be e-mail and
  SMS recorded under `notifications/api/localtest/`, and the production status machinery running on the shared
  Postgres. A fake-only service (C) would re-implement about twenty endpoints with real semantics for four consumers;
  a broker stand-in with the Email and SMS hosts (B) costs four more containers for no visibility.
- **One platform piece is missing for person and organisation recipients:** the Profile contact-point lookups
  (four anonymous endpoints). They belong in the Register component, which already serves Profile's user endpoints.
  Direct e-mail and SMS recipients need nothing else.
  *2026-10-21: superseded by task 14: production Profile serves these lookups.*
- **One design constraint:** the claim loop publishes inside an open transaction that holds the row lock, so the
  capture fake must apply the send result after the commit, through an in-process channel.

## Not verified

No database was involved: the 104 migration folders, the claim and result path, and the responder timing are read, not
run. The host was built and started in a scratch directory with its database switched off. Nothing was built as an
image. Details in section 7 of the report.
