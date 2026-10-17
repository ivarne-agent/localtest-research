#!/bin/bash
# Standalone smoke test of the workflow-engine component: a fresh Postgres 16 container (podman or docker) prepared by
# the compose stack's init script (../../postgres-init/20-workflow-engine.sh, pg_stat_statements preloaded as in
# docker-compose.yml), then the engine on a free port, checked with GET /api/v1/health/ready -> 200.
# Usage: ./smoke-test.sh           build and run the vendored host with dotnet (localtest overrides)
#        ./smoke-test.sh --image   run the image (IMAGE, default localhost/localtest-workflow-engine:dev) on the host network
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
MODE=${1:-dotnet}; IMAGE=${IMAGE:-localhost/localtest-workflow-engine:dev}
CTR=$(command -v podman || command -v docker)
free_port() { python3 -I -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])'; }
PG_PORT=${PG_PORT:-$(free_port)}; PORT=${PORT:-$(free_port)}
PG=lt-wfe-smoke-pg-$$; ENGINE=lt-wfe-smoke-$$; PID=; WORK=$(mktemp -d); FAILED=0
APP=$HERE/vendor/src/Runtime/workflow-engine-app/src/WorkflowEngine.App

cleanup() { [ -n "$PID" ] && kill "$PID" 2>/dev/null; "$CTR" rm -f "$PG" "$ENGINE" >/dev/null 2>&1; }
trap cleanup EXIT
check() { # name, result (0 = ok), detail
  if [ "$2" = 0 ]; then echo "PASS $1 ${3:-}"; else echo "FAIL $1 ${3:-}"; FAILED=1; fi
}

echo "== fresh Postgres 16 on $PG_PORT"
"$CTR" run -d --name "$PG" -p 127.0.0.1:$PG_PORT:5432 -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -v "$HERE/../../postgres-init/20-workflow-engine.sh:/docker-entrypoint-initdb.d/20-workflow-engine.sh:ro,Z" \
  docker.io/library/postgres:16-alpine postgres -c shared_preload_libraries=pg_stat_statements >/dev/null || exit 1
for i in $(seq 1 60); do
  "$CTR" logs "$PG" 2>&1 | grep -q "PostgreSQL init process complete" && "$CTR" exec "$PG" pg_isready -q && break; sleep 1
done

CONN="Host=127.0.0.1;Port=$PG_PORT;Database=workflow_engine;Username=postgres;Password=postgres"
CALLBACK='http://local.altinn.cloud:8000/{Org}/{App}/instances/{InstanceOwnerPartyId}/{InstanceGuid}/workflow-engine-callbacks/'
echo "== engine ($MODE) on $PORT"
if [ "$MODE" = --image ]; then
  "$CTR" run -d --name "$ENGINE" --network host -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_HTTP_PORTS=$PORT \
    -e ConnectionStrings__WorkflowEngine="$CONN" -e AppCommandSettings__CommandEndpoint="$CALLBACK" "$IMAGE" >/dev/null || exit 1
  logs() { "$CTR" logs "$ENGINE" 2>&1; }
else
  export DirectoryBuildPropsPath=$HERE/localtest.build.props DirectoryBuildTargetsPath=$HERE/localtest.build.targets
  (cd "$APP" && dotnet build -c Release --nologo -v q >"$WORK/build.log" 2>&1) || { echo "build failed, see $WORK/build.log"; exit 1; }
  (cd "$APP" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:$PORT ConnectionStrings__WorkflowEngine="$CONN" \
    AppCommandSettings__CommandEndpoint="$CALLBACK" exec dotnet bin/Release/net10.0/WorkflowEngine.App.dll >"$WORK/engine.log" 2>&1) &
  PID=$!
  logs() { cat "$WORK/engine.log"; }
fi

for i in $(seq 1 90); do
  CODE=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORT/api/v1/health/ready); [ "$CODE" = 200 ] && break; sleep 1
done
check "GET /api/v1/health/ready after ${i}s" $([ "$CODE" = 200 ]; echo $?) "($CODE)"
MIGRATIONS=$("$CTR" exec "$PG" psql -U postgres -d workflow_engine -tAc 'select count(*) from engine."__EFMigrationsHistory"' 2>/dev/null)
check "migrations ran on the fresh database at startup" $([ "${MIGRATIONS:-0}" -gt 0 ]; echo $?) "(${MIGRATIONS:-0} applied)"
ERRORS=$(logs | grep -ciE '^(fail|crit):|unhandled exception')
check "no errors in the engine log" $([ "$ERRORS" = 0 ]; echo $?) "($ERRORS)"
if [ $FAILED = 0 ]; then echo "ALL PASSED"; rm -rf "$WORK"; else logs | tail -40; echo "SOME FAILED"; exit 1; fi
