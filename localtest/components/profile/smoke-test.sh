#!/usr/bin/env bash
# Smoke test for the profile component (production Altinn Profile on the shared Postgres) in the running stack,
# everything through the proxy. Covers the migrations and the seed (KRR sync, organisation address sync, profile
# settings), the user endpoints apps, Authentication, Authorization and Access Management call (the checks that were the
# Register component's while it served /profile), production's contact point semantics for Notifications and
# Correspondence, the official organisation addresses seeded from the test data with the main-unit fallback, and the
# Arbeidsflate paths: favourites, profile settings, address verification with the code read from the Notifications
# inbox, professional notification settings feeding units/contactpoint/lookup, and organisation addresses written
# through the PDP-checked endpoints and accepted by the brreg stand-in.
# Requires the stack (`studioctl env up -r localtest-v2`, see ../../README.md); Notifications is in the default stack.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# Restart persistence: `PERSIST=set ./smoke-test.sh` leaves a favourite (DDG Fitness for 1337) and a language (en for
# 1001); after `studioctl env down` and `env up`, `PERSIST=check ./smoke-test.sh` runs only the checks that they survived
# and removes them.
# The health endpoint is not under the proxy prefix, so it is read on the component's host port (HEALTH, default
# http://127.0.0.1:8036/health).
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
BASE=${1:-http://local.altinn.cloud:8000}
API=$BASE/profile/api/v1
LOCAL=$BASE/profile/api/localtest
INBOXES=$BASE/notifications/api/localtest/inboxes
HEALTH=${HEALTH:-http://127.0.0.1:8036/health}
PERSIST=${PERSIST:-}
FAIL=0
check() { # name python-expression-on-d (stdin is JSON)
  local body; body=$(cat)
  if python3 -I -c "import json,sys; d=json.loads(sys.argv[1]); sys.exit(0 if ($2) else 1)" "$body" 2>/dev/null; then echo "ok   $1"; else echo "FAIL $1: ${body:0:400}"; FAIL=1; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
field() { python3 -I -c "import json,sys; print(json.load(sys.stdin)$1)"; }
waitfor() { # url python-expression [curl args...]
  local url=$1 expr=$2; shift 2
  for _ in $(seq 1 40); do curl -s "$@" "$url" | python3 -I -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($expr) else 1)" 2>/dev/null && return 0; sleep 0.5; done; return 1
}

# Test data (see ../register/testdata/README.md): Sophie Salt 1337 (DAGL at DDG Fitness), Ola Nordmann 12345, Pengelens
# Partner 1001, Rik Forelder 1003 (reserved), the self-identified 1004; DDG Fitness 897069644 (party 500000) with its
# sub-unit DDG Fitness Oslo 897069660, Digitaliseringsdirektoratet 991825827, EAS Health Consulting 897069628 (500600).
SOPHIE_SSN=01839012345
OLA_SSN=01817512212
RIK_SSN=08829698278
SOPHIE_MOBILE=+4799001337
DDG=897069644
DDG_UUID=00000000-0000-0000-0000-000000500000
EAS_UUID=00000000-0000-0000-0000-000000500600

JSON=(-H Content-Type:application/json)
PAT=$("$HERE/../../certs/mint-platform-access-token.sh" ttd testapp)
token() { curl -s "$BASE/Home/GetTestUserToken?userId=$1${2:+&authenticationLevel=$2}" | tr -d '"'; }
USR=$(token 1337)
USR3=$(token 1337 3)
U1001=$(token 1001 3)
as1337=(-H "Authorization: Bearer $USR")
app=(-H "Authorization: Bearer $USR" -H "PlatformAccessToken: $PAT")
post() { curl -s "${JSON[@]}" -X POST "$@"; }

for _ in $(seq 1 90); do curl -sf -o /dev/null "$HEALTH" && curl -sf -o /dev/null "$LOCAL/status" && break; sleep 1; done

if [[ "$PERSIST" == check ]]; then
  echo "== restart persistence"
  curl -s "${as1337[@]}" "$API/users/current/party-groups/favorites" | check "the favourite set before the restart is still there" "'$DDG_UUID' in d['parties']"
  post -H "Authorization: Bearer $U1001" "$API/internal/user" -d '{"userId":1001}' | check "1001's language set before the restart is still en" 'd["profileSettingPreference"]["language"] == "en"'
  curl -s -o /dev/null "${as1337[@]}" -X DELETE "$API/users/current/party-groups/favorites/$DDG_UUID"
  curl -s -o /dev/null -H "Authorization: Bearer $U1001" "${JSON[@]}" -X PATCH "$API/users/current/profilesettings" -d '{"language":"nb"}'
  exit $FAIL
fi

echo "== health, migrations and the seed"
expect "GET /health on the host port" 200 "$(code "$HEALTH")"
post "$LOCAL/sync" | check "POST localtest/sync: KRR sync, organisation address sync and settings seed all ok" \
  '[(s["step"], s["outcome"]) for s in d["steps"]] == [("krr", "ok"), ("organizationNotificationAddresses", "ok"), ("profileSettings", "ok")]'
curl -s "$LOCAL/status" | check "status: the six migrations applied, none pending, a KRR change, the persons and settings seeded" \
  'len(d["migrations"]) == 6 and d["pendingMigrations"] == [] and d["krr"]["latestChangeNumber"] > 0 and d["krr"]["persons"] >= 7 and d["profileSettings"] >= 8'
curl -s "$LOCAL/organizations/$DDG" | check "DDG Fitness's official addresses were seeded once, from brreg's change log, accepted" \
  '{(a["addressType"], a["fullAddress"]) for a in d["notificationAddresses"] if a["registryId"].startswith("lt-seed-")} == {("Email", "varsling@ddgfitness.no"), ("SMS", "+4799500000")} and all(a["hasRegistryAccepted"] and a["updateSource"] == "KoFuVi" for a in d["notificationAddresses"] if a["registryId"].startswith("lt-seed-"))'
expect "a sub-unit without addresses of its own gets no row (the main-unit fallback answers)" 404 "$(code "$LOCAL/organizations/897069660")"

echo "== the user endpoints (formerly the Register component's Profile fakes)"
curl -s "${app[@]}" "$API/users/1337" | check "users/1337 with a user token and a platform token: person, party.ssn, language nn" \
  'd["userId"] == 1337 and d["party"]["person"]["name"] == "Sophie Salt" and d["party"]["ssn"] == "'$SOPHIE_SSN'" and d["profileSettingPreference"]["language"] == "nn" and d["userType"] == 1'
post "${app[@]}" "$API/users" -d "\"$OLA_SSN\"" | check "POST users with a national identity number string" 'd["userId"] == 12345'
post "$API/internal/user" -d '{"userId":1004}' | check "internal/user (anonymous): the self-identified user" \
  'd["userType"] == 2 and d["party"]["name"] == "epost:selvregistrert.bruker@example.com" and d["email"] == "selvregistrert.bruker@example.com"'
post "$API/internal/user" -d "{\"ssn\":\"$OLA_SSN\"}" | check "internal/user by national identity number" 'd["userId"] == 12345'
curl -s "${app[@]}" "$API/users/byuuid/00000000-0000-0000-0000-000000510001" | check "users/byuuid: 1001, doNotPromptForParty from the seed" \
  'd["userId"] == 1001 and d["profileSettingPreference"]["doNotPromptForParty"] is True'
expect "users/current without a token" 401 "$(code "$API/users/current")"
curl -s "${as1337[@]}" "$API/users/current" | check "users/current with the user token" 'd["userId"] == 1337'
expect "users/{id} without a user token" 401 "$(code -H "PlatformAccessToken: $PAT" "$API/users/1337")"
expect "users/{id} without a platform token" 403 "$(code "${as1337[@]}" "$API/users/1337")"
expect "POST users without a platform token" 403 "$(code "${as1337[@]}" "${JSON[@]}" -X POST "$API/users" -d "\"$OLA_SSN\"")"

echo "== production semantics"
curl -s "${app[@]}" "$API/users/1337" | check "phoneNumber is the KRR mobile number, e-mail from KRR, not reserved" \
  'd["phoneNumber"] == "'$SOPHIE_MOBILE'" and d["email"] == "1337@altinnstudiotestusers.com" and d["isReserved"] is False'
post "$API/internal/user" -d '{"userId":1003}' | check "Rik Forelder (reserved in the seed) is isReserved through the KRR sync" 'd["isReserved"] is True'
post "$API/users/contactpoint/lookup" -d "{\"nationalIdentityNumbers\":[\"$SOPHIE_SSN\",\"00000000000\"],\"useStaleContactInfo\":false}" | check "contactpoint/lookup: Sophie Salt with e-mail and mobile number, userId 0, an unknown number left out" \
  '[(c["nationalIdentityNumber"], c["userId"], c["isReserved"], c["mobileNumber"], c["email"]) for c in d["contactPointsList"]] == [("'$SOPHIE_SSN'", 0, False, "'$SOPHIE_MOBILE'", "1337@altinnstudiotestusers.com")]'
post "$API/users/contactpoint/lookup" -d "{\"nationalIdentityNumbers\":[\"$RIK_SSN\"],\"useStaleContactInfo\":false}" | check "contactpoint/lookup: a reserved person has isReserved and keeps the contact points" \
  '[(c["isReserved"], c["mobileNumber"], c["email"]) for c in d["contactPointsList"]] == [(True, "+4799001003", "test@test.com")]'
post "$API/users/contactpoint/lookupsi" -d '{"externalIdentities":["urn:altinn:person:idporten-email:selvregistrert.bruker@example.com","urn:altinn:person:idporten-email:ukjent@example.com"]}' | check "contactpoint/lookupsi: every idporten-email identity answers with the e-mail from the URN" \
  '{c["externalIdentity"]: c["email"] for c in d["contactPointsList"]} == {"urn:altinn:person:idporten-email:selvregistrert.bruker@example.com": "selvregistrert.bruker@example.com", "urn:altinn:person:idporten-email:ukjent@example.com": "ukjent@example.com"}'
post "$API/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644","991825827","897069660","897069628","123456785"]}' | check "organizations/notificationaddresses/lookup: the seeded addresses, the main unit's for DDG Fitness Oslo, none for the rest" \
  '{c["organizationNumber"]: (c["addressOrigin"], c["partyId"], c["mobileNumberList"], c["emailList"]) for c in d["contactPointsList"]} == {"897069644": ("897069644", 0, ["+4799500000"], ["varsling@ddgfitness.no"]), "991825827": ("991825827", 0, ["+4799500006"], ["varsling@digdir.no"]), "897069660": ("897069644", 0, ["+4799500000"], ["varsling@ddgfitness.no"])}'

