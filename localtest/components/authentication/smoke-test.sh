#!/usr/bin/env bash
# Smoke test for production Authentication in the running stack, through the front door: the browser sign-in (goTo),
# and Authentication as an OpenID Connect provider for the seeded clients (authorize, picker, callback, token with
# client_secret_basic and PKCE, refresh rotation, logout through the fake ID-porten's end-session endpoint), mostly through
# the localtest app's headless code-flow helper (localtest/api/authentication/). The last two checks, Dialogporten's
# end-user API with the cookie JWT (refused) and with the code-flow access token, run only when the dialogporten group is
# up (`--with dialogporten`): 34 checks with the group, 32 without; one more, Authentication's server span in
# VictoriaTraces (task 53), when the monitoring group is up (`--with monitoring`). The cookie checks (task 21b) also call
# Authentication directly: at AUTHENTICATION_URL when set (a host port you publish yourself; neither runtime publishes one, see localtest/CONVENTIONS.mdge),
# else by name from inside the stack's network (../../smoke/lib/containers.sh).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -u
BASE=${1:-${BASE:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}}
AUTHN="$BASE/authentication/api/v1"
HELPER="$BASE/localtest/api/authentication"
FAIL=0
OK=0
check() { local label=$1 expected=$2 actual=$3; if [[ "$actual" == "$expected" ]]; then printf 'ok   %-66s %s\n' "$label" "$actual"; OK=$((OK + 1)); else printf 'FAIL %-66s %s (expected %s)\n' "$label" "$actual" "$expected"; FAIL=1; fi; }
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
# field FILE KEY: a top-level JSON field ("" when absent).
field() { python3 -c 'import json,sys; v=json.load(open(sys.argv[1])).get(sys.argv[2]); print("" if v is None else v)' "$1" "$2"; }
# claim JWT NAME: a claim of the JWT's payload, unverified ("" when absent).
claim() { python3 -c 'import sys,base64,json; p=sys.argv[1].split(".")[1]; p+="="*(-len(p)%4); v=json.loads(base64.urlsafe_b64decode(p)).get(sys.argv[2]); print("" if v is None else v)' "$1" "$2"; }
# qp URL NAME: a query parameter of URL; path URL: its path.
qp() { python3 -c 'import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[""])[0])' "$1" "$2"; }
path() { python3 -c 'import sys,urllib.parse as u; print(u.urlparse(sys.argv[1]).path)' "$1"; }
# shellcheck source=../../smoke/lib/containers.sh
. "$(cd "$(dirname "$0")" && pwd)/../../smoke/lib/containers.sh"
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/health" && curl -sf -o /dev/null "$AUTHN/openid/.well-known/openid-configuration" && break; sleep 1; done
PID_1337=$(curl -s "$BASE/register/api/localtest/test-users" | python3 -c 'import json,sys; print(next(u["ssn"] for u in json.load(sys.stdin) if u["userId"] == 1337))')

