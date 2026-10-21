# Vendored copy of Altinn/altinn-receipt

- Repository: https://github.com/Altinn/altinn-receipt
- Commit: [`babc0ed23483e8c3fb62dad4da903c6a88024914`](https://github.com/Altinn/altinn-receipt/tree/babc0ed23483e8c3fb62dad4da903c6a88024914) (main, 2026-10-20)
- Copied paths: the whole repository (the backend `src/backend/Altinn.Receipt`, the React frontend `src/frontend` with
  its committed Yarn release, `test/`, the root build and editor files, `.github/`, `.deploy/` and the upstream
  `Dockerfile` for reference). It is small, and the frontend and tests stay comparable with upstream that way.
- Copied with `git archive` of that commit, no subtree yet. The first commit (`receipt: vendor Altinn/altinn-receipt
  at babc0ed, unmodified`) holds the files unmodified, blob for blob; every later change inside this folder is a
  `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST … #endif` addition.
- Pin cadence (decided 2026-10-20): Renovate updates the frontend almost daily; re-pin on backend changes or on
  request only.

## Directive sites

Both in `src/backend/Altinn.Receipt/Program.cs`:

1. Lines 46-48, after `ConfigureServices(builder.Services, builder.Configuration);`, a pure addition: `#if LOCALTEST`
   `Altinn.Platform.Receipt.Localtest.LocaltestReceipt.Apply(builder.Services, builder.Configuration);` `#endif`
   (the hook into `../fakes/`).
2. Lines 259-265, in `Configure`, the status-code page's `goto` URL for a 401: `#if !LOCALTEST` the production
   `https://platform.{GeneralSettings:Hostname}{path}{query}` `#else` the request's own scheme and host `#endif`. There
   is no `platform.` host and no TLS locally, and the proxy keeps the browser's `Host`.

The frontend has no sites: it is built unchanged.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-receipt` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, three-way merges the file with `LOCALTEST` sites, adds and deletes
   files, and reports conflicts (the paths are every top-level entry of the new commit):
   `../../tools/repin-vendor.py <clone> babc0ed <new> vendor $(git -C <clone> ls-tree --name-only <new>)`.
   A top-level entry upstream deleted must be added to the list by hand.
2. Check the result: `git archive <new>` into an empty folder and `diff -rq` it against `vendor/`. Only `UPSTREAM.md`
   and `src/backend/Altinn.Receipt/Program.cs` may differ.
3. **Keep `../Dockerfile` as equal to `Dockerfile` as possible.** It mirrors the upstream file line for line (base
   images and digests, the Node bundle stage, the SDK publish stage, the chiseled runtime as `$APP_UID`), and every
   deviation is marked `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in
   `../Dockerfile`; the same for `.dockerignore`.
4. Fix the localtest code (`../fakes/`, the overrides) for upstream's changes, update the commit line above, build the
   image (`podman build -t localtest-receipt:dev ..`) and run `../smoke-test.sh`. Commit the re-pin of the vendored
   files on its own, separate from localtest changes.
