# Vendored copy of Altinn/altinn-register

```json vendor-sync
{
  "repository": "Altinn/altinn-register",
  "branch": "main",
  "commit": "f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0",
  "date": "2026-10-13",
  "paths": [
    "src/apps/Altinn.Register",
    "src/pkgs/Altinn.Register.Contracts",
    ".editorconfig",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "stylecop.json",
    "Dockerfile",
    ".dockerignore"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["src/apps/Altinn.Register/src/Altinn.Register/Altinn.Register.csproj"],
  "follow": [
    {"package": "Altinn.Common.AccessToken", "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"},
    {"package": "Asp.Versioning.Mvc.ApiExplorer", "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"},
    {"package": "Microsoft.Extensions.ServiceDiscovery", "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"},
    {"package": "OpenTelemetry.Extensions.Hosting", "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"},
    {"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"}
  ],
  "reminders": [
    "Register waits for altinn-auth#4056 (https://github.com/Altinn/altinn-auth/pull/4056), Register moving into Altinn/altinn-auth: check that PR at every sync; once it has landed, retarget with `sync register --repository Altinn/altinn-auth --paths ... --group altinn-auth`"
  ]
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-register
- Commit: [`f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) (main, 2026-10-13)
<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/apps/Altinn.Register/`, `src/pkgs/Altinn.Register.Contracts/`,
  and the root build files `.editorconfig`, `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, `stylecop.json`; plus the upstream `Dockerfile` and `.dockerignore`
  (added 2026-10-19, same commit) for reference.
- Copied with `rsync` of a clean checkout, no subtree yet; identical to `git archive` of the commit (checked
  2026-10-19). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs` | 249-251 | addition | `Altinn.Register.Localtest.LocaltestRegisterExtensions.AddLocaltestRegister(builder);` |
<!-- vendor-sync:end sites -->

One site, a pure addition in `src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs`:
`AddLocaltestRegister(builder)` after the production persistence block.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-register`, list what changed in the copied paths since the pin:
   `git diff --stat <pin> <new> -- src/apps/Altinn.Register src/pkgs/Altinn.Register.Contracts .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props global.json stylecop.json Dockerfile .dockerignore`.
   If Register has moved into `Altinn/altinn-auth` (altinn-auth#4056), re-vendor from there instead.
2. Copy the changed files over the vendored ones (`git archive <new> <paths> | tar -x`; delete files deleted
   upstream). The `LOCALTEST` site (`src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs`) needs a three-way
   merge: take the upstream file and put the directive block back unchanged.
3. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `RegisterHost.cs` may differ.
4. **Keep `../Dockerfile` as equal to `vendor/Dockerfile` as possible.** It mirrors the upstream file line for line
   (base images and digests, build and publish, tzdata, the `dotnet` user), and every deviation is a line or block
   marked `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in
   `../Dockerfile`.
5. Package versions in `../fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj` follow
   `Directory.Packages.props`; update them with it; the OpenTelemetry ones (task 53) follow what the host resolves
   (`obj/project.assets.json`: `OpenTelemetry.Extensions.Hosting` from `Directory.Packages.props`, the OTLP exporter from
   `Altinn.Authorization.ServiceDefaults`).
6. Update the commit line above, build the image (`podman build -t localtest-register:dev ..`) and run
   `../smoke-test.sh`. Commit the sync on its own, separate from localtest changes.
