#!/usr/bin/env bash
# Smoke test for the dialogporten-adapter component in the running stack, everything through the proxy except the
# adapter's /health (no public route; read on its host port). Two modes:
#
#   ./smoke-test.sh [BASE]          the stack is up WITH the group:  studioctl env up -r localtest-v2 --with dialogporten
#   ./smoke-test.sh --off [BASE]    the stack is up WITHOUT the group (the default stack)
#
# With the group: pushes the ttd/testapp fixture the way the app runner does (Studio token, metadata and texts to Storage,
# policy to the PDP, the Resource Registry's subject reload, Dialogporten's localtest sync), then follows one instance of
# party PARTY_ID (default 500000, which user 1337 acts for) from Storage's outbox to a dialog:
#   instance created -> dialog within 10 s (resource, party, go-to link) and dialog.id written back to the instance;
#   process started and a data element uploaded -> the dialog has the attachment on the stack's origin;
#   process ended with a Submited event -> the dialog's status changes and it has a transmission with a receipt link;
#   the end user's dialog token (code-flow token for user 1337) -> the adapter's receipt (200 Markdown, 401 without);
#   syncDialog/simple twice -> 204, no new revision;
#   DELETE storage/dialogporten/api/v1/instance with the dialog token -> Storage soft delete -> outbox -> dialog deleted.
# Without the group: the front door answers 503 problem details for the adapter's prefix, an instance is created as usual, gets no dialog.id,
# and (when podman or docker can read the container's log) Storage logs exactly one "adapter is not running" line for it.
# Instances are left in Storage (soft-deleted in the first mode); dialogs persist like instances (env reset removes both).
#
# PARTY_ID overrides the instance owner; DIALOGPORTEN_ADAPTER_URL the adapter's URL for /health (by default it is read from
# inside the stack's network; http://127.0.0.1:8035 under the Aspire package);
# STORAGE_CONTAINER (or LOCALTEST_CONTAINER_STORAGE) the Storage container for the log check (default: studioctl's
# localtest-v2-storage or the Aspire package's storage-<suffix>, see ../../smoke/lib/containers.sh).
set -uo pipefail
shopt -s lastpipe # run `... | check` in this shell, so its ok and FAIL count
OFF=0
if [[ "${1:-}" == "--off" ]]; then OFF=1; shift; fi
HERE=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=../../smoke/lib/containers.sh
. "$HERE/../../smoke/lib/containers.sh"
FX="$HERE/../../smoke/apps/ttd/testapp"
BASE=${1:-http://local.altinn.cloud:8000}
PARTY_ID=${PARTY_ID:-500000}
ADAPTER_DIRECT=${DIALOGPORTEN_ADAPTER_URL:-}
ADP="$BASE/storage/dialogporten"
ST="$BASE/storage/api/v1"
DP="$BASE/dialogporten"
SO="$DP/api/v1/serviceowner"
FAIL=0
OK=0
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT
pass() { echo "ok   $1"; OK=$((OK + 1)); }
fail() { echo "FAIL $1"; FAIL=1; }
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then pass "$1"; else fail "$1"; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then pass "$1"; else fail "$1 (expected $2, got $3)"; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
# adapter_health: the adapter's own /health, at DIALOGPORTEN_ADAPTER_URL when set, else from inside the stack's network
# (000 when there is no adapter).
adapter_health() {
  if [[ -n "$ADAPTER_DIRECT" ]]; then code --max-time 3 "$ADAPTER_DIRECT/health"; else localtest_stack_code http://dialogporten-adapter:5011/health; fi
}
py() { python3 -c "import json,sys; d=json.load(sys.stdin); print($1)" 2>/dev/null; }
orgtoken() { curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=$1" | tr -d '"'; }
# wait_until seconds url token python-expression-on-d: polls a JSON GET until the expression holds; leaves the body in $T/w.json
wait_until() {
  local deadline=$((SECONDS + $1))
  while ((SECONDS <= deadline)); do
    curl -s -H "Authorization: Bearer $3" "$2" >"$T/w.json"
    python3 -c "import json,sys; d=json.load(open('$T/w.json')); sys.exit(0 if ($4) else 1)" 2>/dev/null && return 0
    sleep 1
  done
  return 1
}

USR=$(curl -s "$BASE/Home/GetTestUserToken?userId=1337&authenticationLevel=3" | tr -d '"')
APP=$(orgtoken "altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write")
create_instance() {
  curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$ST/instances?appId=ttd/testapp" \
    -d "{\"instanceOwner\":{\"partyId\":\"$PARTY_ID\"},\"appId\":\"ttd/testapp\",\"org\":\"ttd\"}"
}

if ((OFF)); then
  echo "== the default stack: the dialogporten group is off"
  expect "no adapter answers" 000 "$(adapter_health)"
  # The front door writes the adapter's route disabled and answers 503 problem details for it; Storage's handler takes
  # that for "not running".
  curl -s -D "$T/oh" -o "$T/off.json" -X POST -H 'Content-Type: application/json' -d '{}' "$ADP/api/localtest/enqueue"
  expect "POST storage/dialogporten/api/localtest/enqueue: 503 from the front door" 503 "$(head -1 "$T/oh" | awk '{print $2}')"
  check "  ... problem details naming component dialogporten-adapter and group dialogporten" \
    'd["status"] == 503 and d["component"] == "dialogporten-adapter" and d["group"] == "dialogporten"' <"$T/off.json"
  SINCE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  R=$(create_instance)
  expect "instance created as usual" 201 "${R##*$'\n'}"
  ID=$(printf '%s' "${R%$'\n'*}" | py 'd["id"]')
  sleep 5
  curl -s -H "Authorization: Bearer $USR" "$ST/instances/$ID" | check "  ... and no dialog.id after 5 s" '"dialog.id" not in (d.get("dataValues") or {})'
  if RT=$(localtest_runtime) && SC=$(localtest_container storage); then
    LINES=$("$RT" logs --since "$SINCE" "$SC" 2>&1 | grep -c "instance $ID dropped: the Dialogporten adapter is not running")
    expect "  ... Storage logged exactly one 'adapter is not running' line for it" 1 "$LINES"
    expect "  ... and no warning or error from the outbox or its handler" 0 \
      "$("$RT" logs --since "$SINCE" "$SC" 2>&1 | grep -E "^(warn|fail|crit)" -A1 | grep -ciE "outbox|Dialogporten")"
  else
    echo "skip Storage log check (no container runtime or no Storage container)"
  fi
  echo "$OK passed, $([[ $FAIL == 0 ]] && echo "no failures" || echo "FAILURES")"
  exit $FAIL
fi

echo "== readiness"
for _ in $(seq 1 60); do [[ "$(adapter_health)" == 200 ]] && break; sleep 1; done
expect "adapter /health (the adapter itself)" 200 "$(adapter_health)"
expect "Dialogporten WebApi readiness" 200 "$(code "$DP/health/readiness")"

echo "== the fixture app, pushed as the runner does"
STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
MC=$(code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$ST/applications?appId=ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
[[ "$MC" == 201 ]] || MC=$(code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$ST/applications/ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
expect "metadata to Storage (POST or PUT)" yes "$([[ "$MC" == 20? ]] && echo yes || echo "$MC")"
for f in "$FX"/texts/resource.*.json; do
  [[ -e "$f" ]] || continue
  lang=$(basename "$f" .json); lang=${lang#resource.}
  TC=$(code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$ST/applications/ttd/testapp/texts" --data-binary @"$f")
  [[ "$TC" == 201 ]] || TC=$(code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$ST/applications/ttd/testapp/texts/$lang" --data-binary @"$f")
  expect "texts ($lang) to Storage (POST or PUT)" yes "$([[ "$TC" == 20? ]] && echo yes || echo "$TC")"
done
policy=$(sed -e "s/\[ORG\]/ttd/g" -e "s/\[org\]/ttd/g" -e "s/\[APP\]/testapp/g" -e "s/\[app\]/testapp/g" "$FX/policy.xml")
expect "policy to the PDP" 200 "$(printf '%s' "$policy" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
expect "Resource Registry reloads the subjects" 200 "$(code "$BASE/resourceregistry/api/v1/resource/app_ttd_testapp/policy/subjects?reloadFromXacml=true")"
expect "Dialogporten localtest sync (the provisioner's step 5)" 200 "$(code -X POST "$DP/api/v1/localtest/sync")"

echo "== authentication on the adapter's endpoints"
SYNC=$(orgtoken "altinn:storage/instances.syncadapter")
SOT=$(orgtoken "digdir:dialogporten.serviceprovider%20digdir:dialogporten.serviceprovider.search")
expect "POST syncDialog without a token: 401" 401 "$(code -X POST -H 'Content-Type: application/json' -d '{}' "$ADP/api/v1/syncDialog")"
expect "POST syncDialog with a user token: 403 (no sync-adapter scope)" 403 "$(code -X POST -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -d '{}' "$ADP/api/v1/syncDialog")"
# Anonymous, as every api/localtest endpoint; a body that is not JSON shows that without enqueueing anything.
expect "POST api/localtest/enqueue is anonymous (malformed body: 400, not 401)" 400 "$(code -X POST -H 'Content-Type: application/json' -d x "$ADP/api/localtest/enqueue")"

echo "== instance created -> dialog"
R=$(create_instance)
expect "POST instances for party $PARTY_ID as user 1337" 201 "${R##*$'\n'}"
ID=$(printf '%s' "${R%$'\n'*}" | py 'd["id"]')
if wait_until 10 "$ST/instances/$ID" "$SYNC" '"dialog.id" in (d.get("dataValues") or {})'; then
  pass "  ... dataValues[\"dialog.id\"] written back within 10 s"
else
  fail "  ... dataValues[\"dialog.id\"] written back within 10 s"
fi
DIALOG=$(py 'd["dataValues"]["dialog.id"]' <"$T/w.json")
curl -s -H "Authorization: Bearer $SOT" "$SO/dialogs/$DIALOG" >"$T/d1.json"
check "service-owner GET dialogs/$DIALOG" 'd["id"] == "'"$DIALOG"'"' <"$T/d1.json"
check "  ... serviceResource urn:altinn:resource:app_ttd_testapp" 'd["serviceResource"] == "urn:altinn:resource:app_ttd_testapp"' <"$T/d1.json"
check "  ... party is the instance owner's urn (organisation or person)" 'd["party"].startswith(("urn:altinn:organization:identifier-no:", "urn:altinn:person:identifier-no:"))' <"$T/d1.json"
GOTO_PREFIX="$BASE/authentication/api/v1/authentication?goto="
check "  ... go-to action: authentication?goto= the app's instance page on the stack's origin" \
  'any(a["url"].startswith("'"$GOTO_PREFIX"'") and __import__("urllib.parse").parse.unquote(a["url"][len("'"$GOTO_PREFIX"'"):]).startswith("'"$BASE"'/ttd/testapp/?dontChooseReportee=true#/instance/'"$ID"'") for a in d["guiActions"] if a["priority"] == "Primary")' <"$T/d1.json"
check "  ... delete action on the adapter's public prefix" \
  'any(a["url"] == "'"$ADP"'/api/v1/instance/'"$ID"'" and a["httpMethod"] == "DELETE" for a in d["guiActions"])' <"$T/d1.json"
STATUS1=$(py 'd["status"]' <"$T/d1.json")
REV1=$(py 'd["revision"]' <"$T/d1.json")
echo "     status $STATUS1"

echo "== process started, data uploaded -> attachment"
TASK='{"flow":2,"elementId":"Task_1","name":"Utfylling","altinnTaskType":"data","started":"2026-10-17T07:00:00Z"}'
EV() { # eventType: an instance event as the app posts it, with the process info (the adapter reads the task of EndTask events)
  printf '{"instanceId":"%s","instanceOwnerPartyId":"%s","eventType":"%s","user":{"userId":1337,"authenticationLevel":3},"processInfo":{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","currentTask":%s}}' \
    "$ID" "$PARTY_ID" "$1" "$TASK"
}
START="{\"started\":\"2026-10-17T07:00:00Z\",\"startEvent\":\"StartEvent_1\",\"currentTask\":$TASK}"
expect "the app starts the process (instanceandevents, org token)" 200 \
  "$(code -H "Authorization: Bearer $APP" -H 'Content-Type: application/json' -X PUT "$ST/instances/$ID/process/instanceandevents" -d "{\"state\":$START,\"events\":[$(EV process_StartEvent),$(EV process_StartTask)]}")"
expect "user 1337 uploads an attachment" 201 \
  "$(code -H "Authorization: Bearer $USR" -H 'Content-Type: text/plain' -H 'Content-Disposition: attachment; filename=hello.txt' -X POST --data-binary 'hello dialog' "$ST/instances/$ID/data?dataType=attachment")"
if wait_until 10 "$SO/dialogs/$DIALOG" "$SOT" 'len(d["attachments"]) >= 1'; then pass "  ... the dialog has the attachment within 10 s"; else fail "  ... the dialog has the attachment within 10 s"; fi
check "  ... every attachment URL is on the stack's origin (no platform. sub-domain)" \
  'all(u["url"].startswith("'"$BASE"'/") and "platform." not in u["url"] for a in d["attachments"] for u in a["urls"])' <"$T/w.json"
check "  ... the dialog has a new revision" 'd["revision"] != "'"$REV1"'"' <"$T/w.json"

echo "== process ended with a submission -> status and transmission"
END='{"started":"2026-10-17T07:00:00Z","startEvent":"StartEvent_1","ended":"2026-10-17T07:05:00Z","endEvent":"EndEvent_1"}'
expect "the app ends the process (instanceandevents with Submited)" 200 \
  "$(code -H "Authorization: Bearer $APP" -H 'Content-Type: application/json' -X PUT "$ST/instances/$ID/process/instanceandevents" -d "{\"state\":$END,\"events\":[$(EV process_EndTask),$(EV Submited),$(EV process_EndEvent)]}")"
if wait_until 10 "$SO/dialogs/$DIALOG" "$SOT" 'd["status"] != "'"$STATUS1"'" and len(d["transmissions"]) >= 1'; then
  pass "  ... status changed and a transmission exists within 10 s"
else
  fail "  ... status changed and a transmission exists within 10 s"
fi
echo "     status $(py 'd["status"]' <"$T/w.json")"
RECEIPT=$(py '[v["value"] for t in d["transmissions"] for v in t["content"]["contentReference"]["value"] if v["languageCode"] == "nb"][0]' <"$T/w.json")
check "  ... the transmission's receipt is the adapter's, on the stack's origin" \
  'any(v["value"].startswith("'"$ADP"'/api/v1/receipt/'"$DIALOG"'/") for t in d["transmissions"] for v in t["content"]["contentReference"]["value"])' <"$T/w.json"

echo "== the end user's dialog token"
END_USER=$(curl -s "$BASE/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3" | py 'd["access_token"]')
curl -s -H "Authorization: Bearer $END_USER" "$DP/api/v1/enduser/dialogs/$DIALOG" >"$T/e.json"
check "end-user GET dialogs/$DIALOG with the code-flow token returns a dialogToken" 'len(d.get("dialogToken") or "") > 0' <"$T/e.json"
DTOKEN=$(py 'd["dialogToken"]' <"$T/e.json")
curl -s -D "$T/rh" -o "$T/receipt.md" -H "Authorization: Bearer $DTOKEN" "$RECEIPT"
expect "GET the transmission receipt with the dialog token" 200 "$(head -1 "$T/rh" | awk '{print $2}')"
expect "  ... as Markdown" yes "$(grep -qi '^content-type: text/markdown' "$T/rh" && echo yes || echo no)"
expect "  ... not empty" yes "$([[ -s "$T/receipt.md" ]] && echo yes || echo no)"
expect "GET the receipt without a token: 400 (the Authorization header is a required parameter)" 400 "$(code "$RECEIPT")"
expect "GET the receipt with a user token instead of a dialog token: 401" 401 "$(code -H "Authorization: Bearer $USR" "$RECEIPT")"

echo "== manual re-sync is idempotent"
REV2=$(curl -s -H "Authorization: Bearer $SOT" "$SO/dialogs/$DIALOG" | py 'd["revision"]')
expect "POST syncDialog/simple/$ID (sync-adapter token)" 204 "$(code -X POST -H "Authorization: Bearer $SYNC" "$ADP/api/v1/syncDialog/simple/$ID")"
expect "  ... again" 204 "$(code -X POST -H "Authorization: Bearer $SYNC" "$ADP/api/v1/syncDialog/simple/$ID")"
expect "  ... the dialog's revision is unchanged" "$REV2" "$(curl -s -H "Authorization: Bearer $SOT" "$SO/dialogs/$DIALOG" | py 'd["revision"]')"

echo "== delete with the dialog token -> Storage soft delete -> dialog deleted"
# The endpoint binds the Authorization header as a required parameter, so without one the answer is 400 (production code).
expect "DELETE storage/dialogporten/api/v1/instance/$ID without a token: 400" 400 "$(code -X DELETE "$ADP/api/v1/instance/$ID")"
expect "DELETE storage/dialogporten/api/v1/instance/$ID with a user token: 401" 401 "$(code -X DELETE -H "Authorization: Bearer $USR" "$ADP/api/v1/instance/$ID")"
expect "DELETE storage/dialogporten/api/v1/instance/$ID with the dialog token" 204 "$(code -X DELETE -H "Authorization: Bearer $DTOKEN" "$ADP/api/v1/instance/$ID")"
curl -s -H "Authorization: Bearer $SYNC" "$ST/instances/$ID" | check "  ... Storage: status.isSoftDeleted" 'd["status"]["isSoftDeleted"] is True'
if wait_until 10 "$SO/dialogs/$DIALOG" "$SOT" 'd.get("deletedAt") is not None'; then
  pass "  ... the service owner sees the dialog deleted within 10 s"
else
  fail "  ... the service owner sees the dialog deleted within 10 s"
fi

echo "$OK passed, $([[ $FAIL == 0 ]] && echo "no failures" || echo "FAILURES")"
exit $FAIL
