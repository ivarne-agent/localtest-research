# Events, Notifications, Correspondence, Dialogporten and the inspection GUI: recommendation

Research date 2026-10-18. This combines five reports written the same day and reconciles where they overlap:

| Report | Verdict | Effort |
| --- | --- | --- |
| [`events-vendoring.md`](events-vendoring.md) (built 2026-10-19, in-memory database) | Vendor production Events. Service Bus off through Wolverine flags, 3 directive sites, webhooks inside the stack delivered, external ones captured | 3–4 days |
| [`notifications-vendoring.md`](notifications-vendoring.md) | Vendor the API, e-mail and SMS hosts as one component. Service Bus replaced by Wolverine's Postgres transport (spike first), capture sinks at the e-mail and SMS provider interfaces | 6–9 days |
| [`correspondence-vendoring.md`](correspondence-vendoring.md) | Vendor production Correspondence. Upstream's own Dev clients for absent neighbours, 2 directive sites | 2–3 days, plus resources |
| [`dialogporten-arbeidsflate.md`](dialogporten-arbeidsflate.md) | Include Dialogporten and the Altinn adapter, then Arbeidsflate as an opt-in profile. Arbeidsflate already logs in through Altinn Authentication's OIDC endpoints | about 3–4 weeks with prerequisites |
| [`inspection-gui.md`](inspection-gui.md) | A static site served by the ingress that reads each component's capture API and stream; the login picker stays in authentication, the start page moves to the GUI | 3–5 days |

The 16 October report recommended stubs for Events and Notifications. That verdict is reversed: the blockers it named
(Service Bus with its MS SQL emulator, Key Vault tokens, missing Register and Profile endpoints, no e-mail or SMS sink)
are now either gone upstream or handled the way Storage and Access Management already handle them.

## How the pieces fit

- **Apps publish events** to Events (`POST events/api/v1/app`). Today that is a 404, and the workflow engine throttles
  the app's namespace for an hour after repeated failures. This is why process-transition-test's `workflow-status.ts`
  fails.
- **Apps order notifications** from Notifications (`POST notifications/api/v1/future/orders`), with only the app's
  platform access token. Today's localtest has no such endpoint, so every instantiation notification fails.
