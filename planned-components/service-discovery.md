# Service discovery for the components

## Status (2026-10-22)

- **In progress** (task 25): can every vendored component resolve the other services by name (`http://storage/`,
  `http://authentication/`, ...) through `Microsoft.Extensions.ServiceDiscovery`, so the runtime injects
  `services__<name>__http__0` instead of a hand-written URL per setting.
- Written against this repository at `7583be9b` (main), branch `research/service-discovery`.
- Sections follow as the work lands.
