#!/usr/bin/env bash
# Smoke test for the localtest register: seeded users (with the schema v3 reserved flag and organisations' notification
# addresses), party list with sub-units, profile endpoints and app
# registration with posted test data (the register fetches nothing). Start the register, then run
#   ./smoke-test.sh [REGISTER_URL]
# The Profile checks need a user token from the authentication component (AUTH, default the ingress); the register
# must validate it against the same component (GeneralSettings__OpenIdWellKnownEndpoint).
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
REGISTER=${1:-http://127.0.0.1:5020}
AUTH=${AUTH:-http://local.altinn.cloud:8000}
OLA_SSN=01817512212   # Ola Nordmann (12345), see testdata/README.md
SOPHIE_MOBILE=+4787654321   # Sophie Salt (1337), as stored in testdata/testData.json

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
# Profile's users routes, called as app-lib does: the user token plus the app's platform access token.
USR=$(curl -sS "$AUTH/Home/GetTestUserToken?userId=1337" | tr -d '"')
prof() { curl -sS -H "Authorization: Bearer $USR" -H "PlatformAccessToken: $PAT" "$@"; }
pcode() { curl -sS -o /dev/null -w '%{http_code}' "$@"; }

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
prof "$REGISTER/profile/api/v1/users/1337" | check "profile users/1337" \
  'd["userId"] == 1337 and d["party"]["person"]["name"] == "Sophie Salt" and d["profileSettingPreference"]["language"] == "nn" and d["userType"] == 1'
prof -H Content-Type:application/json -X POST "$REGISTER/profile/api/v1/users" -d "\"$OLA_SSN\"" | check "profile POST users (ssn string)" 'd["userId"] == 12345'
prof -H Content-Type:application/json -X POST "$REGISTER/profile/api/v1/users" -d "{\"ssn\":\"$OLA_SSN\"}" | check "profile POST users {ssn}" 'd["userId"] == 12345'
post "$REGISTER/profile/api/v1/internal/user" -d '{"userId":1004}' | check "profile internal/user self-identified" 'd["userType"] == 2 and d["party"]["name"] == "epost:selvregistrert.bruker@example.com" and d["email"] == "selvregistrert.bruker@example.com"'
prof "$REGISTER/profile/api/v1/users/byuuid/00000000-0000-0000-0000-000000510001" | check "profile users/byuuid" 'd["userId"] == 1001 and d["profileSettingPreference"]["doNotPromptForParty"] is True'
code=$(pcode "$REGISTER/profile/api/v1/users/current")
echo "$code" | check "users/current needs a token" 'd == 401'
curl -sS -H "Authorization: Bearer $USR" "$REGISTER/profile/api/v1/users/current" | check "users/current with the user token" 'd["userId"] == 1337'
echo "$(pcode -H "PlatformAccessToken: $PAT" "$REGISTER/profile/api/v1/users/1337")" | check "users/{id} without a user token is 401" 'd == 401'
echo "$(pcode -H "Authorization: Bearer $USR" "$REGISTER/profile/api/v1/users/1337")" | check "users/{id} without a platform token is 403" 'd == 403'
echo "$(pcode -H "Authorization: Bearer $USR" -H Content-Type:application/json -X POST "$REGISTER/profile/api/v1/users" -d "\"$OLA_SSN\"")" | check "POST users without a platform token is 403" 'd == 403'

# Profile contact points, as Altinn Notifications calls them: anonymous, answers under contactPointsList.
post "$REGISTER/profile/api/v1/users/contactpoint/lookup" -d '{"nationalIdentityNumbers":["01839012345","00000000000"],"useStaleContactInfo":false}' | check "contactpoint/lookup: Sophie Salt found, unknown number absent, mobile number as stored" \
  'd["contactPointsList"] == [{"userId": 1337, "nationalIdentityNumber": "01839012345", "isReserved": False, "mobileNumber": "'"$SOPHIE_MOBILE"'", "email": "1337@altinnstudiotestusers.com"}]'
post "$REGISTER/profile/api/v1/users/contactpoint/lookup" -d '{"nationalIdentityNumbers":["08829698278"],"useStaleContactInfo":false}' | check "contactpoint/lookup: isReserved stays false for a reserved person (only the KRR stand-in reads the flag)" \
  'd["contactPointsList"][0]["userId"] == 1003 and d["contactPointsList"][0]["isReserved"] is False'
post "$REGISTER/profile/api/v1/users/contactpoint/lookupsi" -d '{"externalIdentities":["urn:altinn:person:idporten-email:selvregistrert.bruker@example.com","urn:altinn:person:idporten-email:ukjent@example.com"]}' | check "contactpoint/lookupsi: the self-identified e-mail, no mobile" \
  'd["contactPointsList"] == [{"externalIdentity": "urn:altinn:person:idporten-email:selvregistrert.bruker@example.com", "email": "selvregistrert.bruker@example.com", "mobileNumber": ""}]'
# Official addresses (schema v3 notificationAddresses): DDG Fitness and Digitaliseringsdirektoratet have their own; the
# DDG Fitness Oslo sub-unit has none and gets its main unit's (addressOrigin); EAS Health Consulting has none and is left
# out, as is an unknown number. Profile never sets partyId in this answer.
post "$REGISTER/profile/api/v1/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644","991825827","897069660","897069628","123456785"]}' | check "notificationaddresses/lookup: seeded addresses, main-unit fallback, none for the rest" \
  'd["contactPointsList"] == [{"organizationNumber": "897069644", "addressOrigin": "897069644", "partyId": 0, "mobileNumberList": ["+4799500000"], "emailList": ["varsling@ddgfitness.no"]}, {"organizationNumber": "991825827", "addressOrigin": "991825827", "partyId": 0, "mobileNumberList": ["+4799500006"], "emailList": ["varsling@digdir.no"]}, {"organizationNumber": "897069660", "addressOrigin": "897069644", "partyId": 0, "mobileNumberList": ["+4799500000"], "emailList": ["varsling@ddgfitness.no"]}]'
