#!/usr/bin/env bash
# Smoke test for the proxy in the running compose stack: the contract the nginx ingress had (routes by host and path,
# trailing-slash redirects, cookies without Secure, streaming, the app-frontend files) and the app route API the app
# runner uses. Registers and removes a route for ttd/proxy-smoke only. Requires `podman-compose up -d`.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
BASE=${1:-http://local.altinn.cloud:8000}
API="$BASE/proxy/api/localtest/apps"
FAIL=0
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
code() { curl -s -m 10 -o /dev/null -w '%{http_code}' "$@"; }
for _ in $(seq 1 30); do curl -sf -m 2 -o /dev/null "$BASE/health" && break; sleep 1; done

echo "== platform routes"
expect "start page at / (authentication)" 200 "$(code "$BASE/")"
expect "/Home is an exact route, not a redirect" 200 "$(code "$BASE/Home")"
expect "authentication prefix" 200 "$(code "$BASE/authentication/api/v1/openid/.well-known/openid-configuration")"
expect "register prefix" 200 "$(code "$BASE/register/api/localtest/test-users")"
expect "events prefix" 200 "$(code "$BASE/events/api/localtest/events")"
expect "a prefix without its slash redirects" "301 /storage/" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{location}' "$BASE/storage")"
expect "a file-like path is forwarded (register answers)" 404 "$(code "$BASE/register/no-such-file.json")"
expect "unknown path: the proxy's own 404" "No app is registered for this path. Start it with run-app.sh <app-dir> <port>." "$(curl -s -m 10 "$BASE/nope/missing.js")"
expect "the PDF host belongs to pdf3, /health included" 404 "$(code -H 'Host: pdf.local.altinn.cloud:8000' "$BASE/health")"
SECURE=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:.*;\s*secure')
COOKIES=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:')
expect "sign-in cookies set, none with Secure" "4 0" "$COOKIES $SECURE"
expect "server-sent events stream through unbuffered" ": connected" "$(timeout 3 curl -sN -m 3 "$BASE/events/api/localtest/stream" | head -1)"
FILE=$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{access-control-allow-origin}' "$BASE/app-frontend/altinn-app-frontend.js")
expect "app-frontend bundle with CORS (needs FRONTEND_DIST)" "200 *" "$FILE"

echo "== app routes"
expect "bad app name" 400 "$(code -X PUT "$API/ttd/Not_Valid" -H Content-Type:application/json -d '{"upstream":"http://register:5020"}')"
expect "upstream with a path" 400 "$(code -X PUT "$API/ttd/proxy-smoke" -H Content-Type:application/json -d '{"upstream":"http://register:5020/x"}')"
# Any reachable server will do as the "app": Register answers 404 for /ttd/proxy-smoke/…, which proves the route.
expect "register ttd/proxy-smoke" 200 "$(code -X PUT "$API/ttd/proxy-smoke" -H Content-Type:application/json -d '{"upstream":"http://register:5020"}')"
expect "listed" 1 "$(curl -s -m 10 "$API" | python3 -c 'import json,sys; print(sum(r["org"]=="ttd" and r["app"]=="proxy-smoke" for r in json.load(sys.stdin)))')"
expect "routed to the upstream (Register's empty 404, not the proxy's text)" "" "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "app path without its slash redirects, query kept" "301 /ttd/proxy-smoke/?a=1" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{location}' "$BASE/ttd/proxy-smoke?a=1")"
expect "remove" 204 "$(code -X DELETE "$API/ttd/proxy-smoke")"
expect "gone: the proxy's 404 again" "No app is registered for this path. Start it with run-app.sh <app-dir> <port>." "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "remove again" 404 "$(code -X DELETE "$API/ttd/proxy-smoke")"

[[ $FAIL == 0 ]] && echo "all ok" || { echo "some checks failed"; exit 1; }
