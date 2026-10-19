#!/usr/bin/env bash
# Smoke test for the localtest app in the running compose stack:
# - the proxy: the contract the nginx ingress had (routes by host and path, trailing-slash redirects, cookies without
#   Secure, streaming, the app-frontend files) and the app route API the app runner uses (registers and removes a route
#   for ttd/proxy-smoke only);
# - the fake ID-porten and Maskinporten;
# - Home: the old localtest login contract (start page, LogInTestUser and the party cookies of the selected party,
#   authentication levels in the token, version probe, token helpers), all through production Authentication.
# Requires `podman-compose up -d`. USER_SELECT must be a user acting for an organisation (userId.orgPartyId); OWN_PARTY
# is that user's own party.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
BASE=${1:-http://local.altinn.cloud:8000}
API="$BASE/localtest/api/proxy/apps"
USER_SELECT=${USER_SELECT:-1337.500000}
OWN_PARTY=${OWN_PARTY:-501337}
USER_ID=${USER_SELECT%%.*}
ORG_PARTY=${USER_SELECT##*.}
FAIL=0
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
code() { curl -s -m 10 -o /dev/null -w '%{http_code}' "$@"; }
claim() { python3 -c "import sys,base64,json; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get(sys.argv[2]))" "$1" "$2"; }
cookie() { grep -i "^Set-Cookie: $2=" "$1" | sed -E "s/^Set-Cookie: $2=([^;]*).*/\1/I" | tr -d '\r'; }
for _ in $(seq 1 30); do curl -sf -m 2 -o /dev/null "$BASE/health" && break; sleep 1; done

echo "== platform routes"
expect "start page at / (the localtest app's own)" 200 "$(code "$BASE/")"
expect "/Home serves the start page, not a redirect" 200 "$(code "$BASE/Home")"
expect "authentication prefix" 200 "$(code "$BASE/authentication/api/v1/openid/.well-known/openid-configuration")"
expect "register prefix" 200 "$(code "$BASE/register/api/localtest/test-users")"
expect "events prefix" 200 "$(code "$BASE/events/api/localtest/events")"
expect "a prefix without its slash redirects" "301 /storage/" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{location}' "$BASE/storage")"
expect "a file-like path is forwarded (register answers)" 404 "$(code "$BASE/register/no-such-file.json")"
expect "unknown path: the proxy's own 404" "No app is registered for this path. Start it with run-app.sh <app-dir> <port>." "$(curl -s -m 10 "$BASE/nope/missing.js")"
expect "the PDF host belongs to pdf3, /health included" 404 "$(code -H 'Host: pdf.local.altinn.cloud:8000' "$BASE/health")"
SECURE=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:.*;\s*secure')
COOKIES=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:')
expect "sign-in cookies set, none with Secure" "4 0" "$COOKIES $SECURE"
expect "server-sent events stream through unbuffered" ": connected" "$(timeout 3 curl -sN -m 3 "$BASE/events/api/localtest/stream" | head -1)"
FILE=$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{access-control-allow-origin}' "$BASE/app-frontend/altinn-app-frontend.js")
expect "app-frontend bundle with CORS (needs FRONTEND_DIST)" "200 *" "$FILE"

echo "== app routes"
expect "bad app name" 400 "$(code -X PUT "$API/ttd/Not_Valid" -H Content-Type:application/json -d '{"upstream":"http://register:5020"}')"
expect "upstream with a path" 400 "$(code -X PUT "$API/ttd/proxy-smoke" -H Content-Type:application/json -d '{"upstream":"http://register:5020/x"}')"
# Any reachable server will do as the "app": Register answers 404 for /ttd/proxy-smoke/…, which proves the route.
expect "register ttd/proxy-smoke" 200 "$(code -X PUT "$API/ttd/proxy-smoke" -H Content-Type:application/json -d '{"upstream":"http://register:5020"}')"
expect "listed" 1 "$(curl -s -m 10 "$API" | python3 -c 'import json,sys; print(sum(r["org"]=="ttd" and r["app"]=="proxy-smoke" for r in json.load(sys.stdin)))')"
expect "routed to the upstream (Register's empty 404, not the proxy's text)" "" "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "app path without its slash redirects, query kept" "301 /ttd/proxy-smoke/?a=1" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{location}' "$BASE/ttd/proxy-smoke?a=1")"
expect "remove" 204 "$(code -X DELETE "$API/ttd/proxy-smoke")"
expect "gone: the proxy's 404 again" "No app is registered for this path. Start it with run-app.sh <app-dir> <port>." "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "remove again" 404 "$(code -X DELETE "$API/ttd/proxy-smoke")"

echo "== identity providers"
IDP="$BASE/localtest/api/idporten"
expect "ID-porten issuer" "$BASE/localtest/api/idporten" "$(curl -s -m 10 "$IDP/.well-known/openid-configuration" | python3 -c 'import json,sys; print(json.load(sys.stdin)["issuer"])')"
expect "ID-porten JWKS has an RS256 key" "RS256" "$(curl -s -m 10 "$IDP/jwks" | python3 -c 'import json,sys; print(json.load(sys.stdin)["keys"][0]["alg"])')"
expect "Maskinporten issuer" "$BASE/localtest/api/maskinporten/" "$(curl -s -m 10 "$BASE/localtest/api/maskinporten/.well-known/oauth-authorization-server" | python3 -c 'import json,sys; print(json.load(sys.stdin)["issuer"])')"
J=$(mktemp); LOC=$(curl -s -m 10 -o /dev/null -w '%{redirect_url}' -c "$J" "$BASE/authentication/api/v1/authentication?goTo=$BASE/")
q() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$LOC" "$1"; }
expect "Authentication sends an S256 PKCE challenge" "S256" "$(q code_challenge_method)"
CB=$(curl -s -m 10 -o /dev/null -w '%{redirect_url}' -X POST "$IDP/authorize" --data-urlencode "client_id=$(q client_id)" --data-urlencode "state=$(q state)" --data-urlencode "redirect_uri=$(q redirect_uri)" --data-urlencode "code_challenge=$(q code_challenge)" -d UserSelect=1337 -d AuthenticationLevel=2)
CODE=$(python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get('code',[''])[0])" "$CB")
TOKEN_REQUEST=(-s -m 10 -o /dev/null -w '%{http_code}' -X POST "$IDP/token" -d grant_type=authorization_code -d "code=$CODE" --data-urlencode "redirect_uri=$(q redirect_uri)")
expect "token endpoint refuses a wrong PKCE verifier" 400 "$(curl "${TOKEN_REQUEST[@]}" -d code_verifier=wrong)"
expect "and the code is spent afterwards" 400 "$(curl "${TOKEN_REQUEST[@]}" -d code_verifier=wrong)"
rm -f "$J"

echo "== home"
expect "GET /Home/Localtest/Version" "8" "$(curl -s -m 10 "$BASE/Home/Localtest/Version")"
expect "version content type" "application/json" "$(curl -s -m 10 -o /dev/null -w '%{content_type}' "$BASE/Home/Localtest/Version")"
START=$(curl -s -m 10 "$BASE/")
expect "start page lists the user's own party" "yes" "$(grep -q "<option value=\"$USER_ID.$OWN_PARTY\">" <<<"$START" && echo yes || echo no)"
expect "start page lists the organisation option" "yes" "$(grep -q "<option value=\"$USER_SELECT\">[^<]* for " <<<"$START" && echo yes || echo no)"
expect "/Home/Index serves the same page" 200 "$(code "$BASE/Home/Index")"
H=$(mktemp)
expect "POST /Home/LogInTestUser reauthenticate" 204 "$(curl -s -m 30 -D "$H" -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_SELECT" -d AuthenticationLevel=2)"
TOKEN=$(cookie "$H" AltinnStudioRuntime)
expect "AltinnPartyId cookie is the selected party" "$ORG_PARTY" "$(cookie "$H" AltinnPartyId)"
expect "AltinnPartyUuid cookie is set" "yes" "$([[ "$(cookie "$H" AltinnPartyUuid)" =~ ^[0-9a-f-]{36}$ && "$(cookie "$H" AltinnPartyUuid)" != 00000000-0000-0000-0000-000000000000 ]] && echo yes || echo no)"
expect "token urn:altinn:userid" "$USER_ID" "$(claim "$TOKEN" urn:altinn:userid)"
expect "token keeps the user's own urn:altinn:partyid" "$OWN_PARTY" "$(claim "$TOKEN" urn:altinn:partyid)"
for level in 0 1 2 3 4; do
  curl -s -m 30 -D "$H" -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_ID.$OWN_PARTY" -d "AuthenticationLevel=$level"
  expect "LogInTestUser level $level -> urn:altinn:authlevel" "$level" "$(claim "$(cookie "$H" AltinnStudioRuntime)" urn:altinn:authlevel)"