echo "== Correspondence's lookups (scope altinn:profile/correspondence.notificationsettings.read)"
CORR=$(curl -s "$BASE/Home/GetTestOrgToken?scopes=altinn:profile/correspondence.notificationsettings.read" | tr -d '"')
OTHER=$(curl -s "$BASE/Home/GetTestOrgToken?scopes=altinn:serviceowner/instances.read" | tr -d '"')
expect "correspondence/units/contactpoint/lookup without a token" 401 "$(code "${JSON[@]}" -X POST "$API/correspondence/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"x"}')"
expect "correspondence/units/contactpoint/lookup without the scope" 403 "$(code -H "Authorization: Bearer $OTHER" "${JSON[@]}" -X POST "$API/correspondence/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"x"}')"
expect "correspondence/units/contactpoint/lookup with the scope" 200 "$(code -H "Authorization: Bearer $CORR" "${JSON[@]}" -X POST "$API/correspondence/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"urn:altinn:resource:ttd-localtest-correspondence"}')"
expect "correspondence/organizations/notificationaddresses/lookup without the scope" 403 "$(code -H "Authorization: Bearer $OTHER" "${JSON[@]}" -X POST "$API/correspondence/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644"]}')"
post -H "Authorization: Bearer $CORR" "$API/correspondence/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644","897069660"]}' | check "correspondence/organizations/notificationaddresses/lookup with the scope: DDG Fitness's official addresses, the main unit's for its sub-unit" \
  '{c["organizationNumber"]: (c["addressOrigin"], c["emailList"]) for c in d["contactPointsList"]} == {"897069644": ("897069644", ["varsling@ddgfitness.no"]), "897069660": ("897069644", ["varsling@ddgfitness.no"])}'

