#!/usr/bin/env bash
# Checks the old localtest login contract on the authentication component: start page, LogInTestUser (204 and the
# party cookies of the selected party), authentication levels in the token, version probe and token endpoints.
# BASE is the authentication service itself or the proxy (default); the register component must be reachable from it.
# USER_SELECT must be a user acting for an organisation (userId.orgPartyId); OWN_PARTY is that user's own party.
set -u
BASE=${BASE:-http://local.altinn.cloud:8000}
USER_SELECT=${USER_SELECT:-1337.500000}
OWN_PARTY=${OWN_PARTY:-501337}
USER_ID=${USER_SELECT%%.*}
ORG_PARTY=${USER_SELECT##*.}
FAIL=0
check() { local label=$1 expected=$2 actual=$3; if [[ "$actual" == "$expected" ]]; then printf 'ok   %-62s %s\n' "$label" "$actual"; else printf 'FAIL %-62s %s (expected %s)\n' "$label" "$actual" "$expected"; FAIL=1; fi; }
claim() { python3 -c "import sys,base64,json; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p)).get(sys.argv[2]))" "$1" "$2"; }
cookie() { grep -i "^Set-Cookie: $2=" "$1" | sed -E "s/^Set-Cookie: $2=([^;]*).*/\1/I" | tr -d '\r'; }

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/authentication/api/v1/openid/.well-known/openid-configuration" && break; sleep 1; done

check "GET /Home/Localtest/Version" "8" "$(curl -s "$BASE/Home/Localtest/Version")"
check "version content type" "application/json" "$(curl -s -o /dev/null -w '%{content_type}' "$BASE/Home/Localtest/Version")"
START=$(curl -s "$BASE/")
check "start page lists the user's own party" "yes" "$(grep -q "<option value=\"$USER_ID.$OWN_PARTY\">" <<<"$START" && echo yes || echo no)"
check "start page lists the organisation option" "yes" "$(grep -q "<option value=\"$USER_SELECT\">[^<]* for " <<<"$START" && echo yes || echo no)"
check "/Home/Index serves the same page" "200" "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/Home/Index")"

H=$(mktemp)
check "POST /Home/LogInTestUser reauthenticate" "204" "$(curl -s -D "$H" -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_SELECT" -d AuthenticationLevel=2)"
TOKEN=$(cookie "$H" AltinnStudioRuntime)
check "AltinnPartyId cookie is the selected party" "$ORG_PARTY" "$(cookie "$H" AltinnPartyId)"
check "AltinnPartyUuid cookie is set" "yes" "$([[ "$(cookie "$H" AltinnPartyUuid)" =~ ^[0-9a-f-]{36}$ && "$(cookie "$H" AltinnPartyUuid)" != 00000000-0000-0000-0000-000000000000 ]] && echo yes || echo no)"
check "token urn:altinn:userid" "$USER_ID" "$(claim "$TOKEN" urn:altinn:userid)"
check "token keeps the user's own urn:altinn:partyid" "$OWN_PARTY" "$(claim "$TOKEN" urn:altinn:partyid)"

for level in 0 1 2 3 4; do
  curl -s -D "$H" -o /dev/null -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_ID.$OWN_PARTY" -d "AuthenticationLevel=$level"
  check "LogInTestUser level $level -> urn:altinn:authlevel" "$level" "$(claim "$(cookie "$H" AltinnStudioRuntime)" urn:altinn:authlevel)"
done
for level in 1 2 3 4; do
  check "GetTestUserToken level $level -> urn:altinn:authlevel" "$level" "$(claim "$(curl -s "$BASE/Home/GetTestUserToken?userId=$USER_ID&authenticationLevel=$level" | tr -d '"')" urn:altinn:authlevel)"
done

check "LogInTestUser start redirects to the app" "/ttd/my-app/" "$(curl -s -o /dev/null -w '%{redirect_url}' -X POST "$BASE/Home/LogInTestUser" -d action=start -d "UserSelect=$USER_SELECT" -d AuthenticationLevel=3 -d AppPathSelection=ttd/my-app | sed -E 's#^https?://[^/]+##')"
check "LogInTestUser accepts JSON" "204" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -H 'Content-Type: application/json' -d "{\"action\":\"reauthenticate\",\"UserSelect\":\"$USER_SELECT\",\"AuthenticationLevel\":3}")"
check "LogInTestUser refuses a party the user cannot act for" "400" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/Home/LogInTestUser" -d action=reauthenticate -d "UserSelect=$USER_ID.1" -d AuthenticationLevel=2)"

ORG=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd" | tr -d '"')
check "GetTestOrgToken urn:altinn:org" "ttd" "$(claim "$ORG" urn:altinn:org)"
check "GetTestOrgToken default service-owner scopes" "altinn:serviceowner/instances.read altinn:serviceowner/instances.write" "$(claim "$ORG" scope)"

rm -f "$H"
exit $FAIL
