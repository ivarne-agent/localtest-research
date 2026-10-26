#!/usr/bin/env bash
# Smoke test for the localtest app in the running stack:
# - the front door: the contract the nginx ingress had (routes by host and path, trailing-slash redirects, cookies
#   without Secure, streaming, the app-frontend files), the 503 for a service that is off, and the registration API the
#   app runners use (registers, provisions and removes ttd/proxy-smoke only, and the frontend dev server's route);
# - the fake ID-porten and Maskinporten;
# - Home: the old localtest login contract (start page, LogInTestUser and the party cookies of the selected party,
#   authentication levels in the token, version probe, token helpers), all through production Authentication;
# - in minimal mode (studioctl env up --minimal, read from GET localtest/api/routes, or LOCALTEST_MINIMAL=1), one check
#   per emulated service: the X-Localtest-Emulated header and one representative answer. Everything above runs in both
#   modes: the emulation serves the same paths (Authentication's sign-in and exchanges, Register's test-users, Events'
#   inspection API and stream).
# The registration section needs the localtest app as the front door (it registers routes through the app's own API);
# it is under studioctl's localtest-v2 runtime (task 27a) and the Aspire package (task 27b); behind another front door
# skip it with PROXY_CHECKS=0.
# USER_SELECT must be a user acting for an organisation (userId.orgPartyId); OWN_PARTY is that user's own party.
# FAKE_APP is any address the localtest container reaches that is not an app (default Storage's, which runs in both
# modes), OFF_PREFIX a platform prefix whose service is not running (default /correspondence/, an optional group).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
BASE=${1:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
# The public host and port, for the dedicated hosts (pdf., app-frontend.).
PUBLIC=$(sed -E 's#^https?://([^/]+).*#\1#' <<<"$BASE")
API="$BASE/localtest/api/apps"
ROUTES="$BASE/localtest/api/routes"
FAKE_APP=${FAKE_APP:-http://storage:5010}
OFF_PREFIX=${OFF_PREFIX:-/correspondence/}
NO_APP="No app is registered for this path. Start it with studioctl run or an Aspire AppHost (or, with this app as the front door, run-app.sh <app-dir> <port>)."
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
expect "a file-like path is forwarded (storage answers)" 404 "$(code "$BASE/storage/no-such-file.json")"
expect "unknown path: the front door's own 404" "$NO_APP" "$(curl -s -m 10 "$BASE/nope/missing.js")"
expect "a service that is off: 503 problem details" "503 application/problem+json" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %{content_type}' "$BASE${OFF_PREFIX}x")"
expect "the PDF host belongs to pdf3, /health included" 404 "$(code -H "Host: pdf.$PUBLIC" "$BASE/health")"
SECURE=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:.*;\s*secure')
COOKIES=$(curl -s -m 10 -D - -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d UserSelect=1337.501337 -d AuthenticationLevel=2 | grep -ci '^set-cookie:')
expect "sign-in cookies set, none with Secure" "4 0" "$COOKIES $SECURE"
expect "server-sent events stream through unbuffered" ": connected" "$(timeout 3 curl -sN -m 3 "$BASE/events/api/localtest/stream" | head -1)"
FILE=$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{access-control-allow-origin}' "$BASE/app-frontend/altinn-app-frontend.js")
expect "app-frontend bundle with CORS (needs FRONTEND_DIST)" "200 *" "$FILE"

echo "== monitoring (studioctl env up -r localtest-v2 --with monitoring)"
GRAFANA_ADDRESS=$(curl -s -m 10 "$ROUTES" | python3 -c "import sys,json; print(next((r.get('address') or '' for r in json.load(sys.stdin)['platform'] if r['service']=='grafana'), ''))" 2>/dev/null)
if [[ -n "$GRAFANA_ADDRESS" ]]; then
  expect "Grafana under /grafana/ (its own health)" 200 "$(code "$BASE/grafana/api/health")"
  expect "OTLP/HTTP under /otlp reaches the collector (an empty trace export)" 200 \
    "$(code -X POST -H 'Content-Type: application/json' -d '{"resourceSpans":[]}' "$BASE/otlp/v1/traces")"
  expect "the Home page links to Grafana" 1 "$(curl -s -m 10 "$BASE/" | grep -c 'href="/grafana/"')"
else
  expect "the group is off: Grafana's route is 503 naming the group" "503 monitoring" \
    "$(curl -s -m 10 -w ' %{http_code}' "$BASE/grafana/" | python3 -c "import sys; body, status = sys.stdin.read().rsplit(' ', 1); import json; print(status, json.loads(body)['group'])")"
  expect "the group is off: no Grafana link on the Home page" 0 "$(curl -s -m 10 "$BASE/" | grep -c 'href="/grafana/"')"
fi

if [[ "${PROXY_CHECKS:-1}" != 0 ]]; then
echo "== app registration"
put() { curl -s -m 60 -o /dev/null -w '%{http_code}' -X PUT "$1" -H Content-Type:application/json -d "{\"url\":\"$2\"}"; }
expect "bad app name" 400 "$(put "$API/ttd/Not_Valid" "$FAKE_APP")"
expect "url with a path" 400 "$(put "$API/ttd/proxy-smoke" "$FAKE_APP/x")"
expect "an org that is a platform prefix" 400 "$(put "$API/storage/proxy-smoke" "$FAKE_APP")"
# Any reachable server will do as the "app": Storage answers 404 for /ttd/proxy-smoke/…, which proves the route,
# and the provisioning fails on the missing application metadata, which keeps the route.
expect "register ttd/proxy-smoke: routed, provisioning failed" 502 "$(put "$API/ttd/proxy-smoke" "$FAKE_APP")"
expect "listed with the failed provisioning" "1 failed" "$(curl -s -m 10 "$API" | python3 -c 'import json,sys; r=[r for r in json.load(sys.stdin) if r["org"]=="ttd" and r["app"]=="proxy-smoke"]; print(len(r), r[0]["provisioning"]["state"] if r else "-")')"
expect "routed to the address (Storage's empty 404, not the front door's text)" "" "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "app path without its slash redirects, query kept" "301 /ttd/proxy-smoke/?a=1" "$(curl -s -m 10 -o /dev/null -w '%{http_code} %header{location}' "$BASE/ttd/proxy-smoke?a=1")"
expect "remove (deprovisions)" 204 "$(code -m 60 -X DELETE "$API/ttd/proxy-smoke")"
expect "gone: the front door's 404 again" "$NO_APP" "$(curl -s -m 10 "$BASE/ttd/proxy-smoke/api/v1/applicationmetadata")"
expect "remove again (idempotent)" 204 "$(code -m 60 -X DELETE "$API/ttd/proxy-smoke")"
DEV=(-s -m 10 -H "Host: app-frontend.$PUBLIC")
expect "frontend dev server route set" 200 "$(put "$ROUTES/frontend-dev-server" "$FAKE_APP")"
expect "its host goes to the dev server (Storage's empty 404)" "" "$(curl "${DEV[@]}" "$BASE/ttd/proxy-smoke/x")"
expect "frontend dev server route removed" 204 "$(code -X DELETE "$ROUTES/frontend-dev-server")"
expect "its host is the main host again" "$NO_APP" "$(curl "${DEV[@]}" "$BASE/ttd/proxy-smoke/x")"
fi

echo "== identity providers"
IDP="$BASE/localtest/api/idporten"
expect "ID-porten issuer" "$BASE/localtest/api/idporten" "$(curl -s -m 10 "$IDP/.well-known/openid-configuration" | python3 -c 'import json,sys; print(json.load(sys.stdin)["issuer"])')"
expect "ID-porten JWKS has an RS256 key" "RS256" "$(curl -s -m 10 "$IDP/jwks" | python3 -c 'import json,sys; print(json.load(sys.stdin)["keys"][0]["alg"])')"
expect "Maskinporten issuer" "$BASE/localtest/api/maskinporten/" "$(curl -s -m 10 "$BASE/localtest/api/maskinporten/.well-known/oauth-authorization-server" | python3 -c 'import json,sys; print(json.load(sys.stdin)["issuer"])')"
MP="$BASE/localtest/api/maskinporten"
expect "Maskinporten discovery has token_endpoint" "$MP/token" "$(curl -s -m 10 "$MP/.well-known/oauth-authorization-server" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token_endpoint"])')"
# An unsigned (alg none) assertion with the given payload claims; the token endpoint reads only the payload.
assertion() { python3 -c "import sys,base64,json; b=lambda o: base64.urlsafe_b64encode(json.dumps(o).encode()).rstrip(b'=').decode(); print(b({'alg':'none'})+'.'+b(json.loads(sys.argv[1]))+'.')" "$1"; }
mp_token() { curl -s -m 10 -X POST "$MP/token" -d grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer --data-urlencode "assertion=$(assertion "$1")"; }
json() { python3 -c "import sys,json; print(json.load(sys.stdin)[sys.argv[1]])" "$1"; }
MP_RESPONSE=$(mp_token '{"iss":"localtest-smoke","scope":"altinn:serviceowner/instances.read"}')
MP_TOKEN=$(json access_token <<<"$MP_RESPONSE")
expect "Maskinporten token response: type, lifetime, scope" "Bearer 1800 altinn:serviceowner/instances.read" "$(json token_type <<<"$MP_RESPONSE") $(json expires_in <<<"$MP_RESPONSE") $(json scope <<<"$MP_RESPONSE")"
expect "Maskinporten token: client_id is iss, consumer is the default org (Digdir)" "localtest-smoke 0192:991825827" "$(claim "$MP_TOKEN" client_id) $(claim "$MP_TOKEN" consumer | python3 -c "import sys,ast; print(ast.literal_eval(sys.stdin.read())['ID'])")"
expect "Maskinporten token: the scope is echoed" "altinn:serviceowner/instances.read" "$(claim "$MP_TOKEN" scope)"
expect "the production exchange accepts it (test=true: ttd)" ttd "$(claim "$(curl -s -m 30 -H "Authorization: Bearer $MP_TOKEN" "$BASE/authentication/api/v1/exchange/maskinporten?test=true")" urn:altinn:org)"
consumer_id() { claim "$(json access_token <<<"$(mp_token "$1")")" consumer | python3 -c "import sys,ast; print(ast.literal_eval(sys.stdin.read())['ID'])"; }
expect "a nine-digit client id is the organisation number" 0192:974760673 "$(consumer_id '{"iss":"974760673"}')"
expect "consumer_org in the assertion wins" 0192:974760673 "$(consumer_id '{"iss":"991825827","consumer_org":"974760673"}')"
expect "Maskinporten token: grant_type client_credentials" unsupported_grant_type "$(curl -s -m 10 -X POST "$MP/token" -d grant_type=client_credentials | json error)"
expect "Maskinporten token: no assertion" invalid_request "$(curl -s -m 10 -X POST "$MP/token" -d grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer | json error)"
expect "Maskinporten token: assertion without iss" invalid_request "$(mp_token '{"scope":"x"}' | json error)"
J=$(mktemp); LOC=$(curl -s -m 10 -o /dev/null -w '%{redirect_url}' -c "$J" "$BASE/authentication/api/v1/authentication?goTo=$BASE/")
q() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$LOC" "$1"; }
expect "Authentication sends an S256 PKCE challenge" "S256" "$(q code_challenge_method)"
CB=$(curl -s -m 10 -o /dev/null -w '%{redirect_url}' -X POST "$IDP/authorize" --data-urlencode "client_id=$(q client_id)" --data-urlencode "state=$(q state)" --data-urlencode "redirect_uri=$(q redirect_uri)" --data-urlencode "code_challenge=$(q code_challenge)" -d UserSelect=1337 -d AuthenticationLevel=2)
CODE=$(python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get('code',[''])[0])" "$CB")
TOKEN_REQUEST=(-s -m 10 -o /dev/null -w '%{http_code}' -X POST "$IDP/token" -d grant_type=authorization_code -d "code=$CODE" --data-urlencode "redirect_uri=$(q redirect_uri)")
expect "token endpoint refuses a wrong PKCE verifier" 400 "$(curl "${TOKEN_REQUEST[@]}" -d code_verifier=wrong)"
expect "and the code is spent afterwards" 400 "$(curl "${TOKEN_REQUEST[@]}" -d code_verifier=wrong)"
rm -f "$J"
# The provider on its own, with a PKCE pair made here: the picker, the code and the tokens.
CB_URI="$BASE/authentication/api/v1/upstream/callback"
read -r VERIFIER CHALLENGE < <(python3 -c 'import secrets,hashlib,base64; v=secrets.token_urlsafe(48); print(v, base64.urlsafe_b64encode(hashlib.sha256(v.encode()).digest()).rstrip(b"=").decode())')
PICKER=$(curl -s -m 10 -w '\n%{http_code}' "$IDP/authorize?client_id=localtest&redirect_uri=$CB_URI&state=smoke-state&nonce=smoke-nonce&code_challenge=$CHALLENGE&code_challenge_method=S256")
expect "the picker page answers" 200 "$(tail -1 <<<"$PICKER")"
expect "the picker keeps state in the form" yes "$(grep -q 'name="state" value="smoke-state"' <<<"$PICKER" && echo yes || echo no)"
expect "the picker preselects level 2 without acr_values" yes "$(grep -q '<option value="2" selected' <<<"$PICKER" && echo yes || echo no)"
pick() { curl -s -m 10 -o /dev/null -w '%{http_code} %{redirect_url}' -X POST "$IDP/authorize" -d client_id=localtest -d state=smoke-state -d nonce=smoke-nonce -d "code_challenge=$CHALLENGE" -d UserSelect=1337 "$@"; }
expect "an off-platform redirect_uri is refused" 400 "$(pick --data-urlencode redirect_uri=https://example.com/cb -d AuthenticationLevel=2 | cut -d' ' -f1)"
expect "an unknown level is refused" 400 "$(pick --data-urlencode "redirect_uri=$CB_URI" -d AuthenticationLevel=7 | cut -d' ' -f1)"
PICKED=$(pick --data-urlencode "redirect_uri=$CB_URI" -d AuthenticationLevel=3 | cut -d' ' -f2)
pq() { python3 -c "import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[''])[0])" "$PICKED" "$1"; }
expect "the code comes back with state and iss" "smoke-state $IDP" "$(pq state) $(pq iss)"
token() { curl -s -m 10 -X POST "$IDP/token" -d grant_type=authorization_code -d "code=$(pq code)" -d client_id=localtest "$@"; }
expect "a wrong redirect_uri at the token endpoint is refused (and spends the code)" invalid_grant "$(token --data-urlencode "redirect_uri=$BASE/elsewhere" -d "code_verifier=$VERIFIER" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"
PICKED=$(pick --data-urlencode "redirect_uri=$CB_URI" -d AuthenticationLevel=3 | cut -d' ' -f2)
ID_TOKEN=$(token --data-urlencode "redirect_uri=$CB_URI" -d "code_verifier=$VERIFIER" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id_token"])')
KID=$(python3 -c "import sys,base64,json; h=sys.argv[1].split('.')[0]; h+='='*(-len(h)%4); print(json.loads(base64.urlsafe_b64decode(h))['kid'])" "$ID_TOKEN")
expect "the id_token's kid is in the JWKS" yes "$(curl -s -m 10 "$IDP/jwks" | python3 -c "import json,sys; print('yes' if any(k['kid']==sys.argv[1] for k in json.load(sys.stdin)['keys']) else 'no')" "$KID")"
expect "the id_token's aud, nonce and acr" "localtest smoke-nonce idporten-loa-substantial" "$(claim "$ID_TOKEN" aud) $(claim "$ID_TOKEN" nonce) $(claim "$ID_TOKEN" acr)"
expect "an unknown localtest API path: the localtest app's own 404" 404 "$(code "$BASE/localtest/api/nope")"
expect "HEAD on the start page" 200 "$(curl -s -m 10 -o /dev/null -w '%{http_code}' -I "$BASE/")"

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
# The production exchange looks the org code up at altinncdn.no, so this needs internet access from Authentication.
ORG=$(curl -s -m 30 "$BASE/Home/GetTestOrgToken?org=ttd")
expect "GetTestOrgToken urn:altinn:org (fake Maskinporten, production exchange)" "ttd" "$(claim "$ORG" urn:altinn:org)"
expect "GetTestOrgToken default service-owner scopes" "altinn:serviceowner/instances.read altinn:serviceowner/instances.write" "$(claim "$ORG" scope)"
expect "GetTestStudioToken carries urn:altinn:app=studio.designer" "studio.designer" "$(claim "$(curl -s -m 30 "$BASE/Home/GetTestStudioToken")" urn:altinn:app)"
rm -f "$H"

MINIMAL=${LOCALTEST_MINIMAL:-$(curl -s -m 10 "$ROUTES" | python3 -c 'import json,sys; print(1 if any(r.get("address") == "emulated" for r in json.load(sys.stdin)["platform"]) else 0)' 2>/dev/null || echo 0)}
if [[ "$MINIMAL" == 1 ]]; then
echo "== emulated services (minimal mode)"
# The header and the body of one request: "<header> <python expression over the JSON body d, or the raw body b>".
emulated() { # name service python-expression curl-args...
  local name=$1 service=$2 expr=$3; shift 3
  local hdr body; hdr=$(mktemp); body=$(mktemp)
  curl -s -m 30 -D "$hdr" -o "$body" "$@"
  local got; got=$(grep -i '^X-Localtest-Emulated:' "$hdr" | awk '{print $2}' | tr -d '\r')
  local ok; ok=$(python3 -I -c "import json,sys; b=open(sys.argv[1],'rb').read(); d=None
try: d=json.loads(b)
except Exception: pass
print('yes' if ($expr) else 'no')" "$body" 2>/dev/null)
  expect "$name" "$service yes" "$got ${ok:-no}"; rm -f "$hdr" "$body"
}
USER_TOKEN=$(curl -s -m 30 "$BASE/Home/GetTestUserToken?userId=$USER_ID")
emulated "register: party lookup by organisation number" register 'd["partyId"] == 500000 and len(d["name"]) > 0' \
  -X POST -H Content-Type:application/json "$BASE/register/api/v1/parties/lookup" -d '{"orgNo":"897069644"}'
emulated "profile: the user's profile with its party" profile "d[\"userId\"] == $USER_ID and d[\"party\"][\"partyId\"] == $OWN_PARTY" \
  "$BASE/profile/api/v1/users/$USER_ID"
emulated "authorization: Permit per request, the action echoed" authorization '[r["decision"] for r in d["response"]] == ["Permit", "Permit"] and [a["value"] for r in d["response"] for c in r["category"] for a in c["attribute"]] == ["read", "sign"]' \
  -X POST -H Content-Type:application/json "$BASE/authorization/api/v1/decision" -d '{"request":{"accessSubject":[{"id":"s1","attribute":[{"attributeId":"urn:altinn:userid","value":"1337","dataType":"string"}]}],"action":[{"id":"a1","attribute":[{"attributeId":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"read","dataType":"string","includeInResult":true}]},{"id":"a2","attribute":[{"attributeId":"urn:oasis:names:tc:xacml:1.0:action:action-id","value":"sign","dataType":"string","includeInResult":true}]}],"resource":[{"id":"r1","attribute":[{"attributeId":"urn:altinn:org","value":"ttd","dataType":"string"}]}],"multiRequests":{"requestReference":[{"referenceId":["s1","a1","r1"]},{"referenceId":["s1","a2","r1"]}]}}}'
emulated "pdf3: POST /pdf answers the static PDF" pdf3 'b.startswith(b"%PDF-1.4") and b"Minimal PDF without content" in b' \
  -X POST -H "Host: pdf.$PUBLIC" -H Content-Type:application/json "$BASE/pdf" -d '{"url":"'"$BASE"'/ttd/app/"}'
emulated "authentication: discovery on the public origin" authentication 'd["issuer"] == "'"$BASE"'/authentication/api/v1/openid/"' \
  "$BASE/authentication/api/v1/openid/.well-known/openid-configuration"
emulated "access-management: the user's authorized parties" access-management "$ORG_PARTY in [p[\"partyId\"] for p in d[\"data\"]]" \
  -H "Authorization: Bearer $USER_TOKEN" "$BASE/accessmanagement/api/v1/enduser/authorizedparties?includeSubParties=true"
emulated "events: the inspection list" events '"items" in d' "$BASE/events/api/localtest/events?limit=1"
emulated "notifications: the inspection list" notifications '"items" in d' "$BASE/notifications/api/localtest/messages?limit=1"
emulated "resource-registry: 501 naming the mode" resource-registry 'd["status"] == 501 and d["mode"] == "minimal"' "$BASE/resourceregistry/api/v1/resource"
fi

if [[ $FAIL == 0 ]]; then echo "all ok"; else echo "some checks failed"; exit 1; fi
