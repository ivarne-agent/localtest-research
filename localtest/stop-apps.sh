#!/usr/bin/env bash
# Stop every app started by run-app.sh.
HERE=$(cd "$(dirname "$0")" && pwd)
for f in "$HERE"/.run/*.pid; do [[ -f "$f" ]] || continue; kill "$(cat "$f")" 2>/dev/null && echo "stopped $(basename "$f" .pid)"; rm -f "$f"; rm -f "$HERE/ingress/app-routes/$(basename "$f" .pid).conf"; done
INGRESS=$(podman ps --format '{{.Names}}' 2>/dev/null | grep -E '^localtest[_-]ingress[_-]1$' || true)
[[ -n "$INGRESS" ]] && podman exec "$INGRESS" nginx -s reload 2>/dev/null || true
