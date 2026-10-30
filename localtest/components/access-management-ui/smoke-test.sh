#!/usr/bin/env bash
# Smoke test for the Access Management GUI (am.ui) in the running stack, everything through the front door: the GUI on
# http://am.ui.local.altinn.cloud:8000, the platform on BASE.
#   studioctl env up -r localtest-v2 --with access-management   (or --with everything)
#   ./smoke-test.sh [BASE]                                       (default http://local.altinn.cloud:8000; AM overrides the GUI)
#
# In order: health; the redirect to the stack's Authentication without a cookie; the sign-in as a browser does it, with a
# cookie jar (the GUI -> authentication?goto -> the fake ID-porten's picker as user 1337 at level 3 -> upstream/callback
# -> back on the GUI with the AltinnStudioRuntime cookie for the public host); the shell (the Razor view, production's
# feature flags, the bundle, the CSP, no HSTS, cookies without Secure); the BFF's API against the stack: the user's
# profile (Profile, with the BFF's own amui platform access token), the user's parties (Access Management's authorized
# parties), the roles on DDG Fitness, the PDP's answer for the seeded altinn_access_management (isAdmin, isHovedadmin);
# then one delegation made through the GUI's API (an access package from DDG Fitness to Kari Nordvik, with the
# antiforgery header the SPA sends), read back from Access Management's own end-user API, and revoked again; a
# delegation without the antiforgery header is refused.
#
# Leaves behind: nothing it created (the delegation and the right-holder connection are removed again; a failure in
# between can leave them, the next run removes them first). The Authentication session of the sign-in stays until it
# expires.
#
# am.ui.local.altinn.cloud must resolve to this machine for a browser (`studioctl env hosts add`); when it does not, curl
# is told with --resolve.
set -uo pipefail
BASE=${1:-${LOCALTEST_ORIGIN:-http://local.altinn.cloud:8000}}
AM=${AM:-$(sed -E 's#^(https?://)#\1am.ui.#' <<<"$BASE")}
UI="$AM/accessmanagement"
API="$UI/api/v1"
AMAPI="$BASE/accessmanagement/api/v1"
FAIL=0
OK=0
T=$(mktemp -d)
trap 'rm -rf "$T"' EXIT
# The test data: Sophie Salt (1337, party 501337), DAGL of DDG Fitness (500000); Kari Nordvik (2002, party 512002).
USER_ID=${USER_ID:-1337}
ORG_PARTY_ID=${ORG_PARTY_ID:-500000}
TO_USER_ID=${TO_USER_ID:-2002}
PACKAGE=${PACKAGE:-urn:altinn:accesspackage:sport-og-fritid}

AM_HOSTPORT=${AM#http://}
AM_HOST=${AM_HOSTPORT%%:*}
RESOLVE=()
if ! getent hosts "$AM_HOST" >/dev/null; then
  RESOLVE=(--resolve "$AM_HOSTPORT:127.0.0.1")
  echo "note: $AM_HOST does not resolve here; curl gets --resolve $AM_HOSTPORT:127.0.0.1 (a browser needs studioctl env hosts add)"
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
header() { grep -i "^$2:" "$1" | head -1 | cut -d' ' -f2- | tr -d '\r'; }
status() { head -1 "$1" | awk '{print $2}'; }
# cookie_attrs HEADERS NAME: the attributes of NAME's Set-Cookie, lower case, sorted, without its value and Expires.
cookie_attrs() {
  grep -i "^Set-Cookie: $2=" "$1" | head -1 | tr -d '\r' | cut -d';' -f2- | tr ';' '\n' | sed 's/^ *//' |
    grep -vi '^expires=' | tr '[:upper:]' '[:lower:]' | sort | paste -sd';'
}
jar_value() { awk -v n="$2" '$6 == n { v = $7 } END { print v }' "$1"; }
token() { curl -s "$BASE/Home/GetTestUserToken?userId=$1&authenticationLevel=3" | tr -d '"'; }

echo "== health"
for _ in $(seq 1 120); do [[ "$(code "$AM/health")" == 200 ]] && break; sleep 1; done
expect "GET /health through the front door" 200 "$(code "$AM/health")"
if [[ "$(code "$AM/health")" != 200 ]]; then
  c "$AM/health" | head -c 400
  echo
  echo "the GUI does not answer; is the access-management group up?"
  exit 1
fi

echo "== no cookie: the redirect to the stack's Authentication"
c -D "$T/r0" -o /dev/null "$UI/ui/users"
expect "GET /accessmanagement/ui/users without a cookie" 302 "$(status "$T/r0")"
LOC=$(header "$T/r0" Location)
expect "  ... to authentication?goto on the platform's origin" "$BASE/authentication/api/v1/authentication" "${LOC%%\?*}"
expect "  ... goto is the page on am.ui with openAccountMenu" "$UI/ui/users?openAccountMenu=true" "$(qp "$LOC" goto)"

echo "== sign-in as a browser does it (user $USER_ID, level 3)"
J="$T/jar"
PICKER=$(c -c "$J" -b "$J" -o /dev/null -w '%{redirect_url}' "$LOC")
expect "Authentication sends the browser to the fake ID-porten's picker" "$BASE/localtest/api/idporten/authorize" "${PICKER%%\?*}"
CB=$(c -c "$J" -b "$J" -o /dev/null -w '%{redirect_url}' -X POST "$BASE/localtest/api/idporten/authorize" \
  --data-urlencode "client_id=$(qp "$PICKER" client_id)" --data-urlencode "state=$(qp "$PICKER" state)" \
  --data-urlencode "nonce=$(qp "$PICKER" nonce)" --data-urlencode "redirect_uri=$(qp "$PICKER" redirect_uri)" \
  --data-urlencode "code_challenge=$(qp "$PICKER" code_challenge)" --data-urlencode "code_challenge_method=S256" \
  --data-urlencode "UserSelect=$USER_ID.50$USER_ID" --data-urlencode "AuthenticationLevel=3")
BACK=$(c -c "$J" -b "$J" -o /dev/null -w '%{redirect_url}' "$CB")
expect "upstream/callback signs in and goes back to the GUI" "$UI/ui/users?openAccountMenu=true" "$BACK"
expect "  ... with the runtime cookie for the public host" "yes" \
  "$(awk '$6 == "AltinnStudioRuntime" && ($1 ~ /^\.?'"${BASE#http://}"'/ || $1 ~ /local/) {f=1} END {print f ? "yes" : "no"}' "$J")"

echo "== the shell"
c -c "$J" -b "$J" -D "$T/sh" -o "$T/shell.html" "$BACK"
expect "GET the page signed in" 200 "$(status "$T/sh")"
expect "  ... the SPA's root and bundle" yes \
  "$(grep -q '<div id="root">' "$T/shell.html" && grep -q '/accessmanagement/assets/accessmanagement.js' "$T/shell.html" && echo yes || echo no)"
expect "  ... not Vite's dev server (the image runs Production)" no "$(grep -q 'localhost:5173' "$T/shell.html" && echo yes || echo no)"
sed -n 's/.*window.featureFlags = \(.*\);.*/\1/p' "$T/shell.html" >"$T/flags.json"
check "  ... production's feature flags: every one off" 'len(d) > 0 and not any(d.values())' <"$T/flags.json"
expect "  ... XSRF-TOKEN without Secure (the cookie policy on http)" "path=/;samesite=strict" "$(cookie_attrs "$T/sh" XSRF-TOKEN)"
expect "  ... a CSP" yes "$( [[ -n "$(header "$T/sh" Content-Security-Policy)" ]] && echo yes || echo no)"
expect "  ... no Strict-Transport-Security" "" "$(header "$T/sh" Strict-Transport-Security)"
c -o "$T/bundle.js" -w '%{http_code}' "$UI/assets/accessmanagement.js" >"$T/bundle.code"
expect "GET the bundle" 200 "$(cat "$T/bundle.code")"
expect "  ... with patch 0003 (the links follow the page's am.ui.<host>)" yes \
  "$(grep -qE 'host\.startsWith\(.am\.ui\..\)' "$T/bundle.js" && echo yes || echo no)"
expect "GET its stylesheet" 200 "$(code "$UI/assets/accessmanagement.css")"
XSRF=$(jar_value "$J" XSRF-TOKEN)
expect "the jar holds the antiforgery pair" yes "$( [[ -n "$XSRF" && -n "$(jar_value "$J" AS-XSRF-TOKEN)" ]] && echo yes || echo no)"

echo "== the BFF's API against the stack"
c -b "$J" "$API/user/profile" >"$T/profile.json"
check "user/profile: user $USER_ID from Profile (the amui platform access token accepted)" \
  "d['userId'] == $USER_ID and d['party']['partyId'] == 50$USER_ID" <"$T/profile.json"
SELF_UUID=$(py "d['party']['id']" <"$T/profile.json")
c -b "$J" "$API/user/actorlist/old" >"$T/actors.json"
check "user/actorlist/old: the user's parties from Access Management, DDG Fitness among them" \
  "any(p['partyId'] == $ORG_PARTY_ID for p in d)" <"$T/actors.json"
ORG_UUID=$(py "next(p['partyUuid'] for p in d if p['partyId'] == $ORG_PARTY_ID)" <"$T/actors.json")
expect "  ... DDG Fitness's party uuid" yes "$( [[ -n "$ORG_UUID" ]] && echo yes || echo no)"
c -b "$J" "$API/user/reportee/$ORG_UUID" >"$T/reportee.json"
check "user/reportee/{uuid}: DDG Fitness" "d['id'] == '$ORG_UUID' and d['partyId'] == $ORG_PARTY_ID" <"$T/reportee.json"
expect "user/isAdmin on DDG Fitness: the PDP permits the seeded altinn_access_management (DAGL's hovedadministrator)" \
  true "$(c -b "$J" "$API/user/isAdmin?party=$ORG_UUID")"
expect "user/isHovedadmin on DDG Fitness" true "$(c -b "$J" "$API/user/isHovedadmin?party=$ORG_UUID")"
expect "user/isAdmin on the user's own party" true "$(c -b "$J" "$API/user/isAdmin?party=$SELF_UUID")"
c -b "$J" "$API/role/permissions?party=$ORG_UUID&from=$ORG_UUID&to=$SELF_UUID" >"$T/roles.json"
check "role/permissions: the user's roles on DDG Fitness from Access Management, daglig leder among them" \
  "any(r['role']['urn'] in ('urn:altinn:external-role:ccr:daglig-leder', 'urn:altinn:rolecode:dagl') for r in d)" <"$T/roles.json"

echo "== the party switch the apps and the inbox link to (reportee/changeandredirect)"
GOTO="$(sed -E 's#^(https?://)#\1af.#' <<<"$BASE")/"
c -b "$J" -D "$T/cr" -o /dev/null -G "$API/reportee/changeandredirect" --data-urlencode "partyId=$ORG_PARTY_ID" --data-urlencode "goTo=$GOTO"
expect "changeandredirect?partyId=$ORG_PARTY_ID&goTo=<the inbox>: redirects to goTo" "302 $GOTO" "$(status "$T/cr") $(header "$T/cr" Location)"
PUBLIC_HOST=$(sed -E 's#^https?://([^:/]+).*#\1#' <<<"$BASE")
expect "  ... the party cookie for the public host, without Secure" "domain=$PUBLIC_HOST;path=/;samesite=lax" \
  "$(cookie_attrs "$T/cr" AltinnPartyId)"
expect "  ... its value" "$ORG_PARTY_ID" "$(grep -i '^Set-Cookie: AltinnPartyId=' "$T/cr" | head -1 | sed -E 's/^[^=]*=([^;]*).*/\1/')"
c -b "$J" -D "$T/cr2" -o /dev/null -G "$API/reportee/changeandredirect" --data-urlencode "partyId=$ORG_PARTY_ID" --data-urlencode "goTo=https://evil.example/"
expect "  ... a goTo outside the allowed domains goes to the GUI instead" "$AM" "$(header "$T/cr2" Location)"

echo "== a delegation through the GUI's API, read back from Access Management"
SOPHIE=$(token "$USER_ID")
# Kari as the GUI adds a person: her national identity number and last name (production's rule: a person without a
# connection to the party is not looked up by uuid).
curl -s "$BASE/register/api/localtest/test-users" >"$T/users.json"
KARI_SSN=$(py "next(u['ssn'] for u in d if u['userId'] == $TO_USER_ID)" <"$T/users.json")
KARI_UUID=$(py "next(u['partyUuid'] for u in d if u['userId'] == $TO_USER_ID)" <"$T/users.json")
KARI_LAST=Nordvik
expect "Kari Nordvik in the Register's test users" yes "$( [[ -n "$KARI_SSN" && -n "$KARI_UUID" ]] && echo yes || echo no)"
c -b "$J" "$API/accesspackage/search?searchString=" >"$T/packages.json"
PACKAGE_ID=$(python3 -c "import json,sys
def walk(x):
    if isinstance(x, dict):
        if x.get('urn') == sys.argv[1]: print(x['id']); sys.exit(0)
        for v in x.values(): walk(v)
    elif isinstance(x, list):
        for v in x: walk(v)
walk(json.load(open(sys.argv[2])))" "$PACKAGE" "$T/packages.json")
expect "accesspackage/search finds $PACKAGE" yes "$( [[ -n "$PACKAGE_ID" ]] && echo yes || echo no)"
xsrf() { c -b "$J" -c "$J" -H "X-XSRF-TOKEN: $XSRF" "$@"; }
# What an earlier, failed run left behind goes first.
xsrf -X DELETE -o /dev/null "$API/accesspackage/delegations?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID&packageId=$PACKAGE_ID"
xsrf -X DELETE -o /dev/null "$API/connection/reportee?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID"
expect "POST without the antiforgery header is refused" 400 \
  "$(code -b "$J" -X POST "$API/accesspackage/delegations?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID&packageId=$PACKAGE_ID")"
RH=$(xsrf -X POST -H 'Content-Type: application/json' -w ' %{http_code}' \
  -d "{\"personIdentifier\":\"$KARI_SSN\",\"lastName\":\"$KARI_LAST\"}" "$API/connection/reportee/$ORG_UUID/rightholder")
expect "connection/reportee/{uuid}/rightholder: Kari becomes a right holder of DDG Fitness, by identifier and last name" \
  "\"$KARI_UUID\" 200" "$RH"
expect "accesspackage/delegations: DDG Fitness delegates $PACKAGE to Kari" 200 \
  "$(xsrf -X POST -o "$T/del.json" -w '%{http_code}' "$API/accesspackage/delegations?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID&packageId=$PACKAGE_ID")"
curl -s -H "Authorization: Bearer $SOPHIE" \
  "$AMAPI/enduser/connections/accesspackages?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID" >"$T/am.json"
check "  ... Access Management's end-user API lists it from DDG Fitness to Kari" "'$PACKAGE' in json.dumps(d)" <"$T/am.json"
c -b "$J" "$API/accesspackage/delegations?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID" >"$T/gui.json"
check "  ... and the GUI's own view of Kari's packages" "'$PACKAGE' in json.dumps(d)" <"$T/gui.json"
REVOKE=$(xsrf -X DELETE -o /dev/null -w '%{http_code}' "$API/accesspackage/delegations?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID&packageId=$PACKAGE_ID")
expect "DELETE accesspackage/delegations: revoked" yes "$( [[ $REVOKE == 200 || $REVOKE == 204 ]] && echo yes || echo "no: $REVOKE")"
curl -s -H "Authorization: Bearer $SOPHIE" \
  "$AMAPI/enduser/connections/accesspackages?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID" >"$T/am2.json"
check "  ... and gone from Access Management's API" "'$PACKAGE' not in json.dumps(d)" <"$T/am2.json"
expect "DELETE connection/reportee: Kari is no longer a right holder" 204 \
  "$(xsrf -X DELETE -o /dev/null -w '%{http_code}' "$API/connection/reportee?party=$ORG_UUID&from=$ORG_UUID&to=$KARI_UUID")"

echo "$OK passed, $([[ $FAIL == 0 ]] && echo "no failures" || echo "FAILURES")"
exit $FAIL
