#!/usr/bin/env bash
# Smoke test for the localtest authorization component (production PDP + fakes), run without Docker or an ingress.
# Starts register, authentication and the PDP from this checkout's Release builds on their own ports, a stub for the
# Access Management PIP (no delegations), pushes the app policies through the production policy endpoint with a Studio
# token (as the app runner does), gets a user token from authentication, and asks the production decision endpoint what
# an app's PEP would ask. Stops only the processes it started. Build first, for each of register, authentication and authorization:
#   (cd ../<component> && DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets \
#      dotnet build -c Release vendor/src/apps/<host project folder>)
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); COMP=$(dirname "$HERE")
R=$COMP/register; A=$COMP/authentication; Z=$HERE
CERT=$COMP/../certs/localtest-accesstoken.pfx   # the shared platform access token certificate (Studio token, PlatformAccessToken)
REG_PORT=${REG_PORT:-5021}; AUTHN_PORT=${AUTHN_PORT:-8001}; PDP_PORT=${PDP_PORT:-5070}; AM_PORT=${AM_PORT:-5081}
APPS=${APPS:-/home/agent/code/altinn-studio/src/test/apps}   # the frontend-test policy comes from the app itself
PLATFORM=http://local.altinn.cloud:$AUTHN_PORT/   # what authentication issues tokens and openid metadata for
PDP=http://127.0.0.1:$PDP_PORT; LOGS=${LOGS:-/tmp/authz-smoke}; mkdir -p "$LOGS"
STORE=$(mktemp -d)   # the policy store, empty: every app policy arrives through POST authorization/api/v1/policies
SEED=$(mktemp); PIDS=(); trap 'kill "${PIDS[@]}" 2>/dev/null; rm -rf "$STORE" "$SEED"' EXIT
# The register seed plus the Cypress default user (12345, Ola Nordmann, party 512345, no roles for anyone), unless the
# register's own seed already has him.
python3 -I - "$R/testdata/testData.json" "$SEED" <<'PY'
import sys, json
d = json.load(open(sys.argv[1]))
if not any(p.get("userId") == 12345 for p in d["persons"]):
    d["persons"].append({"userId": 12345, "partyId": 512345, "ssn": "01017510084", "firstName": "Ola", "lastName": "Nordmann",
                         "userName": "OlaNordmann", "email": "test@test.com", "language": "nb", "partyRoles": {}})
json.dump(d, open(sys.argv[2], "w"))
PY

start() { # name dir dll env...
  local name=$1 dir=$2 dll=$3; shift 3
  (cd "$dir" && exec env "$@" dotnet "$dll" >"$LOGS/$name.log" 2>&1) & PIDS+=($!)
}
start register "$R/vendor/src/apps/Altinn.Register/src/Altinn.Register/bin/Release/net10.0" Altinn.Register.dll \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:$REG_PORT \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath="$CERT" \
  Localtest__Register__TestDataPath="$SEED" \
  GeneralSettings__OpenIdWellKnownEndpoint=${PLATFORM}authentication/api/v1/openid/
start authentication "$A/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/bin/Release/net10.0" Altinn.Platform.Authentication.dll \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:$AUTHN_PORT GeneralSettings__PlatformEndpoint=$PLATFORM \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath="$CERT" \
  Localtest__Authentication__RegisterLocaltestEndpoint=http://127.0.0.1:$REG_PORT/register/api/localtest/
# The PDP's own platform endpoint is the authentication port (openid metadata); register and the register seed are
# pointed at the register's real port, since there is no ingress here.
start authorization "$Z/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/bin/Release/net10.0" Altinn.Authorization.dll \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:$PDP_PORT \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath="$CERT" \
  Localtest__Authorization__PlatformEndpoint=$PLATFORM \
  Localtest__Authorization__PoliciesPath="$Z/policies" \
  Localtest__Authorization__PolicyStorePath="$STORE" \
  Localtest__Authorization__RegisterLocaltestEndpoint=http://127.0.0.1:$REG_PORT/register/api/localtest/ \
  PlatformSettings__ApiRegisterEndpoint=http://127.0.0.1:$REG_PORT/register/api/v1/ \
  PlatformSettings__ApiAccessManagementEndpoint=http://127.0.0.1:$AM_PORT/accessmanagement/api/v1/
# Access Management PIP stand-in: no delegation changes.
python3 -I - $AM_PORT >"$LOGS/am-stub.log" 2>&1 <<'STUB' & PIDS+=($!)
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
class H(BaseHTTPRequestHandler):
    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length") or 0))
        ok = self.path.endswith("policyinformation/getdelegationchanges"); body = b"[]" if ok else b"{}"
        self.send_response(200 if ok else 404); self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *a): pass
