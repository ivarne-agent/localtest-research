# Localtest inspection GUI: design

Research date 2026-10-18. Builds on the four strand reports from the same day:
[`events-vendoring.md`](events-vendoring.md), [`notifications-vendoring.md`](notifications-vendoring.md),
[`correspondence-vendoring.md`](correspondence-vendoring.md) and
[`dialogporten-arbeidsflate.md`](dialogporten-arbeidsflate.md). Nothing here is built.

## Goal

Product owner, 2026-10-18: external communication is replaced by a local web page and APIs where testers can see every
SMS, e-mail and event that was published. A component shows a GUI for this inspection, and possibly the login page.

## Summary

- **The GUI owns no data.** Each vendored component captures what it would have sent out and serves it through a small,
  anonymous, read-mostly API under `{component}/api/localtest/`: list, detail, a server-sent event stream, and reset.
  All four strands arrived at this shape independently. The GUI is a browser page that reads those APIs through the
  ingress and listens on their streams, so nothing polls and no component depends on the GUI.
- **The GUI is a static site served by the ingress** (`/localtest/`), not a new service. It needs no server code: every
  call is same-origin through `local.altinn.cloud:8000`, and statuses always come from the production tables behind
  each component's API.
- **The login picker stays in the authentication component.** It is part of the production sign-in flow (the fake
  upstream identity provider behind Authentication's OIDC endpoints, and the target Arbeidsflate's login will use). The
  start page at `/`, which lists test users and posts to `/Home/LogInTestUser`, moves to the GUI. The POST endpoint stays
  in authentication, because it runs the production sign-in and sets the cookies.

## What the GUI shows

| Page | Source API (through the ingress) | Notes |
| --- | --- | --- |
| Start: sign in as a test user, pick an app | `register/api/localtest/test-users`, `register/api/localtest/apps`; form posts to `/Home/LogInTestUser` (authentication) | Replaces today's start page. Keeps the old form contract that Cypress and developers use. Authentication level selector as today |
| E-mail and SMS | `notifications/api/localtest/v1/messages` (+ `/{id}`, `/stream`), `orders` for scheduled ones | Rendered HTML e-mail in a sandboxed iframe, plain-text view, SMS as a phone-sized bubble. Filter by recipient, app, instance (sender's reference), status. Shows "scheduled for 10:42" for orders waiting out the app's 5-minute delay |
| Events | `events/api/localtest/events` (+ `/{id}`, `deliveries`, `subscriptions`, `stream`) | CloudEvent JSON, lifecycle from `trace_log`, each webhook delivery with request and response, captured or delivered |
| Messages in the inbox (Correspondence) | `correspondence/api/localtest/correspondences` (+ `/{id}`, attachment content, `stream`) | Content as markdown, status history, the notification order it placed (joined to the Notifications page on order id), pending background jobs that explain why a message is not published |
| Dialogs | Arbeidsflate at `af.local.altinn.cloud:8000` when the inbox profile runs; otherwise Dialogporten's end-user API | Link out to Arbeidsflate rather than reimplementing it (see the Dialogporten report, section 4.8, for the fallback page) |
| Instance timeline | Joins the pages above on the instance: events by `resourceinstance`, notifications by sender's reference `app-{partyId}/{guid}`, correspondences by sender's reference | The view a tester most often wants: "what did this instance send?" |
| Test data | `register/api/localtest/test-users`, `organizations`, `apps` | Users, parties, roles, access packages, which app posted which `testData.json`, load warnings and conflicts |
| Tokens | `/Home/GetTestUserToken`, `GetTestOrgToken`, `GetTestStudioToken` | Copy buttons, decoded claims. Parity with today's Tokens page |
| Reset | `DELETE` on each component's localtest API | One button per component and one for all |

Not carried over from today's localtest, by choice: the Storage explorer (pgAdmin or Storage's production APIs cover
it), the Tenor users page (Tenor users come from apps' `testData.json` now), and the frontend version switch, which
needs a decision (open question 3).

## Contracts the GUI relies on

The four reports propose these; the GUI needs only the common shape:

| Component | Base | List | Detail | Stream | Reset |
| --- | --- | --- | --- | --- | --- |
| Events | `events/api/localtest/` | `GET events?after=&org=&app=&type=&instance=` | `GET events/{id}` | `GET stream` | `DELETE events` |
| Notifications | `notifications/api/localtest/v1/` | `GET messages?channel=&recipient=&app=&sendersReference=` | `GET messages/{notificationId}` | `GET messages/stream` | `DELETE messages` |
| Correspondence | `correspondence/api/localtest/` | `GET correspondences?since=&resourceId=&recipient=` | `GET correspondences/{id}` | `GET stream` | not proposed yet; add `DELETE` |
| Register | `register/api/localtest/` | `GET test-users`, `organizations`, `apps` | | | `POST apps/refresh` |

Two things to align before building, so the GUI stays simple:

1. **One paging and filter vocabulary**: `after` cursor, `limit`, and the same filter names for `org`, `app`,
   `instance` and `sendersReference` in all three communication APIs.
2. **One stream event shape**: `{ "component": "events", "kind": "created|updated", "id": "...", "at": "..." }`. The GUI
   then refetches the detail it shows.

Each component keeps its capture inside its own database (the Notifications report proposes a `localtest` schema in
`notificationsdb`; Events a `events_localtest` table), so a component's data and its captures reset together and the
GUI never stores anything.

## Build

- Plain HTML, CSS and a small amount of TypeScript compiled to one bundle, or no build at all. No framework is needed
  for lists, detail panes and `EventSource`.
- Served as static files by the ingress at `/localtest/` (an nginx `location` and a folder in the image). The start
  page at `/` becomes this site's index; `location = /` moves from authentication to the GUI files.
- The site degrades per component: a page whose API answers 404 says the component is not running, so the GUI works
  with any subset of the stack (for example without the opt-in inbox profile).

Effort: 3 to 5 days once two components expose their APIs. It can start with Register and Events, the first
communication component in the recommended order.

## Open questions for the product owner

1. **Start page ownership**: move the user-list start page at `/` to the GUI (proposed), or keep it in authentication
   and add the GUI beside it?
2. **Static site or service**: is a static site served by the ingress acceptable, or should the GUI be its own container
   so it can grow server-side features (for example joining across components on the server)?
3. **Frontend version switch**: today's localtest lets a developer point apps at another app-frontend build through a
   cookie. Needed in the new stack? If so it belongs in the GUI plus an app-side setting, since the ingress already
   serves `/app-frontend/` from a configurable folder.
4. **Instance timeline**: is the cross-component "what did this instance send" view the main use, worth building first?
