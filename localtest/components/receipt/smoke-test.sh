#!/usr/bin/env bash
# Smoke test for the receipt component (production Altinn Receipt, optional group `receipt`) in the running stack,
# everything through the proxy except the health check. Creates its own archived instance of ttd/testapp for party
# 500000 as user 1337 (one attachment, the process ended by the service owner), the way ../../smoke-test.sh does, and
# reads it through the receipt as the browser would. Requires the stack with the group
# (`studioctl env up -r localtest-v2 --with receipt`, see ../../README.md) and local.altinn.cloud in /etc/hosts.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# The log check reads the receipt's container (studioctl's localtest-v2-receipt or the Aspire package's receipt-<suffix>,
# see ../../smoke/lib/containers.sh; override with RECEIPT_CONTAINER or LOCALTEST_CONTAINER_RECEIPT) through podman or
# docker, and is skipped without them.
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=../../smoke/lib/containers.sh
. "$HERE/../../smoke/lib/containers.sh"
BASE=${1:-http://local.altinn.cloud:8000}
HEALTH=${RECEIPT_HEALTH:-}
STARTED=$(date -u +%Y-%m-%dT%H:%M:%SZ)
FAIL=0
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then echo "ok   $1"; else echo "FAIL $1"; FAIL=1; fi
}
code() { curl -s -o /dev/null -m 20 -w '%{http_code}' "$@"; }
# health: the receipt's own /health, at RECEIPT_HEALTH when set (its host port under the Aspire package,
# http://127.0.0.1:8034/health), else from inside the stack's network.
health() { if [[ -n "$HEALTH" ]]; then curl -s -m 10 "$HEALTH"; else localtest_stack_body http://receipt:5060/health; fi; }
location() { curl -s -o /dev/null -m 20 -w '%{redirect_url}' "$@"; }
token() { curl -s "$BASE/Home/GetTestUserToken?userId=$1" | tr -d '"'; }
query() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$1" "$2"; }
path() { python3 -c "import sys,urllib.parse as u; p=u.urlparse(sys.argv[1]); print(f'{p.scheme}://{p.netloc}{p.path}')" "$1"; }

# 1. Health, on the component itself (the receipt's /health is at its root, not under /receipt).
for _ in $(seq 1 60); do [[ "$(health)" == Healthy ]] && break; sleep 1; done
expect "health" "Healthy" "$(health)"

# 2. An archived instance: the fixture app's metadata and policy as the app runner pushes them, an instance for 500000,
# one attachment, the process started and ended with the service-owner token.
FX=$HERE/../../smoke/apps/ttd/testapp
USR=$(token 1337); U1002=$(token 1002)
ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write" | tr -d '"')
DES=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
MC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications?appId=ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
[[ "$MC" == "201" ]] || MC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
expect "set-up: application metadata pushed" "yes" "$([[ "$MC" == 20? ]] && echo yes || echo "$MC")"
TC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications/ttd/testapp/texts" --data-binary @"$FX/texts/resource.nb.json")
[[ "$TC" == "201" ]] || TC=$(code -H "Authorization: Bearer $DES" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/testapp/texts/nb" --data-binary @"$FX/texts/resource.nb.json")
expect "set-up: texts pushed" "yes" "$([[ "$TC" == 20? ]] && echo yes || echo "$TC")"
expect "set-up: policy pushed" 200 "$(sed -e 's/\[ORG\]/ttd/g' -e 's/\[org\]/ttd/g' -e 's/\[APP\]/testapp/g' -e 's/\[app\]/testapp/g' "$FX/policy.xml" \
  | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $DES" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
curl -s -o /dev/null -X POST "$BASE/authorization/api/localtest/refresh"
R=$(curl -s -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/instances?appId=ttd/testapp" -d '{"instanceOwner":{"partyId":"500000"},"appId":"ttd/testapp","org":"ttd"}')
ID=$(echo "$R" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))' 2>/dev/null)
expect "set-up: instance created for 500000" "500000" "${ID%%/*}"
GUID=${ID#*/}
expect "set-up: process started" 200 "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-20T07:00:00Z","startEvent":"StartEvent_1","currentTask":{"flow":2,"elementId":"Task_1","name":"Utfylling","altinnTaskType":"data","started":"2026-10-20T07:00:00Z"}}')"
BYTES="hello receipt $(date +%s)"
expect "set-up: attachment uploaded" 201 "$(code -H "Authorization: Bearer $USR" -H 'Content-Type: text/plain' -H 'Content-Disposition: attachment; filename=hello.txt' -X POST --data-binary "$BYTES" "$BASE/storage/api/v1/instances/$ID/data?dataType=attachment")"
expect "set-up: process ended" 200 "$(code -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/instances/$ID/process" -d '{"started":"2026-10-20T07:00:00Z","startEvent":"StartEvent_1","ended":"2026-10-20T07:05:00Z","endEvent":"EndEvent_1"}')"

RECEIPT="$BASE/receipt/500000/$GUID"
API="$BASE/receipt/api/v1"
COOKIE="AltinnStudioRuntime=$USR"

# 3. Without a cookie: sign-in through Authentication with the request's own origin as goto, and back with one.
LOC=$(location "$RECEIPT")
expect "no cookie: 302 to Authentication" "$BASE/authentication/api/v1/authentication" "$(path "$LOC")"
expect "no cookie: goto is the receipt on the public origin" "$RECEIPT" "$(query "$LOC" goto)"
expect "signed in: Authentication sends the browser back" "$RECEIPT" "$(location -b "$COOKIE" "$LOC")"

# 4. The page and its bundle.
PAGE=$(curl -s -m 20 -b "$COOKIE" -w '\n%{http_code} %{content_type}' "$RECEIPT")
expect "page 200 text/html" "200 text/html; charset=utf-8" "$(echo "$PAGE" | tail -1)"
expect "page loads the bundle" "yes" "$(echo "$PAGE" | grep -q 'receipt/js/react/receipt.js' && echo yes || echo no)"
expect "bundle" 200 "$(code "$BASE/receipt/js/react/receipt.js")"
expect "stylesheet" 200 "$(code "$BASE/receipt/css/receipt.css")"

# 5. The user through Profile, with the platform access token the shared certificate signs.
curl -s -m 20 -b "$COOKIE" "$API/users/current" | check "users/current is user 1337 (Profile with the platform access token)" 'd["userId"] == 1337'

# 6. The archived instance with its owner, and attachment links on the public origin that return the bytes.
INST=$(curl -s -m 20 -b "$COOKIE" "$API/instances/500000/$GUID?includeParty=true")
echo "$INST" | check "instance: party 500000 from Register" 'd["party"]["partyId"] == 500000'
echo "$INST" | check "instance: archived" 'd["instance"]["status"]["isArchived"] is True'
echo "$INST" | check "instance: every selfLinks.platform on $BASE/storage/api/v1/instances/" \
  "d['instance']['data'] and all(e['selfLinks']['platform'].startswith('$BASE/storage/api/v1/instances/') for e in d['instance']['data'])"
LINK=$(echo "$INST" | python3 -c 'import sys,json; print(json.load(sys.stdin)["instance"]["data"][0]["selfLinks"]["platform"])' 2>/dev/null)
expect "the rewritten link returns the attachment with the cookie" "$BYTES" "$(curl -s -m 20 -b "$COOKIE" "$LINK")"

# 7. Language from the Altinn 2 cookie, which nothing local sets.
expect "language: 204 without altinnPersistentContext" 204 "$(code -b "$COOKIE" "$API/users/current/language")"
curl -s -m 20 -b "$COOKIE; altinnPersistentContext=UL=1033" "$API/users/current/language" | check "language: en with UL=1033" 'd["language"] == "en"'

# 8. The attachment groups the receipt hides (production's list).
curl -s -m 20 -b "$COOKIE" "$API/application/attachmentgroupstohide" | check "attachment groups to hide: the five production groups" \
  'len(d["attachmentgroupstohide"].split(";")) == 5'

# 9. Access is Storage's and Register's: a user without rights, an unknown instance, an API call without a cookie.
expect "user 1002: 403" 403 "$(code -b "AltinnStudioRuntime=$U1002" "$API/instances/500000/$GUID?includeParty=true")"
expect "unknown instance: 404" 404 "$(code -b "$COOKIE" "$API/instances/500000/$(cat /proc/sys/kernel/random/uuid)?includeParty=true")"
expect "API without a cookie: 302 to sign-in" 302 "$(code "$API/users/current")"

# 10. The platform access token was signed (the certificate is bind-mounted where the generator looks).
if RT=$(localtest_runtime) && CONTAINER=$(localtest_container receipt); then
  WARNINGS=$("$RT" logs --since "$STARTED" "$CONTAINER" 2>&1 | grep -c 'Not able to generate access token')
  expect "no access-token warning in the receipt's log" 0 "$WARNINGS"
else
  echo "skip the log check (no container runtime or no receipt container)"
fi

echo "instance: $ID"
exit $FAIL
