# The apps' Maskinporten clients on localtest-v2 (task 43, 2026-10-23)

Work in progress: the analysis is written, the proof on the stack follows.

## Decision (analysis, before the proof)

Both libraries decide in `RuntimeEnvironment.IsLocaltestPlatform()`, which is true only when
`GeneralSettings:HostName` (port stripped) is `local.altinn.cloud` or `altinn3local.no`. The runners set `HostName` to
the public host, so at the default origin the service-owner (`AltinnToken`) requests go to `/Home/GetTestOrgToken`; at
a custom host they go to the real `MaskinportenClient`, which has no client to use.

Proof at the default origin done (2026-10-23 21:30): both apps' resolver path gets `ttd` from `/Home/GetTestOrgToken`,
Storage 200; the real client with a hand-written client against the fake gets `digdir`, Storage 403; app-lib 8 does
not read studioctl's secrets directory. Custom origin next.
