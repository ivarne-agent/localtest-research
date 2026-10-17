#!/usr/bin/env bash
# Smoke test for the localtest authorization component (production PDP + fakes), run without Docker or an ingress.
# Starts register, authentication and the PDP from this checkout's Release builds on their own ports, gets a user token
# from authentication, and asks the production decision endpoint what an app's PEP would ask. Stops only the processes
# it started. Build first, for each of register, authentication and authorization:
#   (cd ../<component> && DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets \
#      dotnet build -c Release vendor/src/apps/<host project folder>)
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); COMP=$(dirname "$HERE")
R=$COMP/register; A=$COMP/authentication; Z=$HERE
REG_PORT=${REG_PORT:-5021}; AUTHN_PORT=${AUTHN_PORT:-8001}; PDP_PORT=${PDP_PORT:-5070}
PLATFORM=http://local.altinn.cloud:$AUTHN_PORT/   # what authentication issues tokens and openid metadata for
PDP=http://127.0.0.1:$PDP_PORT; LOGS=${LOGS:-/tmp/authz-smoke}; mkdir -p "$LOGS"
POLICIES=$(mktemp -d); cp -r "$Z/policies/." "$POLICIES/"   # a copy, edited by the hot-reload check below
SEED=$(mktemp); PIDS=(); trap 'kill "${PIDS[@]}" 2>/dev/null; rm -rf "$POLICIES" "$SEED"' EXIT
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
  Localtest__Register__TestDataPath="$SEED" \
  GeneralSettings__OpenIdWellKnownEndpoint=${PLATFORM}authentication/api/v1/openid/
start authentication "$A/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/bin/Release/net10.0" Altinn.Platform.Authentication.dll \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:$AUTHN_PORT GeneralSettings__PlatformEndpoint=$PLATFORM \
  Localtest__Authentication__RegisterLocaltestEndpoint=http://127.0.0.1:$REG_PORT/register/api/localtest/
# The PDP's own platform endpoint is the authentication port (openid metadata); register and the register seed are
# pointed at the register's real port, since there is no ingress here.
start authorization "$Z/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/bin/Release/net10.0" Altinn.Authorization.dll \
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:$PDP_PORT \
  Localtest__Authorization__PlatformEndpoint=$PLATFORM \
  Localtest__Authorization__PoliciesPath="$POLICIES" \
  Localtest__Authorization__RegisterLocaltestEndpoint=http://127.0.0.1:$REG_PORT/register/api/localtest/ \
  PlatformSettings__ApiRegisterEndpoint=http://127.0.0.1:$REG_PORT/register/api/v1/

for i in $(seq 1 90); do
  curl -sf -o /dev/null http://127.0.0.1:$REG_PORT/health && curl -sf -o /dev/null http://127.0.0.1:$AUTHN_PORT/health \
    && curl -sf -o /dev/null $PDP/health && break; sleep 1
done
echo "health after ${i}s: register $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$REG_PORT/health)," \
  "authentication $(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$AUTHN_PORT/health), pdp $(curl -s -o /dev/null -w '%{http_code}' $PDP/health)"

FAIL=0
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