echo "== Arbeidsflate: favourites and profile settings (1337)"
FAV=$API/users/current/party-groups/favorites
STATUS=$(code "${as1337[@]}" -X PUT "$FAV/$DDG_UUID")
[[ "$STATUS" == 201 || "$STATUS" == 204 ]] && echo "ok   PUT favourite DDG Fitness ($STATUS)" || { echo "FAIL PUT favourite DDG Fitness (got $STATUS)"; FAIL=1; }
curl -s "${as1337[@]}" "$FAV" | check "GET favourites contains DDG Fitness" "d['isFavorite'] is True and '$DDG_UUID' in d['parties']"
curl -s -o /dev/null "${as1337[@]}" -X DELETE "$FAV/$DDG_UUID"
curl -s "${as1337[@]}" "$FAV" | check "DELETE favourite: gone" "'$DDG_UUID' not in d.get('parties', [])"
curl -s "${as1337[@]}" "${JSON[@]}" -X PATCH "$API/users/current/profilesettings" -d '{"language":"en"}' | check "PATCH profilesettings language en" 'd["language"] == "en"'
curl -s "${app[@]}" "$API/users/1337" | check "users/1337 says en (a user change; the seed never overwrites it)" 'd["profileSettingPreference"]["language"] == "en"'
post "$LOCAL/sync" >/dev/null
curl -s "${app[@]}" "$API/users/1337" | check "... still en after another seed run" 'd["profileSettingPreference"]["language"] == "en"'
curl -s "${as1337[@]}" "${JSON[@]}" -X PATCH "$API/users/current/profilesettings" -d '{"language":"nn"}' | check "PATCH profilesettings back to nn" 'd["language"] == "nn"'

