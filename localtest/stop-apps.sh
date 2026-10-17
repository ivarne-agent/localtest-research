#!/usr/bin/env bash
# Stop every app started by run-app.sh.
HERE=$(cd "$(dirname "$0")" && pwd)
for f in "$HERE"/.run/*.pid; do [[ -f "$f" ]] || continue; kill "$(cat "$f")" 2>/dev/null && echo "stopped $(basename "$f" .pid)"; rm -f "$f"; done
