# Plan: Notifications in the new localtest

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
- **One design constraint:** the claim loop publishes inside an open transaction that holds the row lock, so the
  capture fake must apply the send result after the commit, through an in-process channel.

## Not verified

No database was involved: the 104 migration folders, the claim and result path, and the responder timing are read, not
run. The host was built and started in a scratch directory with its database switched off. Nothing was built as an
image. Details in section 7 of the report.
