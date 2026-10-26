#!/usr/bin/env bash
# End-to-end check of the stack through the front door: sign-in, tokens, PDP decisions and a full instance lifecycle on
# production Storage. Requires the stack (`studioctl env up -r localtest-v2`) and local.altinn.cloud in /etc/hosts.
# 26 checks, 27 with the monitoring group up (Storage's server span in VictoriaTraces, task 53); check 26 (no errors in Storage's log, read from its container: localtest-v2-storage under studioctl,
# storage-<suffix> under the Aspire package, see smoke/lib/containers.sh) skipped without podman or docker or the
# container. In minimal mode (studioctl env up --minimal: the front door emulates the platform and its PDP permits
# everything; read from GET localtest/api/routes, or forced with LOCALTEST_MINIMAL=1, or off with LOCALTEST_MINIMAL=0)
# the three checks that need real authorization decisions are skipped by name with the reason: 23 checks.
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=smoke/lib/containers.sh
. "$HERE/smoke/lib/containers.sh"
STARTED=$(date -u +%Y-%m-%dT%H:%M:%SZ)
BASE=${BASE:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
FAIL=0
check() { local label=$1 expected=$2 actual=$3; if [[ "$actual" == "$expected" ]]; then printf 'ok   %-60s %s\n' "$label" "$actual"; else printf 'FAIL %-60s %s (expected %s)\n' "$label" "$actual" "$expected"; FAIL=1; fi; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/health" && curl -sf -o /dev/null "$BASE/authentication/api/v1/openid/.well-known/openid-configuration" && break; sleep 1; done
# Minimal mode: the front door reports an emulated service's address as "emulated".
MINIMAL=${LOCALTEST_MINIMAL:-$(curl -s "$BASE/localtest/api/routes" | python3 -c 'import sys,json; print(1 if any(r.get("address") == "emulated" for r in json.load(sys.stdin)["platform"]) else 0)' 2>/dev/null || echo 0)}
[[ "$MINIMAL" == 1 ]] && echo "minimal mode: the PDP permits everything; the checks of real authorization decisions are skipped"
# Runs the check unless minimal mode cannot answer it: undefined there (planned-components/minimal-emulation, section 7).
decided() { local label=$1; if [[ "$MINIMAL" == 1 ]]; then printf 'skip %-60s %s\n' "$label" "(minimal mode: the emulated PDP permits everything)"; else check "$@"; fi; }

# Sign-in through the picker, as a browser would.
J=$(mktemp); LOC=$(curl -s -o /dev/null -w '%{redirect_url}' -c "$J" "$BASE/authentication/api/v1/authentication?goTo=$BASE/ttd/testapp/")
check "authentication redirects to the ID-porten picker" "/localtest/api/idporten/authorize" "$(python3 -c "import sys,urllib.parse as u; print(u.urlparse(sys.argv[1]).path)" "$LOC")"
q() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$LOC" "$1"; }
CB=$(curl -s -o /dev/null -w '%{redirect_url}' -b "$J" -c "$J" -X POST "$BASE/localtest/api/idporten/authorize" --data-urlencode "client_id=$(q client_id)" --data-urlencode "state=$(q state)" --data-urlencode "nonce=$(q nonce)" --data-urlencode "redirect_uri=$(q redirect_uri)" --data-urlencode "code_challenge=$(q code_challenge)" --data-urlencode "code_challenge_method=$(q code_challenge_method)" --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
H=$(mktemp); curl -s -b "$J" -c "$J" -D "$H" -o /dev/null "$CB"
check "callback redirects to goTo" "$BASE/ttd/testapp/" "$(grep -i '^Location:' "$H" | tr -d '\r' | awk '{print $2}')"
check "cookie JWT set without Secure" "yes" "$(grep -i '^Set-Cookie: AltinnStudioRuntime=' "$H" | grep -qi 'secure' && echo no || echo yes)"
TOKEN=$(grep -i '^Set-Cookie: AltinnStudioRuntime=' "$H" | sed -E 's/^Set-Cookie: AltinnStudioRuntime=([^;]*).*/\1/')
check "refresh with the cookie JWT" "200" "$(code -H "Authorization: Bearer $TOKEN" "$BASE/authentication/api/v1/refresh")"

USR=$(curl -s "$BASE/Home/GetTestUserToken?userId=1337" | tr -d '"'); U2=$(curl -s "$BASE/Home/GetTestUserToken?userId=1003" | tr -d '"')
ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write" | tr -d '"')
check "org token through exchange/maskinporten" "ttd" "$(python3 -c "import sys,base64,json; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get('urn:altinn:org'))" "$ORG")"

# What the app runner pushes before an app is used (see README.md): metadata and texts through production Storage.
FX=$(dirname "$0")/smoke/apps/ttd/testapp
DES=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
check "studio token carries urn:altinn:app=studio.designer" "studio.designer" "$(python3 -c "import sys,base64,json; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get('urn:altinn:app'))" "$DES")"
check "storage: an org token is not a Studio token (403)" "403" "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/testapp" --data-binary @"$FX/applicationmetadata.json")"
MC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications?appId=ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
[[ "$MC" == "201" ]] || MC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
check "storage: runner pushes application metadata (POST or PUT)" "yes" "$([[ "$MC" == 20? ]] && echo yes || echo "$MC")"
TC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications/ttd/testapp/texts" --data-binary @"$FX/texts/resource.nb.json")
[[ "$TC" == "201" ]] || TC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/testapp/texts/nb" --data-binary @"$FX/texts/resource.nb.json")
check "storage: runner pushes texts (POST or PUT)" "yes" "$([[ "$TC" == 20? ]] && echo yes || echo "$TC")"
check "storage: pushed metadata is served" "attachment" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/applications/ttd/testapp" | python3 -c 'import sys,json; print("attachment" if any(d["id"] == "attachment" for d in json.load(sys.stdin)["dataTypes"]) else "missing")')"
check "storage: pushed texts are served (texts/nb)" "Testapp (pushed)" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/applications/ttd/testapp/texts/nb" | python3 -c 'import sys,json; print(json.load(sys.stdin)["resources"][0]["value"])')"
check "storage: GET applications/ttd lists ttd/testapp" "yes" "$(curl -s "$BASE/storage/api/v1/applications/ttd" | python3 -c 'import sys,json; print("yes" if "ttd/testapp" in [a["id"] for a in json.load(sys.stdin)["applications"]] else "no")')"
PC=$(sed -e 's/\[ORG\]/ttd/g' -e 's/\[org\]/ttd/g' -e 's/\[APP\]/testapp/g' -e 's/\[app\]/testapp/g' "$FX/policy.xml" | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $DES" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)
check "authorization: runner pushes the app policy" "200" "$PC"
check "proxy: unregistered app path is a 404" "404" "$(code "$BASE/ttd/not-running/api/v1/applicationmetadata")"

dec() { curl -s "$BASE/authorization/api/localtest/decision?userId=$1&partyId=$2&org=ttd&app=testapp&action=$3" | python3 -c 'import sys,json; print(json.load(sys.stdin)["response"][0]["decision"])'; }
check "PDP: Sophie (DAGL) may read ttd/testapp for 500000" "Permit" "$(dec 1337 500000 read)"
decided "PDP: Sophie has nothing on 500005" "NotApplicable" "$([[ "$MINIMAL" == 1 ]] || dec 1337 500005 read)"

R=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/instances?appId=ttd/testapp" -d '{"instanceOwner":{"partyId":"500000"},"appId":"ttd/testapp","org":"ttd"}')
check "storage: user creates an instance for 500000" "201" "$(echo "$R" | tail -1)"
ID=$(echo "$R" | head -1 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))')
decided "storage: instance for 500005 is refused by the PDP" "403" "$([[ "$MINIMAL" == 1 ]] || code -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/instances?appId=ttd/testapp" -d '{"instanceOwner":{"partyId":"500005"},"appId":"ttd/testapp","org":"ttd"}')"
check "storage: the app starts the process (org token)" "200" "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","currentTask":{"flow":2,"elementId":"Task_1","name":"Utfylling","altinnTaskType":"data","started":"2026-10-17T07:00:00Z"}}')"
DR=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: text/plain' -H 'Content-Disposition: attachment; filename=hello.txt' -X POST --data-binary 'hello localtest' "$BASE/storage/api/v1/instances/$ID/data?dataType=attachment")
check "storage: user uploads data in Task_1" "201" "$(echo "$DR" | tail -1)"
DID=$(echo "$DR" | head -1 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))')
check "storage: user reads the data back" "hello localtest" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/instances/$ID/data/$DID")"
check "storage: file scan result" "Clean" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/instances/$ID" | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"][0]["fileScanResult"])')"
decided "storage: user without the role cannot upload" "403" "$([[ "$MINIMAL" == 1 ]] || code -H "Authorization: Bearer $U2" -H 'Content-Type: text/plain' -X POST --data-binary x "$BASE/storage/api/v1/instances/$ID/data?dataType=attachment")"
check "storage: the app ends the process" "200" "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","ended":"2026-10-17T07:05:00Z","endEvent":"EndEvent_1"}')"
check "storage: service owner lists instances" "200" "$(code -H "Authorization: Bearer $ORG" "$BASE/storage/api/v1/instances?org=ttd&appId=ttd/testapp")"
# Storage's own log since the start of this run (what the removed standalone Storage smoke test checked).
if RT=$(localtest_runtime) && SC=$(localtest_container storage); then
  SL=$(mktemp); "$RT" logs --since "$STARTED" "$SC" >"$SL" 2>&1
  check "storage: no fail/crit/unhandled exception lines in its log" "0" "$(grep -ciE '^(fail|crit):|unhandled exception' "$SL")"
  grep -iE '^(fail|crit):|unhandled exception' -A1 "$SL" | cut -c1-240 | head -8; rm -f "$SL"
else
  echo "skip storage: no errors in its log (no container runtime or no Storage container)"
fi
# OTLP (TODO.md task 53): with the monitoring group up, studioctl sets OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_SERVICE_NAME,
# so a request with our traceparent shows up as a server span named storage in VictoriaTraces (smoke/lib/traces.sh).
# shellcheck source-path=SCRIPTDIR source=smoke/lib/traces.sh
. "$(cd "$(dirname "$0")" && pwd)/smoke/lib/traces.sh"
if localtest_monitoring_up "$BASE"; then
  read -r TRACE TP < <(localtest_traceparent)
  curl -s -o /dev/null -H "traceparent: $TP" "$BASE/storage/api/v1/applications/ttd"
  SPANS=$(localtest_wait_spans "$BASE" "$TRACE" storage)
  check "OTLP: a server span from service.name storage" yes "$([[ " $SPANS" == *" 2:"* ]] && echo yes || echo "no: $SPANS")"
else
  echo "skip the OTLP span check (the monitoring group is not up)"
fi
rm -f "$J" "$H"
exit $FAIL
