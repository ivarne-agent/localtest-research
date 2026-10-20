#!/usr/bin/env bash
# Smoke test for the dialogporten component (the WebApi, GraphQL and Service hosts) in the running stack, everything
# through the proxy except the two hosts that have no route of their own (GraphQL's and the Service's health endpoints,
# read on their host ports). The stack must be up with the optional group:
#   studioctl env up -r localtest-v2 --with dialogporten      (see ../../README.md)
# Pushes the ttd/testapp fixture the way the app runner does (Studio token, metadata to Storage, policy to the PDP, then the
# Resource Registry's subject reload), and one app of another organisation (skd/dpforeign, left in Storage afterwards, see
# below). Then creates, reads, searches and deletes a dialog through the service-owner API with an org token, checks the
# refusals, and sends a request through the /dialogporten/graphql rewrite. It deletes the dialog it creates, so it can be
# run repeatedly. The end-user API is not covered: it needs a token with a `pid` claim, which no token in the stack carries
# yet (TODO.md task 2).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# DIALOGPORTEN_GRAPHQL_URL and DIALOGPORTEN_SERVICE_URL override the host-port URLs (default http://127.0.0.1:8031 and :8032).
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
FX="$HERE/../../smoke/apps/ttd/testapp"
BASE=${1:-http://local.altinn.cloud:8000}
GRAPHQL_DIRECT=${DIALOGPORTEN_GRAPHQL_URL:-http://127.0.0.1:8031}
SERVICE_DIRECT=${DIALOGPORTEN_SERVICE_URL:-http://127.0.0.1:8032}
DP="$BASE/dialogporten"
SO="$DP/api/v1/serviceowner"
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
token() { curl -s "$BASE/Home/GetTestOrgToken?orgNumber=991825827&scopes=$1" | tr -d '"'; }

echo "== readiness"
for _ in $(seq 1 90); do [[ "$(code "$DP/health/readiness")" == 200 ]] && break; sleep 1; done
expect "WebApi /dialogporten/health/readiness (through the proxy)" 200 "$(code "$DP/health/readiness")"
expect "GraphQL /health/readiness (host port)" 200 "$(code "$GRAPHQL_DIRECT/health/readiness")"
expect "Service /health/readiness (host port)" 200 "$(code "$SERVICE_DIRECT/health/readiness")"
curl -s "$DP/health/readiness" | check "readiness reports the database healthy" 'd["status"] == "healthy" and d["entries"]["postgres"]["status"] == "healthy"'

echo "== well-known endpoints (the committed test keys, the localtest issuer)"
curl -s "$DP/api/v1/.well-known/jwks.json" | check "jwks.json lists localtest-primary and localtest-secondary (Ed25519)" \
  '{k["kid"] for k in d["keys"]} == {"localtest-primary", "localtest-secondary"} and all(k["crv"] == "Ed25519" for k in d["keys"])'
curl -s "$DP/api/v1/.well-known/jwks.json" | check "jwks.json publishes the committed public halves" \
  '{k["x"] for k in d["keys"]} == {"Xg0SfHY0oNB6M3om3bY6jJ6JFE16gP4JY6mQeL-1FR4", "wzuM7xyqsySfrhbWomQh_cfa3HETBjleagfPZQwLva8"}'
curl -s "$DP/api/v1/.well-known/oauth-authorization-server" | check "oauth-authorization-server issuer is the platform host's /dialogporten/api/v1" \
  'd["issuer"] == "http://local.altinn.cloud:8000/dialogporten/api/v1"'

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
# in Storage afterwards: Dialogporten caches the Resource Registry's resource list for 20 minutes, so an app pushed and
# removed on every run would be missing from the cached list on the next one.
expect "push skd/dpforeign metadata to Storage" yes "$([[ "$(push_metadata skd/dpforeign)" == 20? ]] && echo yes || echo no)"
expect "push ttd/testapp metadata to Storage" yes "$([[ "$(push_metadata ttd/testapp)" == 20? ]] && echo yes || echo no)"
policy=$(sed -e "s/\[ORG\]/ttd/g" -e "s/\[org\]/ttd/g" -e "s/\[APP\]/testapp/g" -e "s/\[app\]/testapp/g" "$FX/policy.xml")
expect "push ttd/testapp policy to the PDP" 200 "$(printf '%s' "$policy" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
expect "Resource Registry reloads the subjects from the pushed policy (Designer's call)" 200 "$(code "$RR/app_ttd_testapp/policy/subjects?reloadFromXacml=true")"

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

echo "== refusals"
expect "a token without the serviceprovider scope is 403" 403 "$(post "$NOSCOPE" "$(dialog app_ttd_testapp)" | tail -n 1)"
expect "no token is 401" 401 "$(code -X POST -H 'Content-Type: application/json' "$SO/dialogs" -d '{}')"
FOREIGN=$(post "$ORG" "$(dialog app_skd_dpforeign)")
FOREIGN_STATUS=${FOREIGN##*$'\n'}
expect "a resource owned by another organisation (app_skd_dpforeign) is 403" 403 "$FOREIGN_STATUS"
[[ "$FOREIGN_STATUS" == 422 ]] && echo "     (422: Dialogporten's cached resource list, up to 20 minutes old, predates the push of skd/dpforeign; rerun later)"
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

echo "== GraphQL through the /dialogporten/graphql rewrite"
# The host serves /graphql with no path base. Without the rewrite the proxy would hand it /dialogporten/graphql and it would
# answer 404; the host answers 401 to an anonymous request on both paths (every operation, the schema included, requires
# authentication), so equal 401s prove the request reached the right path.
expect "the GraphQL host itself answers an anonymous request on /graphql with 401" 401 "$(code "$GRAPHQL_DIRECT/graphql")"
expect "/dialogporten/graphql is rewritten to /graphql (401, not the 404 of an unrewritten path)" 401 "$(code "$DP/graphql")"
expect "/dialogporten/graphql/schema.graphql is rewritten to /graphql/schema.graphql" \
  "$(code "$GRAPHQL_DIRECT/graphql/schema.graphql")" "$(code "$DP/graphql/schema.graphql")"
expect "a POST of a query through the rewrite is 401 as well" 401 "$(code -X POST -H 'Content-Type: application/json' "$DP/graphql" -d '{"query":"{ __typename }"}')"
expect "the WebApi's paths are not rewritten (/dialogporten/api/v1/metadata/limits answers anonymously)" 200 "$(code "$DP/api/v1/metadata/limits")"

echo
echo "$OK ok"
exit $FAIL
