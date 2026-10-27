#!/bin/bash
# Standalone smoke test of the workflow engine image the stack runs: a fresh Postgres container (podman or docker), the
# version the stack's localtest-postgres image is built from (../postgres/Dockerfile), prepared by the stack's init
# script (../postgres-init/20-workflow-engine.sh, pg_stat_statements preloaded and UTC as studioctl runs it), then the
# engine on a free port of the host network, checked with GET /api/v1/health/ready -> 200, its migrations and its log.
# The engine is not vendored here (TODO.md task 58): studioctl runs the monorepo's image, and so does this test.
# Usage: ./workflow-engine.sh          IMAGE, default the monorepo image studioctl's localtest-v2 runtime runs (the
#                                      manifest's workflow-engine entry); IMAGE=localhost/<name> for one built from an
#                                      altinn-studio checkout (src/Runtime/workflow-engine-app/Dockerfile, context src/)
# Starts containers of its own: run it under the machine's lock (localtest/docs/verify-after-sync.md, 3.5).
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
IMAGE=${IMAGE:-ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:tt02}
CTR=$(command -v podman || command -v docker)
PG_IMAGE=$(sed -n 's/^FROM //p' "$HERE/../postgres/Dockerfile")
free_port() { python3 -I -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])'; }
PG_PORT=${PG_PORT:-$(free_port)}; PORT=${PORT:-$(free_port)}
PG=lt-wfe-smoke-pg-$$; ENGINE=lt-wfe-smoke-$$; FAILED=0

cleanup() { "$CTR" rm -f "$PG" "$ENGINE" >/dev/null 2>&1; }
trap cleanup EXIT
check() { # name, result (0 = ok), detail
  if [ "$2" = 0 ]; then echo "PASS $1 ${3:-}"; else echo "FAIL $1 ${3:-}"; FAILED=1; fi
}

echo "== fresh Postgres $PG_IMAGE on $PG_PORT"
"$CTR" run -d --name "$PG" -p "127.0.0.1:$PG_PORT:5432" -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres \
  -v "$HERE/../postgres-init/20-workflow-engine.sh:/docker-entrypoint-initdb.d/20-workflow-engine.sh:ro,Z" \
  "$PG_IMAGE" postgres -c shared_preload_libraries=pg_stat_statements -c timezone=UTC -c log_timezone=UTC >/dev/null || exit 1
for _ in $(seq 1 60); do
  "$CTR" logs "$PG" 2>&1 | grep -q "PostgreSQL init process complete" && "$CTR" exec "$PG" pg_isready -q && break; sleep 1
done

CONN="Host=127.0.0.1;Port=$PG_PORT;Database=workflow_engine;Username=postgres;Password=postgres"
CALLBACK='http://local.altinn.cloud:8000/{Org}/{App}/instances/{InstanceOwnerPartyId}/{InstanceGuid}/workflow-engine-callbacks/'
echo "== engine $IMAGE on $PORT"
"$CTR" run -d --name "$ENGINE" --network host -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_HTTP_PORTS="$PORT" \
  -e ConnectionStrings__WorkflowEngine="$CONN" -e AppCommandSettings__CommandEndpoint="$CALLBACK" "$IMAGE" >/dev/null || exit 1
logs() { "$CTR" logs "$ENGINE" 2>&1; }

for i in $(seq 1 90); do
  CODE=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/api/v1/health/ready"); [ "$CODE" = 200 ] && break; sleep 1
done
check "GET /api/v1/health/ready after ${i}s" "$([ "$CODE" = 200 ] && echo 0 || echo 1)" "($CODE)"
MIGRATIONS=$("$CTR" exec "$PG" psql -U postgres -d workflow_engine -tAc 'select count(*) from engine."__EFMigrationsHistory"' 2>/dev/null)
check "migrations ran on the fresh database at startup" "$([ "${MIGRATIONS:-0}" -gt 0 ] && echo 0 || echo 1)" "(${MIGRATIONS:-0} applied)"
ERRORS=$(logs | grep -ciE '^(fail|crit):|unhandled exception')
check "no errors in the engine log" "$([ "$ERRORS" = 0 ] && echo 0 || echo 1)" "($ERRORS)"
if [ $FAILED = 0 ]; then echo "ALL PASSED"; else logs | tail -40; echo "SOME FAILED"; exit 1; fi
