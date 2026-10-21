#!/usr/bin/env bash
# Smoke test for the localtest register: seeded users (with the schema v3 reserved flag and organisations' notification
# addresses), party list with sub-units, the KRR stand-in and app registration with posted test data (the register
# fetches nothing). Start the register, then run
#   ./smoke-test.sh [REGISTER_URL]   (default http://127.0.0.1:5020, a register run as a plain process; against the
#                                     stack pass http://local.altinn.cloud:8000)
# Profile is its own component since task 14 (../profile/smoke-test.sh has the Profile checks that used to be here).
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
REGISTER=${1:-http://127.0.0.1:5020}
SOPHIE_MOBILE=+4799001337   # Sophie Salt (1337), as stored in testdata/testData.json

APP_DATA='{ "persons": [ { "userId": 3001, "partyId": 513001, "ssn": "24917440014", "firstName": "Stub", "lastName": "Appbruker",
                 "partyRoles": { "513001": [ { "type": "altinn", "value": "regna" } ] } } ] }'

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
# InternalOrPlatformAccess is validated for real against the shared test certificate (see ../../certs/README.md).
PAT=$("$HERE/../../certs/mint-platform-access-token.sh" platform localtest)
post() { curl -sS -H Content-Type:application/json -X POST "$@"; }

for _ in $(seq 1 30); do curl -sf "$REGISTER/health" >/dev/null && break; sleep 1; done

curl -sS "$REGISTER/register/api/localtest/test-users" | check "seeded Cypress users" \
  '{(u["userId"], u["partyId"], u["displayName"]) for u in d} >= {(12345, 512345, "Ola Nordmann"), (1001, 510001, "Pengelens Partner"), (1337, 501337, "Sophie Salt"), (1002, 510002, "Gjentagende Forelder"), (1004, 510004, "selvregistrert.bruker@example.com"), (2001, 512001, "MultiParty Prompt"), (2002, 512002, "Kari Nordvik")}'
curl -sS "$REGISTER/register/api/localtest/test-users" | check "reserved (schema v3): only Rik Forelder (1003)" \
  '{u["userId"] for u in d if u["reserved"]} == {1003} and all(isinstance(u["reserved"], bool) for u in d)'
curl -sS "$REGISTER/register/api/localtest/organizations" | check "organizations carry their notification addresses" \
  '[{k: v for k, v in a.items() if v is not None} for a in {o["partyId"]: o["notificationAddresses"] for o in d}[500000]] == [{"email": "varsling@ddgfitness.no"}, {"mobileNumber": "+4799500000"}] and {o["partyId"]: o["notificationAddresses"] for o in d}[500600] == []'
curl -sS "$REGISTER/register/api/localtest/test-users" | check "self-party role codes and acting-for list" \
  'any(u["userId"] == 12345 and u["parties"][0]["partyId"] == 512345 and "regna" in u["parties"][0]["roleCodes"] for u in d) and any(u["userId"] == 1337 and {500000, 500001, 500800}.issubset({a["partyId"] for a in u["actingFor"]}) for u in d)'
post -H "PlatformAccessToken: $PAT" "$REGISTER/register/api/v1/parties/partylist?fetchSubUnits=true" -d '[500000]' | check "partylist [500000] with sub-units" \
  'd[0]["partyId"] == 500000 and [c["partyId"] for c in d[0]["childParties"]] == [500001, 500002, 500003] and d[0]["organization"]["name"] == "DDG Fitness"'
post -H "PlatformAccessToken: $PAT" "$REGISTER/register/api/v1/parties/partylist" -d '[512345, 510004]' | check "person and self-identified parties" \
  '{p["partyId"]: p["name"] for p in d} == {512345: "Ola Nordmann", 510004: "selvregistrert.bruker@example.com"} and [p for p in d if p["partyId"] == 512345][0]["person"]["name"] == "Ola Nordmann"'
# KRR stand-in, as Correspondence's production client and the Profile component's KRR change log read it (anonymous).
post "$REGISTER/register/api/localtest/krr/rest/v2/personer" -d '{"personidentifikatorer":["01839012345","08829698278","00000000000"]}' | check "krr personer: Sophie Salt not reserved, Rik Forelder (reserved) JA, unknown number absent" \
  'd["personer"] == [{"personidentifikator": "01839012345", "reservasjon": "NEI", "status": "AKTIV", "varslingsstatus": "KAN_VARSLES", "kontaktinformasjon": {"epostadresse": "1337@altinnstudiotestusers.com", "mobiltelefonnummer": "'"$SOPHIE_MOBILE"'"}}, {"personidentifikator": "08829698278", "reservasjon": "JA", "status": "AKTIV", "varslingsstatus": "KAN_IKKE_VARSLES", "kontaktinformasjon": {"epostadresse": "test@test.com", "mobiltelefonnummer": "+4799001003"}}]'
post "$REGISTER/register/api/localtest/krr/rest/v2/personer" -d '{"personidentifikatorer":[]}' | check "krr personer: empty lookup" 'd["personer"] == []'

put() { curl -sS -H Content-Type:application/json -X PUT "$@"; }
put "$REGISTER/register/api/localtest/apps/ttd/smoke-app" -d "$APP_DATA" | check "register app with posted test data (apps only)" \
  'd["source"] == "apps" and d["users"] == 1 and d["apps"][0]["status"] == "TestData"'
curl -sS "$REGISTER/register/api/localtest/test-users" | check "the app's user is listed" '[u["displayName"] for u in d] == ["Stub Appbruker"]'
put "$REGISTER/register/api/localtest/apps/ttd/smoke-nodata" | check "app without test data brings back the seed" \
  'd["source"] == "seed+apps" and d["users"] == 9 and {a["status"] for a in d["apps"]} == {"TestData", "NoTestData"}'
post "$REGISTER/register/api/localtest/apps/refresh" | check "refresh" 'd["users"] == 9 and not d["conflicts"]'
code=$(curl -sS -o /dev/null -w '%{http_code}' -H Content-Type:application/json -X PUT "$REGISTER/register/api/localtest/apps/ttd/smoke-app" -d '{"persons": [')
echo "$code" | check "broken testData.json is a 400" 'd == 400'
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-nodata" >/dev/null
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-app" | check "unregister restores the seed" 'd["apps"] == [] and d["users"] == 8'

exit $fail
