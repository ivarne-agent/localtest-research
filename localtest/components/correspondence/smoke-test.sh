#!/usr/bin/env bash
# Smoke test for the correspondence component (production Altinn Correspondence, the API host) in the running stack,
# everything through the proxy except /health on the host port. The stack must be up with the optional group, which also
# starts Dialogporten:
#   studioctl env up -r localtest-v2 --with correspondence      (see ../../README.md)
# Covers: health and the migration count (upstream swallows a failed migration, so /health alone proves nothing), the
# refusals (401, 403, 400 CORR-01036 for an unknown resource), the sequence with a service owner's token (attachment, upload
# with MD5, the simulated malware scan to Published, the blob in the file store, the correspondence to Sophie Salt with an
# e-mail notification to Published), the dialog in Dialogporten, the e-mail in the Notifications capture, the notification
# condition check with a raw fake-Maskinporten token, a checksum mismatch, an unknown recipient, the reserved person (KRR)
# alone (422 CORR-01030) and with another recipient (Reserved), app-lib's five calls with its /Home/GetTestOrgToken credential
# (no altinn:serviceowner, so the PDP decides on Digdir's 6-digit party id 500006: attachment, upload, its status,
# correspondence, details), the recipient's path on that correspondence with a dialog token (task 2's code-flow token for
# user 1337, party 501337, the dialog's dialogToken: details, markasread, content, attachment download, confirm, the opened
# and confirmed activities on the dialog, purge), a scheduled publish and the sender's purge of it, and the sender's refused
# purge of a published correspondence, and the browser download from the inbox (task 11b: the attachment URL without a token,
# Correspondence's OIDC challenge to Authentication's correspondence-localtest client, no picker with user 1337's session
# cookie, idporten-callback, download?session= with the bytes; a reused session, no cookie, a code of another client and a
# missing correlation cookie, every walk bounded). With CORRESPONDENCE_RESTART=1 it also restarts the container (podman) and checks that the
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
pass() { echo "ok   $1"; OK=$((OK + 1)); }
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
# Generous limits: Hangfire polls every 5 s and the first jobs after `env up` are slow; each wait returns as soon as it holds.
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

# Two senders. A service owner's direct token (altinn:serviceowner, as a service owner calling with Maskinporten would),
# which production lets past the PDP when the token's consumer owns the resource, and app-lib's credential (without
# altinn:serviceowner), whose every call goes to the PDP. Every PDP request names the party by its party id; production
# accepts only 8- or 10-digit ids there and the test data's have 6 (500006, 501337), so app-lib's calls and the
# recipient's path depend on the one #if !LOCALTEST site in XacmlRequestFactory (README, "Directive sites").
APPLIB_SCOPES="altinn:serviceowner/instances.read%20altinn:serviceowner/instances.write%20altinn:correspondence.write"
SENDER=$(orgtoken "altinn:serviceowner%20$APPLIB_SCOPES")
APPLIB=$(orgtoken "$APPLIB_SCOPES")
NOSCOPE=$(orgtoken "altinn:serviceowner/instances.read")
DPSO=$(orgtoken "digdir:dialogporten.serviceprovider")
send() { curl -s -H "Authorization: Bearer ${AS:-$SENDER}" "$@"; } # AS: the sender's token, the service owner's by default

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
okwait "GET attachment/{id} reaches Published (upstream's simulated malware scan)" 60 "$API/attachment/$AID" 'd["status"] == "Published"' -H "Authorization: Bearer $SENDER"
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
okwait "GET correspondence/{id}/details reaches Published (dialog job, then the publish job)" 180 "$API/correspondence/$CID/details" \
  'd["status"] == "Published"' -H "Authorization: Bearer $SENDER"
curl -s "$LT/correspondences?resourceId=$RESOURCE" | check "inspection list has it with a dialog id and one attachment" \
  'any(c["id"] == "'"$CID"'" and c["status"] == "Published" and c["dialogId"] and c["attachmentCount"] == 1 for c in d["items"])'
DIALOG=$(curl -s "$LT/correspondences/$CID" | field 'd["dialog"].rsplit("/", 1)[1]')
curl -s -H "Authorization: Bearer $DPSO" "$SO/dialogs/$DIALOG" | check "the dialog exists in Dialogporten with the resource, the party and the content URL" \
  'd["serviceResource"] == "urn:altinn:resource:'$RESOURCE'" and d["party"] == "'"$RECIPIENT"'" and "correspondence/api/v1/correspondence/'"$CID"'/content" in json.dumps(d)'