- **Apps send correspondence** through Correspondence (signing's call to sign and receipt). Correspondence orders its
  own notifications from Notifications, publishes events to Events, and creates dialogs in Dialogporten.
- **App instances become dialogs** through Storage's outbox and the Altinn Dialogporten adapter, not through the app.
  The adapter reads the instance back and calls Dialogporten.
- **End users see dialogs** in Arbeidsflate, which reads Dialogporten.
- **Testers see everything that left the platform** in the GUI: e-mail and SMS captured at Notifications' provider
  clients, events and webhook deliveries in Events, correspondences in Correspondence, dialogs in Arbeidsflate.

## Shared prerequisites

Several strands need the same things. Build each once.

| # | What | Needed by | Recommendation |
| --- | --- | --- | --- |
| S1 | **Ingress routes** for `/events/`, `/notifications/`, `/correspondence/`, `/dialogporten/`, `/localtest/` | all | One line each in the ingress template |
| S2 | **Profile contact-point lookups** (`users/contactpoint/lookup`, `lookupsi`, `units/contactpoint/lookup`, `organizations/notificationaddresses/lookup`) in the register component's Profile fakes, using each person's `mobileNumber` | Notifications | Anonymous, production shapes |
| S3 | **Maskinporten for service-to-service calls**: a token endpoint in the authentication component (`localtest/maskinporten/token`) and one shared `IMaskinportenService` replacement compiled into each component that needs it | Correspondence, Dialogporten, adapter, Arbeidsflate's BFF | One implementation in a shared fakes folder. The Correspondence report's per-component Maskinporten fake becomes this shared one |
| S4 | **Resource Registry over HTTP**: resources, resource list, the subject-resource feed, policies | Correspondence (its message resources), Dialogporten (service resources, search authorization), Arbeidsflate, Events generic events; Access Management already carries an in-process stand-in | **Decision needed** (question 1). Recommended: vendor the production Resource Registry. It already derives app resources from Storage's application list, as the stand-in imitates, so the stand-in in Access Management can then be deleted. A spike must first confirm how app subject mappings reach it. Correspondence and other non-app resources come as committed resource folders, pushed at start |
| S5 | **Redis** | Dialogporten (required), Arbeidsflate's BFF sessions, Correspondence (a hard-wired connection) | A `redis:7-alpine` container, added with Correspondence. Correspondence then runs unchanged on it, one directive fewer than its report proposes |
| S6 | **Postgres 18** | Dialogporten (a migration uses `uuidv7()`); Notifications tests upstream on 18 | Bump the shared container to 18 if Storage, Access Management, Register and the workflow engine migrate cleanly on it (a short test). Otherwise a second Postgres for Dialogporten |
| S7 | **Service-owner names** (`altinncdn.no/orgs/altinn-orgs.json`) | adapter, Dialogporten, signing receipts | A static copy served by the ingress |

## Recommended order

1. **Events and the GUI skeleton.** Smallest and highest value: it fixes `workflow-status.ts`, and it sets the pattern
   for every localtest capture API. The GUI starts with the start page, test data, tokens and events.
2. **S2, then Notifications.** The one-day transport spike comes first. If Wolverine's Postgres transport does not hold,
   fall back to RabbitMQ (one more container) or the report's Plan B (API host only). The GUI gains e-mail and SMS.
3. **S3, S4, S5, then Correspondence.** Real Events and Notifications clients from the start; Dialogporten stays on
   upstream's Dev client until Dialogporten runs. Signing's Cypress spec can then stop tolerating
   "varsling mislyktes".
4. **S6, S7, then Dialogporten, the adapter and Storage's outbox.** App instances and correspondences become dialogs.
5. **Authentication's OIDC client stores, then Arbeidsflate** behind an opt-in `inbox` compose profile, since it adds
   about seven containers and 1.5–2.5 GB of memory (the sandbox this was verified on has 7 GB in total).

Rough total: 6–8 weeks of focused work for all of it; steps 1 and 2 alone (about two weeks) remove today's two app-facing
404s.

## Cross-cutting findings

- **A date-bound risk in Events**: its `trace_log` table has partitions for 2025 and 2026 only. Unless upstream adds a
  2027 partition before 1 January, saving a webhook response fails. The Events plan adds a startup step that creates
  the partition locally; the Events team should hear about it now.
- **Production-internal polling stays.** Correspondence polls Notifications for shipment status, and Hangfire polls its
  own queue. That is production behaviour, not localtest wiring, and the "no polling between components" principle is
  about the latter. Recommended: accept it (question 4).
- **Test-user token scopes.** The picker's tokens carry `altinn:portal/enduser` but not `altinn:correspondence.read` or
  `digdir:dialogporten`. Testers who want the production recipient APIs need those scopes, which in production come
  from end-user systems through `exchange/id-porten`. The GUI's capture APIs do not need them.
- **Upstream asks** that would shrink the local changes: a pluggable transport or local mode in Notifications,
  recording mock clients in Notifications, the 2027 partition in Events, and two small Arbeidsflate changes (scheme in
  `OIDC_PLATFORM_URL`, a `local.altinn.cloud` host mapping).

## Open questions for the product owner

Collected from the five reports, merged where they overlap.

1. **Resource Registry**: vendor the production service (recommended, after a spike) or grow Access Management's
   stand-in into an HTTP fake? And for Correspondence resources: committed folders, generated by the app runner from
   each app's BPMN, or both?
2. **Capture ownership**: each component keeps its captures and serves list, detail, stream and reset; the GUI only
   reads (recommended by all reports). Agreed?
3. **External endpoints**: webhooks to endpoints outside the stack and all e-mail and SMS are captured with a synthetic
   success. Should testers be able to simulate failures (the Notifications report proposes per-recipient rules), and
   should any real delivery be possible behind an opt-in?
4. **Timing**: keep production's waits (SMS only 09–17, apps' 5-minute delay on instantiation notifications, retry
   delays up to 12 hours in Events, Correspondence polling Notifications after 5 minutes), shorten them locally, or let
   the GUI release a waiting order early?
5. **Fake transport**: is Wolverine on Postgres acceptable as "fake Azure" for Notifications, and is RabbitMQ an
   acceptable fallback?
6. **Postgres 18** for the shared container, or a separate one for Dialogporten?
7. **Opt-in inbox profile** for Dialogporten, the adapter and Arbeidsflate, and the host name
   `af.local.altinn.cloud:8000` for Arbeidsflate?
8. **Token scopes**: should the picker issue tokens that can call the production recipient APIs
   (`altinn:correspondence.read`, `digdir:dialogporten`)?
9. **Subscriptions**: only v8 apps with eFormidling subscribe to events, and they need an app-side Maskinporten token.
   In scope?
10. **Persistence and reset**: should captured messages survive `docker compose down`, and is reset global or per app?
11. **Start page and GUI form**: move the start page to the GUI, and serve the GUI as a static site from the ingress
    (both recommended)?
