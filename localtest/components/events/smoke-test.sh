#!/usr/bin/env bash
# Smoke test for the events component (production Altinn Events on the shared Postgres) in the running stack,
# everything through the proxy. Publishes app events the way app-lib does (service-owner token, the app's platform
# access token, an idempotency key), subscribes the way an integrator does, and reads the outcome back from the
# localtest inspection API. Resets the component's data first. Requires the stack (`studioctl env up -r localtest-v2`, see ../../README.md).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# 24 checks. Run it before the Dialogporten and adapter smoke tests (or a few seconds after): their late dialog events
# would land after the reset and break "stored once".
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
BASE=${1:-http://local.altinn.cloud:8000}
FAIL=0
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then echo "ok   $1"; else echo "FAIL $1"; FAIL=1; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
inspect() { curl -s "$BASE/events/api/localtest/$1"; }
# The pipeline is asynchronous (local queues, the claim loop): wait until the inspection API shows the expected state.
waitfor() { # path python-expression
  for _ in $(seq 1 30); do inspect "$1" | python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null && return 0; sleep 1; done; return 1
}
uuid() { cat /proc/sys/kernel/random/uuid; }

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/events/api/localtest/events" && break; sleep 1; done
expect "reset" 204 "$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/events/api/localtest/events?subscriptions=true")"

ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827" | tr -d '"')
PAT=$("$HERE/../../certs/mint-platform-access-token.sh" ttd testapp)
# The PDP decides who may receive ttd/testapp events from the app policy, as the app runner pushes it.
STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
expect "push ttd/testapp policy" 200 "$(sed -e 's/\[ORG\]/ttd/g' -e 's/\[org\]/ttd/g' -e 's/\[APP\]/testapp/g' -e 's/\[app\]/testapp/g' "$HERE/../../smoke/apps/ttd/testapp/policy.xml" \
  | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
# The PDP caches policies; drop its caches so the pushed version is the one decided on.
curl -s -o /dev/null -X POST "$BASE/authorization/api/localtest/refresh"

event() { # type party alternativesubject
  printf '{"specversion":"1.0","source":"%s/ttd/testapp/instances/%s/%s","type":"%s","subject":"/party/%s","alternativesubject":"%s"}' "$BASE" "$2" "$(uuid)" "$1" "$2" "$3"
}
publish() { # body [idempotency-key] [platform-access-token]
  curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/events/api/v1/app" -H "Authorization: Bearer $ORG" -H "PlatformAccessToken: ${3:-$PAT}" \
    ${2:+-H "Idempotency-Key: $2"} -H Content-Type:application/json -d "$1"
}

echo "== publish"
KEY=$(uuid); BODY=$(event app.instance.created 512345 /person/01039012345)
expect "POST app with service-owner and platform access token" 201 "$(publish "$BODY" "$KEY")"
expect "same idempotency key again (accepted, not stored)" 201 "$(publish "$BODY" "$KEY")"
expect "platform access token of another org" 401 "$(publish "$(event app.instance.created 512345 /person/01039012345)" "" "$("$HERE/../../certs/mint-platform-access-token.sh" skd testapp)")"
expect "no platform access token" 403 "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/events/api/v1/app" -H "Authorization: Bearer $ORG" -H Content-Type:application/json -d "$BODY")"
waitfor events 'len(d["items"]) == 1 and d["items"][0]["status"] == "processed"' && echo "ok   stored once and processed by the claim loop" || { echo "FAIL stored once and processed"; FAIL=1; }
inspect events | check "org, app and instance derived" 'd["items"][0]["org"] == "ttd" and d["items"][0]["app"] == "testapp" and d["items"][0]["instance"].startswith("512345/") and d["items"][0]["idempotencyKey"] is not None'
inspect tracelog | check "trace log: Registered twice, DuplicateIdempotencyKeySkipped once" '[t["activity"] for t in d].count("Registered") == 2 and [t["activity"] for t in d].count("DuplicateIdempotencyKeySkipped") == 1'