curl -s "$LT/correspondences/$CID" | check "inspection detail: statuses Initialized, ReadyForPublish, Published; Hangfire jobs succeeded" \
  '[s["status"] for s in d["statusHistory"]][:3] == ["Initialized", "ReadyForPublish", "Published"] and d["jobs"] and not any(j["state"] == "Failed" for j in d["jobs"])'

echo "== the notification"
okwait "the notification order is stored on the correspondence" 120 "$LT/correspondences/$CID" 'any(n["captured"] for n in d["notifications"])'
CAPTURED=$(curl -s "$LT/correspondences/$CID" | field '[n["captured"] for n in d["notifications"] if n["captured"]][0]')
okwait "the e-mail to $EMAIL is in the Notifications capture and Delivered" 120 "$CAPTURED" \
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

SENDER_PURGE=$(send -w '\n%{http_code}' -X DELETE "$API/correspondence/$CID/purge")
expect "the sender cannot purge a published correspondence (400)" 400 "${SENDER_PURGE##*$'\n'}"
[[ "$SENDER_PURGE" == *CORR-01013* ]] && pass "  ... CORR-01013" || fail "  ... CORR-01013: ${SENDER_PURGE:0:300}"

echo "== app-lib's credential (no altinn:serviceowner, the PDP decides on Digdir's party id 500006): its five calls"
AS=$APPLIB
printf 'Localtest correspondence attachment from app-lib %s\n' "$(date -Is)" > "$TMP/applib.txt"
AAID=$(attachment "$(md5 "$TMP/applib.txt")")
[[ "$AAID" =~ ^[0-9a-f-]{36}$ ]] && pass "POST attachment with app-lib's token answers its id ($AAID)" || fail "POST attachment with app-lib's token: $AAID"
send -H 'Content-Type: application/octet-stream' -X POST "$API/attachment/$AAID/upload" --data-binary @"$TMP/applib.txt" | check "POST attachment/{id}/upload with app-lib's token accepted" \
  'd["attachmentId"] == "'"$AAID"'" and d["status"] in ("UploadProcessing", "Published")'
okwait "GET attachment/{id} with app-lib's token reaches Published" 60 "$API/attachment/$AAID" 'd["status"] == "Published"' -H "Authorization: Bearer $APPLIB"
ARESPONSE=$(correspondence smoke-applib null "$RECIPIENT" "$AAID")
expect "POST correspondence with app-lib's token is 200 (a PDP permit for ttd on party 500006)" 200 "${ARESPONSE##*$'\n'}"
ACID=$(printf '%s' "${ARESPONSE%$'\n'*}" | field 'd["correspondences"][0]["correspondenceId"]')
okwait "GET correspondence/{id}/details with app-lib's token reaches Published" 180 "$API/correspondence/$ACID/details" \
  'd["status"] == "Published"' -H "Authorization: Bearer $APPLIB"
unset AS
ADIALOG=$(curl -s "$LT/correspondences/$ACID" | field 'd["dialog"].rsplit("/", 1)[1]')
curl -s -H "Authorization: Bearer $DPSO" "$SO/dialogs/$ADIALOG" | check "  ... its dialog exists in Dialogporten for Sophie Salt" \
  'd["serviceResource"] == "urn:altinn:resource:'$RESOURCE'" and d["party"] == "'"$RECIPIENT"'"'
curl -s "$LT/correspondences/$ACID" | check "  ... and its notification order is stored" 'len(d["notifications"]) >= 1'

echo "== the recipient of app-lib's correspondence, with a dialog token (task 2's code-flow token for user 1337, party 501337)"
USER_TOKEN=$(curl -s "$BASE/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3" | field 'd["access_token"]')
DIALOG_TOKEN=$(curl -s -H "Authorization: Bearer $USER_TOKEN" "$EU/dialogs/$ADIALOG" | field 'd["dialogToken"]')
[[ -n "$DIALOG_TOKEN" && "$DIALOG_TOKEN" != None ]] && pass "GET enduser/dialogs/{id} gives a dialogToken" || fail "no dialogToken for $ADIALOG"
recipient() { curl -s -H "Authorization: Bearer $DIALOG_TOKEN" "$@"; }
CONTENT=$(recipient "$API/correspondence/$ACID/content")
[[ "$CONTENT" == *"Innhold fra smoke-applib"* ]] && pass "GET correspondence/{id}/content with the dialog token returns the body (and marks it Read)" || fail "content with the dialog token: ${CONTENT:0:300}"
recipient -o "$TMP/download.txt" "$API/correspondence/$ACID/attachment/$AAID/download"
cmp -s "$TMP/applib.txt" "$TMP/download.txt" && pass "GET .../attachment/{id}/download with the dialog token returns the bytes" || fail "download with the dialog token: $(head -c 300 "$TMP/download.txt")"
AGAIN=$(recipient -w '\n%{http_code}' -X POST "$API/correspondence/$ACID/markasread")
[[ "${AGAIN##*$'\n'}" == 400 && "$AGAIN" == *1040* ]] && pass "POST correspondence/{id}/markasread after the content is production's 400 (1040, already marked as read)" || fail "markasread after the content: ${AGAIN:0:300}"
# The read and the download each enqueue a Dialogporten activity job; a confirm that patches the dialog while one of them
# writes gets Dialogporten's 409 (optimistic concurrency) and answers 500, production's behaviour for a very fast user.
okwait "  ... the opened and download activity jobs have run" 90 "$LT/correspondences/$ACID" \
  'not any(j["state"] in ("Enqueued", "Processing", "Awaiting") for j in d["jobs"]) and {"IDialogportenService.CreateOpenedActivity", "IDialogportenService.CreateDownloadStartedActivity"} <= {j["method"] for j in d["jobs"] if j["state"] == "Succeeded"}'
