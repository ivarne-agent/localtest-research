# Vendored copy of Altinn/altinn-auth (Authentication)

```json vendor-sync
{
  "repository": "Altinn/altinn-auth",
  "branch": "main",
  "commit": "3d51810374b255aeaa179993e53864d122586a65",
  "date": "2026-10-19",
  "paths": [
    ".editorconfig",
    "global.json",
    "src/Directory.Build.props",
    "src/Directory.Build.targets",
    "src/Directory.Packages.props",
    "src/apps/Altinn.Authentication",
    "src/apps/Directory.Build.props",
    "src/apps/Directory.Build.targets",
    "src/pkgs/JWTCookieAuthentication",
    "src/pkgs/Directory.Build.props",
    "src/pkgs/Directory.Build.targets",
    "src/pkgs/Directory.Packages.props"
  ],
  "group": "altinn-auth",
  "local": "directives",
  "mirrors": [
    {
      "upstream": "src/apps/Altinn.Authentication/Dockerfile",
      "ours": "Dockerfile",
      "merge": true
    }
  ],
  "watch": ["src/apps/Altinn.Authentication/docker-bake.hcl"],
  "projects": [
    "src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication"
  ],
  "follow": [],
  "reminders": [
    "grep the new tree for cookies written other than through `Response.Cookies`: `grep -rn 'Set-Cookie\\|HeaderNames.SetCookie' vendor/src/apps/Altinn.Authentication/src` must find nothing (the cookie policy in `fakes/Cookies/` sees only `Response.Cookies`; `reviews/cookies.md`)"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`3d51810374b255aeaa179993e53864d122586a65`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) (main, 2026-10-19)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/apps/Altinn.Authentication/`, `src/pkgs/JWTCookieAuthentication/`
  (referenced by relative path from the host csproj), and the build files on the path from the repository root:
  `.editorconfig`, `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`
  at the root, `src/`, `src/apps/` and `src/pkgs/` levels where they exist.
- Copied with `rsync` of a clean checkout, no subtree yet; identical to `git archive` of the commit (checked
  2026-10-19). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                                        | Lines     | Kind        | First line of the `LOCALTEST` branch                                                        |
| ----------------------------------------------------------------------------------------------------------- | --------- | ----------- | ------------------------------------------------------------------------------------------- |
| `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/AuthenticationHost.cs`                   | 293-295   | addition    | `Altinn.Platform.Authentication.Localtest.LocaltestAuthenticationExtensions....`            |
| `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs` | 1057-1070 | replacement | `if (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)` |

<!-- vendor-sync:end sites -->

Two sites, under `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`:

- `AuthenticationHost.cs`: a pure addition, `AddLocaltestAuthentication(builder)` right before `AddPersistanceLayer()`.
- `Controllers/AuthenticationController.cs`: `IsSafeSameOrSubdomainHttps`, whose `#else` also accepts http (the stack
  is plain http).

## Syncing

Run it for the group: Authentication, Authorization and Access Management vendor one `Altinn/altinn-auth` commit and
share libraries (`Api.Contracts`, `ABAC`, `PEP`): they are the group `altinn-auth`, which the tool moves as one (`sync
altinn-auth`, or any of the three). `python3 localtest/tools/vendor-sync.py sync altinn-auth` from the repository root
stages the sync and prints what to triage, build and verify; the agent finishes it by hand as
[`docs/vendor-sync.md`](../../../docs/vendor-sync.md) describes (resolve, `check`, build, verify, two commits). Without
`--to` it takes the head of `main`. The metadata block above drives it: the copied `paths`, the `mirrors` it merges or
reports, the `watch`ed paths, the `projects` to build, the package versions that `follow` the host, and the `reminders`
it prints.

`../Dockerfile` mirrors upstream's `src/apps/Altinn.Authentication/Dockerfile` (base images and digests, publish, the
`dotnet` user), and every deviation is marked `localtest:`; the tool three-way merges upstream's changes into it when
that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a different build.
`docker-bake.hcl` builds from the shared `containers/` files, but its default target list is empty, so the classic
`Dockerfile` is the one to follow; the tool reports a change to the bake file (`watch`).

The cookie policy in `../fakes/Cookies/` removes `Secure` on http only from cookies appended or deleted through
`Response.Cookies` (`../../../reviews/cookies.md`, task 21b); none is written otherwise at this pin, and a reminder
greps the new tree for a raw `Set-Cookie` header at every sync.
