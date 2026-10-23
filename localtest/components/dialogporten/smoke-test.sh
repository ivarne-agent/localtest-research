#!/usr/bin/env bash
# Smoke test for the dialogporten component (the WebApi, GraphQL and Service hosts) in the running stack, everything
# through the proxy except the two hosts that have no route of their own (GraphQL's and the Service's health endpoints,
# read from inside the stack's network, ../../smoke/lib/containers.sh). The stack must be up with the optional group:
#   studioctl env up -r localtest-v2 --with dialogporten      (see ../../README.md)
# Pushes the ttd/testapp fixture the way the app runner does (Studio token, metadata to Storage, policy to the PDP, then the
# Resource Registry's subject reload), and one app of another organisation (skd/dpforeign, left in Storage afterwards, see
# below), and runs Dialogporten's localtest sync as the provisioner does. Then creates, reads, searches and deletes a dialog
# through the service-owner API with an org token, checks the refusals, shows that an app pushed after Dialogporten cached the
# Resource Registry's list is known after the sync or a cache clear (two throwaway apps, removed again), finds the dialog's
# event in the Events component, queries GraphQL through the /dialogporten/graphql rewrite with the org token, checks CORS for
# Arbeidsflate's origin, and lists and finally deletes the dialogs through the localtest endpoints.
# NOTE: the last step, DELETE dialogporten/api/v1/localtest/dialogs, removes EVERY dialog in the stack, not only the test's.
# The end-user API is not covered here: with the code-flow token (which carries `pid`) it is the last check of
# ../authentication/smoke-test.sh.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# DIALOGPORTEN_GRAPHQL_URL and DIALOGPORTEN_SERVICE_URL name the hosts on host ports you publish yourself instead (http://127.0.0.1:<port> and
# :<port>); by default they are read by name from inside the stack's network.
set -uo pipefail
shopt -s lastpipe # run `... | check` in this shell, so its ok and FAIL count
HERE=$(cd "$(dirname "$0")" && pwd)
FX="$HERE/../../smoke/apps/ttd/testapp"
BASE=${1:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
# shellcheck source=../../smoke/lib/containers.sh
. "$HERE/../../smoke/lib/containers.sh"
GRAPHQL_DIRECT=${DIALOGPORTEN_GRAPHQL_URL:-}
SERVICE_DIRECT=${DIALOGPORTEN_SERVICE_URL:-}
DP="$BASE/dialogporten"
SO="$DP/api/v1/serviceowner"
LT="$DP/api/v1/localtest"
EV="$BASE/events/api/localtest"
RR="$BASE/resourceregistry/api/v1/resource"
PARTY="urn:altinn:person:identifier-no:01839012345"
FAIL=0
OK=0
pass() { echo "ok   $1"; OK=$((OK + 1)); }
fail() { echo "FAIL $1"; FAIL=1; }
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then pass "$1"; else fail "$1"; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then pass "$1"; else fail "$1 (expected $2, got $3)"; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
# direct_code BASE NAME PATH: the status of a host's own PATH, at BASE when set, else by NAME inside the stack's network.
direct_code() { if [[ -n "$1" ]]; then code "$1$3"; else localtest_stack_code "http://$2:8080$3"; fi; }
token() { curl -s "$BASE/Home/GetTestOrgToken?orgNumber=991825827&scopes=$1" | tr -d '"'; }

echo "== readiness"
for _ in $(seq 1 90); do [[ "$(code "$DP/health/readiness")" == 200 ]] && break; sleep 1; done
expect "WebApi /dialogporten/health/readiness (through the proxy)" 200 "$(code "$DP/health/readiness")"
expect "GraphQL /health/readiness (the host itself)" 200 "$(direct_code "$GRAPHQL_DIRECT" dialogporten-graphql /health/readiness)"
expect "Service /health/readiness (the host itself)" 200 "$(direct_code "$SERVICE_DIRECT" dialogporten-service /health/readiness)"
curl -s "$DP/health/readiness" | check "readiness reports the database healthy" 'd["status"] == "healthy" and d["entries"]["postgres"]["status"] == "healthy"'

echo "== well-known endpoints (the committed test keys, the localtest issuer)"
curl -s "$DP/api/v1/.well-known/jwks.json" | check "jwks.json lists localtest-primary and localtest-secondary (Ed25519)" \
  '{k["kid"] for k in d["keys"]} == {"localtest-primary", "localtest-secondary"} and all(k["crv"] == "Ed25519" for k in d["keys"])'
curl -s "$DP/api/v1/.well-known/jwks.json" | check "jwks.json publishes the committed public halves" \
  '{k["x"] for k in d["keys"]} == {"Xg0SfHY0oNB6M3om3bY6jJ6JFE16gP4JY6mQeL-1FR4", "wzuM7xyqsySfrhbWomQh_cfa3HETBjleagfPZQwLva8"}'
curl -s "$DP/api/v1/.well-known/oauth-authorization-server" | check "oauth-authorization-server issuer is the platform host's /dialogporten/api/v1" \
  'd["issuer"] == "'"$BASE"'/dialogporten/api/v1"'

echo "== the apps, pushed as the runner does"
STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
push_metadata() { # org/app: the fixture's metadata under that id
  local body status
  body=$(sed "s#ttd/testapp#$1#g" "$FX/applicationmetadata.json")
  status=$(printf '%s' "$body" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications?appId=$1" --data-binary @-)
  [[ "$status" == 201 ]] || status=$(printf '%s' "$body" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/$1" --data-binary @-)
  echo "$status"
}
# skd is another organisation than the one the org token speaks for (ttd shares Digdir's organisation number). The app stays
# in Storage afterwards, as in 9a (the sync below clears Dialogporten's 20-minute resource cache, so it would no longer have to).
expect "push skd/dpforeign metadata to Storage" yes "$([[ "$(push_metadata skd/dpforeign)" == 20? ]] && echo yes || echo no)"
expect "push ttd/testapp metadata to Storage" yes "$([[ "$(push_metadata ttd/testapp)" == 20? ]] && echo yes || echo no)"
policy=$(sed -e "s/\[ORG\]/ttd/g" -e "s/\[org\]/ttd/g" -e "s/\[APP\]/testapp/g" -e "s/\[app\]/testapp/g" "$FX/policy.xml")
expect "push ttd/testapp policy to the PDP" 200 "$(printf '%s' "$policy" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
expect "Resource Registry reloads the subjects from the pushed policy (Designer's call)" 200 "$(code "$RR/app_ttd_testapp/policy/subjects?reloadFromXacml=true")"

echo "== localtest sync (the provisioner's step 5)"
SYNC=$(curl -s -w '\n%{http_code}' -X POST "$LT/sync")
expect "POST localtest/sync" 200 "${SYNC##*$'\n'}"
printf '%s' "${SYNC%$'\n'*}" | check "  ... synced subject-resource pairs for app_ttd_testapp (from the policy's subjects)" \
  'd["subjectResourcesByResource"].get("urn:altinn:resource:app_ttd_testapp", 0) > 0'
printf '%s' "${SYNC%$'\n'*}" | check "  ... synced resource policy information and cleared the resource-registry and subject caches" \
  'd["resourcePolicyInformation"] > 0 and {"ResourceRegistry", "SubjectResource", "Authorization"} <= set(d["cachesCleared"])'

echo "== tokens"
ORG=$(token "digdir:dialogporten.serviceprovider%20digdir:dialogporten.serviceprovider.search")
NOSCOPE=$(token "digdir:dialogporten.serviceprovider.search")
[[ -n "$ORG" && -n "$NOSCOPE" ]] && pass "org tokens from /Home/GetTestOrgToken (with and without the serviceprovider scope)" || fail "org tokens"

MARKER="smoke-$(date +%s)-$RANDOM"
dialog() { # resource
  printf '{"serviceResource":"urn:altinn:resource:%s","party":"%s","externalReference":"%s","content":{"title":{"value":[{"languageCode":"nb","value":"Smoke test"}]},"summary":{"value":[{"languageCode":"nb","value":"Created by the dialogporten smoke test"}]}}}' "$1" "$PARTY" "$MARKER"
}
post() { curl -s -w '\n%{http_code}' -X POST -H "Authorization: Bearer $1" -H 'Content-Type: application/json' "$SO/dialogs" -d "$2"; }

echo "== create"
RESPONSE=$(post "$ORG" "$(dialog app_ttd_testapp)")
STATUS=${RESPONSE##*$'\n'}
ID=$(printf '%s' "${RESPONSE%$'\n'*}" | tr -d '"')
expect "POST serviceowner/dialogs for app_ttd_testapp with the org token" 201 "$STATUS"
[[ "$ID" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$ ]] && pass "the id is a UUIDv7 ($ID)" || fail "the id is not a UUIDv7: $ID"

echo "== the event in the Events component"
# The Service host's outbox consumer publishes dialogporten.dialog.created.v1 to events/api/v1/events (generic events
# endpoint, Dialogporten's Maskinporten token); the Events inspection API lists it by resource and type.
EVENT=""
for _ in $(seq 1 30); do
  EVENT=$(curl -s "$EV/events?resource=urn:altinn:resource:app_ttd_testapp&type=dialogporten.dialog.created.v1&limit=1000" \
    | python3 -c 'import json,sys; print(json.dumps([e for e in json.load(sys.stdin)["items"] if e.get("resourceInstance") == "'"$ID"'"]))' 2>/dev/null)
  [[ "$EVENT" != "[]" && -n "$EVENT" ]] && break
  sleep 1
done
printf '%s' "$EVENT" | check "events/api/localtest/events has dialogporten.dialog.created.v1 for the dialog (source .../dialogporten/api/v1/enduser/dialogs/{id})" \
  'len(d) == 1 and d[0]["source"] == "'"$DP"'/api/v1/enduser/dialogs/'"$ID"'" and d[0]["subject"] == "'"$PARTY"'"'
# The inspection API derives org and app from the resource, so a dialog event for an app's resource is listed with that app
# (README, "Events"). Nothing subscribes, so nothing is delivered.
printf '%s' "$EVENT" | check "  ... listed under org ttd, app testapp (derived from the app resource), no deliveries" \
  'd[0]["org"] == "ttd" and d[0]["app"] == "testapp" and d[0]["deliveries"]["total"] == 0'

echo "== refusals"
expect "a token without the serviceprovider scope is 403" 403 "$(post "$NOSCOPE" "$(dialog app_ttd_testapp)" | tail -n 1)"
expect "no token is 401" 401 "$(code -X POST -H 'Content-Type: application/json' "$SO/dialogs" -d '{}')"
FOREIGN=$(post "$ORG" "$(dialog app_skd_dpforeign)")
FOREIGN_STATUS=${FOREIGN##*$'\n'}
expect "a resource owned by another organisation (app_skd_dpforeign) is 403" 403 "$FOREIGN_STATUS"
[[ "$FOREIGN_STATUS" == 422 ]] && echo "     (422: Dialogporten's cached resource list, up to 20 minutes old, predates the push of skd/dpforeign; POST dialogporten/api/v1/localtest/sync)"
printf '%s' "${FOREIGN%$'\n'*}" | check "  ... naming the unowned resource" '"unowned resources" in json.dumps(d)'

echo "== read, search, delete"
curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs/$ID" | check "GET serviceowner/dialogs/{id} returns the dialog" \
  'd["id"] == "'"$ID"'" and d["serviceResource"] == "urn:altinn:resource:app_ttd_testapp" and d["party"] == "'"$PARTY"'" and d["externalReference"] == "'"$MARKER"'"'
# The caller is the organisation number 991825827, which Digdir, ttd and the seeded resources share. Dialogporten names the
# caller's organisation by the first short name the Resource Registry lists for that number (digdir, from the seeded
# altinn_keyrole_access), and a service-owner search only returns dialogs of that organisation. A dialog for a ttd app has
# the org ttd and is read by id but not found by search, so the search is checked on a dialog for the Digdir resource.
SEARCH_RESPONSE=$(post "$ORG" "$(dialog altinn_keyrole_access)")
SEARCH_ID=$(printf '%s' "${SEARCH_RESPONSE%$'\n'*}" | tr -d '"')
expect "POST a dialog for the Digdir-owned altinn_keyrole_access" 201 "${SEARCH_RESPONSE##*$'\n'}"
HITS=""
for _ in $(seq 1 30); do
  HITS=$(curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs?party=$PARTY&externalReference=$MARKER" | python3 -c 'import json,sys; print(" ".join(i["id"] for i in json.load(sys.stdin)["items"]))' 2>/dev/null || echo "")
  [[ -n "$HITS" ]] && break
  sleep 1
done
expect "GET serviceowner/dialogs?party=...&externalReference=... finds exactly that one dialog" "$SEARCH_ID" "$HITS"
expect "DELETE the searched dialog" 204 "$(code -X DELETE -H "Authorization: Bearer $ORG" "$SO/dialogs/$SEARCH_ID")"
expect "DELETE serviceowner/dialogs/{id}" 204 "$(code -X DELETE -H "Authorization: Bearer $ORG" "$SO/dialogs/$ID")"
# Deletion is a soft delete (a purge removes the row): the dialog stays readable, marked with deletedAt.
curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs/$ID" | check "the deleted dialog carries deletedAt" '"deletedAt" in d'
curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs?party=$PARTY&externalReference=$MARKER" | check "and no longer appears in a search" 'd["items"] == []'

echo "== an app pushed after Dialogporten cached the Resource Registry's list (the 9a gap)"
LATE_SYNC="ttd/dpsync$RANDOM"
LATE_CLEAR="ttd/dpclear$RANDOM"
late() { printf 'app_%s' "${1/\//_}"; }
created() { # resource: the status of a create (the dialog, if any, is removed by the localtest DELETE below)
  post "$ORG" "$(dialog "$1")" | tail -n 1
}
expect "push $LATE_SYNC metadata to Storage" yes "$([[ "$(push_metadata "$LATE_SYNC")" == 20? ]] && echo yes || echo no)"
expect "a dialog for $(late "$LATE_SYNC") is refused while the cached list predates the push (422)" 422 "$(created "$(late "$LATE_SYNC")")"
expect "POST localtest/sync" 200 "$(code -X POST "$LT/sync")"
expect "  ... then the dialog is created, without a restart" 201 "$(created "$(late "$LATE_SYNC")")"
expect "push $LATE_CLEAR metadata to Storage" yes "$([[ "$(push_metadata "$LATE_CLEAR")" == 20? ]] && echo yes || echo no)"
expect "a dialog for $(late "$LATE_CLEAR") is refused (422)" 422 "$(created "$(late "$LATE_CLEAR")")"
expect "POST localtest/caches/clear" 200 "$(code -X POST "$LT/caches/clear")"
expect "  ... then the dialog is created on the next request" 201 "$(created "$(late "$LATE_CLEAR")")"
for app in "$LATE_SYNC" "$LATE_CLEAR"; do
  expect "remove $app from Storage again" 200 "$(code -X DELETE -H "Authorization: Bearer $STUDIO" "$BASE/storage/api/v1/applications/$app?hard=true")"
done
code -X POST "$LT/caches/clear" >/dev/null

echo "== GraphQL through the /dialogporten/graphql rewrite"
# The host serves /graphql with no path base. Without the rewrite the proxy would hand it /dialogporten/graphql and it would
# answer 404; the host answers 401 to an anonymous request on both paths (every operation, the schema included, requires
# authentication), so equal 401s prove the request reached the right path.
expect "the GraphQL host itself answers an anonymous request on /graphql with 401" 401 "$(direct_code "$GRAPHQL_DIRECT" dialogporten-graphql /graphql)"
expect "/dialogporten/graphql is rewritten to /graphql (401, not the 404 of an unrewritten path)" 401 "$(code "$DP/graphql")"
expect "/dialogporten/graphql/schema.graphql is rewritten to /graphql/schema.graphql" \
  "$(direct_code "$GRAPHQL_DIRECT" dialogporten-graphql /graphql/schema.graphql)" "$(code "$DP/graphql/schema.graphql")"
expect "a POST of a query through the rewrite is 401 as well" 401 "$(code -X POST -H 'Content-Type: application/json' "$DP/graphql" -d '{"query":"{ __typename }"}')"
expect "the WebApi's paths are not rewritten (/dialogporten/api/v1/metadata/limits answers anonymously)" 200 "$(code "$DP/api/v1/metadata/limits")"
# Any authenticated caller reaches the endpoint (the schema and introspection); every query field needs the end-user policy
# (scope digdir:dialogporten and a person), so a real query needs a token with pid (the code-flow token of task 2, not used here).
gql() { curl -s -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X POST "$DP/graphql" -d "$1"; }
curl -s -H "Authorization: Bearer $ORG" "$DP/graphql?sdl" | grep -q '^type Queries' \
  && pass "the schema (GET /dialogporten/graphql?sdl) with the org token" || fail "the schema with the org token"
gql '{"query":"{ __typename }"}' | check "{ __typename } with the org token is Queries" 'd == {"data": {"__typename": "Queries"}}'
gql '{"query":"{ parties { party } }"}' | check "an end-user query with the org token is AUTH_NOT_AUTHORIZED (needs a pid token, task 2)" \
  'd["errors"][0]["extensions"]["code"] == "AUTH_NOT_AUTHORIZED"'

echo "== CORS for Arbeidsflate (GraphQl:Cors:AllowedOrigins)"
AF=$(sed -E 's#^(https?://)#\1af.#' <<<"$BASE")
PREFLIGHT=$(curl -s -D - -o /dev/null -X OPTIONS "$DP/graphql" -H "Origin: $AF" -H 'Access-Control-Request-Method: POST' \
  -H 'Access-Control-Request-Headers: authorization,content-type' | tr -d '\r')
expect "the preflight from $AF is answered" 204 "$(printf '%s' "$PREFLIGHT" | head -n 1 | cut -d' ' -f2)"
printf '%s' "$PREFLIGHT" | grep -qi "^access-control-allow-origin: $AF$" && printf '%s' "$PREFLIGHT" | grep -qi '^access-control-allow-methods: .*POST' \
  && printf '%s' "$PREFLIGHT" | grep -qi '^access-control-allow-headers: .*authorization' \
  && pass "  ... allowing the origin, POST and the authorization header" || fail "  ... allow headers: $PREFLIGHT"
curl -s -D - -o /dev/null -X OPTIONS "$DP/graphql" -H 'Origin: http://elsewhere.example' -H 'Access-Control-Request-Method: POST' \
  | grep -qi '^access-control-allow-origin' && fail "another origin is allowed" || pass "another origin gets no allow header"
curl -s -D - -o /dev/null -H "Origin: $AF" -H "Authorization: Bearer $ORG" -H 'Content-Type: application/json' -X POST "$DP/graphql" \
  -d '{"query":"{ __typename }"}' | tr -d '\r' | grep -qi "^access-control-allow-origin: $AF$" \
  && pass "the actual POST from $AF carries the allow-origin header" || fail "the actual POST lacks the allow-origin header"

echo "== localtest inspection and reset"
curl -s "$LT/dialogs?party=$PARTY&resource=app_ttd_testapp" | check "GET localtest/dialogs?party=&resource= lists the deleted smoke dialog, past authorization" \
  'any(i["id"] == "'"$ID"'" and i["deleted"] and i["externalReference"] == "'"$MARKER"'" and i["title"]["nb"] == "Smoke test" for i in d["items"])'
curl -s "$LT/dialogs?limit=1" | check "  ... paged: limit=1 gives one item and a next" 'len(d["items"]) == 1 and d["next"] == 1'
KEEP=$(post "$ORG" "$(dialog altinn_keyrole_access)" | head -n 1 | tr -d '"')
HITS=""
for _ in $(seq 1 30); do
  HITS=$(curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs?party=$PARTY" | python3 -c 'import json,sys; print(" ".join(i["id"] for i in json.load(sys.stdin)["items"]))' 2>/dev/null || echo "")
  [[ " $HITS " == *" $KEEP "* ]] && break
  sleep 1
done
[[ " $HITS " == *" $KEEP "* ]] && pass "a service-owner search for the party finds a live dialog ($KEEP)" || fail "search before the reset: $HITS"
RESET=$(curl -s -w '\n%{http_code}' -X DELETE "$LT/dialogs")
expect "DELETE localtest/dialogs" 200 "${RESET##*$'\n'}"
printf '%s' "${RESET%$'\n'*}" | check "  ... reports the dialogs it removed" 'd["dialogsDeleted"] >= 4'
curl -s -H "Authorization: Bearer $ORG" "$SO/dialogs?party=$PARTY" | check "  ... the service-owner search for the party is empty" 'd["items"] == []'
expect "  ... the live dialog is gone (404)" 404 "$(code -H "Authorization: Bearer $ORG" "$SO/dialogs/$KEEP")"
curl -s "$LT/dialogs" | check "  ... and GET localtest/dialogs is empty" 'd["items"] == [] and "next" not in d'

echo
echo "$OK ok"
exit $FAIL
