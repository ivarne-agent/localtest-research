#!/usr/bin/env bash
# Smoke test for Arbeidsflate (the SPA's nginx and the BFF behind it) in the running stack, everything through the front
# door: the inbox on http://af.local.altinn.cloud:8000, the platform on BASE.
#   studioctl env up -r localtest-v2 --with arbeidsflate      (also starts the dialogporten group)
#   ./smoke-test.sh [BASE]                                    (default http://local.altinn.cloud:8000; AF overrides the inbox)
#
# In order: readiness and the BFF's own health checks; the SPA (index.html with the stream URL start.sh wrote, an asset,
# the CSP and no HSTS, a deep link, GraphiQL behind /api/); the login round trip as a browser does it, with a cookie jar
# (/api/login -> Authentication authorize -> the fake ID-porten's picker as user 1337 -> upstream/callback -> /api/cb ->
# the BFF's session cookie, which has no Secure flag); the fixture app pushed as the app runner does and one instance of
# it for party PARTY_ID (default 500000, which user 1337 acts for) followed to its dialog; the GraphQL proxy (parties,
# the dialog in the inbox search, dialogById with a dialog token); the profile page against production Profile (the user,
# a favourite added and removed, a setting changed and restored, the favourites read back from Profile directly); the
# same-origin calls need no CORS, the stream's preflight on the platform does; the init-session test seam with a
# code-flow token; the refusals; logout (/api/logout -> Authentication openid/logout -> the fake ID-porten's endsession
# -> handleloggedout) and Authentication's server-side front-channel logout of a second session.
#
# Leaves behind: the ttd/testapp fixture's metadata, texts and policy (as every platform smoke test), one instance of it
# in Storage with its dialog in Dialogporten (both persist until `studioctl env reset`), user 1337's row in the BFF's
# profile table (created on first use, as production does), an empty favourites group in Profile if there was none, and
# Authentication sessions that the logout steps end. No BFF session survives the test.
#
# af.local.altinn.cloud must resolve to this machine for a browser (`studioctl env hosts add`); when it does not, curl
# is told with --resolve. PARTY_ID overrides the instance owner.
# GraphQL variables ($p) and the start.sh placeholder are meant literally in single quotes.
# shellcheck disable=SC2016
set -uo pipefail
shopt -s lastpipe
HERE=$(cd "$(dirname "$0")" && pwd)
FX="$HERE/../../smoke/apps/ttd/testapp"
BASE=${1:-http://local.altinn.cloud:8000}
AF=${AF:-http://af.local.altinn.cloud:8000}
PARTY_ID=${PARTY_ID:-500000}
AUTHN="$BASE/authentication/api/v1"
ST="$BASE/storage/api/v1"
DP="$BASE/dialogporten"
FAIL=0
OK=0
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT

AF_HOSTPORT=${AF#http://}
AF_HOST=${AF_HOSTPORT%%:*}
RESOLVE=()
if ! getent hosts "$AF_HOST" >/dev/null; then
  RESOLVE=(--resolve "$AF_HOSTPORT:127.0.0.1")
  echo "note: $AF_HOST does not resolve here; curl gets --resolve $AF_HOSTPORT:127.0.0.1 (a browser needs studioctl env hosts add)"
fi
c() { curl -s "${RESOLVE[@]}" "$@"; }
code() { c -o /dev/null -w '%{http_code}' "$@"; }
pass() { echo "ok   $1"; OK=$((OK + 1)); }
fail() { echo "FAIL $1"; FAIL=1; }
expect() { if [[ "$2" == "$3" ]]; then pass "$1"; else fail "$1 (expected $2, got $3)"; fi; }
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then pass "$1"; else fail "$1"; fi
}
py() { python3 -c "import json,sys; d=json.load(sys.stdin); print($1)" 2>/dev/null; }
qp() { python3 -c 'import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[""])[0])' "$1" "$2"; }
path() { python3 -c 'import sys,urllib.parse as u; print(u.urlparse(sys.argv[1]).path)' "$1"; }
header() { grep -i "^$2:" "$1" | head -1 | cut -d' ' -f2- | tr -d '\r'; }
status() { head -1 "$1" | awk '{print $2}'; }
# gql JAR QUERY [VARIABLES-JSON]: POST /api/graphql with the jar's session, as the SPA does; the response in $T/g.json.
gql() {
  local variables=${3:-'{}'}
  python3 -c 'import json,sys; print(json.dumps({"query": sys.argv[1], "variables": json.loads(sys.argv[2])}))' "$2" "$variables" |
    c -b "$1" -c "$1" -H 'Content-Type: application/json' -H "Origin: $AF" -X POST --data-binary @- -D "$T/gh" -o "$T/g.json" "$AF/api/graphql"
}
# login JAR: the browser's sign-in through the picker as user 1337 at level 3; prints the last redirect (/ when done).
login() {
  local loc cb cbto
  loc=$(c -c "$1" -b "$1" -o /dev/null -w '%{redirect_url}' "$AF/api/login")
  loc=$(c -c "$1" -b "$1" -o /dev/null -w '%{redirect_url}' "$loc")
  echo "$loc" >"$T/picker"
  cb=$(c -c "$1" -b "$1" -o /dev/null -w '%{redirect_url}' -X POST "$BASE/localtest/api/idporten/authorize" \
    --data-urlencode "client_id=$(qp "$loc" client_id)" --data-urlencode "state=$(qp "$loc" state)" \
    --data-urlencode "nonce=$(qp "$loc" nonce)" --data-urlencode "redirect_uri=$(qp "$loc" redirect_uri)" \
    --data-urlencode "code_challenge=$(qp "$loc" code_challenge)" --data-urlencode "code_challenge_method=S256" \
    --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
  cbto=$(c -c "$1" -b "$1" -o /dev/null -w '%{redirect_url}' "$cb")
  echo "$cbto" >"$T/cb"
  c -c "$1" -b "$1" -o /dev/null -w '%{redirect_url}' "$cbto"
}
wait_until() { # seconds url token python-expression-on-d; the body in $T/w.json
  local deadline=$((SECONDS + $1))
  while ((SECONDS <= deadline)); do
    curl -s -H "Authorization: Bearer $3" "$2" >"$T/w.json"
    python3 -c "import json,sys; d=json.load(open('$T/w.json')); sys.exit(0 if ($4) else 1)" 2>/dev/null && return 0
    sleep 1
  done
  return 1
}

echo "== readiness"
for _ in $(seq 1 180); do [[ "$(code "$AF/api/readiness")" == 200 ]] && break; sleep 1; done
expect "GET /api/readiness through nginx" 200 "$(code "$AF/api/readiness")"
c "$AF/api/health" >"$T/health.json"
check "GET /api/health: ok, with postgresql, redis, oidc (over http, patch 0001) and dialogporten ok" \
  'd["status"] == "ok" and all(d["healthChecks"][k]["status"] == "ok" for k in ("postgresql", "redis", "oidc", "dialogporten"))' <"$T/health.json"

echo "== the SPA"
c -D "$T/ih" -o "$T/index.html" "$AF/"
expect "GET / (index.html)" 200 "$(status "$T/ih")"
expect "  ... start.sh wrote the stream URL into it" yes \
  "$(grep -qF "window.dialogportenStreamUrl = '$BASE/dialogporten/graphql/stream'" "$T/index.html" && echo yes || echo no)"
expect "  ... no placeholder left" no "$(grep -qF '$DIALOGPORTEN_STREAM_URL' "$T/index.html" && echo yes || echo no)"
expect "  ... not cached" "no-cache, no-store, must-revalidate" "$(header "$T/ih" Cache-Control)"
ASSET=$(grep -oE '/assets/[^"]+\.js' "$T/index.html" | head -1)
c -D "$T/ah" -o /dev/null "$AF$ASSET"
expect "GET $ASSET" 200 "$(status "$T/ah")"
expect "  ... immutable" "public, max-age=31536000, immutable" "$(header "$T/ah" Cache-Control)"
# Upstream's nginx sends its security headers on what location / serves itself, which is a deep link's index.html; / goes
# through the index directive to the .html location, which sends only its cache headers (upstream's behaviour, kept).
c -D "$T/dh" -o "$T/deep.html" "$AF/inbox"
expect "GET /inbox (a deep link): the SPA" yes "$( [[ "$(status "$T/dh")" == 200 ]] && grep -q '<div id="root">' "$T/deep.html" && echo yes || echo no)"
expect "  ... CSP allows the platform origin for connect-src and img-src" yes \
  "$(header "$T/dh" Content-Security-Policy | grep -qF "connect-src 'self' $BASE https:; img-src 'self' data: $BASE https:" && echo yes || echo no)"
expect "  ... and no Strict-Transport-Security" "" "$(header "$T/dh" Strict-Transport-Security)"
expect "GET /api/graphiql: the BFF's GraphiQL, not the SPA's .html or asset locations" 200 "$(code "$AF/api/graphiql")"

echo "== login round trip (user 1337, level 3)"
LAST=$(login "$T/jar")
LOC=$(cat "$T/picker")
expect "/api/login -> authorize -> the fake ID-porten's picker" "/localtest/api/idporten/authorize" "$(path "$LOC")"
CBTO=$(cat "$T/cb")
expect "upstream/callback -> the BFF's /api/cb with code and state" "$AF/api/cb yes" \
  "${CBTO%%\?*} $([[ -n "$(qp "$CBTO" code)" && -n "$(qp "$CBTO" state)" ]] && echo yes || echo no)"
expect "/api/cb -> / (token exchanged, session saved)" "$AF/" "$LAST"
SESSION_LINE=$(grep -P '\tarbeidsflate\t' "$T/jar" | head -1)
expect "the jar holds the arbeidsflate session cookie for $AF_HOST, without Secure" "$AF_HOST FALSE" \
  "$(echo "$SESSION_LINE" | awk -F'\t' '{print $1, $4}' | sed 's/^#HttpOnly_//')"
expect "  ... and Authentication's AltinnStudioRuntime for local.altinn.cloud" yes \
  "$(grep -qP 'local\.altinn\.cloud\t.*\tAltinnStudioRuntime\t' "$T/jar" && echo yes || echo no)"
c -b "$T/jar" "$AF/api/isAuthenticated" >"$T/auth.json"
check "GET /api/isAuthenticated" 'd["isAuthenticated"] is True' <"$T/auth.json"

echo "== the fixture app, pushed as the runner does, and an instance -> dialog"
STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
MC=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$ST/applications?appId=ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
[[ "$MC" == 201 ]] || MC=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$ST/applications/ttd/testapp" --data-binary @"$FX/applicationmetadata.json")
expect "metadata to Storage (POST or PUT)" yes "$([[ "$MC" == 20? ]] && echo yes || echo "$MC")"
for f in "$FX"/texts/resource.*.json; do
  [[ -e "$f" ]] || continue
  lang=$(basename "$f" .json); lang=${lang#resource.}
  TC=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$ST/applications/ttd/testapp/texts" --data-binary @"$f")
  [[ "$TC" == 201 ]] || TC=$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$ST/applications/ttd/testapp/texts/$lang" --data-binary @"$f")
  expect "texts ($lang) to Storage (POST or PUT)" yes "$([[ "$TC" == 20? ]] && echo yes || echo "$TC")"
done
policy=$(sed -e "s/\[ORG\]/ttd/g" -e "s/\[org\]/ttd/g" -e "s/\[APP\]/testapp/g" -e "s/\[app\]/testapp/g" "$FX/policy.xml")
expect "policy to the PDP" 200 "$(printf '%s' "$policy" | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=testapp" --data-binary @-)"
expect "Resource Registry reloads the subjects" 200 "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/resourceregistry/api/v1/resource/app_ttd_testapp/policy/subjects?reloadFromXacml=true")"
expect "Dialogporten localtest sync (the provisioner's step 5)" 200 "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$DP/api/v1/localtest/sync")"
USR=$(curl -s "$BASE/Home/GetTestUserToken?userId=1337&authenticationLevel=3" | tr -d '"')
SYNC=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&authenticationLevel=3&scopes=altinn:storage/instances.syncadapter" | tr -d '"')
R=$(curl -s -w '\n%{http_code}' -H "Authorization: Bearer $USR" -H 'Content-Type: application/json' -X POST "$ST/instances?appId=ttd/testapp" \
  -d "{\"instanceOwner\":{\"partyId\":\"$PARTY_ID\"},\"appId\":\"ttd/testapp\",\"org\":\"ttd\"}")
expect "POST instances for party $PARTY_ID as user 1337" 201 "${R##*$'\n'}"
INSTANCE=$(printf '%s' "${R%$'\n'*}" | py 'd["id"]')
if wait_until 15 "$ST/instances/$INSTANCE" "$SYNC" '"dialog.id" in (d.get("dataValues") or {})'; then
  pass "  ... the adapter made a dialog within 15 s"
else
  fail "  ... the adapter made a dialog within 15 s"
fi
DIALOG=$(py 'd["dataValues"]["dialog.id"]' <"$T/w.json")

echo "== GraphQL through the BFF (stitched: Dialogporten with the session's access token, and the BFF's own types)"
gql "$T/jar" 'query { parties { party partyUuid partyId name isCurrentEndUser } }'
expect "parties: 200" 200 "$(status "$T/gh")"
check "  ... user 1337 is the current end user, and party $PARTY_ID is among the parties" \
  'any(p["isCurrentEndUser"] for p in d["data"]["parties"]) and any(str(p["partyId"]) == "'"$PARTY_ID"'" for p in d["data"]["parties"])' <"$T/g.json"
PARTY_URN=$(py '[p["party"] for p in d["data"]["parties"] if str(p["partyId"]) == "'"$PARTY_ID"'"][0]' <"$T/g.json")
PARTY_UUID=$(py '[p["partyUuid"] for p in d["data"]["parties"] if str(p["partyId"]) == "'"$PARTY_ID"'"][0]' <"$T/g.json")
expect "  ... no Access-Control-Allow-Origin on the same-origin call (none needed)" "" "$(header "$T/gh" Access-Control-Allow-Origin)"
gql "$T/jar" 'query($p: [String!]) { searchDialogs(input: { party: $p, serviceResource: ["urn:altinn:resource:app_ttd_testapp"], limit: 100 }) { items { id party serviceResource } } }' \
  "{\"p\": [\"$PARTY_URN\"]}"
check "searchDialogs for $PARTY_URN: the instance's dialog is in the inbox" \
  'any(i["id"] == "'"$DIALOG"'" for i in d["data"]["searchDialogs"]["items"])' <"$T/g.json"
gql "$T/jar" 'query($id: UUID!) { dialogById(dialogId: $id) { dialog { id dialogToken } errors { __typename } } }' "{\"id\": \"$DIALOG\"}"
check "dialogById: the dialog with a dialog token (what the SPA sends to the stream and to content URLs)" \
  'd["data"]["dialogById"]["dialog"]["id"] == "'"$DIALOG"'" and len(d["data"]["dialogById"]["dialog"]["dialogToken"]) > 0' <"$T/g.json"
# The SPA's subscription stream goes from the browser to the platform host, cross-origin, as in production.
c -D "$T/ph" -o /dev/null -X OPTIONS -H "Origin: $AF" -H 'Access-Control-Request-Method: POST' \
  -H 'Access-Control-Request-Headers: authorization,content-type' "$DP/graphql/stream"
expect "preflight of the stream URL from $AF: allowed by Dialogporten" "$AF" "$(header "$T/ph" Access-Control-Allow-Origin)"
expect "POST the stream URL without a token: Dialogporten's 401 (the path reaches the GraphQL host)" 401 \
  "$(code -X POST -H 'Content-Type: application/json' -H 'Accept: text/event-stream' -d '{"query":"subscription { __typename }"}' "$DP/graphql/stream")"

echo "== the profile page against production Profile"
gql "$T/jar" 'query { profile { language groups { name isFavorite parties } user { userId partyId profileSettingPreference { showClientUnits } } } }'
check "profile: user 1337 from Profile's users/current" 'd["data"]["profile"]["user"]["userId"] == 1337' <"$T/g.json"
SHOW=$(py 'str(d["data"]["profile"]["user"]["profileSettingPreference"]["showClientUnits"]).lower()' <"$T/g.json")
[[ "$SHOW" == true ]] || SHOW=false
gql "$T/jar" 'mutation($p: String!) { addFavoriteParty(partyId: $p) { success } }' "{\"p\": \"$PARTY_UUID\"}"
check "addFavoriteParty (Profile PUT users/current/party-groups/favorites/{uuid})" 'd["data"]["addFavoriteParty"]["success"] is True' <"$T/g.json"
gql "$T/jar" 'query { profile { groups { isFavorite parties } } }'
check "  ... the favourites group lists the party" '"'"$PARTY_UUID"'" in json.dumps(d["data"]["profile"]["groups"])' <"$T/g.json"
END_USER=$(curl -s "$BASE/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3" | py 'd["access_token"]')
curl -s -H "Authorization: Bearer $END_USER" "$BASE/profile/api/v1/users/current/party-groups/favorites" >"$T/fav.json"
check "  ... and Profile itself has it (read with another code-flow token for user 1337)" '"'"$PARTY_UUID"'" in json.dumps(d)' <"$T/fav.json"
gql "$T/jar" 'mutation($p: String!) { deleteFavoriteParty(partyId: $p) { success } }' "{\"p\": \"$PARTY_UUID\"}"
curl -s -H "Authorization: Bearer $END_USER" "$BASE/profile/api/v1/users/current/party-groups/favorites" >"$T/fav.json"
check "deleteFavoriteParty: Profile no longer lists it" '"'"$PARTY_UUID"'" not in json.dumps(d)' <"$T/fav.json"
NEW=$([[ "$SHOW" == true ]] && echo false || echo true)
gql "$T/jar" "mutation { setShowClientUnits(shouldShowClientUnits: $NEW) { success } }"
check "setShowClientUnits($NEW) (Profile PATCH users/current/profilesettings)" 'd["data"]["setShowClientUnits"]["success"] is True' <"$T/g.json"
gql "$T/jar" 'query { profile { user { profileSettingPreference { showClientUnits } } } }'
check "  ... read back through Profile" 'str(d["data"]["profile"]["user"]["profileSettingPreference"]["showClientUnits"]).lower() == "'"$NEW"'"' <"$T/g.json"
gql "$T/jar" "mutation { setShowClientUnits(shouldShowClientUnits: $SHOW) { success } }"
check "  ... and restored to $SHOW" 'd["data"]["setShowClientUnits"]["success"] is True' <"$T/g.json"

echo "== the init-session test seam (ENABLE_INIT_SESSION_ENDPOINT)"
SEAM=$(curl -s "$BASE/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3" | py 'd["access_token"]')
c -X POST -H 'Content-Type: application/json' -d "{\"token\":\"$SEAM\"}" "$AF/api/init-session" >"$T/seam.json"
check "POST /api/init-session with a code-flow access token: a session cookie" 'd["cookie"].startswith("arbeidsflate=")' <"$T/seam.json"
SEAM_COOKIE=$(py 'd["cookie"]' <"$T/seam.json")
expect "  ... GET /api/isAuthenticated with it" 200 "$(code -H "Cookie: $SEAM_COOKIE" "$AF/api/isAuthenticated")"
printf '# Netscape HTTP Cookie File\n%s\tFALSE\t/\tFALSE\t0\tarbeidsflate\t%s\n' "$AF_HOST" "${SEAM_COOKIE#arbeidsflate=}" >"$T/seamjar"
gql "$T/seamjar" 'query { parties { party } }'
check "  ... and GraphQL answers with Dialogporten's parties" 'len(d["data"]["parties"]) > 0' <"$T/g.json"

echo "== refusals"
expect "POST /api/graphql without a session: 401" 401 "$(code -X POST -H 'Content-Type: application/json' -d '{"query":"{ parties { party } }"}' "$AF/api/graphql")"
expect "GET /api/isAuthenticated without a session: 401" 401 "$(code "$AF/api/isAuthenticated")"
expect "GET /api/cb with a wrong state: back to /api/login" "/api/login" "$(path "$(c -o /dev/null -w '%{redirect_url}' "$AF/api/cb?code=x&state=wrong")")"
expect "GET /api/frontchannel-logout with a foreign issuer: 400" 400 "$(code "$AF/api/frontchannel-logout?iss=https://example.com/&sid=x")"

echo "== logout through the BFF"
OUT=$(c -b "$T/jar" -c "$T/jar" -o /dev/null -w '%{http_code} %{redirect_url}' "$AF/api/logout")
expect "GET /api/logout -> Authentication's end_session_endpoint with id_token_hint" "302 /authentication/api/v1/openid/logout/ yes" \
  "${OUT%% *} $(path "${OUT#* }") $([[ -n "$(qp "${OUT#* }" id_token_hint)" ]] && echo yes || echo no)"
NEXT=$(c -b "$T/jar" -c "$T/jar" -o /dev/null -w '%{redirect_url}' "${OUT#* }")
expect "  ... openid/logout (the AltinnStudioRuntime cookie) -> the fake ID-porten's endsession" "/localtest/api/idporten/endsession" "$(path "$NEXT")"
NEXT=$(c -b "$T/jar" -c "$T/jar" -o /dev/null -w '%{redirect_url}' "$NEXT")
expect "  ... endsession -> Authentication's handleloggedout" "$AUTHN/logout/handleloggedout" "$NEXT"
expect "  ... handleloggedout -> the platform" 302 "$(code -b "$T/jar" "$NEXT")"
expect "GET /api/isAuthenticated after logout: 401" 401 "$(code -b "$T/jar" "$AF/api/isAuthenticated")"

echo "== front-channel logout: Authentication ends a second BFF session server-side"
LAST=$(login "$T/jar2")
expect "a second sign-in" "$AF/" "$LAST"
expect "  ... GET /api/isAuthenticated" 200 "$(code -b "$T/jar2" "$AF/api/isAuthenticated")"
# The browser leaves through Authentication directly (another tab, an app's logout): Authentication calls the client's
# front-channel logout URI from its container (af.local.altinn.cloud through the host gateway), and the BFF drops the
# session it stored under the token's sid.
expect "GET openid/logout with the second session's cookie" 302 "$(code -b "$T/jar2" "$AUTHN/openid/logout")"
expect "  ... GET /api/isAuthenticated: 401, the BFF session is gone" 401 "$(code -b "$T/jar2" "$AF/api/isAuthenticated")"

echo "$OK passed, $([[ $FAIL == 0 ]] && echo "no failures" || echo "FAILURES")"
exit $FAIL