HTTPServer(("127.0.0.1", int(sys.argv[1])), H).serve_forever()
STUB

for i in $(seq 1 90); do
  curl -sf -o /dev/null http://127.0.0.1:$REG_PORT/health && curl -sf -o /dev/null http://127.0.0.1:$AUTHN_PORT/health \
    && curl -sf -o /dev/null $PDP/health && break; sleep 1
done
echo "health after ${i}s: register $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$REG_PORT/health)," \
  "authentication $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$AUTHN_PORT/health), pdp $(curl -s -o /dev/null -w '%{http_code}' $PDP/health)"

FAIL=0
# What the app runner pushes: the app policies, [ORG]/[org]/[APP]/[app] replaced as Designer does, with a Studio token.
STUDIO=$(curl -s "http://127.0.0.1:$AUTHN_PORT/Home/GetTestStudioToken" | tr -d '"')
pushpolicy() { # org app file [token]
  sed -e "s/\[ORG\]/$1/g" -e "s/\[org\]/$1/g" -e "s/\[APP\]/$2/g" -e "s/\[app\]/$2/g" "$3" | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer ${4:-$STUDIO}" -H 'Content-Type: application/xml' -X POST "$PDP/authorization/api/v1/policies?org=$1&app=$2" --data-binary @-
}
TESTAPP_POLICY=$Z/../../smoke/apps/ttd/testapp/policy.xml
echo "== policy pushes: ttd/testapp $(pushpolicy ttd testapp "$TESTAPP_POLICY"), ttd/frontend-test $(pushpolicy ttd frontend-test "$APPS/frontend-test/App/config/authorization/policy.xml") (expected 200 200)"
echo "== a service-owner token is not a Studio token: $(pushpolicy ttd testapp "$TESTAPP_POLICY" "$(curl -s "http://127.0.0.1:$AUTHN_PORT/Home/GetTestOrgToken?org=ttd" | tr -d '"')") (expected 403)"
token() { curl -s "http://127.0.0.1:$AUTHN_PORT/Home/GetTestUserToken?userId=$1&authenticationLevel=2" | tr -d '"'; }
# Builds the request the app's PEP builds (Altinn.Common.PEP DecisionHelper.CreateDecisionRequest): subject attributes
# from the token claims, action, and org/app/party (+ instance) resource attributes.
request() { # token action org app partyId [instanceGuid]
  python3 -I - "$@" <<'PY'
import sys, json, base64
tok, action, org, app, party = sys.argv[1:6]; inst = sys.argv[6] if len(sys.argv) > 6 else None
p = tok.split('.')[1]; p += '=' * (-len(p) % 4); claims = json.loads(base64.urlsafe_b64decode(p))
S = "http://www.w3.org/2001/XMLSchema#string"
a = lambda i, v, r=False: {"AttributeId": i, "Value": str(v), "DataType": S, "IncludeInResult": r}
subject = [a(k, v) for k, v in claims.items() if k.startswith("urn:") and k not in ("urn:altinn:userid", "urn:altinn:partyid")]
subject.append(a("urn:altinn:userid", claims["urn:altinn:userid"]))
resource = [a("urn:altinn:org", org), a("urn:altinn:app", app), a("urn:altinn:partyid", party)]
if inst: resource.append(a("urn:altinn:instance-id", f"{party}/{inst}"))
print(json.dumps({"Request": {"AccessSubject": [{"Attribute": subject}], "Action": [{"Attribute": [a("urn:oasis:names:tc:xacml:1.0:action:action-id", action, True)]}], "Resource": [{"Attribute": resource}]}}))
PY
}
decide() { # expected label token action org app partyId [instanceGuid]
  local expected=$1 label=$2; shift 2
  local out; out=$(request "$@" | curl -s -X POST -H 'Content-Type: application/json' --data-binary @- "$PDP/authorization/api/v1/decision" -w '\n%{http_code}')
  local code=${out##*$'\n'} body=${out%$'\n'*}
  local decision; decision=$(python3 -I -c 'import sys,json; print(json.loads(sys.argv[1])["response"][0]["decision"])' "$body" 2>/dev/null || echo "HTTP $code")
  local mark=ok; [[ "$decision" == "$expected" ]] || { mark=FAIL; FAIL=1; }
  printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$label" "$decision" "$expected"
}

SOPHIE=$(token 1337); KARI=$(token 2002)   # Sophie Salt: DAGL of DDG Fitness and its sub-units; Kari Nordvik: A0239-41 on DDG Fitness, nothing on 500700
echo "== token for Sophie Salt (1337): ${#SOPHIE} chars; claims:"; python3 -I -c 'import sys,json,base64; p=sys.argv[1].split(".")[1]; p+="="*(-len(p)%4); d=json.loads(base64.urlsafe_b64decode(p)); print({k:v for k,v in d.items() if k.startswith("urn:")})' "$SOPHIE"
echo "== decisions on ttd/testapp (smoke/apps/ttd/testapp/policy.xml: DAGL/REGNA may instantiate and read)"
decide Permit         "Sophie (DAGL) read on 500000 DDG Fitness"              "$SOPHIE" read ttd testapp 500000
decide Permit         "Sophie (DAGL) instantiate on 500000"                   "$SOPHIE" instantiate ttd testapp 500000
decide Permit         "Sophie (DAGL) read on sub-unit 500003"                 "$SOPHIE" read ttd testapp 500003
decide Permit         "Sophie (REGNA) instantiate for herself 501337"         "$SOPHIE" instantiate ttd testapp 501337
decide NotApplicable  "Sophie read on 500005 Testdepartementet (no role)"     "$SOPHIE" read ttd testapp 500005
decide NotApplicable  "Sophie instantiate on 500900 Kari Consulting (no role)" "$SOPHIE" instantiate ttd testapp 500900
decide NotApplicable  "Kari (A0239-41 only) instantiate on 500000"            "$KARI" instantiate ttd testapp 500000
decide NotApplicable  "Kari instantiate on 500700 (listed, no roles)"         "$KARI" instantiate ttd testapp 500700
echo "== instance-scoped (needs storage instances/{id}/process/authinfo; expected to fail without storage)"
decide Indeterminate  "Sophie read instance 500000/<guid> (no storage yet)"   "$SOPHIE" read ttd testapp 500000 0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7
echo "== app whose policy was never pushed"
decide Indeterminate  "Sophie read on ttd/otherapp (policy not found)"        "$SOPHIE" read ttd otherapp 500000
echo "== a re-pushed policy applies at once (the store is read on every app-policy lookup)"
EDITED=$(mktemp); sed -e 's/>DAGL</>NOTDAGL</g' -e 's/>REGNA</>NOTREGNA</g' "$TESTAPP_POLICY" > "$EDITED"   # Sophie holds both
echo "   push edited: $(pushpolicy ttd testapp "$EDITED")"; rm -f "$EDITED"
decide NotApplicable  "Sophie read on 500000 after removing DAGL and REGNA"      "$SOPHIE" read ttd testapp 500000
echo "   push original: $(pushpolicy ttd testapp "$TESTAPP_POLICY")"
decide Permit         "Sophie (DAGL) read on 500000 after restoring"          "$SOPHIE" read ttd testapp 500000
echo "   store: $(cd "$STORE" && find . -type f | sort | tr '\n' ' ')"
echo "== ttd/frontend-test (the app's own App/config/authorization/policy.xml: lower-case dagl/regna/seln; org ttd may write/complete)"
helper() { # expected label userId partyId action [task]
  local expected=$1 label=$2 q="userId=$3&partyId=$4&org=ttd&app=frontend-test&action=$5${6:+&task=$6}"
  local decision; decision=$(curl -s "$PDP/authorization/api/localtest/decision?$q" | python3 -I -c 'import sys,json; print(json.load(sys.stdin)["response"][0]["decision"])' 2>/dev/null || echo error)
  local mark=ok; [[ "$decision" == "$expected" ]] || { mark=FAIL; FAIL=1; }
  printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$label" "$decision" "$expected"
}
helper Permit        "Sophie 1337 (DAGL->dagl) instantiate on 500000"        1337 500000 instantiate
helper Permit        "Sophie 1337 read on 500000"                             1337 500000 read
helper Permit        "Sophie 1337 write on 500000 in Task_1"                  1337 500000 write Task_1
helper Permit        "Sophie 1337 confirm on 500000 in Task_Custom_Confirm"   1337 500000 confirm Task_Custom_Confirm
helper NotApplicable "Sophie 1337 write on 500000 without a task (rule 4: org only)" 1337 500000 write
helper NotApplicable "Sophie 1337 complete on 500000 (org only)"              1337 500000 complete
helper Permit        "Sophie 1337 instantiate on sub-unit 500001"             1337 500001 instantiate
helper NotApplicable "Sophie 1337 instantiate on 500005 (no role)"            1337 500005 instantiate
helper Permit        "Kari 2002 (regna/dagl) instantiate for herself 512002"  2002 512002 instantiate
helper NotApplicable "Kari 2002 instantiate on 500700 (no roles)"             2002 500700 instantiate
helper Permit        "Ola 12345 (regna/dagl) instantiate for himself 512345"  12345 512345 instantiate
echo "== Access Management authorized parties (what app-lib's GetPartyList reads)"
AP=accessmanagement/api/v1/enduser/authorizedparties
cat >"$LOGS/summary.py" <<'PY'
import sys, json
s = sys.stdin.read(); body, code = s.rsplit(" HTTP ", 1)
if code != "200": print("HTTP", code, body[:200]); sys.exit()
d = json.loads(body)
def f(p):
    out = f"{p['partyId']} {p['type']} {p['name']!r} roles={p['authorizedRoles']} packages={p['authorizedAccessPackages']}"
    if p["onlyHierarchyElementWithNoAccess"]: out += " hierarchy-only"
    if p["subunits"]: out += " subunits=[" + "; ".join(f(c) for c in p["subunits"]) + "]"
    return out
print("HTTP 200, top-level keys", sorted(d)); print("\n".join("  " + f(p) for p in d["data"]))
PY
summary() { python3 -I "$LOGS/summary.py"; }
echo "GET $AP (Ola's token) ->"; curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?includeRoles=true&includeAccessPackages=true&includeResources=true&includeInstances=true&includePartiesViaKeyRoles=true&includeSubParties=true&includeInactiveParties=true" -w ' HTTP %{http_code}' | summary
check() { local mark=ok; [[ "$2" == "$3" ]] || { mark=FAIL; FAIL=1; }; printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$1" "$2" "$3"; }
check "$AP for Sophie: party ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP" | python3 -I -c 'import sys,json; print(" ".join(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in p["subunits"]) for p in json.load(sys.stdin)["data"]))')" "501337 500000/500001/500002/500003 500004 500600/500800/500801/500802 500700"
check "$AP for Sophie: 500000 type, orgnumber, dagl on it and its sub-unit" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP" | python3 -I -c 'import sys,json; p=[p for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0]; print(p["type"], p["organizationNumber"], "dagl" in p["authorizedRoles"], "dagl" in p["subunits"][0]["authorizedRoles"], p["onlyHierarchyElementWithNoAccess"])')" "Organization 897069644 True True False"
check "$AP?includeRoles=false for Sophie: 500000 roles" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?includeRoles=false" | python3 -I -c 'import sys,json; print([p["authorizedRoles"] for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0])')" "[]"
check "$AP?userId=12345 without a token (tools)" "$(curl -s "$PDP/$AP?userId=12345" | python3 -I -c 'import sys,json; print([(p["partyId"], p["type"], sorted(p["authorizedRoles"])) for p in json.load(sys.stdin)["data"]])')" "[(512345, 'Person', ['dagl', 'priv', 'regna'])]"
check "$AP without a token or userId" "$(curl -s -o /dev/null -w '%{http_code}' "$PDP/$AP")" "401"
check "$AP?userId=2002 with Sophie's token" "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?userId=2002")" "403"
check "POST authorization/api/localtest/refresh" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PDP/authorization/api/localtest/refresh")" "204"
echo "== localtest helper endpoint"
echo "GET authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read ->" \
  "$(curl -s "$PDP/authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read" | head -c 300)"
