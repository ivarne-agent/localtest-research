# shellcheck shell=bash
# Spans in the monitoring group's trace store, for the components' OTLP checks (TODO.md task 53: a component exports
# over OTLP exactly when OTEL_EXPORTER_OTLP_ENDPOINT is set, which studioctl does while the collector runs, with
# OTEL_SERVICE_NAME set to the manifest name). Source it; it defines four functions and sets nothing else:
#
#   localtest_monitoring_up BASE          status 0 when the front door at BASE routes Grafana (the monitoring group is up)
#   localtest_traceparent                 prints "TRACE_ID TRACEPARENT": a new sampled trace id and a W3C traceparent
#                                         header value in it, to send with the request whose spans are looked up
#   localtest_spans BASE TRACE SERVICE    prints "kind:name" of the spans in TRACE whose service.name is SERVICE, sorted
#                                         by kind (2 server, 3 client), space separated; empty when there are none
#   localtest_wait_spans BASE TRACE SERVICE [SECONDS]
#                                         polls localtest_spans until it prints a server span (default 90 s; the exporters
#                                         batch for up to 5 s and VictoriaTraces makes spans searchable about 30 s later)
#                                         and prints its last answer
#
# The spans are read from VictoriaTraces through Grafana's datasource proxy (LogsQL), as the front door's check does
# (../../localtest/smoke-test.sh).
#
#   . "$HERE/../../smoke/lib/traces.sh"
#   if localtest_monitoring_up "$BASE"; then
#     read -r TRACE TP < <(localtest_traceparent)
#     curl -s -o /dev/null -H "traceparent: $TP" "$BASE/register/api/localtest/test-users"
#     SPANS=$(localtest_wait_spans "$BASE" "$TRACE" register)
#   fi

localtest_monitoring_up() {
  local base=${1:?localtest_monitoring_up: BASE}
  [[ -n "$(curl -s -m 10 "$base/localtest/api/routes" 2>/dev/null | python3 -I -c 'import sys, json; print(next((r.get("address") or "" for r in json.load(sys.stdin)["platform"] if r["service"] == "grafana"), ""))' 2>/dev/null)" ]]
}

localtest_traceparent() {
  python3 -I -c 'import secrets; t = secrets.token_hex(16); print(t, f"00-{t}-{secrets.token_hex(8)}-01")'
}

localtest_spans() {
  local base=${1:?localtest_spans: BASE} trace=${2:?localtest_spans: TRACE} service=${3:?localtest_spans: SERVICE}
  curl -s -m 10 "$base/grafana/api/datasources/proxy/uid/altinn-studio-traces-logsql/select/logsql/query" \
    --data-urlencode "query=_time:10m trace_id:=\"$trace\" \"resource_attr:service.name\":=\"$service\" | fields name, kind | sort by (kind)" \
    | python3 -I -c 'import sys, json; print(" ".join(json.loads(l)["kind"] + ":" + json.loads(l)["name"] for l in sys.stdin if l.strip()))' 2>/dev/null
}

localtest_wait_spans() {
  local base=${1:?localtest_wait_spans: BASE} trace=${2:?localtest_wait_spans: TRACE} service=${3:?localtest_wait_spans: SERVICE}
  local seconds=${4:-90} found=""
  for _ in $(seq 1 "$seconds"); do
    found=$(localtest_spans "$base" "$trace" "$service")
    [[ " $found" == *" 2:"* ]] && break
    sleep 1
  done
  printf '%s\n' "$found"
}
