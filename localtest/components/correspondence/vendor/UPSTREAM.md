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

Three pure additions, all in `src/Altinn.Correspondence.API/Program.cs` (line numbers checked 2026-10-22), and two `#if !LOCALTEST` / `#else` / `#endif` blocks, in
`src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs` (task 7b) and
`src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs` (task 11b).

| Lines | Adds | Why there |
| --- | --- | --- |
| `Program.cs:44-49`, right after `var builder = WebApplication.CreateBuilder(args);` | the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`) | before every registration, so every factory client resolves service names |
| `Program.cs:56-58`, right after the `.AddEnvironmentVariables();` that ends the `builder.Configuration` chain | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(builder.Configuration);` `#endif` | `Program.cs:51-55` adds the JSON files and the environment variables a second time, so the localtest defaults have to be inserted after that chain (just below its environment source, above `appsettings.Development.json`) and before `ConfigureServices`, which reads the settings eagerly |
| `Program.cs:173-175`, right after `services.ConfigureHangfire(config);` in `ConfigureServices` | `#if LOCALTEST` `Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, config);` `#endif` | after every production registration (the last but Kestrel's), so `RemoveAll`/`Add` win, and after `ConfigureAuthentication`, so the JWT and OIDC `Configure` actions run after upstream's |
| `XacmlRequestFactory.cs:31-36`, the party-id branch of `CreateResourceCategory` | `#if !LOCALTEST` upstream's condition (`party.StartsWith(AltinnXacmlUrns.PartyId) \|\| partyWithoutPrefix.IsPartyId()`) `#else` the same `urn:altinn:partyid` prefix test, or a value that `long.TryParse` reads with `NumberStyles.None` and the invariant culture as a positive number `#endif` | Every PDP request names the party by its bare Register party id, and `IsPartyId` accepts only 8 or 10 digits, so the test data's 6-digit ids (Digdir 500006, Sophie Salt 501337, app-supplied 600000-700002) threw "RecipientId is not a valid organization number, person number or party id" (500). The branch comes after the organisation-number and national-identity-number branches, so 9- and valid 11-digit values are unchanged, and 8- and 10-digit ids pass as upstream. `IsPartyId` itself stays upstream's: `CreateDecisionRequest` and `WithUrnPrefix` use it to decide whether to resolve a party, and a looser rule there would change the production request shape (`planned-components/correspondence/party-ids.md`, option (b)). Delete it when upstream passes `urn:altinn:partyid:{id}` (option (c)) |
| `IdportenTokenValidator.cs:26-31`, the document retriever of `ValidateTokenAsync`'s `ConfigurationManager` | `#if !LOCALTEST` upstream's `new HttpDocumentRetriever());` `#else` a `// localtest:` comment and `new HttpDocumentRetriever { RequireHttps = false });` `#endif` | The browser download of an attachment (an inbox link, no token) ends in the cascade's OIDC branch, which validates the access token it cached at the callback against the discovery document and JWKS at `{Authority}/.well-known/openid-configuration`, Authentication's on `http://local.altinn.cloud:8000`. The retriever's `RequireHttps` defaults to true, so `GetConfigurationAsync` threw, the cascade fell through to a new challenge, and Authentication's live session answered with a new code at once: a redirect loop. The fakes' `RequireHttpsMetadata = false` does not reach this retriever (built here, not from the scheme's options), and the validator is a concrete class injected by type, so no fake can replace it. Reported first in `../../arbeidsflate/README.md`, "Correspondence attachments (11b)" |

Considered and not used (see the plan, section 6.2): the Redis connect (`Program.cs:144-152`, configuration only: the group's Redis), the
Development-only migrations (`Program.cs:80-89`) and the recurring jobs (`Program.cs:91`). For task 11b: the OIDC scheme's correlation
cookie (`DependencyInjection.cs:103-107`, `SecurePolicy=Always`, `SameSite=None`) and the nonce cookie, set in the fakes after
upstream's configuration instead (`../fakes/Auth/LocaltestAuthentication.cs`). For task 21b: the retry path's `ExpireCookie`
(`DependencyInjection.cs:140-152`, hard-coded `Secure`, `SameSite=None`), handled by the cookie policy in `../fakes/Cookies/` instead,
which covers every cookie written through `Response.Cookies` (none is written as a raw `Set-Cookie` header at this pin; grep for
`Set-Cookie` after a re-pin). A further site needs a report first.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-correspondence` with both commits, re-pin with the shared script, which takes upstream's new version of
   every file that is unchanged locally, three-way merges the three locally changed files (`Program.cs`, `XacmlRequestFactory.cs`, `IdportenTokenValidator.cs`), adds and deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src Dockerfile docker-compose.yml`.
2. Check the result: `git archive <new> src Dockerfile docker-compose.yml` into an empty folder and `diff -rq` it against `vendor/`.
   Only `UPSTREAM.md`, `src/Altinn.Correspondence.API/Program.cs`,
   `src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs` and
   `src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs` may differ.
   Check that the factory's `#if !LOCALTEST` branch is still upstream's party-id condition word for word; if
   upstream now passes `urn:altinn:partyid:{id}` from `AltinnAuthorizationService.CreateDecisionRequest` and
   `AltinnTokenXacmlMapper` (the issue drafted in `planned-components/correspondence/party-ids.md`, option (c)), delete the block
   and the smoke test's app-lib and recipient checks prove the 6-digit ids still pass. Check that the validator's `#if !LOCALTEST`
   branch is still upstream's retriever word for word; if upstream now builds it from the scheme's options (its
   `RequireHttpsMetadata`, which the fakes set to false) or reads `options.Configuration`'s discovery, delete the block and the smoke
   test's browser-download section proves the http metadata is still read. Check also that `ConfigureAuthentication` still names the
   correlation cookie and the callback path the fakes and the smoke test rely on.
3. **Keep `../Dockerfile` as equal to `Dockerfile` as possible.** It mirrors the upstream file (the six-project restore layer, publish,
   port 2525, the `dotnet` user, `/mnt/storage`), and every deviation is marked `localtest:`. When `git diff <pin> <new> -- Dockerfile`
   shows a change, make the same change in `../Dockerfile`; bump the two pinned base-image digests to what `10.0` points at then.
4. Fix the localtest code (`../fakes/`, the overrides) for upstream's changes: `IStorageRepository`, `IResourceManager`, the scheme names
   and `ConfigureAuthentication`, the configuration keys of `../fakes/LocaltestCorrespondence.cs`, the entities and the Hangfire schema
   the inspection API reads. Update the commit line above and the migration count the smoke test expects, build the image
   (`podman build -t localtest-correspondence:dev ..`) and run `../smoke-test.sh`. Commit the re-pin of the vendored files on its own,
   separate from localtest changes.