echo "== parties endpoint (JWT cookie validated against authentication's openid metadata)"
check "GET authorization/api/v1/parties?userid=1337: ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties?userid=1337" | python3 -I -c 'import sys,json; print(" ".join(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in (p.get("childParties") or [])) for p in json.load(sys.stdin)))')" "501337 500000/500001/500002/500003 500004 500600/500800/500801/500802 500700"
echo "GET authorization/api/v1/parties?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties?userid=1337" -w ' HTTP %{http_code}' | python3 -I -c 'import sys,json; s=sys.stdin.read(); b,c=s.rsplit(" HTTP ",1); print("HTTP",c, [(p["partyId"],p["name"],[x["partyId"] for x in (p.get("childParties") or [])]) for p in json.loads(b)] if c=="200" else b[:200])')"
echo "GET authorization/api/v1/parties/500000/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties/500000/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties/500003/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties/500003/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties?userid=1337 without token -> $(curl -s -o /dev/null -w 'HTTP %{http_code}' "$PDP/authorization/api/v1/parties?userid=1337")"
echo "== PDP log errors: $(grep -ciE 'fail:|exception' "$LOGS/authorization.log")"; grep -iE 'fail:|exception' -A1 "$LOGS/authorization.log" | cut -c1-240 | head -8
exit $FAIL
