#!/usr/bin/env bash
# Smoke test for the notifications component (production Altinn Notifications orders host on the shared Postgres) in the
# running stack, everything through the proxy. Orders notifications the way app-lib, Authentication and Correspondence do
# (platform access token or a service-owner token with the notifications scope), reads the production status and
# shipment endpoints, and reads the capture (inboxes and messages) from the localtest inspection API.
# Requires the stack (`studioctl env up -r localtest-v2`, see ../../README.md).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# The health endpoint is not under the proxy prefix, so it is read from inside the stack's network
# (../../smoke/lib/containers.sh), or at HEALTH when set (a host port you publish yourself: http://127.0.0.1:<port>/health).
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
BASE=${1:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
API=$BASE/notifications/api/v1
LOCAL=$BASE/notifications/api/localtest
HEALTH=${HEALTH:-}
# shellcheck source-path=SCRIPTDIR source=../../smoke/lib/containers.sh
. "$HERE/../../smoke/lib/containers.sh"

FAIL=0
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then echo "ok   $1"; else echo "FAIL $1"; FAIL=1; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
health_code() { if [[ -n "$HEALTH" ]]; then code "$HEALTH"; else localtest_stack_code http://notifications:5090/health; fi; }
# Processing is asynchronous (the order loops, the in-process queue and the responder): wait until a URL answers JSON
# for which the python expression holds.
waitfor() { # url python-expression [curl args...]
  local url=$1 expr=$2; shift 2
  for _ in $(seq 1 40); do curl -s "$@" "$url" | python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($expr) else 1)" 2>/dev/null && return 0; sleep 0.5; done; return 1
}
okwait() { # name url expression [curl args...]
  local name=$1; shift
  if waitfor "$@"; then echo "ok   $name"; else echo "FAIL $name"; FAIL=1; fi
}
uuid() { cat /proc/sys/kernel/random/uuid; }
field() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

for _ in $(seq 1 60); do [[ "$(health_code)" == 200 ]] && curl -sf -o /dev/null "$LOCAL/inboxes" && break; sleep 1; done
expect "GET /health (the component itself)" 200 "$(health_code)"
expect "the inspection API answers through the proxy" 200 "$(code "$LOCAL/inboxes")"

ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&scopes=altinn:serviceowner/notifications.create" | tr -d '"')
NOSCOPE=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd" | tr -d '"')
PAT=$("$HERE/../../certs/mint-platform-access-token.sh" ttd testapp)
JSON=(-H Content-Type:application/json)
bearer=(-H "Authorization: Bearer $ORG")
platform=(-H "PlatformAccessToken: $PAT")
EMAIL_ORDER='{"subject":"Smoke subject","body":"Smoke body","sendersReference":"smoke-v1","recipients":[{"emailAddress":"Smoke.Mottaker@Example.com"}]}'

echo "== authentication"
expect "order without a token" 401 "$(code "${JSON[@]}" -X POST "$API/orders/email" -d "$EMAIL_ORDER")"
expect "platform access token that does not validate" 401 "$(code "${JSON[@]}" -H 'PlatformAccessToken: not-a-token' -X POST "$API/orders/email" -d "$EMAIL_ORDER")"
NOSCOPE_CODE=$(code "${JSON[@]}" -H "Authorization: Bearer $NOSCOPE" -X POST "$API/orders/email" -d "$EMAIL_ORDER")
[[ "$NOSCOPE_CODE" == 401 || "$NOSCOPE_CODE" == 403 ]] && echo "ok   service-owner token without the notifications scope ($NOSCOPE_CODE)" || { echo "FAIL service-owner token without the notifications scope (got $NOSCOPE_CODE)"; FAIL=1; }

echo "== capture reset"
expect "DELETE messages" 204 "$(code -X DELETE "$LOCAL/messages")"
curl -s "$LOCAL/messages" | check "no messages after the reset" 'd["items"] == []'
curl -s "$LOCAL/inboxes" | check "every inbox is empty and receiving (the flag is always true)" 'all(i["messageCount"] == 0 and i["receiving"] is True for i in d["items"])'

echo "== v1 orders, direct recipients"
RESPONSE=$(curl -s -w '\n%{http_code}' "${JSON[@]}" "${platform[@]}" -X POST "$API/orders/email" -d "$EMAIL_ORDER")
expect "POST orders/email with a platform access token" 202 "${RESPONSE##*$'\n'}"
ORDER=$(echo "${RESPONSE%$'\n'*}" | field '["orderId"]')
okwait "captured in the inbox keyed by the exact address, delivered by the production status machine" "$LOCAL/messages?orderId=$ORDER" \
  'len(d["items"]) == 1 and d["items"][0]["address"] == "Smoke.Mottaker@Example.com" and d["items"][0]["subject"] == "Smoke subject" and d["items"][0]["body"] == "Smoke body" and d["items"][0]["creator"] == "ttd" and d["items"][0]["outcome"] == "Delivered" and d["items"][0]["kind"] == "queued"'
curl -s "$LOCAL/inboxes/Smoke.Mottaker@Example.com" | check "inbox by the exact address: one message, receiving" 'd["inbox"]["messageCount"] == 1 and d["inbox"]["receiving"] is True and d["messages"][0]["orderId"] == "'"$ORDER"'"'
STATUS=$(curl -s "${platform[@]}" "$API/orders/$ORDER/status")
echo "$STATUS" | check "order status: Completed, e-mail summary link under the platform origin" \
  'd["processingStatus"]["status"] == "Completed" and d["notificationsStatusSummary"]["email"]["generated"] == 1 and d["notificationsStatusSummary"]["email"]["links"]["self"] == "'"$BASE"'/notifications/api/v1/orders/'"$ORDER"'/notifications/email"'
curl -s "${platform[@]}" "$API/orders/$ORDER/notifications/email" | check "following links.self: the notification is succeeded (delivered)" 'd["succeeded"] == 1 and d["notifications"][0]["sendStatus"]["status"] == "Delivered"'
RESPONSE=$(curl -s -w '\n%{http_code}' "${JSON[@]}" "${bearer[@]}" -X POST "$API/orders/sms" -d '{"body":"Smoke sms","senderNumber":"Altinn","sendersReference":"smoke-sms","recipients":[{"mobileNumber":"+4799123456"}]}')
expect "POST orders/sms with the service-owner token (scope notifications.create)" 202 "${RESPONSE##*$'\n'}"
SMS_ORDER=$(echo "${RESPONSE%$'\n'*}" | field '["orderId"]')
okwait "SMS in the phone number's inbox, delivered" "$LOCAL/inboxes/%2B4799123456" \
  'd["messages"][0]["body"] == "Smoke sms" and d["messages"][0]["sender"] == "Altinn" and d["messages"][0]["outcome"] == "Delivered" and d["messages"][0]["orderId"] == "'"$SMS_ORDER"'" and d["inbox"]["channel"] == "sms"'
expect "a bare eight-digit number is refused by the production validation" 400 "$(code "${JSON[@]}" "${bearer[@]}" -X POST "$API/orders/sms" -d '{"body":"x","recipients":[{"mobileNumber":"99123456"}]}')"
curl -s "$LOCAL/inboxes" | check "inbox list has both inboxes with counts" \
  '{(i["address"], i["channel"], i["messageCount"]) for i in d["items"]} >= {("Smoke.Mottaker@Example.com", "email", 1), ("+4799123456", "sms", 1)}'
curl -s "$LOCAL/messages?channel=sms" | check "messages filtered by channel" 'len(d["items"]) == 1 and d["items"][0]["address"] == "+4799123456"'

echo "== future orders (v2)"
future() { # recipient-json idempotency-id (empty: a new one) [auth args...]
  local recipient=$1 key=${2:-$(uuid)}; shift 2
  curl -s -w '\n%{http_code}' "${JSON[@]}" "$@" -X POST "$API/future/orders" -d "{\"idempotencyId\":\"$key\",\"sendersReference\":\"smoke-v2\",\"recipient\":$recipient}"
}
KEY=$(uuid)
RESPONSE=$(future '{"recipientEmail":{"emailAddress":"v2.direkte@example.com","emailSettings":{"subject":"V2","body":"V2 body"}}}' "$KEY" "${bearer[@]}")
expect "POST future/orders, direct e-mail recipient" 201 "${RESPONSE##*$'\n'}"
CHAIN=$(echo "${RESPONSE%$'\n'*}" | field '["notificationOrderId"]'); SHIPMENT=$(echo "${RESPONSE%$'\n'*}" | field '["notification"]["shipmentId"]')
RESPONSE=$(future '{"recipientEmail":{"emailAddress":"v2.direkte@example.com","emailSettings":{"subject":"V2","body":"V2 body"}}}' "$KEY" "${bearer[@]}")
expect "the same idempotency id again answers 200" 200 "${RESPONSE##*$'\n'}"
echo "${RESPONSE%$'\n'*}" | check "... with the same order chain" 'd["notificationOrderId"] == "'"$CHAIN"'"'
okwait "shipment manifest: order completed, e-mail delivered" "$API/future/shipment/$SHIPMENT" \
  'd["status"] == "Order_Completed" and [(r["type"], r["destination"], r["status"]) for r in d["recipients"]] == [("Email", "v2.direkte@example.com", "Email_Delivered")]' "${bearer[@]}"
okwait "messages by the order chain id and by the shipment id" "$LOCAL/messages?orderId=$CHAIN" 'len(d["items"]) == 1 and d["items"][0]["orderId"] == "'"$SHIPMENT"'" and d["items"][0]["orderChainId"] == "'"$CHAIN"'"'
RESPONSE=$(future '{"recipientSms":{"phoneNumber":"+4799123457","smsSettings":{"body":"V2 sms"}}}' "" "${platform[@]}")
expect "POST future/orders, direct SMS recipient (platform access token)" 201 "${RESPONSE##*$'\n'}"
okwait "the SMS is in its inbox, delivered" "$LOCAL/inboxes/%2B4799123457" 'd["messages"][0]["body"] == "V2 sms" and d["messages"][0]["outcome"] == "Delivered"'

echo "== recipients resolved through Profile and Register"
# Kari Nordvik (2002, 12889898044) has an e-mail address and a valid +47 mobile number in the seed.
RESPONSE=$(future '{"recipientPerson":{"nationalIdentityNumber":"12889898044","channelSchema":"EmailAndSms","emailSettings":{"subject":"Person","body":"To a person"},"smsSettings":{"body":"To a person by SMS"}}}' "" "${bearer[@]}")
expect "POST future/orders, person (through Profile contactpoint/lookup)" 201 "${RESPONSE##*$'\n'}"
okwait "the e-mail lands in the person's inbox, delivered" "$LOCAL/inboxes/kari.nordvik@test.com" 'd["messages"][0]["subject"] == "Person" and d["messages"][0]["outcome"] == "Delivered"'
okwait "the SMS lands in the person's phone inbox, delivered" "$LOCAL/inboxes/%2B4791234567" 'd["messages"][0]["body"] == "To a person by SMS" and d["messages"][0]["outcome"] == "Delivered"'
# Sophie Salt (1337, 01839012345): the seed's +4799001337 (until schema v3 the bare, invalid 87654321) gets both.
RESPONSE=$(future '{"recipientPerson":{"nationalIdentityNumber":"01839012345","channelSchema":"EmailAndSms","emailSettings":{"subject":"Sophie","body":"B"},"smsSettings":{"body":"Sophie sms"}}}' "" "${bearer[@]}")
expect "POST future/orders, Sophie Salt" 201 "${RESPONSE##*$'\n'}"
okwait "the e-mail lands in Sophie's inbox" "$LOCAL/inboxes/1337@altinnstudiotestusers.com" 'any(m["subject"] == "Sophie" and m["outcome"] == "Delivered" for m in d["messages"])'
okwait "the SMS lands on Sophie's number" "$LOCAL/inboxes/%2B4799001337" 'any(m["body"] == "Sophie sms" and m["outcome"] == "Delivered" for m in d["messages"])'
# Rik Forelder (1003, 08829698278) is reserved in the seed (KRR); Profile says isReserved. Production accepts the order
# (a reserved person is not "missing contact") and fails the notification with Failed_RecipientReserved; nothing is sent.
# With ignoreReservation the e-mail goes out.
RESPONSE=$(future '{"recipientPerson":{"nationalIdentityNumber":"08829698278","channelSchema":"Email","emailSettings":{"subject":"Reserved","body":"B"}}}' "" "${bearer[@]}")
expect "POST future/orders, a reserved person" 201 "${RESPONSE##*$'\n'}"
RCHAIN=$(echo "${RESPONSE%$'\n'*}" | field '["notificationOrderId"]'); RSHIPMENT=$(echo "${RESPONSE%$'\n'*}" | field '["notification"]["shipmentId"]')
okwait "... the notification fails as reserved" "$API/future/shipment/$RSHIPMENT" 'any("RecipientReserved" in r["status"] for r in d["recipients"])' "${bearer[@]}"
expect "... nothing is captured for it" 0 "$(curl -s "$LOCAL/messages?orderId=$RCHAIN" | field '["items"].__len__()')"
RESPONSE=$(future '{"recipientPerson":{"nationalIdentityNumber":"08829698278","channelSchema":"Email","ignoreReservation":true,"emailSettings":{"subject":"Reserved ignored","body":"B"}}}' "" "${bearer[@]}")
expect "POST future/orders, a reserved person with ignoreReservation" 201 "${RESPONSE##*$'\n'}"
RCHAIN=$(echo "${RESPONSE%$'\n'*}" | field '["notificationOrderId"]')
okwait "... the e-mail is sent and delivered" "$LOCAL/messages?orderId=$RCHAIN" 'len(d["items"]) == 1 and d["items"][0]["address"] == "test@test.com"'
RESPONSE=$(future '{"recipientExternalIdentity":{"externalIdentity":"urn:altinn:person:idporten-email:selvregistrert.bruker@example.com","channelSchema":"Email","emailSettings":{"subject":"Self-identified","body":"B"}}}' "" "${bearer[@]}")
expect "POST future/orders, self-identified user (through Profile contactpoint/lookupsi)" 201 "${RESPONSE##*$'\n'}"
okwait "the e-mail lands in the self-identified user's inbox" "$LOCAL/inboxes/selvregistrert.bruker@example.com" 'd["messages"][0]["subject"] == "Self-identified" and d["messages"][0]["outcome"] == "Delivered"'
RESPONSE=$(future '{"recipientPerson":{"nationalIdentityNumber":"11111111111","channelSchema":"Email","emailSettings":{"subject":"Nobody","body":"B"}}}' "" "${bearer[@]}")
expect "a person without contact points: 422" 422 "${RESPONSE##*$'\n'}"
echo "${RESPONSE%$'\n'*}" | check "... with the NOT-00001 problem details (Authentication, Access Management and Correspondence branch on it)" 'd["code"] == "NOT-00001" and d["status"] == 422'

# Organisation recipients: official addresses come from the seed's notificationAddresses (Profile's
# organizations/notificationaddresses/lookup). DDG Fitness has an e-mail address and a mobile number. An organisation
# without any (EAS Health Consulting) is 422 at registration whatever the policy says: Notifications adds the
# user-registered contact points (the PDP-filtered role holders) only when processing an order. Production behaviour.
RESPONSE=$(future '{"recipientOrganization":{"orgNumber":"897069644","channelSchema":"EmailAndSms","resourceId":"urn:altinn:resource:altinn_keyrole_access","emailSettings":{"subject":"Org","body":"B"},"smsSettings":{"body":"Org sms"}}}' "" "${bearer[@]}")
expect "POST future/orders, organisation with official addresses (DDG Fitness)" 201 "${RESPONSE##*$'\n'}"
okwait "the e-mail lands in the organisation's official inbox" "$LOCAL/inboxes/varsling@ddgfitness.no" 'any(m["subject"] == "Org" and m["outcome"] == "Delivered" for m in d["messages"])'
okwait "the SMS lands on the organisation's official number" "$LOCAL/inboxes/%2B4799500000" 'any(m["body"] == "Org sms" and m["outcome"] == "Delivered" for m in d["messages"])'
# A sub-unit without addresses (DDG Fitness Oslo, 897069660) gets its main unit's through Profile's fallback.
RESPONSE=$(future '{"recipientOrganization":{"orgNumber":"897069660","channelSchema":"Email","resourceId":"urn:altinn:resource:altinn_keyrole_access","emailSettings":{"subject":"Sub-unit","body":"B"}}}' "" "${bearer[@]}")
expect "POST future/orders, a sub-unit without addresses of its own (DDG Fitness Oslo)" 201 "${RESPONSE##*$'\n'}"
okwait "the e-mail lands in the main unit's official inbox" "$LOCAL/inboxes/varsling@ddgfitness.no" 'any(m["subject"] == "Sub-unit" and m["outcome"] == "Delivered" for m in d["messages"])'
RESPONSE=$(future '{"recipientOrganization":{"orgNumber":"897069628","channelSchema":"Email","resourceId":"urn:altinn:resource:altinn_keyrole_access","emailSettings":{"subject":"Org","body":"B"}}}' "" "${bearer[@]}")
expect "an organisation without official addresses (EAS Health Consulting): 422" 422 "${RESPONSE##*$'\n'}"
echo "${RESPONSE%$'\n'*}" | check "... NOT-00001" 'd["code"] == "NOT-00001"'

echo "== condition endpoint (the production client with the Maskinporten fake; the production sink answers conditionMet)"
for MET in false true; do
  RESPONSE=$(curl -s -w '\n%{http_code}' "${JSON[@]}" "${bearer[@]}" -X POST "$API/future/orders" -d "{\"idempotencyId\":\"$(uuid)\",\"sendersReference\":\"smoke-condition\",\"conditionEndpoint\":\"$BASE/notifications/api/v1/tests/sendcondition?conditionMet=$MET\",\"recipient\":{\"recipientEmail\":{\"emailAddress\":\"condition-$MET@example.com\",\"emailSettings\":{\"subject\":\"Condition $MET\",\"body\":\"B\"}}}}")
  expect "POST future/orders with conditionEndpoint conditionMet=$MET" 201 "${RESPONSE##*$'\n'}"
done
okwait "condition true: the e-mail is sent and delivered" "$LOCAL/inboxes/condition-true@example.com" 'd["messages"][0]["outcome"] == "Delivered"'
sleep 3
expect "condition false: nothing is captured" 404 "$(code "$LOCAL/inboxes/condition-false@example.com")"

echo "== composed e-mail (attachments by reference)"
COMPOSED=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&scopes=altinn:serviceowner/notifications.create%20altinn:serviceowner/notifications.composedemail.create" | tr -d '"')
expect "POST future/orders/composed-email" 201 "$(code "${JSON[@]}" -H "Authorization: Bearer $COMPOSED" -X POST "$API/future/orders/composed-email" -d "{\"idempotencyId\":\"$(uuid)\",\"sendersReference\":\"smoke-composed\",\"recipient\":{\"emailAddress\":\"composed@example.com\",\"emailSettings\":{\"subject\":\"Composed\",\"body\":\"B\",\"attachments\":[{\"filename\":\"a.pdf\",\"mimeType\":\"application/pdf\",\"sasUrl\":\"https://example.blob.core.windows.net/x/a.pdf?se=2030-01-01T00:00:00Z&sig=abc&sp=r&sr=b\"}]}}}")"
okwait "captured as composed with the attachment by reference, delivered" "$LOCAL/inboxes/composed@example.com" \
  'd["messages"][0]["kind"] == "composed" and d["messages"][0]["attachments"][0]["filename"] == "a.pdf" and d["messages"][0]["outcome"] == "Delivered"'

echo "== instant orders (the Email and SMS hosts' endpoints are served by the localtest host)"
RESPONSE=$(curl -s -w '\n%{http_code}' "${JSON[@]}" "${bearer[@]}" -X POST "$API/future/orders/instant/email" -d "{\"idempotencyId\":\"$(uuid)\",\"sendersReference\":\"smoke-instant\",\"recipientEmail\":{\"emailAddress\":\"instant@example.com\",\"emailSettings\":{\"subject\":\"Instant\",\"body\":\"<b>Instant</b>\",\"contentType\":\"Html\"}}}")
expect "POST future/orders/instant/email" 201 "${RESPONSE##*$'\n'}"
okwait "captured as an instant message with the HTML content type, delivered" "$LOCAL/inboxes/instant@example.com" \
  'd["messages"][0]["kind"] == "instant" and d["messages"][0]["contentType"] == "Html" and d["messages"][0]["outcome"] == "Delivered" and d["messages"][0]["orderId"] is not None'
RESPONSE=$(curl -s -w '\n%{http_code}' "${JSON[@]}" "${bearer[@]}" -X POST "$API/future/orders/instant/sms" -d "{\"idempotencyId\":\"$(uuid)\",\"sendersReference\":\"smoke-instant\",\"recipientSms\":{\"phoneNumber\":\"+4799123458\",\"timeToLiveInSeconds\":3600,\"smsSettings\":{\"body\":\"Instant sms\"}}}")
expect "POST future/orders/instant/sms" 201 "${RESPONSE##*$'\n'}"
okwait "the instant SMS is delivered" "$LOCAL/inboxes/%2B4799123458" 'd["messages"][0]["kind"] == "instant" and d["messages"][0]["outcome"] == "Delivered"'

echo "== cancel"
LATER=$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%SZ)
LATER_ORDER=$(curl -s "${JSON[@]}" "${bearer[@]}" -X POST "$API/orders/email" -d "{\"subject\":\"Later\",\"body\":\"B\",\"sendersReference\":\"smoke-later\",\"requestedSendTime\":\"$LATER\",\"recipients\":[{\"emailAddress\":\"later@example.com\"}]}" | field '["orderId"]')
expect "PUT orders/{id}/cancel on an order with a future send time" 200 "$(code "${bearer[@]}" -X PUT "$API/orders/$LATER_ORDER/cancel")"
curl -s "${bearer[@]}" "$API/orders/$LATER_ORDER/status" | check "the order is Cancelled" 'd["processingStatus"]["status"] == "Cancelled"'
sleep 3
expect "nothing was captured for it" 404 "$(code "$LOCAL/inboxes/later@example.com")"

echo "== reset keeps the production data"
expect "DELETE messages" 204 "$(code -X DELETE "$LOCAL/messages")"
curl -s "$LOCAL/messages" | check "the capture is empty" 'd["items"] == []'
curl -s "$LOCAL/inboxes" | check "the inboxes stay, empty" 'len(d["items"]) >= 5 and all(i["messageCount"] == 0 for i in d["items"])'
expect "the production order is still readable" 200 "$(code "${platform[@]}" "$API/orders/$ORDER")"

exit $FAIL
