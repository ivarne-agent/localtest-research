#!/usr/bin/env bash
# Stop every app started by run-app.sh and remove its route from the localtest app's own proxy.
HERE=$(cd "$(dirname "$0")" && pwd)
PLATFORM=${PLATFORM:-http://local.altinn.cloud:8000}
for f in "$HERE"/.run/*.pid; do
  [[ -f "$f" ]] || continue
  name=$(basename "$f" .pid)            # {org}-{app}; org codes have no hyphen
  kill "$(cat "$f")" 2>/dev/null && echo "stopped $name"
  rm -f "$f"
  curl -s -o /dev/null -X DELETE "$PLATFORM/localtest/api/proxy/apps/${name%%-*}/${name#*-}" || true
done