echo "== Arbeidsflate: address verification and professional notification settings (1337 at DDG Fitness)"
ADDR="smoke.$(cat /proc/sys/kernel/random/uuid | cut -c1-8)@example.com"
VER=$API/users/current/verification
expect "verification/send for $ADDR" 204 "$(code "${as1337[@]}" "${JSON[@]}" -X POST "$VER/send" -d "{\"value\":\"$ADDR\",\"type\":\"Email\"}")"
if waitfor "$INBOXES/$ADDR" 'len(d["messages"]) > 0'; then echo "ok   the code reached the Notifications inbox for $ADDR"; else echo "FAIL the code never reached the Notifications inbox for $ADDR"; FAIL=1; fi
CODE=$(curl -s "$INBOXES/$ADDR" | python3 -I -c 'import json,re,sys; m=json.load(sys.stdin)["messages"][-1]; print(re.search(r"\b(\d{6})\b", m.get("body") or "").group(1))' 2>/dev/null)
expect "verification/verify with a wrong code" 422 "$(code "${as1337[@]}" "${JSON[@]}" -X POST "$VER/verify" -d "{\"value\":\"$ADDR\",\"type\":\"Email\",\"verificationCode\":\"000000\"}")"
expect "verification/verify with the code from the inbox" 204 "$(code "${as1337[@]}" "${JSON[@]}" -X POST "$VER/verify" -d "{\"value\":\"$ADDR\",\"type\":\"Email\",\"verificationCode\":\"$CODE\"}")"
curl -s "${as1337[@]}" "$VER/verified-addresses" | check "verified-addresses lists it" "any(a['value'] == '$ADDR' for a in d)"
NS=$API/users/current/notificationsettings/parties
expect "PUT notificationsettings with an unverified address" 422 "$(code "${as1337[@]}" "${JSON[@]}" -X PUT "$NS/$DDG_UUID" -d '{"emailAddress":"never.verified@example.com","resourceIncludeList":[]}')"
STATUS=$(code "${as1337[@]}" "${JSON[@]}" -X PUT "$NS/$DDG_UUID" -d "{\"emailAddress\":\"$ADDR\",\"resourceIncludeList\":[]}")
[[ "$STATUS" == 201 || "$STATUS" == 204 ]] && echo "ok   PUT notificationsettings for DDG Fitness with the verified address ($STATUS)" || { echo "FAIL PUT notificationsettings (got $STATUS)"; FAIL=1; }
curl -s "${as1337[@]}" "$NS" | check "GET notificationsettings/parties lists DDG Fitness" "any(s['partyUuid'] == '$DDG_UUID' and s['emailAddress'] == '$ADDR' for s in d)"
post "$API/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"urn:altinn:resource:altinn_keyrole_access"}' | check "units/contactpoint/lookup now returns 1337's registered address for DDG Fitness" \
  "any(u['userId'] == 1337 and u['email'] == '$ADDR' for c in d['contactPointsList'] for u in c['userContactPoints'])"