echo "== subscriptions"
subscribe() { curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/events/api/v1/subscriptions" -H "Authorization: Bearer $ORG" -H Content-Type:application/json -d "{\"endPoint\":\"$1\",\"sourceFilter\":\"$BASE/ttd/testapp\"}"; }
SINK="$BASE/events/api/v1/tests/webhookreceiver"; EXTERNAL="https://webhook.example.com/altinn"
expect "subscribe the production test sink (http, inside the stack)" 201 "$(subscribe "$SINK")"
expect "subscribe an external endpoint" 201 "$(subscribe "$EXTERNAL")"
waitfor subscriptions 'len(d) == 2 and all(s["validated"] for s in d)' && echo "ok   both validated by the production handshake" || { echo "FAIL both validated"; FAIL=1; }
inspect deliveries | check "validation delivered to the sink, captured for the external endpoint" \
  '{(x["kind"], x["mode"], x["endpoint"], x["responseStatus"]) for x in d} == {("validation", "delivered", "'"$SINK"'", 200), ("validation", "captured", "'"$EXTERNAL"'", 200)}'

echo "== delivery"
expect "publish an org-owned event (Register main-unit lookup)" 201 "$(publish "$(event app.instance.process.completed 500000 /org/991825827)")"
waitfor events 'len(d["items"]) == 2 and d["items"][1]["status"] == "processed" and d["items"][1]["deliveries"]["total"] == 2' \
  && echo "ok   processed and sent to both subscribers" || { echo "FAIL processed and sent to both subscribers"; FAIL=1; }
inspect events | check "one delivered, one captured" 'd["items"][1]["deliveries"] == {"total": 2, "delivered": 1, "captured": 1, "failed": 0}'
inspect tracelog | check "trace log: OutboundQueue (PDP permit) and WebhookPostResponse 200 per subscription" \
  'sum(t["activity"] == "OutboundQueue" for t in d) == 2 and sum(t["activity"] == "WebhookPostResponse" and t.get("responseCode") == 200 for t in d) == 2'
ID=$(inspect events | python3 -c 'import json,sys; print(json.load(sys.stdin)["items"][1]["id"])')
inspect "events/$ID" | check "event detail: CloudEvent, trace log and deliveries" 'd["items"][0]["cloudEvent"]["id"] == "'"$ID"'" and len(d["traceLog"]) >= 5 and len(d["deliveries"]) == 2'

echo "== production read API"
# registeredtime comes from upstream's column default `now() at time zone 'utc'`, right only on a Postgres session in UTC
# (studioctl runs the stack's Postgres in UTC since task 12; under Europe/Oslo it was stored two hours early).
expect "registeredTime within 60 s of now (UTC)" "yes" "$(inspect events | python3 -c 'import json,sys,datetime as t; now=t.datetime.now(t.timezone.utc); ages=[(now-t.datetime.fromisoformat(e["registeredTime"])).total_seconds() for e in json.load(sys.stdin)["items"]]; print("yes" if ages and all(abs(a) < 60 for a in ages) else "ages %s" % [round(a) for a in ages])')"
# Production returns only events registered at least 30 seconds ago: the event just delivered is held back.
curl -s "$BASE/events/api/v1/app/ttd/testapp?after=0" -H "Authorization: Bearer $ORG" | check "GET app/ttd/testapp holds back the event registered seconds ago" '"'"$ID"'" not in [e["id"] for e in d]'
sleep 31
curl -s "$BASE/events/api/v1/app/ttd/testapp?after=0" -H "Authorization: Bearer $ORG" | check "GET app/ttd/testapp returns both events (PDP-filtered, after the 30 s delay)" 'len(d) == 2'

echo "== reset"
expect "DELETE events keeps subscriptions" 204 "$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/events/api/localtest/events")"
inspect events | check "no events left" 'd["items"] == []'
inspect subscriptions | check "subscriptions kept" 'len(d) == 2'
curl -s -o /dev/null -X DELETE "$BASE/events/api/localtest/events?subscriptions=true"

[[ $FAIL == 0 ]] && echo "all ok" || { echo "some checks failed"; exit 1; }
