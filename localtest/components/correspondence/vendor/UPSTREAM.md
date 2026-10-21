# Vendored copy of Altinn/altinn-correspondence

- Repository: https://github.com/Altinn/altinn-correspondence
- Commit: [`6dcf561945f73e8f28f0a95747ee4fafa012aa89`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89)
  (main, 2026-10-16, "attachment authorization context (#2150)"; the pin of the planning report,
  `planned-components/correspondence/correspondence.md`)
- Copied paths, relative layout preserved: `src/` (the six projects `Altinn.Correspondence.{API,Application,Common,Core,Integrations,Persistence}`
  with their `Properties/` and the 69 EF migrations), the root `Dockerfile` (the reference for `../Dockerfile`) and `docker-compose.yml`
  (reference only, nothing runs it). Upstream has no `Directory.Build.props`/`.targets`, no `Directory.Packages.props`, no `global.json`
  and no lock files. Not copied: `Test/`, `flux/`, the Postman collection, the READMEs, `start.bat`, `version.txt`, `renovate.json`, the
  solution file.
- Copied with `git archive` of that commit, no subtree. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST ... #endif` addition.

## Directive sites

Two pure additions, both in `src/Altinn.Correspondence.API/Program.cs`, and one `#if !LOCALTEST` / `#else` / `#endif` block in
`src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs` (task 7b).

| Lines | Adds | Why there |
| --- | --- | --- |
| `Program.cs:50-52`, right after the `.AddEnvironmentVariables();` that ends the `builder.Configuration` chain | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(builder.Configuration);` `#endif` | `Program.cs:45-49` adds the JSON files and the environment variables a second time, so the localtest defaults have to be inserted after that chain (just below its environment source, above `appsettings.Development.json`) and before `ConfigureServices`, which reads the settings eagerly |
| `Program.cs:167-169`, right after `services.ConfigureHangfire(config);` in `ConfigureServices` | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, config);` `#endif` | after every production registration (the last but Kestrel's), so `RemoveAll`/`Add` win, and after `ConfigureAuthentication`, so the JWT and OIDC `Configure` actions run after upstream's |
| `XacmlRequestFactory.cs:31-36`, the party-id branch of `CreateResourceCategory` | `#if !LOCALTEST` upstream's condition (`party.StartsWith(AltinnXacmlUrns.PartyId) \|\| partyWithoutPrefix.IsPartyId()`) `#else` the same `urn:altinn:partyid` prefix test, or a value that `long.TryParse` reads with `NumberStyles.None` and the invariant culture as a positive number `#endif` | Every PDP request names the party by its bare Register party id, and `IsPartyId` accepts only 8 or 10 digits, so the test data's 6-digit ids (Digdir 500006, Sophie Salt 501337, app-supplied 600000-700002) threw "RecipientId is not a valid organization number, person number or party id" (500). The branch comes after the organisation-number and national-identity-number branches, so 9- and valid 11-digit values are unchanged, and 8- and 10-digit ids pass as upstream. `IsPartyId` itself stays upstream's: `CreateDecisionRequest` and `WithUrnPrefix` use it to decide whether to resolve a party, and a looser rule there would change the production request shape (`planned-components/correspondence/party-ids.md`, option (b)). Delete it when upstream passes `urn:altinn:partyid:{id}` (option (c)) |

Considered and not used (see the plan, section 6.2): the Redis connect (`Program.cs:135-143`, configuration only: the group's Redis), the
Development-only migrations (`Program.cs:71`), the recurring jobs (`Program.cs:82`) and `API/Auth/IdportenTokenValidator.cs:26`
(`new HttpDocumentRetriever()`, only needed for the browser download of attachments from Arbeidsflate, task 11). A fourth site needs a
report first.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-correspondence` with both commits, re-pin with the shared script, which takes upstream's new version of
   every file that is unchanged locally, three-way merges the two locally changed files (`Program.cs`, `XacmlRequestFactory.cs`), adds and deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src Dockerfile docker-compose.yml`.
2. Check the result: `git archive <new> src Dockerfile docker-compose.yml` into an empty folder and `diff -rq` it against `vendor/`.
   Only `UPSTREAM.md`, `src/Altinn.Correspondence.API/Program.cs` and
   `src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs` may differ.
   Check that the factory's `#if !LOCALTEST` branch is still upstream's party-id condition word for word; if
   upstream now passes `urn:altinn:partyid:{id}` from `AltinnAuthorizationService.CreateDecisionRequest` and
   `AltinnTokenXacmlMapper` (the issue drafted in `planned-components/correspondence/party-ids.md`, option (c)), delete the block
   and the smoke test's app-lib and recipient checks prove the 6-digit ids still pass.
3. **Keep `../Dockerfile` as equal to `Dockerfile` as possible.** It mirrors the upstream file (the six-project restore layer, publish,
   port 2525, the `dotnet` user, `/mnt/storage`), and every deviation is marked `localtest:`. When `git diff <pin> <new> -- Dockerfile`
   shows a change, make the same change in `../Dockerfile`; bump the two pinned base-image digests to what `10.0` points at then.
4. Fix the localtest code (`../fakes/`, the overrides) for upstream's changes: `IStorageRepository`, `IResourceManager`, the scheme names
   and `ConfigureAuthentication`, the configuration keys of `../fakes/LocaltestCorrespondence.cs`, the entities and the Hangfire schema
   the inspection API reads. Update the commit line above and the migration count the smoke test expects, build the image
   (`podman build -t localtest-correspondence:dev ..`) and run `../smoke-test.sh`. Commit the re-pin of the vendored files on its own,
   separate from localtest changes.
