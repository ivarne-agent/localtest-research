# Arbeidsflate BFF: Application Insights to OpenTelemetry

Upstream: [Altinn/dialogporten-frontend](https://github.com/Altinn/dialogporten-frontend), vendored at `bb74dff2`
(`../../components/arbeidsflate/vendor/`, pristine; localtest changes are patches in `../../components/arbeidsflate/patches/`).
Reviewed 2026-10-26 (TODO.md task 53). **Classification: small upstream; not done in localtest** (needs a decision).

## Upstream today

- **OpenTelemetry only, no Application Insights in the BFF** (the frontend's browser analytics are out of scope).
  `packages/bff/src/config.ts`: `openTelemetry.enabled = !!OTEL_EXPORTER_OTLP_ENDPOINT`.
  `packages/bff/src/instrumentation.ts`: NodeSDK with HTTP, Fastify, GraphQL, ioredis and pg instrumentation, a ratio
  sampler (`OTEL_TRACES_SAMPLER_ARG`), a span-name filter, and the **gRPC** trace and metric exporters
  (`@opentelemetry/exporter-*-otlp-grpc`) with `url` = the endpoint. `OTEL_EXPORTER_OTLP_PROTOCOL` is read and logged
  but not used. Logs: `pino-opentelemetry-transport` (`packages/node-logger/src/index.ts`) next to the console when the
  endpoint is set. Resource: `service.name` `bff` (`resourceFromAttributes`).
- So it follows the rule for `grpc`, not for `http/protobuf`: traces and metrics would be sent as gRPC to an HTTP port.
- Not verified: whether `OTEL_SERVICE_NAME` wins over the resource attribute (NodeSDK merges the detected resource; the
  order decides), and which protocol the pino transport uses (it reads the OTLP environment variables itself).

## Upstream change

Pick the exporter by `OTEL_EXPORTER_OTLP_PROTOCOL`: the `-proto` (and `-http`) exporters next to the gRPC ones, or let
NodeSDK build the exporters from the environment and keep only the wrappers (`SafeSpanExporter`, the span-name filter).
Without `url`, the JS exporters read the endpoint from the environment and append `/v1/<signal>` for HTTP. Risk low;
`instrumentation.ts` and `package.json`/lockfile.

## Localtest (not done)

A localtest patch would add the proto exporters to `packages/bff/package.json`, which changes `pnpm-lock.yaml` (the
image installs with `--frozen-lockfile`), so it is more than a directive-sized change. The alternative is to give the
BFF `OTEL_EXPORTER_OTLP_PROTOCOL=grpc` and the collector's gRPC port, as TODO.md task 53 already notes for the stack.
Decision needed (it also decides task 52's studioctl variables):

1. **gRPC for the BFF** (no change here; the runner sets the BFF's endpoint to the collector's 4317), or
2. **a patch** `0004-bff-otlp-protocol.patch` choosing the exporter by protocol, with the lockfile change.

Proof once decided: the BFF image build and a smoke check that a request creates a `bff` span.