done
for level in 1 2 3 4; do
  expect "GetTestUserToken level $level -> urn:altinn:authlevel" "$level" "$(claim "$(curl -s -m 30 "$BASE/Home/GetTestUserToken?userId=$USER_ID&authenticationLevel=$level")" urn:altinn:authlevel)"
done
expect "GetTestUserToken/{userId} path form" "$USER_ID" "$(claim "$(curl -s -m 30 "$BASE/Home/GetTestUserToken/$USER_ID")" urn:altinn:userid)"
expect "LogInTestUser start redirects to the app" "/ttd/my-app/" "$(curl -s -m 30 -o /dev/null -w '%{redirect_url}' -X POST "$BASE/Home/LogInTestUser" -d action=start -d "UserSelect=$USER_SELECT" -d AuthenticationLevel=3 -d AppPathSelection=ttd/my-app | sed -E 's#^https?://[^/]+##')"
expect "LogInTestUser does not redirect off-site" "/" "$(curl -s -m 30 -o /dev/null -w '%{redirect_url}' -X POST "$BASE/Home/LogInTestUser" -d action=start -d "UserSelect=$USER_SELECT" -d AuthenticationLevel=2 -d RedirectUrl=https://example.com/ | sed -E 's#^https?://[^/]+##')"
expect "LogInTestUser accepts JSON" 204 "$(curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -H 'Content-Type: application/json' -d "{\"action\":\"reauthenticate\",\"UserSelect\":\"$USER_SELECT\",\"AuthenticationLevel\":3}")"
expect "LogInTestUser refuses a party the user cannot act for" 400 "$(curl -s -m 30 -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_ID.1" -d AuthenticationLevel=2)"
ORG=$(curl -s -m 30 "$BASE/Home/GetTestOrgToken?org=ttd")
expect "GetTestOrgToken urn:altinn:org (fake Maskinporten, production exchange)" "ttd" "$(claim "$ORG" urn:altinn:org)"
expect "GetTestOrgToken default service-owner scopes" "altinn:serviceowner/instances.read altinn:serviceowner/instances.write" "$(claim "$ORG" scope)"
expect "GetTestStudioToken carries urn:altinn:app=studio.designer" "studio.designer" "$(claim "$(curl -s -m 30 "$BASE/Home/GetTestStudioToken")" urn:altinn:app)"
rm -f "$H"

[[ $FAIL == 0 ]] && echo "all ok" || { echo "some checks failed"; exit 1; }
