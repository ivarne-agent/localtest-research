#!/usr/bin/env bash
# Smoke test for the correspondence component (production Altinn Correspondence, the API host) in the running stack,
# everything through the proxy except /health on the host port. The stack must be up with the optional group, which also
# starts Dialogporten:
#   studioctl env up -r localtest-v2 --with correspondence      (see ../../README.md)
# Covers: health and the migration count (upstream swallows a failed migration, so /health alone proves nothing), the
# refusals (401, 403, 400 CORR-01036 for an unknown resource), app-lib's sequence with its /Home/GetTestOrgToken credential
# (attachment, upload with MD5, the simulated malware scan to Published, the blob in the file store, the correspondence to
# Sophie Salt with an e-mail notification to Published), the dialog in Dialogporten, the e-mail in the Notifications capture,
# the notification condition check with a raw fake-Maskinporten token, a checksum mismatch, an unknown recipient, the
# reserved person (KRR) alone (422 CORR-01030) and with another recipient (Reserved), a
# scheduled publish and the sender's purge of it, the recipient's path with a dialog token (task 2's code-flow token for user
# 1337, the dialog's dialogToken: content, attachment download, markasread, confirm, purge) and the sender's refused purge of
# a published correspondence. With CORRESPONDENCE_RESTART=1 it also restarts the container (podman) and checks that the
# correspondences remain and no migration ran again.
# The resources are the Resource Registry seed's ttd-localtest-correspondence (see ../resource-registry/README.md); KRR and
# Profile's correspondence lookups are the Register component's fakes.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# CORRESPONDENCE_URL overrides the host-port URL for /health (default http://127.0.0.1:8033). CTR (default podman) and the
# container names localtest-v2-correspondence / localtest-v2-postgres are used for the database and restart checks; without a
# container runtime those checks are skipped.
set -uo pipefail
shopt -s lastpipe # run `... | check` in this shell, so its ok and FAIL count
BASE=${1:-http://local.altinn.cloud:8000}
DIRECT=${CORRESPONDENCE_URL:-http://127.0.0.1:8033}
CTR=${CTR:-podman}
API="$BASE/correspondence/api/v1"
LT="$BASE/correspondence/api/localtest"
SO="$BASE/dialogporten/api/v1/serviceowner"
EU="$BASE/dialogporten/api/v1/enduser"
RESOURCE=ttd-localtest-correspondence
SSN=01839012345 # Sophie Salt, user 1337, party 501337
RECIPIENT="urn:altinn:person:identifier-no:$SSN"
EMAIL=1337@altinnstudiotestusers.com
MIGRATIONS=69 # EF migrations at the pin (vendor/UPSTREAM.md); bump on a re-pin
FAIL=0
OK=0
KNOWN=0
pass() { echo "ok   $1"; OK=$((OK + 1)); }
known() { echo "KNOWN $1"; KNOWN=$((KNOWN + 1)); }
fail() { echo "FAIL $1"; FAIL=1; }
check() { # name python-expression-on-d (stdin is JSON)
  local body
  body=$(cat)
  if python3 -I -c "import json,sys; d=json.loads(sys.argv[1]); sys.exit(0 if ($2) else 1)" "$body" 2>/dev/null; then pass "$1"; else fail "$1: ${body:0:400}"; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then pass "$1"; else fail "$1 (expected $2, got $3)"; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
field() { python3 -I -c "import json,sys; d=json.load(sys.stdin); print($1)" 2>/dev/null; }
waitfor() { # seconds url python-expression [curl args...]: polls every second until the expression holds
  local seconds=$1 url=$2 expr=$3; shift 3
  for _ in $(seq 1 "$seconds"); do
    curl -s "$@" "$url" | python3 -I -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($expr) else 1)" 2>/dev/null && return 0
    sleep 1
  done
  return 1
}
okwait() { # name seconds url expression [curl args...]
  local name=$1; shift
  if waitfor "$@"; then pass "$name"; else fail "$name (last: $(curl -s "${@:4}" "$2" | head -c 400))"; fi
}
orgtoken() { curl -s "$BASE/Home/GetTestOrgToken?orgNumber=991825827&scopes=$1" | tr -d '"'; }
md5() { md5sum "$1" | cut -d' ' -f1; }
container_up() { command -v "$CTR" >/dev/null && "$CTR" container exists "$1" 2>/dev/null; }
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# The known gap (README, "Known gaps"): every PDP request Correspondence builds names the party by its party id, and
# production accepts only 8- or 10-digit party ids (StringExtensions.IsPartyId), while the test data's are 6 digits
# (500006, 501337). Every request that reaches the PDP therefore answers 500 "... is not a valid organization number,
# person number or party id". The sender here uses a service owner's direct token (altinn:serviceowner, as a service owner
# calling with Maskinporten would), which production lets past the PDP when the consumer owns the resource; app-lib's
# credential (without altinn:serviceowner) and the recipient's dialog token go to the PDP and are reported as KNOWN.
APPLIB_SCOPES="altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write%20altinn:correspondence.write"
SENDER=$(orgtoken "altinn:serviceowner%20$APPLIB_SCOPES")
APPLIB=$(orgtoken "$APPLIB_SCOPES")
PARTY_ID_GAP="is not a valid organization number, person number or party id"
NOSCOPE=$(orgtoken "altinn:serviceowner/instances.read")
DPSO=$(orgtoken "digdir:dialogporten.serviceprovider")
send() { curl -s -H "Authorization: Bearer $SENDER" "$@"; }

echo "== health and migrations"
for _ in $(seq 1 120); do [[ "$(code "$DIRECT/health")" == 200 ]] && break; sleep 1; done
curl -s "$DIRECT/health" | check "GET /health on the host port: Healthy (the database answers)" 'd["status"] == "Healthy"'
okwait "the inspection API answers through the proxy" 30 "$LT/status" 'True'
curl -s "$LT/status" | check "status: all $MIGRATIONS EF migrations applied, none pending" \
  "d['migrations']['known'] == $MIGRATIONS and d['migrations']['applied'] == $MIGRATIONS and d['migrations']['pending'] == []"
curl -s "$LT/status" | check "status: a Hangfire server is beating" 'len(d["hangfireServers"]) >= 1'
if container_up localtest-v2-postgres; then
  HISTORY=$("$CTR" exec localtest-v2-postgres psql -U postgres -d correspondence -tAc \
    "SELECT n.nspname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relname = '__EFMigrationsHistory'" | head -1)
  COUNT=$("$CTR" exec localtest-v2-postgres psql -U postgres -d correspondence -tAc "SELECT count(*) FROM \"$HISTORY\".\"__EFMigrationsHistory\"")
  expect "the database itself has $MIGRATIONS rows in $HISTORY.__EFMigrationsHistory" "$MIGRATIONS" "$COUNT"
  OWNER=$("$CTR" exec localtest-v2-postgres psql -U postgres -d correspondence -tAc \
    "SELECT r.rolname || ':' || r.rolsuper FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE d.datname = 'correspondence'")
  expect "database correspondence is owned by the non-superuser role correspondence" "correspondence:false" "$OWNER"
else
  echo "skip the database checks: no $CTR container localtest-v2-postgres"
fi

echo "== refusals"
BODY='{"correspondence":{"resourceId":"'$RESOURCE'","sendersReference":"smoke-refused","content":{"language":"nb","messageTitle":"t","messageSummary":"s","messageBody":"b"}},"recipients":["'$RECIPIENT'"]}'
expect "POST correspondence without a token is 401" 401 "$(code -H 'Content-Type: application/json' -X POST "$API/correspondence" -d "$BODY")"
expect "POST correspondence with an org token without altinn:correspondence.write is 403" 403 \
  "$(code -H "Authorization: Bearer $NOSCOPE" -H 'Content-Type: application/json' -X POST "$API/correspondence" -d "$BODY")"
UNKNOWN=$(send -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST "$API/correspondence" -d "${BODY//$RESOURCE/localtest-no-such-resource}")
expect "POST correspondence for an unknown resource is 400" 400 "${UNKNOWN##*$'\n'}"
[[ "$UNKNOWN" == *CORR-01036* ]] && pass "  ... with CORR-01036 (after a real Resource Registry lookup)" || fail "  ... CORR-01036: ${UNKNOWN:0:300}"

echo "== app-lib's credential (the PDP decides)"
APPLIB_POST=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $APPLIB" -H 'Content-Type: application/json' -X POST "$API/correspondence" -d "${BODY//smoke-refused/smoke-applib}")
if [[ "${APPLIB_POST##*$'\n'}" == 200 ]]; then
  pass "POST correspondence with app-lib's token (no altinn:serviceowner): 200 after a PDP permit for ttd"
elif [[ "$APPLIB_POST" == *"$PARTY_ID_GAP"* ]]; then
  known "POST correspondence with app-lib's token: 500, the PDP request names Digdir's 6-digit party id 500006"
else
  fail "POST correspondence with app-lib's token: ${APPLIB_POST:0:300}"
fi

echo "== the sequence with a service owner's token: attachment"
printf 'Localtest correspondence attachment %s\n' "$(date -Is)" > "$TMP/attachment.txt"
SUM=$(md5 "$TMP/attachment.txt")
attachment() { # checksum -> attachment id
  send -H 'Content-Type: application/json' -X POST "$API/attachment" \
    -d '{"resourceId":"'$RESOURCE'","fileName":"smoke.txt","displayName":"Smoke","isEncrypted":false,"checksum":"'"$1"'","sendersReference":"smoke-attachment"}' | tr -d '"'
}
AID=$(attachment "$SUM")
[[ "$AID" =~ ^[0-9a-f-]{36}$ ]] && pass "POST attachment answers its id ($AID)" || fail "POST attachment: $AID"
send -H 'Content-Type: application/octet-stream' -X POST "$API/attachment/$AID/upload" --data-binary @"$TMP/attachment.txt" | check "POST attachment/{id}/upload accepted" \
  'd["attachmentId"] == "'"$AID"'" and d["status"] in ("UploadProcessing", "Published")'
okwait "GET attachment/{id} reaches Published (upstream's simulated malware scan)" 20 "$API/attachment/$AID" 'd["status"] == "Published"' -H "Authorization: Bearer $SENDER"
curl -s "$LT/blobs/$AID" -o "$TMP/blob.txt"
cmp -s "$TMP/attachment.txt" "$TMP/blob.txt" && pass "the bytes are in the file store (correspondence/api/localtest/blobs/{id})" || fail "blob differs from the upload"

echo "== a wrong checksum"
BAD=$(attachment 00000000000000000000000000000000)
expect "upload with a checksum that does not match is 400" 400 "$(code -H "Authorization: Bearer $SENDER" -H 'Content-Type: application/octet-stream' -X POST "$API/attachment/$BAD/upload" --data-binary @"$TMP/attachment.txt")"
send "$API/attachment/$BAD" | check "  ... and the attachment is Failed with the checksum text" 'd["status"] == "Failed" and "Checksum mismatch" in d["statusText"]'
expect "  ... and nothing is left in the file store" 404 "$(code "$LT/blobs/$BAD")"

echo "== the correspondence to Sophie Salt with an e-mail notification"
DUE=$(date -u -d '+14 days' +%Y-%m-%dT%H:%M:%SZ)
correspondence() { # sendersReference requestedPublishTime(or null) recipient [existing attachment]
  local publish=$2 existing=""
  [[ "$publish" == null ]] || publish="\"$publish\""
  [[ -n "${4:-}" ]] && existing="\"$4\""
  send -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST "$API/correspondence" -d '{
    "correspondence": {
      "resourceId": "'$RESOURCE'", "sendersReference": "'"$1"'", "requestedPublishTime": '"$publish"', "dueDateTime": "'"$DUE"'",
      "isConfirmationNeeded": true,
      "content": {"language": "nb", "messageTitle": "Localtest smoke '"$1"'", "messageSummary": "Oppsummering", "messageBody": "# Localtest\n\nInnhold fra '"$1"'."},
      "notification": {"notificationTemplate": "GenericAltinnMessage", "notificationChannel": "Email"}
    },
    "recipients": ["'"$3"'"], "existingAttachments": ['"$existing"']}'
}
RESPONSE=$(correspondence smoke-main null "$RECIPIENT" "$AID")
expect "POST correspondence is 200" 200 "${RESPONSE##*$'\n'}"
CID=$(printf '%s' "${RESPONSE%$'\n'*}" | field 'd["correspondences"][0]["correspondenceId"]')
printf '%s' "${RESPONSE%$'\n'*}" | check "  ... Initialized or ReadyForPublish (its attachment is published already) for the recipient, with the existing attachment" \
  'd["correspondences"][0]["status"] in ("Initialized", "ReadyForPublish") and d["correspondences"][0]["recipient"].endswith("'$SSN'") and d["attachmentIds"] == ["'"$AID"'"]'
okwait "GET correspondence/{id}/details reaches Published (dialog job, then the publish job)" 90 "$API/correspondence/$CID/details" \
  'd["status"] == "Published"' -H "Authorization: Bearer $SENDER"
curl -s "$LT/correspondences?resourceId=$RESOURCE" | check "inspection list has it with a dialog id and one attachment" \
  'any(c["id"] == "'"$CID"'" and c["status"] == "Published" and c["dialogId"] and c["attachmentCount"] == 1 for c in d["items"])'
DIALOG=$(curl -s "$LT/correspondences/$CID" | field 'd["dialog"].rsplit("/", 1)[1]')
curl -s -H "Authorization: Bearer $DPSO" "$SO/dialogs/$DIALOG" | check "the dialog exists in Dialogporten with the resource, the party and the content URL" \
  'd["serviceResource"] == "urn:altinn:resource:'$RESOURCE'" and d["party"] == "'"$RECIPIENT"'" and "correspondence/api/v1/correspondence/'"$CID"'/content" in json.dumps(d)'
curl -s "$LT/correspondences/$CID" | check "inspection detail: statuses Initialized, ReadyForPublish, Published; Hangfire jobs succeeded" \
  '[s["status"] for s in d["statusHistory"]][:3] == ["Initialized", "ReadyForPublish", "Published"] and d["jobs"] and not any(j["state"] == "Failed" for j in d["jobs"])'

echo "== the notification"
okwait "the notification order is stored on the correspondence" 60 "$LT/correspondences/$CID" 'any(n["captured"] for n in d["notifications"])'
CAPTURED=$(curl -s "$LT/correspondences/$CID" | field '[n["captured"] for n in d["notifications"] if n["captured"]][0]')
okwait "the e-mail to $EMAIL is in the Notifications capture and Delivered" 60 "$CAPTURED" \
  'any(m["address"] == "'$EMAIL'" and m["outcome"] == "Delivered" for m in d["items"])'
MP_ASSERTION=$(python3 -I -c 'import base64,json
b=lambda o: base64.urlsafe_b64encode(json.dumps(o).encode()).rstrip(b"=").decode()
print(b({"alg":"none","typ":"JWT"}) + "." + b({"iss":"991825827","scope":"altinn:system/notifications.condition.check"}) + ".")')
MPTOKEN=$(curl -s -X POST "$BASE/localtest/api/maskinporten/token" --data-urlencode grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer --data-urlencode "assertion=$MP_ASSERTION" | field 'd["access_token"]')
expect "notification/check without a token is 401" 401 "$(code "$API/correspondence/$CID/notification/check")"
expect "notification/check with a raw fake-Maskinporten token (the Maskinporten scheme's metadata override) is 200" 200 \
  "$(code -H "Authorization: Bearer $MPTOKEN" "$API/correspondence/$CID/notification/check")"

echo "== an unknown recipient"
STRANGER=$(correspondence smoke-stranger null "urn:altinn:person:identifier-no:08895699684")
[[ "${STRANGER##*$'\n'}" =~ ^4 ]] && pass "a person not in the register gets production's 4xx (${STRANGER##*$'\n'})" || fail "unknown recipient: ${STRANGER:0:300}"

echo "== a reserved recipient (KRR: Rik Forelder, 1003, reserved: true in the test data)"
RESERVED="urn:altinn:person:identifier-no:08829698278"
ALONE=$(correspondence smoke-reserved null "$RESERVED")
expect "a correspondence to the reserved person alone is 422" 422 "${ALONE##*$'\n'}"
[[ "$ALONE" == *CORR-01030* ]] && pass "  ... CORR-01030 (reserved in KRR)" || fail "  ... CORR-01030: ${ALONE:0:300}"
BOTH=$(send -w '\n%{http_code}' -H 'Content-Type: application/json' -X POST "$API/correspondence" -d '{
  "correspondence": {"resourceId": "'$RESOURCE'", "sendersReference": "smoke-reserved-pair",
    "content": {"language": "nb", "messageTitle": "Localtest reserved", "messageSummary": "s", "messageBody": "b"}},
  "recipients": ["'"$RESERVED"'", "'"$RECIPIENT"'"]}')
expect "with a second recipient it is 200" 200 "${BOTH##*$'\n'}"
printf '%s' "${BOTH%$'\n'*}" | check "  ... the reserved recipient's correspondence is Reserved, the other's Initialized or ReadyForPublish" \
  '{c["recipient"][-11:]: c["status"] for c in d["correspondences"]} in ({"08829698278": "Reserved", "'$SSN'": "Initialized"}, {"08829698278": "Reserved", "'$SSN'": "ReadyForPublish"})'

echo "== the recipient, with a dialog token (task 2's code-flow token for user 1337)"
USER_TOKEN=$(curl -s "$BASE/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3" | field 'd["access_token"]')
DIALOG_TOKEN=$(curl -s -H "Authorization: Bearer $USER_TOKEN" "$EU/dialogs/$DIALOG" | field 'd["dialogToken"]')
[[ -n "$DIALOG_TOKEN" && "$DIALOG_TOKEN" != None ]] && pass "GET enduser/dialogs/{id} gives a dialogToken" || fail "no dialogToken for $DIALOG"
recipient() { curl -s -H "Authorization: Bearer $DIALOG_TOKEN" "$@"; }
CONTENT=$(recipient "$API/correspondence/$CID/content")
RECIPIENT_PATH=1
if [[ "$CONTENT" == *"Innhold fra smoke-main"* ]]; then
  pass "GET correspondence/{id}/content with the dialog token returns the body"
elif [[ "$CONTENT" == *"$PARTY_ID_GAP"* ]]; then
  known "GET correspondence/{id}/content with the dialog token: the token is accepted (keys fetched on the miss), then 500, the PDP request names Sophie Salt's 6-digit party id 501337; the recipient's read, download, confirm and purge are skipped"
  RECIPIENT_PATH=0
else
  fail "content with the dialog token: ${CONTENT:0:300}"
fi
if [[ $RECIPIENT_PATH == 1 ]]; then
recipient -o "$TMP/download.txt" "$API/correspondence/$CID/attachment/$AID/download"
cmp -s "$TMP/attachment.txt" "$TMP/download.txt" && pass "GET .../attachment/{id}/download with the dialog token returns the bytes" || fail "download with the dialog token"
expect "POST correspondence/{id}/markasread with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X POST "$API/correspondence/$CID/markasread")"
expect "POST correspondence/{id}/confirm with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X POST "$API/correspondence/$CID/confirm")"
curl -s "$LT/correspondences/$CID" | check "  ... the status history has Read and Confirmed" '{"Read", "Confirmed"} <= {s["status"] for s in d["statusHistory"]}'
okwait "  ... and the dialog has the opened and confirmed activities" 30 "$SO/dialogs/$DIALOG" \
  'any("Opened" in a["type"] for a in d["activities"]) and any("Confirmed" in a["type"] for a in d["activities"])' -H "Authorization: Bearer $DPSO"
fi
SENDER_PURGE=$(send -w '\n%{http_code}' -X DELETE "$API/correspondence/$CID/purge")
expect "the sender cannot purge a published correspondence (400)" 400 "${SENDER_PURGE##*$'\n'}"
[[ "$SENDER_PURGE" == *CORR-01013* ]] && pass "  ... CORR-01013" || fail "  ... CORR-01013: ${SENDER_PURGE:0:300}"
if [[ $RECIPIENT_PATH == 1 ]]; then
expect "DELETE correspondence/{id}/purge with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X DELETE "$API/correspondence/$CID/purge")"
curl -s "$LT/correspondences/$CID" | check "  ... status PurgedByRecipient" 'd["status"] == "PurgedByRecipient"'
okwait "  ... and the dialog is deleted in Dialogporten" 30 "$SO/dialogs/$DIALOG" 'd.get("deletedAt") or d.get("status") == 404 or d.get("status") == 410' -H "Authorization: Bearer $DPSO"
fi

echo "== a scheduled publish, purged by the sender before it"
LATER=$(date -u -d '+5 minutes' +%Y-%m-%dT%H:%M:%SZ)
SCHEDULED=$(correspondence smoke-scheduled "$LATER" "$RECIPIENT")
expect "POST correspondence with requestedPublishTime in 5 minutes is 200" 200 "${SCHEDULED##*$'\n'}"
SID=$(printf '%s' "${SCHEDULED%$'\n'*}" | field 'd["correspondences"][0]["correspondenceId"]')
okwait "  ... ReadyForPublish once its dialog exists" 60 "$API/correspondence/$SID/details" 'd["status"] == "ReadyForPublish"' -H "Authorization: Bearer $SENDER"
curl -s "$LT/jobs?state=Scheduled" | check "  ... and jobs?state=Scheduled lists its publish job" 'any("'"$SID"'" in " ".join(j["arguments"]) for j in d["items"])'
expect "  ... the sender purges it (200)" 200 "$(code -H "Authorization: Bearer $SENDER" -X DELETE "$API/correspondence/$SID/purge")"
curl -s "$LT/correspondences/$SID" | check "  ... status PurgedByAltinn" 'd["status"] == "PurgedByAltinn"'

echo "== the inspection API"
curl -s "$LT/correspondences?limit=1" | check "correspondences?limit=1 pages with next" 'len(d["items"]) == 1 and d["next"] == d["items"][0]["id"]'
curl -s "$LT/jobs?limit=5" | check "jobs lists Hangfire jobs with method and state" 'len(d["items"]) >= 1 and all(j["method"] and j["state"] for j in d["items"])'
expect "correspondences/{unknown} is 404" 404 "$(code "$LT/correspondences/00000000-0000-0000-0000-000000000000")"

if [[ "${CORRESPONDENCE_RESTART:-0}" == 1 ]] && container_up localtest-v2-correspondence; then
  echo "== restart"
  "$CTR" restart localtest-v2-correspondence >/dev/null
  for _ in $(seq 1 120); do [[ "$(code "$DIRECT/health")" == 200 ]] && break; sleep 1; done
  curl -s "$LT/correspondences/$CID" | check "after a restart the correspondence is still there" 'd["id"] == "'"$CID"'"'
  curl -s "$LT/status" | check "  ... and no migration is pending" "d['migrations']['applied'] == $MIGRATIONS and d['migrations']['pending'] == []"
  "$CTR" logs --since 2m localtest-v2-correspondence 2>&1 | grep -q "Applying migration" && fail "  ... a migration ran again" || pass "  ... no migration ran again"
fi

echo
echo "$OK ok, $KNOWN known (see the README's known gaps)"
exit $FAIL
