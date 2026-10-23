#!/usr/bin/env bash
# An OTLP collector that writes every span it receives to a file, and a summary of the client spans' destinations
# per service, for the egress review (localtest/reviews/2026-10-23-egress-domains.md).
#
#   localtest/tools/egress/otel.sh start DIR     # listens on 127.0.0.1:4317 (gRPC) and :4318 (HTTP)
#   localtest/tools/egress/otel.sh stop
#   localtest/tools/egress/otel.sh summarize DIR [OUT]   # OUT defaults to DIR/summary
#
# The app runner gives every app OTEL_EXPORTER_OTLP_ENDPOINT=http://otel.local.altinn.cloud:4317, and the hosts file
# maps that name to 127.0.0.1, so an app started by `studioctl run` exports here with no change, provided its telemetry
# goes to OTLP at all (the app backend's AppSettings__UseOpenTelemetry; see the review). The localtest-v2 stack has no
# collector of its own, so nothing else listens on 4317. The stack's containers do not export (their images set no
# OTLP endpoint) and are covered by capture.sh instead.
#
# summarize writes:
#   client-spans.tsv  service.name, instrumentation scope, server.address, server.port, method, path, count: every
#                     span of kind CLIENT (System.Net.Http, Npgsql, ...), the path without its query and with GUIDs
#                     and numbers replaced, so it groups.
#   addresses.tsv     service.name, server.address, count.
set -euo pipefail

CTR=${CTR:-podman}
NAME=${EGRESS_OTEL_NAME:-egress-otel}
IMAGE=${EGRESS_OTEL_IMAGE:-docker.io/otel/opentelemetry-collector-contrib:0.161.0}

die() {
  echo "otel.sh: $*" >&2
  exit 1
}

start() {
  local dir=${1:?usage: otel.sh start DIR}
  mkdir -p "$dir"
  dir=$(cd "$dir" && pwd)
  cat >"$dir/collector.yaml" <<'YAML'
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318
exporters:
  file/traces:
    path: /out/traces.jsonl
  file/logs:
    path: /out/logs.jsonl
service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [file/traces]
    logs:
      receivers: [otlp]
      exporters: [file/logs]
YAML
  "$CTR" run -d --name "$NAME" --user 0 \
    -p 127.0.0.1:4317:4317 -p 127.0.0.1:4318:4318 \
    -v "$dir:/out:Z" "$IMAGE" --config=/out/collector.yaml >/dev/null
  echo "Collector $NAME writing to $dir/traces.jsonl. Stop with: $0 stop"
}

stop() {
  "$CTR" rm -f "$NAME" >/dev/null
  echo "Removed $NAME."
}

summarize() {
  local dir=${1:?usage: otel.sh summarize DIR [OUT]}
  local out=${2:-$dir/summary}
  [[ -s $dir/traces.jsonl ]] || die "no spans in $dir/traces.jsonl"
  mkdir -p "$out"
  # SPAN_KIND_CLIENT is 3 in OTLP JSON. Older semantic conventions use net.peer.name and http.url.
  jq -r '
    .resourceSpans[]
    | (.resource.attributes // [] | map({(.key): (.value.stringValue // (.value.intValue | tostring))}) | add // {}) as $res
    | .scopeSpans[]
    | .scope.name as $scope
    | .spans[]
    | select(.kind == 3 or .kind == "SPAN_KIND_CLIENT")
    | (.attributes // [] | map({(.key): (.value.stringValue // (.value.intValue | tostring))}) | add // {}) as $a
    | ($a["url.full"] // $a["http.url"] // "") as $url
    | [
        ($res["service.name"] // "-"),
        ($scope // "-"),
        ($a["server.address"] // $a["net.peer.name"] // $a["db.system"] // "-"),
        ($a["server.port"] // $a["net.peer.port"] // "-"),
        ($a["http.request.method"] // $a["http.method"] // "-"),
        ($url | sub("^[a-z]+://[^/]*"; "") | sub("\\?.*$"; "")
              | gsub("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"; "{guid}")
              | gsub("/[0-9]+(?=/|$)"; "/{n}"))
      ]
    | @tsv' "$dir/traces.jsonl" |
    sort | uniq -c | awk -v OFS='\t' '{ c = $1; sub(/^ *[0-9]+ /, ""); print $0, c }' >"$out/client-spans.tsv"
  awk -F'\t' -v OFS='\t' '{ n[$1 OFS $3] += $7 } END { for (k in n) print k, n[k] }' "$out/client-spans.tsv" |
    sort >"$out/addresses.tsv"
  wc -l "$out/client-spans.tsv" "$out/addresses.tsv"
}

case ${1:-} in
start) start "${2:-}" ;;
stop) stop ;;
summarize) summarize "${2:-}" "${3:-}" ;;
*) die "usage: otel.sh start DIR | stop | summarize DIR [OUT]" ;;
esac
