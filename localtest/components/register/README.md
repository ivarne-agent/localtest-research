# register

Production Altinn Register (`vendor/`, see `UPSTREAM.md`) running on an in-memory register seeded from
`testdata/testData.json`.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except `#if LOCALTEST` blocks (`git diff` against the baseline commit shows them all) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, link the fakes project |
| `fakes/Altinn.Register.Localtest/` | In-memory `IPartyPersistence`, `IPartyExternalRolePersistence`, static role definitions, rate limiter, lenient platform-token handler, localtest-only controllers, DI composition |
| `testdata/testData.json` | Seed: persons, organisations (with `parentPartyId` for sub-units), `partyRoles` (CCR role codes or identifiers) and `accessPackages` |
| `Dockerfile` | Publishes the vendored host with the overrides |

Directive sites in `vendor/`: one, in `RegisterHost.cs` (`AddLocaltestRegister(builder)` after the production
persistence block). Everything else is replaced through DI or switched off by configuration
(`Altinn:IsTest=true` skips MassTransit, jobs, leases and CCR federation; `Altinn:register:ApiSource:Default=DB`
makes the v1 endpoints answer from persistence).

Localtest-only endpoints (anonymous): `GET register/api/localtest/test-users` and `GET register/api/localtest/organizations`,
used by the authentication fake's picker and the Access Management / PDP fakes (roles and access packages per party).

Run locally without Docker:

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5020 \
  Localtest__Register__TestDataPath=$PWD/testdata/testData.json \
  dotnet run --project vendor/src/apps/Altinn.Register/src/Altinn.Register
curl -H PlatformAccessToken:x -H Content-Type:application/json \
  -X POST 'http://127.0.0.1:5020/register/api/v1/parties/partylist?fetchSubUnits=true' -d '[500000]'
```
