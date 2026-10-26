# Arbeidsflate BFF: Application Insights to OpenTelemetry

Upstream: [Altinn/dialogporten-frontend](https://github.com/Altinn/dialogporten-frontend), vendored at `bb74dff2`
(`../../components/arbeidsflate/vendor/`, pristine; localtest changes are patches in `../../components/arbeidsflate/patches/`).
Reviewed 2026-10-26 (TODO.md task 53). **Classification: small upstream; nothing to do in localtest** (the stack sends it gRPC).

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

Not needed on the stack as it is: studioctl (task 52, `studioctl/src/cli/internal/cmd/env/localtestv2/monitoring.go`)
gives every container `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` and `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`
while the monitoring group runs, so the BFF already follows the rule there. A localtest patch for `http/protobuf` would
add the proto exporters to `packages/bff/package.json` and change `pnpm-lock.yaml` (the image installs with
`--frozen-lockfile`), more than a directive-sized change, and only matters if a runner ever sends the BFF
`http/protobuf` (Aspire, or the front door's `/otlp`). Left as the upstream proposal above.

Not added: an OTLP span check in `../../components/arbeidsflate/smoke-test.sh` (the BFF is unchanged here); with the
monitoring group up it should show a `bff` server span, or `arbeidsflate-bff` if `OTEL_SERVICE_NAME` wins (see "Not
verified" above).