expect "POST correspondence/{id}/confirm with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X POST "$API/correspondence/$ACID/confirm")"
okwait "  ... the status history has Read, AttachmentsDownloaded and Confirmed (the confirmation is committed by a job)" 90 "$LT/correspondences/$ACID" \
  '{"Read", "AttachmentsDownloaded", "Confirmed"} <= {s["status"] for s in d["statusHistory"]}'
okwait "  ... and the dialog in Dialogporten has the opened and confirmed activities" 90 "$SO/dialogs/$ADIALOG" \
  'any("Opened" in a["type"] for a in d["activities"]) and any("Confirmed" in a["type"] for a in d["activities"])' -H "Authorization: Bearer $DPSO"
expect "DELETE correspondence/{id}/purge with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X DELETE "$API/correspondence/$ACID/purge")"
curl -s "$LT/correspondences/$ACID" | check "  ... status PurgedByRecipient" 'd["status"] == "PurgedByRecipient"'
okwait "  ... and the dialog is deleted in Dialogporten" 90 "$SO/dialogs/$ADIALOG" 'd.get("deletedAt") or d.get("status") == 404 or d.get("status") == 410' -H "Authorization: Bearer $DPSO"

echo "== the recipient as an API user on the service owner's correspondence: fetch, then mark as read"
DIALOG_TOKEN=$(curl -s -H "Authorization: Bearer $USER_TOKEN" "$EU/dialogs/$DIALOG" | field 'd["dialogToken"]')
[[ -n "$DIALOG_TOKEN" && "$DIALOG_TOKEN" != None ]] && pass "GET enduser/dialogs/{id} gives a dialogToken" || fail "no dialogToken for $DIALOG"
recipient "$API/correspondence/$CID/details" | check "GET correspondence/{id}/details with the dialog token (the recipient's fetch)" \
  'd["correspondenceId"] == "'"$CID"'" and d["status"] == "Published"'
expect "POST correspondence/{id}/markasread with the dialog token is 200" 200 "$(code -H "Authorization: Bearer $DIALOG_TOKEN" -X POST "$API/correspondence/$CID/markasread")"
curl -s "$LT/correspondences/$CID" | check "  ... the status history has Read" '"Read" in [s["status"] for s in d["statusHistory"]]'

