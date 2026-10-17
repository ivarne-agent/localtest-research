#!/usr/bin/env bash
# Smoke test for the localtest register: seeded users, party list with sub-units, profile endpoints and app
# registration against a stub app. Start the register with
#   Localtest__Register__AppsBaseUrl=http://127.0.0.1:$STUB_PORT/
# (for the container: podman run --network host -e ASPNETCORE_URLS=http://127.0.0.1:$PORT ...), then run
#   ./smoke-test.sh [REGISTER_URL] [STUB_PORT]
set -euo pipefail

REGISTER=${1:-http://127.0.0.1:5020}
STUB_PORT=${2:-18093}
OLA_SSN=01817512212   # Ola Nordmann (12345), see testdata/README.md

stub=$(mktemp -d)
trap 'kill "${stub_pid:-}" 2>/dev/null || true; rm -rf "$stub"' EXIT
mkdir -p "$stub/ttd/smoke-app" "$stub/ttd/smoke-nodata"
cat > "$stub/ttd/smoke-app/testData.json" <<'JSON'
{ "persons": [ { "userId": 3001, "partyId": 513001, "ssn": "24917440014", "firstName": "Stub", "lastName": "Appbruker",
                 "partyRoles": { "513001": [ { "type": "altinn", "value": "regna" } ] } } ] }
JSON
cat > "$stub/serve.py" <<'PY'
import functools, http.server, sys
http.server.ThreadingHTTPServer(("127.0.0.1", int(sys.argv[2])),
    functools.partial(http.server.SimpleHTTPRequestHandler, directory=sys.argv[1])).serve_forever()
PY
python3 -I "$stub/serve.py" "$stub" "$STUB_PORT" >/dev/null 2>&1 &
stub_pid=$!

fail=0
check() { # name, python expression over d (parsed JSON)
  local name=$1 expr=$2 body
  body=$(cat)
  if python3 -I -c "import json,sys; d=json.loads(sys.argv[1]); sys.exit(0 if ($expr) else 1)" "$body" 2>/dev/null; then
    echo "ok   $name"
  else
    echo "FAIL $name: ${body:0:400}"; fail=1
  fi
}
post() { curl -sS -H Content-Type:application/json -X POST "$@"; }

for _ in $(seq 1 30); do curl -sf "$REGISTER/health" >/dev/null && break; sleep 1; done
for _ in $(seq 1 20); do curl -sf "http://127.0.0.1:$STUB_PORT/ttd/smoke-app/testData.json" >/dev/null && break; sleep 0.2; done

curl -sS "$REGISTER/register/api/localtest/test-users" | check "seeded Cypress users" \
  '{(u["userId"], u["partyId"], u["displayName"]) for u in d} >= {(12345, 512345, "Ola Nordmann"), (1001, 510001, "Pengelens Partner"), (1337, 501337, "Sophie Salt"), (1002, 510002, "Gjentagende Forelder"), (1004, 510004, "Selvregistrert Bruker"), (2001, 512001, "MultiParty Prompt"), (2002, 512002, "Kari Nordvik")}'
curl -sS "$REGISTER/register/api/localtest/test-users" | check "self-party role codes and acting-for list" \
  'any(u["userId"] == 12345 and u["parties"][0]["partyId"] == 512345 and "regna" in u["parties"][0]["roleCodes"] for u in d) and any(u["userId"] == 1337 and {500000, 500001, 500800}.issubset({a["partyId"] for a in u["actingFor"]}) for u in d)'
post -H PlatformAccessToken:x "$REGISTER/register/api/v1/parties/partylist?fetchSubUnits=true" -d '[500000]' | check "partylist [500000] with sub-units" \
  'd[0]["partyId"] == 500000 and [c["partyId"] for c in d[0]["childParties"]] == [500001, 500002, 500003] and d[0]["organization"]["name"] == "DDG Fitness"'
post -H PlatformAccessToken:x "$REGISTER/register/api/v1/parties/partylist" -d '[512345, 510004]' | check "person and self-identified parties" \
  '{p["partyId"]: p["name"] for p in d} == {512345: "Ola Nordmann", 510004: "Selvregistrert Bruker"} and [p for p in d if p["partyId"] == 512345][0]["person"]["name"] == "Ola Nordmann"'
curl -sS "$REGISTER/profile/api/v1/users/1337" | check "profile users/1337" \
  'd["userId"] == 1337 and d["party"]["person"]["name"] == "Sophie Salt" and d["profileSettingPreference"]["language"] == "nn" and d["userType"] == 1'
post "$REGISTER/profile/api/v1/users" -d "\"$OLA_SSN\"" | check "profile POST users (ssn string)" 'd["userId"] == 12345'
post "$REGISTER/profile/api/v1/users" -d "{\"ssn\":\"$OLA_SSN\"}" | check "profile POST users {ssn}" 'd["userId"] == 12345'
post "$REGISTER/profile/api/v1/internal/user" -d '{"userId":1004}' | check "profile internal/user self-identified" 'd["userType"] == 2 and d["party"]["name"] == "Selvregistrert Bruker"'
curl -sS "$REGISTER/profile/api/v1/users/byuuid/00000000-0000-0000-0000-000000510001" | check "profile users/byuuid" 'd["userId"] == 1001 and d["profileSettingPreference"]["doNotPromptForParty"] is True'
code=$(curl -sS -o /dev/null -w '%{http_code}' "$REGISTER/profile/api/v1/users/current")
echo "$code" | check "users/current needs a token" 'd == 401'

post "$REGISTER/register/api/localtest/apps" -d '{"org":"ttd","app":"smoke-app"}' | check "register app with test data (apps only)" \
  'd["source"] == "apps" and d["users"] == 1 and d["apps"][0]["status"] == "TestData"'
curl -sS "$REGISTER/profile/api/v1/users/3001" | check "app user has a profile" 'd["party"]["name"] == "Stub Appbruker"'
post "$REGISTER/register/api/localtest/apps" -d '{"org":"ttd","app":"smoke-nodata"}' | check "app without test data brings back the seed" \
  'd["source"] == "seed+apps" and d["users"] == 9 and {a["status"] for a in d["apps"]} == {"TestData", "NoTestData"}'
post "$REGISTER/register/api/localtest/apps/refresh" | check "refresh" 'd["users"] == 9 and not d["conflicts"]'
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-nodata" >/dev/null
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-app" | check "unregister restores the seed" 'd["apps"] == [] and d["users"] == 8'

exit $fail