expect "notificationsettings for a party the user may not act for (1001 at EAS Health Consulting)" 403 "$(code -H "Authorization: Bearer $U1001" "$NS/$EAS_UUID")"
curl -s -o /dev/null "${as1337[@]}" -X DELETE "$NS/$DDG_UUID"
post "$API/units/contactpoint/lookup" -d '{"organizationNumbers":["897069644"],"resourceId":"urn:altinn:resource:altinn_keyrole_access"}' | check "after DELETE the unit lookup has no address from 1337 (production: only registered addresses, no role holders)" \
  "not any(u['userId'] == 1337 for c in d['contactPointsList'] for u in c['userContactPoints'])"

echo "== organisation notification addresses through the PDP (resource altinn-profil-api-varslingsdaresser-for-virksomheter)"
ORGAPI=$API/organizations/$DDG/notificationaddresses/mandatory
as1337l3=(-H "Authorization: Bearer $USR3")
curl -s "${as1337l3[@]}" "$ORGAPI" | check "GET mandatory as 1337 (DAGL, level 3): the seeded addresses" \
  "{a.get('email') or ((a.get('countryCode') or '') + (a.get('phone') or '')) for a in d['notificationAddresses']} >= {'varsling@ddgfitness.no', '+4799500000'}"
expect "GET mandatory of EAS Health Consulting as 1001 without a role there" 403 "$(code -H "Authorization: Bearer $U1001" "$API/organizations/897069628/notificationaddresses/mandatory")"
ORGADDR="smoke.$(cat /proc/sys/kernel/random/uuid | cut -c1-8)@ddgfitness.no"
CREATED=$(curl -s -w '\n%{http_code}' "${as1337l3[@]}" "${JSON[@]}" -X POST "$ORGAPI" -d "{\"email\":\"$ORGADDR\"}")
expect "POST mandatory $ORGADDR" 201 "${CREATED##*$'\n'}"
ORGADDR_ID=$(echo "${CREATED%$'\n'*}" | field '["notificationAddressId"]' 2>/dev/null)
post "$API/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644"]}' | check "the new address waits for the registry: not yet in the lookup" \
  "'$ORGADDR' not in d['contactPointsList'][0]['emailList']"
post "$LOCAL/sync" >/dev/null
post "$API/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644"]}' | check "after the organisation sync the brreg stand-in has accepted it: in the lookup" \
  "'$ORGADDR' in d['contactPointsList'][0]['emailList'] and 'varsling@ddgfitness.no' in d['contactPointsList'][0]['emailList']"
expect "DELETE mandatory/$ORGADDR_ID" 200 "$(code "${as1337l3[@]}" -X DELETE "$ORGAPI/$ORGADDR_ID")"
post "$LOCAL/sync" >/dev/null
post "$API/organizations/notificationaddresses/lookup" -d '{"organizationNumbers":["897069644"]}' | check "after DELETE and a sync: gone, the seeded ones stay (no reseed)" \
  "d['contactPointsList'][0]['emailList'] == ['varsling@ddgfitness.no'] and d['contactPointsList'][0]['mobileNumberList'] == ['+4799500000']"

echo "== inspection"
curl -s "$LOCAL/users/1337" | check "localtest/users/1337: settings, the KRR row, the verified address" \
  "d['profileSettings']['languageType'] == 'nn' and d['krr']['mobileNumber'] == '$SOPHIE_MOBILE' and any(v['address'] == '$ADDR' for v in d['verifiedAddresses'])"

if [[ "$PERSIST" == set ]]; then
  curl -s -o /dev/null "${as1337[@]}" -X PUT "$FAV/$DDG_UUID"
  curl -s -o /dev/null -H "Authorization: Bearer $U1001" "${JSON[@]}" -X PATCH "$API/users/current/profilesettings" -d '{"language":"en"}'
  echo "left a favourite for 1337 and language en for 1001; restart the stack and run PERSIST=check $0"
fi

exit $FAIL