echo "== browser sign-in (goTo)"
LOC=$(curl -s -o /dev/null -w '%{redirect_url}' -c "$T/jar" "$AUTHN/authentication?goTo=$BASE/ttd/testapp/")
check "authentication redirects to the ID-porten picker" "/localtest/api/idporten/authorize" "$(path "$LOC")"
CB=$(curl -s -o /dev/null -w '%{redirect_url}' -X POST "$BASE/localtest/api/idporten/authorize" --data-urlencode "client_id=$(qp "$LOC" client_id)" --data-urlencode "state=$(qp "$LOC" state)" --data-urlencode "nonce=$(qp "$LOC" nonce)" --data-urlencode "redirect_uri=$(qp "$LOC" redirect_uri)" --data-urlencode "code_challenge=$(qp "$LOC" code_challenge)" --data-urlencode "code_challenge_method=S256" --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
curl -s -b "$T/jar" -D "$T/h" -o /dev/null "$CB"
check "callback redirects to goTo" "$BASE/ttd/testapp/" "$(grep -i '^Location:' "$T/h" | tr -d '\r' | awk '{print $2}')"
COOKIE=$(grep -i '^Set-Cookie: AltinnStudioRuntime=' "$T/h" | sed -E 's/^Set-Cookie: AltinnStudioRuntime=([^;]*).*/\1/I')
check "the cookie JWT has no pid (production: isAuthCookie)" "" "$(claim "$COOKIE" pid)"
check "refresh with the cookie JWT" "200" "$(code -H "Authorization: Bearer $COOKIE" "$AUTHN/refresh")"

echo "== cookies over plain http (task 21b)"
# The same sign-in, its callback sent straight to Authentication (bypassing the front door, with the public Host): the four cookies come without Secure as Authentication sends them (fakes/Cookies), and the front door's
# callback above passed the same attributes unchanged.
DIRECT=${AUTHENTICATION_URL:-}
LOC=$(curl -s -o /dev/null -w '%{redirect_url}' "$AUTHN/authentication?goTo=$BASE/ttd/testapp/")
CB=$(curl -s -o /dev/null -w '%{redirect_url}' -X POST "$BASE/localtest/api/idporten/authorize" --data-urlencode "client_id=$(qp "$LOC" client_id)" --data-urlencode "state=$(qp "$LOC" state)" --data-urlencode "nonce=$(qp "$LOC" nonce)" --data-urlencode "redirect_uri=$(qp "$LOC" redirect_uri)" --data-urlencode "code_challenge=$(qp "$LOC" code_challenge)" --data-urlencode "code_challenge_method=S256" --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
HOSTPORT=${BASE#http://}; HOSTPORT=${HOSTPORT%%/*}
if [[ -n "$DIRECT" ]]; then
  curl -s -H "Host: $HOSTPORT" -D "$T/hd" -o /dev/null "$DIRECT${CB#"$BASE"}"
else
  localtest_stack_headers "http://authentication:5101${CB#"$BASE"}" "Host: $HOSTPORT" > "$T/hd"
fi
# cookie_attrs HEADERS: each Set-Cookie as "name attributes", lower case, sorted, without its value and Expires.
cookie_attrs() { grep -i '^Set-Cookie:' "$1" | tr -d '\r' | sed -E 's/^Set-Cookie: ([^=]+)=[^;]*/\1/I' | python3 -c 'import sys; print("|".join(sorted(p[0] + " " + ";".join(sorted(a.strip().lower() for a in p[1:] if not a.strip().lower().startswith("expires="))) for p in (l.strip().split(";") for l in sys.stdin))))'; }
check "the callback sent to Authentication directly sets 4 cookies, none with Secure" "4 0" "$(grep -ci '^Set-Cookie:' "$T/hd") $(grep -ci '^Set-Cookie:.*;\s*secure' "$T/hd")"
check "the front door passed the same cookie attributes unchanged" "$(cookie_attrs "$T/hd")" "$(cookie_attrs "$T/h")"

echo "== discovery"
curl -s "$AUTHN/openid/.well-known/openid-configuration" > "$T/disco.json"
check "Authentication's discovery lists end_session_endpoint" "$AUTHN/openid/logout/" "$(field "$T/disco.json" end_session_endpoint)"
curl -s "$BASE/localtest/api/idporten/.well-known/openid-configuration" > "$T/idp.json"
check "the fake ID-porten's discovery lists end_session_endpoint" "$BASE/localtest/api/idporten/endsession" "$(field "$T/idp.json" end_session_endpoint)"

echo "== a registered client with a browser (Arbeidsflate's BFF parameters)"
read -r VERIFIER CHALLENGE < <(python3 -c 'import secrets,hashlib,base64; v=secrets.token_urlsafe(32); print(v, base64.urlsafe_b64encode(hashlib.sha256(v.encode()).digest()).rstrip(b"=").decode())')
AF_REDIRECT="$(sed -E 's#^(https?://)#\1af.#' <<<"$BASE")/api/cb"
LOC=$(curl -s -o /dev/null -w '%{redirect_url}' -G "$AUTHN/authorize" --data-urlencode response_type=code --data-urlencode client_id=arbeidsflate-localtest --data-urlencode "redirect_uri=$AF_REDIRECT" --data-urlencode "scope=digdir:dialogporten.noconsent openid altinn:portal/enduser altinn:correspondence.forward.check" --data-urlencode acr_values=idporten-loa-substantial --data-urlencode state=af-state --data-urlencode nonce=af-nonce --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode code_challenge_method=S256)
# Production widens the upstream acr_values with selfregistered-email for a browser without a live session; the picker
# preselects the highest level it knows.
check "authorize redirects to the picker with acr_values" "/localtest/api/idporten/authorize yes" "$(path "$LOC") $([[ " $(qp "$LOC" acr_values) " == *" idporten-loa-substantial "* ]] && echo yes || echo no)"
CB=$(curl -s -o /dev/null -w '%{redirect_url}' -X POST "$BASE/localtest/api/idporten/authorize" --data-urlencode "client_id=$(qp "$LOC" client_id)" --data-urlencode "state=$(qp "$LOC" state)" --data-urlencode "nonce=$(qp "$LOC" nonce)" --data-urlencode "redirect_uri=$(qp "$LOC" redirect_uri)" --data-urlencode "code_challenge=$(qp "$LOC" code_challenge)" --data-urlencode "code_challenge_method=S256" --data-urlencode "UserSelect=1337.501337" --data-urlencode "AuthenticationLevel=3")
TO=$(curl -s -o /dev/null -w '%{redirect_url}' "$CB")
check "callback redirects to the client's redirect_uri with code and state" "$AF_REDIRECT af-state yes" "${TO%%\?*} $(qp "$TO" state) $([[ -n "$(qp "$TO" code)" ]] && echo yes || echo no)"
curl -s -o "$T/af.json" -u arbeidsflate-localtest:arbeidsflate-localtest-secret "$AUTHN/token" -d grant_type=authorization_code -d client_id=arbeidsflate-localtest --data-urlencode "code=$(qp "$TO" code)" --data-urlencode "code_verifier=$VERIFIER" --data-urlencode "redirect_uri=$AF_REDIRECT"
check "token (client_secret_basic, PKCE): id_token aud, nonce, pid" "arbeidsflate-localtest af-nonce $PID_1337" "$(claim "$(field "$T/af.json" id_token)" aud) $(claim "$(field "$T/af.json" id_token)" nonce) $(claim "$(field "$T/af.json" id_token)" pid)"
check "token: refresh_token_expires_in" "1800" "$(field "$T/af.json" refresh_token_expires_in)"
check "authorize with an unregistered redirect_uri is refused" "400" "$(code -G "$AUTHN/authorize" --data-urlencode response_type=code --data-urlencode client_id=arbeidsflate-localtest --data-urlencode redirect_uri=http://evil.example/cb --data-urlencode scope=openid --data-urlencode state=s --data-urlencode nonce=n --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode code_challenge_method=S256)"

echo "== the code-flow helper (localtest-smoke, user 1337, level 3)"
curl -s -o "$T/t.json" "$HELPER/token?client=localtest-smoke&userId=1337&authenticationLevel=3"
ACCESS=$(field "$T/t.json" access_token); ID=$(field "$T/t.json" id_token); RT=$(field "$T/t.json" refresh_token)
check "helper returns access, id and refresh tokens" "yes yes yes" "$([[ -n "$ACCESS" ]] && echo yes || echo no) $([[ -n "$ID" ]] && echo yes || echo no) $([[ -n "$RT" ]] && echo yes || echo no)"
check "access token pid is user 1337's national identity number" "$PID_1337" "$(claim "$ACCESS" pid)"
check "helper's pid field matches the access token" "$PID_1337" "$(field "$T/t.json" pid)"
check "access token: acr, authlevel, userid" "idporten-loa-substantial 3 1337" "$(claim "$ACCESS" acr) $(claim "$ACCESS" urn:altinn:authlevel) $(claim "$ACCESS" urn:altinn:userid)"
check "access token scope includes digdir:dialogporten.noconsent" "yes" "$([[ " $(claim "$ACCESS" scope) " == *" digdir:dialogporten.noconsent "* ]] && echo yes || echo no)"
check "id token has nonce and sid, aud is the client" "yes yes localtest-smoke" "$([[ -n "$(claim "$ID" nonce)" ]] && echo yes || echo no) $([[ -n "$(claim "$ID" sid)" ]] && echo yes || echo no) $(claim "$ID" aud)"
check "helper with an unknown client is a 400" "400" "$(code "$HELPER/token?client=nope&userId=1337")"

echo "== token endpoint refusals"
curl -s -o "$T/c.json" "$HELPER/code?client=localtest-smoke&userId=1337&authenticationLevel=3"
C=$(field "$T/c.json" code); V=$(field "$T/c.json" code_verifier); R=$(field "$T/c.json" redirect_uri)
redeem() { curl -s -u "localtest-smoke:$1" "$AUTHN/token" -d grant_type=authorization_code --data-urlencode "code=$C" --data-urlencode "code_verifier=$V" --data-urlencode "redirect_uri=$R"; }
check "wrong client secret: invalid_client" "invalid_client" "$(redeem wrong-secret | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"
check "the right secret redeems the code" "Bearer" "$(redeem localtest-smoke-secret | python3 -c 'import json,sys; print(json.load(sys.stdin).get("token_type"))')"
check "a spent code: invalid_grant" "invalid_grant" "$(redeem localtest-smoke-secret | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"

echo "== refresh rotation"
curl -s -o "$T/r1.json" -X POST "$HELPER/refresh" -d client=localtest-smoke --data-urlencode "refresh_token=$RT"
RT2=$(field "$T/r1.json" refresh_token)
check "refresh answers a new refresh token and the same pid" "yes $PID_1337" "$([[ -n "$RT2" && "$RT2" != "$RT" ]] && echo yes || echo no) $(field "$T/r1.json" pid)"
check "the old refresh token is refused on reuse" "invalid_grant" "$(curl -s -X POST "$HELPER/refresh" -d client=localtest-smoke --data-urlencode "refresh_token=$RT" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"
check "reuse revoked the family: the new one is refused too" "invalid_grant" "$(curl -s -X POST "$HELPER/refresh" -d client=localtest-smoke --data-urlencode "refresh_token=$RT2" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"

echo "== logout"
# The helper sets the session cookies as /Home/GetTestUserToken does. Production takes the session from the cookie: an
# id_token_hint of its own is not validated (ChooseProviderByIssuer knows only the upstream issuers), so a logout without
# the cookie ends no session.
curl -s -c "$T/session" -o "$T/l.json" "$HELPER/token?client=localtest-smoke&userId=1337&authenticationLevel=3"
LID=$(field "$T/l.json" id_token); LRT=$(field "$T/l.json" refresh_token)
check "logout without the session cookie: 302, not 500" "302" "$(code "$AUTHN/openid/logout?id_token_hint=$LID")"
check "... and the session's refresh token still works" "200" "$(code -X POST "$HELPER/refresh" -d client=localtest-smoke --data-urlencode "refresh_token=$LRT")"
curl -s -c "$T/session" -o "$T/l.json" "$HELPER/token?client=localtest-smoke&userId=1337&authenticationLevel=3"
LID=$(field "$T/l.json" id_token); LRT=$(field "$T/l.json" refresh_token)
OUT=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' -b "$T/session" -G "$AUTHN/openid/logout" --data-urlencode "id_token_hint=$LID" --data-urlencode "post_logout_redirect_uri=$BASE/" --data-urlencode state=bye)
check "logout redirects to the fake ID-porten's endsession" "302 /localtest/api/idporten/endsession" "${OUT%% *} $(path "${OUT#* }")"
BACK=$(curl -s -o /dev/null -w '%{redirect_url}' "${OUT#* }")
check "endsession redirects to Authentication's handleloggedout" "$AUTHN/logout/handleloggedout" "$BACK"
check "handleloggedout redirects to the platform" "302" "$(code "$BACK")"
check "after logout the session's refresh token is refused" "invalid_grant" "$(curl -s -X POST "$HELPER/refresh" -d client=localtest-smoke --data-urlencode "refresh_token=$LRT" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("error"))')"

echo "== Dialogporten's end-user API with the code-flow access token"
if [[ "$(code "$BASE/dialogporten/health/readiness")" == 200 ]]; then
  curl -s -o "$T/t.json" "$HELPER/token?client=localtest-smoke&userId=1337&authenticationLevel=3"
  # The list needs a party or a service resource; the person's own party.
  DIALOGS="$BASE/dialogporten/api/v1/enduser/dialogs?party=urn:altinn:person:identifier-no:$PID_1337"
  check "the cookie JWT (no pid) is refused: unknown user type" "403" "$(code -H "Authorization: Bearer $COOKIE" "$DIALOGS")"
  check "GET dialogporten/api/v1/enduser/dialogs with the code-flow token" "200" "$(code -H "Authorization: Bearer $(field "$T/t.json" access_token)" "$DIALOGS")"
else
  echo "skip the end-user dialog list (the dialogporten group is not up)"
fi

# OTLP (TODO.md task 53): with the monitoring group up, studioctl sets OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_SERVICE_NAME,
# so a request with our traceparent shows up as a server span named authentication in VictoriaTraces (../../smoke/lib/traces.sh).
# shellcheck source-path=SCRIPTDIR source=../../smoke/lib/traces.sh
. "$(cd "$(dirname "$0")" && pwd)/../../smoke/lib/traces.sh"
if localtest_monitoring_up "$BASE"; then
  read -r TRACE TP < <(localtest_traceparent)
  curl -s -o /dev/null -H "traceparent: $TP" "$AUTHN/openid/.well-known/openid-configuration"
  SPANS=$(localtest_wait_spans "$BASE" "$TRACE" authentication)
  check "OTLP: a server span from service.name authentication" yes "$([[ " $SPANS" == *" 2:"* ]] && echo yes || echo "no: $SPANS")"
else
  echo "skip the OTLP span check (the monitoring group is not up)"
fi

echo "$OK ok"
exit $FAIL