OLA=$(token 1337); KARI=$(token 1338)
echo "== token for Ola (1337): ${#OLA} chars; claims:"; python3 -I -c 'import sys,json,base64; p=sys.argv[1].split(".")[1]; p+="="*(-len(p)%4); d=json.loads(base64.urlsafe_b64decode(p)); print({k:v for k,v in d.items() if k.startswith("urn:")})' "$OLA"
echo "== decisions on ttd/testapp (policies/ttd/testapp/policy.xml: DAGL/REGNA may instantiate and read)"
decide Permit         "Ola (DAGL) read on 500000 DDG Fitness"              "$OLA" read ttd testapp 500000
decide Permit         "Ola (DAGL) instantiate on 500000"                   "$OLA" instantiate ttd testapp 500000
decide NotApplicable  "Ola read on 500003 (no role)"                       "$OLA" read ttd testapp 500003
decide NotApplicable  "Ola instantiate on 500003 (no role)"                "$OLA" instantiate ttd testapp 500003
decide NotApplicable  "Ola (INNH) instantiate on 500002 (role not in policy)" "$OLA" instantiate ttd testapp 500002
decide NotApplicable  "Ola instantiate for himself 501337 (PRIV not in policy)" "$OLA" instantiate ttd testapp 501337
decide Permit         "Ola read on sub-unit 500001 (DAGL of main unit 500000)" "$OLA" read ttd testapp 500001
decide Permit         "Kari (DAGL) instantiate on 500003"                  "$KARI" instantiate ttd testapp 500003
decide NotApplicable  "Kari (LEDE/MEDL) instantiate on 500000"             "$KARI" instantiate ttd testapp 500000
echo "== instance-scoped (needs storage instances/{id}/process/authinfo; expected to fail without storage)"
decide Indeterminate  "Ola read instance 500000/<guid> (no storage yet)"   "$OLA" read ttd testapp 500000 0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7
echo "== app without a mounted policy: fetched from {platform}ttd/otherapp/api/v1/meta/authorizationpolicy (no app runs here)"
decide Indeterminate  "Ola read on ttd/otherapp (policy not found)"        "$OLA" read ttd otherapp 500000
echo "== policy edit applies within seconds (production PRP caches for whole minutes)"
sed -i 's/>DAGL</>NOTDAGL</g' "$POLICIES/ttd/testapp/policy.xml"; sleep 6
decide NotApplicable  "Ola (DAGL) read on 500000 after removing DAGL"      "$OLA" read ttd testapp 500000
cp "$Z/policies/ttd/testapp/policy.xml" "$POLICIES/ttd/testapp/policy.xml"; sleep 6
decide Permit         "Ola (DAGL) read on 500000 after restoring"          "$OLA" read ttd testapp 500000
echo "== ttd/frontend-test (policies/ttd/frontend-test/policy.xml: lower-case dagl/regna/seln; org ttd may write/complete)"
helper() { # expected label userId partyId action [task]
  local expected=$1 label=$2 q="userId=$3&partyId=$4&org=ttd&app=frontend-test&action=$5${6:+&task=$6}"
  local decision; decision=$(curl -s "$PDP/authorization/api/localtest/decision?$q" | python3 -I -c 'import sys,json; print(json.load(sys.stdin)["response"][0]["decision"])' 2>/dev/null || echo error)
  local mark=ok; [[ "$decision" == "$expected" ]] || { mark=FAIL; FAIL=1; }
  printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$label" "$decision" "$expected"
}
helper Permit        "Ola 1337 (DAGL->dagl) instantiate on 500000"           1337 500000 instantiate
helper Permit        "Ola 1337 read on 500000"                                1337 500000 read
helper Permit        "Ola 1337 write on 500000 in Task_1"                     1337 500000 write Task_1
helper Permit        "Ola 1337 confirm on 500000 in Task_Custom_Confirm"      1337 500000 confirm Task_Custom_Confirm
helper NotApplicable "Ola 1337 write on 500000 without a task (rule 4: org only)" 1337 500000 write
helper NotApplicable "Ola 1337 complete on 500000 (org only)"                 1337 500000 complete
helper Permit        "Ola 1337 instantiate on sub-unit 500001 (main unit DAGL)" 1337 500001 instantiate
helper NotApplicable "Ola 1337 (INNH) instantiate on 500002"                  1337 500002 instantiate
helper Permit        "Kari 1338 (DAGL) instantiate on 500003"                 1338 500003 instantiate
helper NotApplicable "Ola 12345 instantiate for himself 512345 (PRIV/priv only)" 12345 512345 instantiate
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
echo "GET $AP (Ola's token) ->"; curl -s -H "Authorization: Bearer $OLA" "$PDP/$AP?includeRoles=true&includeAccessPackages=true&includeResources=true&includeInstances=true&includePartiesViaKeyRoles=true&includeSubParties=true&includeInactiveParties=true" -w ' HTTP %{http_code}' | summary
check() { local mark=ok; [[ "$2" == "$3" ]] || { mark=FAIL; FAIL=1; }; printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$1" "$2" "$3"; }
check "$AP for Ola: party ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $OLA" "$PDP/$AP" | python3 -I -c 'import sys,json; print(" ".join(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in p["subunits"]) for p in json.load(sys.stdin)["data"]))')" "501337 500000/500001 500002"
check "$AP for Ola: 500000 roles; sub-unit 500001 roles" "$(curl -s -H "Authorization: Bearer $OLA" "$PDP/$AP" | python3 -I -c 'import sys,json; p=[p for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0]; print(p["type"], p["organizationNumber"], p["authorizedRoles"], p["subunits"][0]["authorizedRoles"], p["onlyHierarchyElementWithNoAccess"])')" "Organization 910000012 ['dagl'] ['dagl'] False"
check "$AP?includeRoles=false for Ola: 500000 roles" "$(curl -s -H "Authorization: Bearer $OLA" "$PDP/$AP?includeRoles=false" | python3 -I -c 'import sys,json; print([p["authorizedRoles"] for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0])')" "[]"
check "$AP?userId=12345 without a token (tools)" "$(curl -s "$PDP/$AP?userId=12345" | python3 -I -c 'import sys,json; print([(p["partyId"], p["type"], p["authorizedRoles"]) for p in json.load(sys.stdin)["data"]])')" "[(512345, 'Person', ['priv'])]"
check "$AP without a token or userId" "$(curl -s -o /dev/null -w '%{http_code}' "$PDP/$AP")" "401"
check "$AP?userId=1338 with Ola's token" "$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $OLA" "$PDP/$AP?userId=1338")" "403"
check "POST authorization/api/localtest/refresh" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PDP/authorization/api/localtest/refresh")" "204"
echo "== localtest helper endpoint"
echo "GET authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read ->" \
  "$(curl -s "$PDP/authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read" | head -c 300)"
echo "== parties endpoint (JWT cookie validated against authentication's openid metadata)"
check "GET authorization/api/v1/parties?userid=1337: ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $OLA" "$PDP/authorization/api/v1/parties?userid=1337" | python3 -I -c 'import sys,json; print(" ".join(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in (p.get("childParties") or [])) for p in json.load(sys.stdin)))')" "501337 500000/500001 500002"
echo "GET authorization/api/v1/parties?userid=1337 -> $(curl -s -H "Authorization: Bearer $OLA" "$PDP/authorization/api/v1/parties?userid=1337" -w ' HTTP %{http_code}' | python3 -I -c 'import sys,json; s=sys.stdin.read(); b,c=s.rsplit(" HTTP ",1); print("HTTP",c, [(p["partyId"],p["name"],[x["partyId"] for x in (p.get("childParties") or [])]) for p in json.loads(b)] if c=="200" else b[:200])')"
echo "GET authorization/api/v1/parties/500000/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $OLA" "$PDP/authorization/api/v1/parties/500000/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties/500003/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $OLA" "$PDP/authorization/api/v1/parties/500003/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties?userid=1337 without token -> $(curl -s -o /dev/null -w 'HTTP %{http_code}' "$PDP/authorization/api/v1/parties?userid=1337")"
echo "== PDP log errors: $(grep -ciE 'fail:|exception' "$LOGS/authorization.log")"; grep -iE 'fail:|exception' -A1 "$LOGS/authorization.log" | cut -c1-240 | head -8
exit $FAIL
