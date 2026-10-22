# Vendored copy of Altinn/dialogporten

- Repository: https://github.com/Altinn/dialogporten
- Commit: [`ec333e4b4742344dd303785f5fc15f0cb5d821f6`](https://github.com/Altinn/dialogporten/tree/ec333e4b4742344dd303785f5fc15f0cb5d821f6) (main, 2026-10-20; `origin/main` of the reference clone that day)
- Copied paths, relative layout preserved: `Directory.Build.props`, `.editorconfig`, `global.json`, and under `src/` the nine projects
  the WebApi, GraphQL and Service hosts build from: `Digdir.Domain.Dialogporten.{WebApi,GraphQL,Service,Application,Domain,Infrastructure}`,
  `Digdir.Library.Entity.Abstractions`, `Digdir.Library.Entity.EntityFrameworkCore`, `Digdir.Library.Utils.AspNet`, each with its
  `packages.lock.json` (upstream restores with `--locked-mode`; `../Dockerfile.*` do not since task 25, see `../localtest.build.targets`). The upstream per-host `Dockerfile`s and
  `Infrastructure/MigrationBundle.dockerfile` stay in their project folders as the reference for `../Dockerfile.*`.
  Not copied: the Janitor (no host needs it; its two Resource Registry syncs are MediatR commands the WebApi can send, task 9b), the
  samples, tools and `WebApiClient`, `tests/`, `docs/`, `scripts/`, the compose and nginx files, `.github/`, the solution files.
- Copied with `git archive` of that commit, no subtree. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST ... #endif` addition.

## Changes upstream since the planning report's pin (`0946da8`, 2026-10-16)

`0946da8` to `ec333e4`, 28 files: HotChocolate 16.6.7 to 16.6.8 (GraphQL and Infrastructure csproj, `Utils.AspNet`) with the matching lock-file
changes in every project, four GitHub Actions bumps and a manual E2E dispatch workflow, and, new since the report's `1a7c584` check, one source
change in the GraphQL host: `SearchDialog.Content` became nullable (`ObjectTypes.cs`, `Mapper.cs`: "May be null for API-only dialogs"), a schema
change. No change to a Program.cs, a Dockerfile, a migration or a configuration file. Every site and line the report names was re-located:
the three `WebApplication.CreateBuilder(args)` calls are at WebApi `Program.cs:62`, GraphQL `:44`, Service `:40`.

## Directive sites

Three pure additions, one per host, each right after `var builder = WebApplication.CreateBuilder(args);`:

| File | Adds |
| --- | --- |
| `src/Digdir.Domain.Dialogporten.WebApi/Program.cs:63-65` | `#if LOCALTEST` `Digdir.Domain.Dialogporten.WebApi.Localtest.LocaltestWebApi.Apply(builder);` `#endif` |
| `src/Digdir.Domain.Dialogporten.GraphQL/Program.cs:45-47` | the same with `GraphQL.Localtest.LocaltestGraphQl` |
| `src/Digdir.Domain.Dialogporten.Service/Program.cs:41-43` | the same with `Service.Localtest.LocaltestService` |

The hook goes there, and not before `Build()` as the planning report had it, because the hosts read their settings eagerly while
registering (the JWT schemes' metadata addresses, the health probes, the Infrastructure settings), so the configuration defaults must be in
before the first registration. The service replacements (the two `ITokenIssuerCache`s, `IMaskinportenService`) must come after the production
`AddSingleton`s, so the hook installs an `IServiceProviderFactory` (`fakes/Infrastructure/LocaltestOverrides.cs`) that applies them to the finished
service collection: one site per host instead of two.

One `#if !LOCALTEST` block, added with the Dialogporten adapter (task 10):

| File | Site |
| --- | --- |
| `src/Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs:29-34`, `:39-44` | Both `IsValidHttpsUrl` overloads (string and `Uri`) require `https`; `#else` accepts `http` too. The stack is plain http on `http://local.altinn.cloud:8000`, so every link the adapter puts in a dialog (GUI actions, API action endpoints, attachment and transmission URLs) is http, and Dialogporten refused the dialog with 400 `'Url' is not a well-formatted HTTPS URL.` The rule is a validator inside the Application project's FluentValidation rules, not reachable through DI, hence a directive. The HTML content rule (`<a href>` must start with `https://`) is unchanged |

## Syncing to a newer upstream commit

1. In a clone of `Altinn/dialogporten` with both commits, re-pin with the shared script, which takes upstream's new version of every file that is
   unchanged locally, three-way merges the three Program.cs files and `FluentValidationStringExtensions.cs`, adds and deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor Directory.Build.props .editorconfig global.json src/Digdir.Domain.Dialogporten.WebApi ... `
   (every path of the list above).
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only `UPSTREAM.md`, the three
   `Program.cs` files and `FluentValidationStringExtensions.cs` may differ.
3. **Keep `../Dockerfile.webapi`, `.graphql` and `.service` as equal to upstream's three per-host Dockerfiles as possible.** When
   `git diff <pin> <new> -- src/Digdir.Domain.Dialogporten.*/Dockerfile` shows a change (a base image digest, a step), make the same change in all three.
4. Fix the fakes (`../fakes/`) for upstream's changes (a new `LocalDevelopment` flag goes into `LocaltestConfiguration`; the two
   `TokenIssuerCache.cs` copies follow upstream's), update the commit line above, build the three images
   (`podman build -f Dockerfile.<host> -t localtest-dialogporten-<host>:dev ..`) and run `../smoke-test.sh`. Commit the re-pin of the vendored
   files on its own, separate from localtest changes.
