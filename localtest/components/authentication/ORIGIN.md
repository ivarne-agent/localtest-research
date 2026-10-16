# Origin

Unmodified copy of `src/Runtime/localtest` from Altinn/altinn-studio at
[`447e53eb9797d4fffe63d0c61d05a1f692b4ec48`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest)
(main, 2026-10-16). `LocalTest.csproj` compiles shared sources by relative path from
`src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common/{HostBridge,EnvTopology}`; those are copied unmodified to
`../common/dotnet/Altinn.Studio.Runtime.Common/` so the pristine copy builds.

This component becomes the fake-only authentication service (login page, test-user picker, token issuing,
openid/jwks, test data). Storage, authorization, events, notifications and the proxy are trimmed in later commits
so each removal is a visible diff against this baseline.
