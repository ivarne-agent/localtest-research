# Vendored copy of Altinn/dialogporten

```json vendor-sync
{
  "repository": "Altinn/dialogporten",
  "branch": "main",
  "commit": "ec333e4b4742344dd303785f5fc15f0cb5d821f6",
  "date": "2026-10-20",
  "paths": [
    "Directory.Build.props",
    ".editorconfig",
    "global.json",
    "src/Digdir.Domain.Dialogporten.WebApi",
    "src/Digdir.Domain.Dialogporten.GraphQL",
    "src/Digdir.Domain.Dialogporten.Service",
    "src/Digdir.Domain.Dialogporten.Application",
    "src/Digdir.Domain.Dialogporten.Domain",
    "src/Digdir.Domain.Dialogporten.Infrastructure",
    "src/Digdir.Library.Entity.Abstractions",
    "src/Digdir.Library.Entity.EntityFrameworkCore",
    "src/Digdir.Library.Utils.AspNet"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    {
      "upstream": "src/Digdir.Domain.Dialogporten.WebApi/Dockerfile",
      "ours": "Dockerfile.webapi",
      "merge": true
    },
    {
      "upstream": "src/Digdir.Domain.Dialogporten.GraphQL/Dockerfile",
      "ours": "Dockerfile.graphql",
      "merge": true
    },
    {
      "upstream": "src/Digdir.Domain.Dialogporten.Service/Dockerfile",
      "ours": "Dockerfile.service",
      "merge": true
    }
  ],
  "watch": [
    "src/Digdir.Domain.Dialogporten.Infrastructure/MigrationBundle.dockerfile"
  ],
  "projects": [
    {
      "path": "src/Digdir.Domain.Dialogporten.WebApi",
      "configuration": "Debug"
    },
    "src/Digdir.Domain.Dialogporten.GraphQL",
    "src/Digdir.Domain.Dialogporten.Service"
  ],
  "follow": [],
  "reminders": [
    "the host builds rewrite each host's `packages.lock.json`: `git checkout -- localtest/components/dialogporten/vendor` before `check`",
    "a new `LocalDevelopment` flag goes into `fakes/` `LocaltestConfiguration`"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/dialogporten
- Commit: [`ec333e4b4742344dd303785f5fc15f0cb5d821f6`](https://github.com/Altinn/dialogporten/tree/ec333e4b4742344dd303785f5fc15f0cb5d821f6) (main, 2026-10-20)

<!-- vendor-sync:end pin -->
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
the three `WebApplication.CreateBuilder(args)` calls are at WebApi `Program.cs:62`, GraphQL `:44`, Service `:40` (on 2026-10-20).

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                                                | Lines   | Kind        | First line of the `LOCALTEST` branch                                                         |
| ------------------------------------------------------------------------------------------------------------------- | ------- | ----------- | -------------------------------------------------------------------------------------------- |
| `src/Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs` | 29-34   | replacement | `.Must(x => x is null \|\| (Uri.TryCreate(x, UriKind.Absolute, out var uri) && (uri....`     |
| `src/Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs` | 39-44   | replacement | `.Must(x => x is null \|\| (x.IsAbsoluteUri && (x.Scheme == Uri.UriSchemeHttps \|\| x....`   |
| `src/Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/TokenIssuerCache.cs`                                  | 53-58   | replacement | `schema.WellKnown, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever ...` |
| `src/Digdir.Domain.Dialogporten.GraphQL/Program.cs`                                                                 | 45-51   | addition    | `Digdir.Domain.Dialogporten.GraphQL.Localtest.LocaltestGraphQl.Apply(builder);`              |
| `src/Digdir.Domain.Dialogporten.Service/Program.cs`                                                                 | 41-47   | addition    | `Digdir.Domain.Dialogporten.Service.Localtest.LocaltestService.Apply(builder);`              |
| `src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs`                                   | 61-66   | replacement | `schema.WellKnown, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever ...` |
| `src/Digdir.Domain.Dialogporten.WebApi/Program.cs`                                                                  | 63-69   | addition    | `Digdir.Domain.Dialogporten.WebApi.Localtest.LocaltestWebApi.Apply(builder);`                |
| `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs`                                                        | 106-112 | replacement | `// localtest: endpoint and protocol from the standard variables (TODO.md task 53), so that` |
| `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs`                                                        | 134-139 | replacement | `// localtest: from the standard variables, so that http/protobuf posts to ...`              |

<!-- vendor-sync:end sites -->

Three pure additions, one per host, each right after `var builder = WebApplication.CreateBuilder(args);`. Each block also holds the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`), after the `Apply` call; the package is added in `../localtest.build.targets`,
which turns the locked restore off (the three Dockerfiles drop `--locked-mode` for the same reason):

| File | Adds |
| --- | --- |
| `src/Digdir.Domain.Dialogporten.WebApi/Program.cs` | `#if LOCALTEST` `Digdir.Domain.Dialogporten.WebApi.Localtest.LocaltestWebApi.Apply(builder);` and the service-discovery registration `#endif` |
| `src/Digdir.Domain.Dialogporten.GraphQL/Program.cs` | the same with `GraphQL.Localtest.LocaltestGraphQl` |
| `src/Digdir.Domain.Dialogporten.Service/Program.cs` | the same with `Service.Localtest.LocaltestService` |

The hook goes there, and not before `Build()` as the planning report had it, because the hosts read their settings eagerly while
registering (the JWT schemes' metadata addresses, the health probes, the Infrastructure settings), so the configuration defaults must be in
before the first registration. The service replacements (`IMaskinportenService`, the prefix routing) must come after the production
`AddSingleton`s, so the hook installs an `IServiceProviderFactory` (`fakes/Infrastructure/LocaltestOverrides.cs`) that applies them to the finished
service collection: one site per host instead of two.

Three `#if !LOCALTEST` sites, the first added with the Dialogporten adapter (task 10), the second with task 53, the third
with task 63:

| File | Site |
| --- | --- |
| `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs` (two blocks) | The trace and metric `AddOtlpExporter` options of `AddDialogportenTelemetry`: `#if !LOCALTEST` upstream's `options.Endpoint = endpoint; options.Protocol = otlpProtocol;` `#else` nothing, so the exporter reads `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL` itself. An endpoint set in code gets no signal path, so with `http/protobuf` upstream posts traces and metrics to the bare endpoint, where a collector answers 404; from the variable the SDK appends `/v1/traces` and `/v1/metrics`. `grpc` is unchanged. The Serilog log sinks and the "protocol must be set" checks are unchanged (TODO.md task 53) |
| `src/Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs` (two blocks) | Both `IsValidHttpsUrl` overloads (string and `Uri`) require `https`; `#else` accepts `http` too. The stack is plain http on `http://local.altinn.cloud:8000`, so every link the adapter puts in a dialog (GUI actions, API action endpoints, attachment and transmission URLs) is http, and Dialogporten refused the dialog with 400 `'Url' is not a well-formatted HTTPS URL.` The rule is a validator inside the Application project's FluentValidation rules, not reachable through DI, hence a directive. The HTML content rule (`<a href>` must start with `https://`) is unchanged |
| `src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs` and `src/Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/TokenIssuerCache.cs` (one block each) | The `ConfigurationManager` that reads each JWT scheme's issuer: `#else` passes `new HttpDocumentRetriever { RequireHttps = false }`, because the stack's Authentication, Maskinporten and ID-porten publish their metadata over http. Replaced the two `LocaltestTokenIssuerCache` copies in `fakes/` (task 63) |

## Syncing

`python3 localtest/tools/vendor-sync.py sync dialogporten` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile.webapi`, `.graphql` and `.service` mirror upstream's three per-host Dockerfiles, and every deviation is
marked `localtest:`; the tool merges upstream's changes into each when that is clean, and otherwise prints the hunk (a
`localtest:` comment next to a changed `FROM` line conflicts). `Infrastructure/MigrationBundle.dockerfile` is watched.

What the localtest code follows: a new `LocalDevelopment` flag goes into `LocaltestConfiguration`. The host builds rewrite each host's `packages.lock.json`;
restore them before `check`.