echo "== the browser download from the inbox (task 11b): no token, Correspondence's own OIDC client, no picker with a session"
# What a click on an attachment link in Arbeidsflate does: a top-level GET of the dialog's attachment URL with cookies only.
# The AllSchemes cascade finds no bearer, no dialog token and no ?session=, and challenges its OIDC client
# (correspondence-localtest, seeded in Authentication); Authentication reuses the session of the AltinnStudioRuntime cookie,
# Correspondence redeems the code, keeps the access token 5 minutes under a one-time ?session= and validates it with
# IdportenTokenValidator (the #if !LOCALTEST site that lets its discovery retriever read http). Every walk is bounded.
qp() { python3 -I -c 'import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[""])[0])' "$1" "$2"; }
urlpath() { python3 -I -c 'import sys,urllib.parse as u; print(u.urlparse(sys.argv[1]).path)' "$1"; }
HOPS=() # walk JAR URL [MAX]: follows redirects one at a time (the jar as a browser keeps it); HOPS gets "status url" per step,
walk() { # the last body is in $TMP/walk.body and its headers in $TMP/walk.h; returns 1 when MAX hops did not reach a non-redirect
  local jar=$1 url=$2 max=${3:-8} out
  HOPS=()
  for _ in $(seq 1 "$max"); do
    out=$(curl -s -b "$jar" -c "$jar" -D "$TMP/walk.h" -o "$TMP/walk.body" -w '%{http_code} %{redirect_url}' "$url")
    HOPS+=("$out ($url)")
    [[ "${out%% *}" == 3* ]] || return 0
    url=${out#* }
  done
  return 1
}
hop() { local h=${HOPS[$1]:-}; h=${h% (*}; echo "${h#* }"; } # the redirect target of step N (0-based)
DOWNLOAD="$API/correspondence/$CID/attachment/$AID/download"
JAR="$TMP/browser.jar"
# User 1337 signed in, as in a browser after the inbox login: the helper sets the session cookies production's callback sets.
expect "user 1337 signs in (the code-flow helper sets AltinnStudioRuntime)" 200 \
  "$(curl -s -c "$JAR" -o /dev/null -w '%{http_code}' "$BASE/localtest/api/authentication/code?client=localtest-smoke&userId=1337&authenticationLevel=3")"
CHALLENGE=$(curl -s -b "$JAR" -c "$JAR" -D "$TMP/challenge.h" -o /dev/null -w '%{redirect_url}' "$DOWNLOAD")
[[ "$(urlpath "$CHALLENGE")" == /authentication/api/v1/authorize* && "$(qp "$CHALLENGE" client_id)" == correspondence-localtest \
  && "$(qp "$CHALLENGE" redirect_uri)" == "$API/idporten-callback" && "$(qp "$CHALLENGE" scope)" == "openid profile" \
  && "$(qp "$CHALLENGE" code_challenge_method)" == S256 && "$(qp "$CHALLENGE" acr_values)" == "selfregistered-email idporten-loa-substantial" ]] \
  && pass "GET .../download without a token: 302 to Authentication's authorize for correspondence-localtest (exact redirect_uri, openid profile, PKCE, upstream's acr_values)" \
  || fail "the challenge: $CHALLENGE"
COOKIES=$(grep -i '^set-cookie: .AspNetCore.\(Correlation\|OpenIdConnect.Nonce\)' "$TMP/challenge.h" | tr '[:upper:]' '[:lower:]')
[[ $(grep -c . <<<"$COOKIES") == 2 && $(grep -c 'samesite=lax' <<<"$COOKIES") == 2 && "$COOKIES" != *secure* ]] \
  && pass "  ... the correlation and nonce cookies are SameSite=Lax without Secure (the fakes' cookie decision)" || fail "  ... cookies: $COOKIES"
STATE=$(qp "$CHALLENGE" state)
walk "$JAR" "$CHALLENGE" 6 || fail "  ... the walk from authorize did not end within 6 redirects: ${HOPS[*]}"
[[ "$(urlpath "$(hop 0)")" == /correspondence/api/v1/idporten-callback && -n "$(qp "$(hop 0)" code)" ]] \
  && pass "  ... authorize answers 302 straight to idporten-callback?code=… (the session is reused: no picker)" || fail "  ... authorize: ${HOPS[0]:-}"
SESSION=$(qp "$(hop 1)" session)
[[ "$(urlpath "$(hop 1)")" == "${DOWNLOAD#"$BASE"}" && "$SESSION" =~ ^[0-9a-f-]{36}$ ]] \
  && pass "  ... the callback redeems the code and answers 302 to download?session={guid}" || fail "  ... callback: ${HOPS[1]:-}"
[[ ${#HOPS[@]} == 3 && "${HOPS[2]%% *}" == 200 ]] && cmp -s "$TMP/attachment.txt" "$TMP/walk.body" \
  && pass "  ... and download?session= answers 200 with the attachment's bytes (IdportenTokenValidator read the metadata over http)" \
  || fail "  ... download?session=: ${HOPS[*]} $(head -c 200 "$TMP/walk.body")"
grep -qi '^content-disposition: attachment; filename=smoke.txt' "$TMP/walk.h" && pass "  ... as an attachment download (Content-Disposition), what the browser saves" \
  || fail "  ... Content-Disposition: $(grep -i '^content-disposition' "$TMP/walk.h")"
REUSED=$(curl -s -o "$TMP/reused.body" -w '%{http_code} %{redirect_url}' "$DOWNLOAD?session=$SESSION")
[[ "${REUSED%% *}" == 302 && "$(urlpath "${REUSED#* }")" == /authentication/api/v1/authorize* ]] && ! cmp -s "$TMP/attachment.txt" "$TMP/reused.body" \
  && pass "  ... the session is one-time: reused, it gives no bytes but a new challenge (production removes the cached token on first use)" \
  || fail "  ... reused session: $REUSED"
NOCOOKIE="$TMP/nocookie.jar"
if walk "$NOCOOKIE" "$DOWNLOAD" 6 && [[ ${#HOPS[@]} == 3 && "${HOPS[2]%% *}" == 200 && "$(urlpath "$(hop 1)")" == /localtest/api/idporten/authorize ]]; then
  pass "without the AltinnStudioRuntime cookie: challenge, authorize, then the fake ID-porten's picker (200), no loop"
else
  fail "without the cookie: ${HOPS[*]}"
fi
# A code issued to another client, presented with a valid state of this flow: the token endpoint refuses it and upstream's
# OnRemoteFailure restarts the flow once (download?oidcRetry=1), which then completes with the session.
OTHER_CODE=$(curl -s "$BASE/localtest/api/authentication/code?client=localtest-smoke&userId=1337&authenticationLevel=3" | field 'd["code"]')
STATE=$(qp "$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{redirect_url}' "$DOWNLOAD")" state)
if walk "$JAR" "$API/idporten-callback?code=$OTHER_CODE&state=$STATE" 8 && [[ "$(hop 0)" == "$DOWNLOAD?oidcRetry=1" && "${HOPS[-1]%% *}" == 200 ]] \
  && cmp -s "$TMP/attachment.txt" "$TMP/walk.body"; then
  pass "a code issued to localtest-smoke at the callback: invalid_grant, one restart (oidcRetry=1), then the bytes within ${#HOPS[@]} steps"
else
  fail "wrong-client code: ${HOPS[*]}"
fi
# The callback without the correlation cookie ("Correlation failed"): one restart, and a second failure ends in the inbox
# (GeneralSettings:ArbeidsflateBaseUrl) instead of looping.
callback_without_correlation() { # download URL -> where the callback sends the browser
  local authz cb
  authz=$(curl -s -b "$JAR" -c "$JAR" -o /dev/null -w '%{redirect_url}' "$1")
  cb=$(curl -s -b "$JAR" -o /dev/null -w '%{redirect_url}' "$authz")
  curl -s -b "$TMP/session-only.jar" -o /dev/null -w '%{redirect_url}' "$cb"
}
grep -v 'AspNetCore' "$JAR" >"$TMP/session-only.jar"
expect "the callback without the correlation cookie restarts once (download?oidcRetry=1)" "$DOWNLOAD?oidcRetry=1" "$(callback_without_correlation "$DOWNLOAD")"
expect "  ... and a second failure ends in the inbox, not in a loop" "http://af.local.altinn.cloud:8000/" \
  "$(callback_without_correlation "$DOWNLOAD?oidcRetry=1")"

echo "== a scheduled publish, purged by the sender before it"
LATER=$(date -u -d '+5 minutes' +%Y-%m-%dT%H:%M:%SZ)
SCHEDULED=$(correspondence smoke-scheduled "$LATER" "$RECIPIENT")
expect "POST correspondence with requestedPublishTime in 5 minutes is 200" 200 "${SCHEDULED##*$'\n'}"
SID=$(printf '%s' "${SCHEDULED%$'\n'*}" | field 'd["correspondences"][0]["correspondenceId"]')
okwait "  ... ReadyForPublish once its dialog exists" 120 "$API/correspondence/$SID/details" 'd["status"] == "ReadyForPublish"' -H "Authorization: Bearer $SENDER"
curl -s "$LT/jobs?state=Scheduled" | check "  ... and jobs?state=Scheduled lists its publish job" 'any("'"$SID"'" in " ".join(j["arguments"]) for j in d["items"])'
expect "  ... the sender purges it (200)" 200 "$(code -H "Authorization: Bearer $SENDER" -X DELETE "$API/correspondence/$SID/purge")"
curl -s "$LT/correspondences/$SID" | check "  ... status PurgedByAltinn" 'd["status"] == "PurgedByAltinn"'

echo "== the inspection API"
curl -s "$LT/correspondences?limit=1" | check "correspondences?limit=1 pages with next" 'len(d["items"]) == 1 and d["next"] == d["items"][0]["id"]'
# A job Hangfire is still creating has no state yet (its state row is written in a second transaction).
curl -s "$LT/jobs?limit=5" | check "jobs lists Hangfire jobs with method and state" 'len(d["items"]) >= 1 and all(j["method"] for j in d["items"]) and any(j["state"] for j in d["items"])'
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
echo "$OK ok"
exit $FAIL
