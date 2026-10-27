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
    {"upstream": "src/apps/Altinn.Authentication/Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": ["src/apps/Altinn.Authentication/docker-bake.hcl"],
  "projects": ["src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication"],
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
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/AuthenticationHost.cs` | 293-295 | addition | `Altinn.Platform.Authentication.Localtest.LocaltestAuthenticationExtensions.AddLocaltest...` |
| `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs` | 1057-1070 | replacement | `if (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)` |
<!-- vendor-sync:end sites -->

Two sites, under `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`:

- `AuthenticationHost.cs`: a pure addition, `AddLocaltestAuthentication(builder)` right before `AddPersistanceLayer()`.
- `Controllers/AuthenticationController.cs`: `IsSafeSameOrSubdomainHttps`, whose `#else` also accepts http (the stack
  is plain http).

## Syncing to a newer upstream commit

Authentication, Authorization and Access Management vendor the same commit; re-pin all three together.
`../../tools/repin-vendor.py <clone> <pin> <new> vendor <paths>` does steps 1 and 2 (it three-way merges
the files with `LOCALTEST` sites and reports conflicts).

1. In a clone of `Altinn/altinn-auth`, list what changed in the copied paths since the pin. The build files to
   include are `.editorconfig`, `global.json` and the `Directory.Build.*` / `Directory.Packages.props` files that
   exist at `src/`, `src/apps/` and `src/pkgs/` (the repository root has none):
   `git diff --stat <pin> <new> -- src/apps/Altinn.Authentication src/pkgs/JWTCookieAuthentication <build files>`.
2. Copy the changed files over the vendored ones (`git archive <new> <paths> | tar -x`; delete files deleted upstream).
   Quote the paths: upstream has a file named `TokenClientAuthType .cs`, with a space, which an unquoted path list
   splits. The two `LOCALTEST` sites (`AuthenticationHost.cs`, `Controllers/AuthenticationController.cs`) need a
   three-way merge: take the upstream file and put the directive blocks back unchanged.
3. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and the two files with `LOCALTEST` sites may differ.
   Then grep the new tree for cookies written other than through `Response.Cookies` (a raw `Set-Cookie` header):
   `grep -rn 'Set-Cookie\|HeaderNames.SetCookie' src/apps/Altinn.Authentication/src`. The cookie policy in
   `../fakes/Cookies/` removes `Secure` on http only from cookies appended or deleted through `Response.Cookies`
   (`../../../reviews/cookies.md`, task 21b); none is written otherwise at this pin.
4. **Keep `../Dockerfile` as equal to `src/apps/Altinn.Authentication/Dockerfile` as possible.** It mirrors the upstream
   file line for line (base images and digests, publish, the `dotnet` user), and every deviation is a line or block
   marked `localtest:`. When `git diff <pin> <new> -- src/apps/Altinn.Authentication/Dockerfile` shows a change, make
   the same change in `../Dockerfile`. (`docker-bake.hcl` builds from the shared `containers/` files, but its default
   target list is empty, so the classic `Dockerfile` is the one to follow.)
5. Update the commit line above, build the image (`podman build -t localtest-authentication:dev ..`) and run
   `../smoke-test.sh`. Commit the sync on its own, separate from localtest changes.