post "$REGISTER/profile/api/v1/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"urn:altinn:resource:app_ttd_x"}' | check "units/contactpoint/lookup: users holding something at DDG Fitness (500000), e-mail and mobile as stored" \
  'd["contactPointsList"][0]["partyId"] == 500000 and {u["userId"]: (u["email"], u["mobileNumber"]) for u in d["contactPointsList"][0]["userContactPoints"]}[1337] == ("1337@altinnstudiotestusers.com", "'"$SOPHIE_MOBILE"'") and 1004 not in {u["userId"] for u in d["contactPointsList"][0]["userContactPoints"]}'

put() { curl -sS -H Content-Type:application/json -X PUT "$@"; }
put "$REGISTER/register/api/localtest/apps/ttd/smoke-app" -d "$APP_DATA" | check "register app with posted test data (apps only)" \
  'd["source"] == "apps" and d["users"] == 1 and d["apps"][0]["status"] == "TestData"'
prof "$REGISTER/profile/api/v1/users/3001" | check "app user has a profile" 'd["party"]["name"] == "Stub Appbruker"'
put "$REGISTER/register/api/localtest/apps/ttd/smoke-nodata" | check "app without test data brings back the seed" \
  'd["source"] == "seed+apps" and d["users"] == 9 and {a["status"] for a in d["apps"]} == {"TestData", "NoTestData"}'
post "$REGISTER/register/api/localtest/apps/refresh" | check "refresh" 'd["users"] == 9 and not d["conflicts"]'
code=$(curl -sS -o /dev/null -w '%{http_code}' -H Content-Type:application/json -X PUT "$REGISTER/register/api/localtest/apps/ttd/smoke-app" -d '{"persons": [')
echo "$code" | check "broken testData.json is a 400" 'd == 400'
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-nodata" >/dev/null
curl -sS -X DELETE "$REGISTER/register/api/localtest/apps/ttd/smoke-app" | check "unregister restores the seed" 'd["apps"] == [] and d["users"] == 8'

exit $fail
