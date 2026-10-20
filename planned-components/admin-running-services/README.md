# Research: admin of running services and the inspection GUI

Research date 2026-10-20 (Europe/Oslo), task 17 of [`../../TODO.md`](../../TODO.md), written against
`Altinn/altinn-studio` `origin/main` at `9493f65300`, `altinn-studio-docs` at `dd8bba13`, and GitHub issues and pull
requests read that day. The question: does Altinn Studio's administration of running services (the Designer `admin`
frontend, `src/Designer/frontend/admin`, and its backend) overlap with the planned inspection GUI of this stack
(task 8, [`../inspection-gui/`](../inspection-gui/README.md)), and could it be pointed at the stack instead of building
a separate page? One report in this folder.

| Report | What it covers |
| --- | --- |
| [`admin-running-services.md`](admin-running-services.md) | The admin frontend's pages and roadmap, its data path (Designer endpoints, runtime gateway, Storage's `studio/instances`, Gitea teams, Maskinporten, Designer's platform token), what pointing it at the stack would take (three ways), whether running Designer locally makes sense for an app developer, the overlap matrix against the eight proposed GUI pages, recommendation, effort, risks, open questions |

## Headline findings

- **Different audiences, one shared page.** Admin is for service owners operating apps in tt02 and production
  (epic [altinn-studio#14876](https://github.com/Altinn/altinn-studio/issues/14876)): deployments, versions, metrics,
  alerts, instance lists and soft delete, workflow-engine health in flight. The inspection GUI is for app developers
  on their own machine: test users, events, notifications, dialogs, tokens, resets. Of the GUI's eight pages only
  **Instances** overlaps in substance, and **Apps** in name only.
- **Admin's data path is production infrastructure.** Designer calls the runtime gateway in each service owner's
  cluster with a Maskinporten token (Flux `HelmRelease`s for the app list, Azure Monitor and Kubernetes for metrics,
  Grafana for alerts), its own database and Gitea for app details, the app itself for metadata and process, and
  Storage's `storage/api/v1/studio/instances/` with Designer's `studio.designer` platform token. User rights come
  only from Gitea teams (`Admin-{env}`, `Owners`). Environments come from altinncdn.no with one global app-URL pattern.
- **The instance pages could run against the stack today.** The vendored Storage has the `studio/instances`
  controller, and `/Home/GetTestStudioToken` already mints the token it needs, for every org with one token. The
  app list, app details, metrics, alerts and the access check have no local source.
- **Running Designer locally is not an app developer's path.** Designer's compose is twelve services with its own
  Gitea login, org and teams; app developers use studioctl and never run Designer. Pointing admin at the stack would
  help Studio's own admin developers (real instances instead of the mock's 1000 random ones), not app developers.
- **A production alternative for the GUI's Storage gap:** the Studio token plus `studio/instances` lists any org's
  instances without the per-org token problem that task 8's plan weighed, but per app and without party, data values
  or instance events. Decision F (an anonymous `storage/api/localtest/instances`) still holds.

## Recommendation

Build task 8 as planned and do not point the admin frontend at the stack. Amend the Instances page to use admin's
vocabulary (status tags, filters) and its grouping of data elements by data type and task, so a developer sees the
same concepts locally and in admin, and keep adding what admin leaves out (party, data values, instance events, all
apps). No shared components now: they live in the Designer frontend workspace, which the GUI's no-build approach
cannot use; reconsider at task 13 when the stack moves into the monorepo. Offer squad Kjøring a recipe for running
Designer's compose against the stack for their own development, built only on request. Details and effort in section
5 of the report; six open questions in section 6.

## Not verified

Nothing was run: no Designer, admin frontend or stack call. That `studio/instances` answers in the stack with the
token from `/Home/GetTestStudioToken` is read from the vendored source and the smoke test's check of the token's
claim. Designer's signing-certificate setting was not looked up. Effort for the alternatives is estimated from
reading. The roadmap item and Confluence insight behind #14876 were not read, and the open workflow PRs only by title.
Details in section 7 of the report.

## Proposed brief

**Goal.** Task 8 as briefed in [`../inspection-gui/README.md`](../inspection-gui/README.md) (one GUI under
`/localtest/inspect/` over the components' `api/localtest/` endpoints, eight pages), no longer deferred: task 17 found
one overlapping page (Instances) and no way to reuse the admin frontend that serves app developers.

**Changes to the task 8 brief (proposed 2026-10-20).**

- Instances page: status tags and filters as the admin frontend's (`InstanceStatus`: unread, active, completed,
  confirmed, deleted; filters current task, completed, confirmed, deleted, created before, archive reference or
  instance id), applied in the browser or as query parameters of `storage/api/localtest/instances`.
- Instance detail: data elements grouped by data type with their task and kind (data model, PDF, signature, signee
  states, payment, attachment), derived as admin's `InstanceDataView` does from the app's metadata
  (`storage/api/v1/applications/{org}/{app}`) and process (`/{org}/{app}/api/v1/meta/process`); plus the GUI's own
  party, data values and instance events.
- Section 3.5 of the GUI plan records the production alternative (`storage/api/v1/studio/instances/{org}/{app}` with
  `/Home/GetTestStudioToken`) as a fallback if 8a slips; decision F unchanged.
- Workflow engine: link to its dashboard only; no health column.
- No change in the monorepo, no admin-frontend change, no Designer in the stack.

**Steps.** As the task 8 brief (8a Storage endpoint, 8b GUI, 8c follow-ups), with the two Instances items in 8b
(about 0.5 day more: 9 days for the first version).

**Open.** Questions 1 to 6 in section 6 of [`admin-running-services.md`](admin-running-services.md): un-deferring
task 8; Norwegian for the Instances page; a compose recipe for squad Kjøring; whether "one tool for local and
production" is a goal (decide at task 13); the Storage endpoint's shape; workflow health. Plus the GUI plan's own
questions 1 to 11.

**Verification.** As the task 8 brief, plus: the smoke section checks that the Instances page's filters map to fields
the Storage endpoint returns, and a manual pass compares one `ttd/testapp` instance's status tag and data-element
groups with what the admin frontend's logic would show for the same metadata and process.
