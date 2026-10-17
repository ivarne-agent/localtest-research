#!/usr/bin/env bash
# End-to-end check of the compose stack through the ingress: sign-in, tokens, PDP decisions and a full instance
# lifecycle on production Storage. Requires `docker compose up -d` (or podman-compose) and local.altinn.cloud in /etc/hosts.
set -u
BASE=${BASE:-http://local.altinn.cloud:8000}
FAIL=0
check() { local label=$1 expected=$2 actual=$3; if [[ "$actual" == "$expected" ]]; then printf 'ok   %-60s %s\n' "$label" "$actual"; else printf 'FAIL %-60s %s (expected %s)\n' "$label" "$actual" "$expected"; FAIL=1; fi; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

for i in $(seq 1 60); do curl -sf -o /dev/null "$BASE/health" && curl -sf -o /dev/null "$BASE/authentication/api/v1/openid/.well-known/openid-configuration" && break; sleep 1; done

# Sign-in through the picker, as a browser would.
J=$(mktemp); LOC=$(curl -s -o /dev/null -w '%{redirect_url}' -c "$J" "$BASE/authentication/api/v1/authentication?goTo=$BASE/ttd/testapp/")
check "authentication redirects to the picker" "localtest/login" "$(python3 -c "import sys,urllib.parse as u; print(u.urlparse(sys.argv[1]).path.split('/v1/')[-1])" "$LOC")"
q() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$LOC" "$1"; }
CB=$(curl -s -o /dev/null -w '%{redirect_url}' -b "$J" -c "$J" -X POST "$BASE/authentication/api/v1/localtest/login" --data-urlencode "state=$(q state)" --data-urlencode "nonce=$(q nonce)" --data-urlencode "redirect_uri=$(q redirect_uri)" --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
H=$(mktemp); curl -s -b "$J" -c "$J" -D "$H" -o /dev/null "$CB"
check "callback redirects to goTo" "$BASE/ttd/testapp/" "$(grep -i '^Location:' "$H" | tr -d '\r' | awk '{print $2}')"
check "cookie JWT set without Secure" "yes" "$(grep -i '^Set-Cookie: AltinnStudioRuntime=' "$H" | grep -qi 'secure' && echo no || echo yes)"
TOKEN=$(grep -i '^Set-Cookie: AltinnStudioRuntime=' "$H" | sed -E 's/^Set-Cookie: AltinnStudioRuntime=([^;]*).*/\1/')
check "refresh with the cookie JWT" "200" "$(code -H "Authorization: Bearer $TOKEN" "$BASE/authentication/api/v1/refresh")"

USR=$(curl -s "$BASE/Home/GetTestUserToken?userId=1337" | tr -d '"'); U2=$(curl -s "$BASE/Home/GetTestUserToken?userId=1003" | tr -d '"')
ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write" | tr -d '"')
check "org token through exchange/maskinporten" "ttd" "$(python3 -c "import sys,base64,json; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get('urn:altinn:org'))" "$ORG")"

dec() { curl -s "$BASE/authorization/api/localtest/decision?userId=$1&partyId=$2&org=ttd&app=testapp&action=$3" | python3 -c 'import sys,json; print(json.load(sys.stdin)["response"][0]["decision"])'; }
check "PDP: Sophie (DAGL) may read ttd/testapp for 500000" "Permit" "$(dec 1337 500000 read)"
check "PDP: Sophie has nothing on 500005" "NotApplicable" "$(dec 1337 500005 read)"

R=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/instances?appId=ttd/testapp" -d '{"instanceOwner":{"partyId":"500000"},"appId":"ttd/testapp","org":"ttd"}')
check "storage: user creates an instance for 500000" "201" "$(echo "$R" | tail -1)"
ID=$(echo "$R" | head -1 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))')
check "storage: instance for 500005 is refused by the PDP" "403" "$(code -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/instances?appId=ttd/testapp" -d '{"instanceOwner":{"partyId":"500005"},"appId":"ttd/testapp","org":"ttd"}')"
check "storage: the app starts the process (org token)" "200" "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","currentTask":{"flow":2,"elementId":"Task_1","name":"Utfylling","altinnTaskType":"data","started":"2026-10-17T07:00:00Z"}}')"
DR=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: text/plain' -H 'Content-Disposition: attachment; filename=hello.txt' -X POST --data-binary 'hello localtest' "$BASE/storage/api/v1/instances/$ID/data?dataType=attachment")
check "storage: user uploads data in Task_1" "201" "$(echo "$DR" | tail -1)"
DID=$(echo "$DR" | head -1 | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))')
check "storage: user reads the data back" "hello localtest" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/instances/$ID/data/$DID")"
check "storage: file scan result" "Clean" "$(curl -s -H "Authorization: Bearer $USR" "$BASE/storage/api/v1/instances/$ID" | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"][0]["fileScanResult"])')"
check "storage: user without the role cannot upload" "403" "$(code -H "Authorization: Bearer $U2" -H 'Content-Type: text/plain' -X POST --data-binary x "$BASE/storage/api/v1/instances/$ID/data?dataType=attachment")"
check "storage: the app ends the process" "200" "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","ended":"2026-10-17T07:05:00Z","endEvent":"EndEvent_1"}')"
check "storage: service owner lists instances" "200" "$(code -H "Authorization: Bearer $ORG" "$BASE/storage/api/v1/instances?org=ttd&appId=ttd/testapp")"
rm -f "$J" "$H"
exit $FAIL
