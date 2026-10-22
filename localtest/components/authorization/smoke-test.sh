#!/usr/bin/env bash
# Smoke test for the authorization component (production PDP + fakes) in the running stack: everything through the
# proxy. Roles, access packages, delegations and authorized parties come from the real Access Management component, so
# the PDP is no longer tested on its own. Pushes the app policies through the production policy endpoint with a Studio
# token (as the app runner does), gets user tokens from authentication, and asks the production decision endpoint what
# an app's PEP would ask. Requires the stack (`studioctl env up -r localtest-v2`, see ../../README.md).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
# 31 checks. The "store:" listing and the closing "PDP log errors" check read the PDP's container (studioctl's
# localtest-v2-authorization or the Aspire package's authorization-<suffix>, see ../../smoke/lib/containers.sh; override
# with CONTAINER=... or LOCALTEST_CONTAINER_AUTHORIZATION=...) through podman or docker; without either or the container
# they say "skipped" and are not counted as failures.
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); Z=$HERE
BASE=${1:-http://local.altinn.cloud:8000}; PDP=$BASE; LOGS=${LOGS:-/tmp/authz-smoke}; mkdir -p "$LOGS"
APPS=${APPS:-/home/agent/code/altinn-studio/src/test/apps}   # the frontend-test policy comes from the app itself
STARTED=$(date -u +%Y-%m-%dT%H:%M:%SZ)
# shellcheck source-path=SCRIPTDIR source=../../smoke/lib/containers.sh
. "$HERE/../../smoke/lib/containers.sh"
[[ -n "${CONTAINER:-}" ]] && export LOCALTEST_CONTAINER_AUTHORIZATION=$CONTAINER
HAVE_CONTAINER=0; RT=$(localtest_runtime) && CONTAINER=$(localtest_container authorization) && HAVE_CONTAINER=1

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/health" && curl -sf -o /dev/null "$BASE/authentication/api/v1/openid/.well-known/openid-configuration" && break; sleep 1; done
# The decisions below need the PIP's view of the register seed: re-import it, and drop the PDP's caches.
echo "== access-management refresh: $(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/accessmanagement/api/localtest/refresh"), pdp cache refresh: $(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/authorization/api/localtest/refresh")"
FAIL=0
# What the app runner pushes: the app policies, [ORG]/[org]/[APP]/[app] replaced as Designer does, with a Studio token.
STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
pushpolicy() { # org app file [token]
  sed -e "s/\[ORG\]/$1/g" -e "s/\[org\]/$1/g" -e "s/\[APP\]/$2/g" -e "s/\[app\]/$2/g" "$3" | curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer ${4:-$STUDIO}" -H 'Content-Type: application/xml' -X POST "$PDP/authorization/api/v1/policies?org=$1&app=$2" --data-binary @-
}
TESTAPP_POLICY=$Z/../../smoke/apps/ttd/testapp/policy.xml
echo "== policy pushes: ttd/testapp $(pushpolicy ttd testapp "$TESTAPP_POLICY"), ttd/frontend-test $(pushpolicy ttd frontend-test "$APPS/frontend-test/App/config/authorization/policy.xml") (expected 200 200)"
echo "== a service-owner token is not a Studio token: $(pushpolicy ttd testapp "$TESTAPP_POLICY" "$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd" | tr -d '"')") (expected 403)"
token() { curl -s "$BASE/Home/GetTestUserToken?userId=$1&authenticationLevel=2" | tr -d '"'; }
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
echo "== instance-scoped on an instance Storage does not have (authinfo 404)"
decide Indeterminate  "Sophie read instance 500000/<guid> (unknown instance)"   "$SOPHIE" read ttd testapp 500000 0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7
echo "== app whose policy was never pushed"
decide Indeterminate  "Sophie read on ttd/otherapp (policy not found)"        "$SOPHIE" read ttd otherapp 500000
echo "== a re-pushed policy applies at once (the store is read on every app-policy lookup)"
EDITED=$(mktemp); sed -e 's/>DAGL</>NOTDAGL</g' -e 's/>REGNA</>NOTREGNA</g' "$TESTAPP_POLICY" > "$EDITED"   # Sophie holds both
echo "   push edited: $(pushpolicy ttd testapp "$EDITED")"; rm -f "$EDITED"
decide NotApplicable  "Sophie read on 500000 after removing DAGL and REGNA"      "$SOPHIE" read ttd testapp 500000
echo "   push original: $(pushpolicy ttd testapp "$TESTAPP_POLICY")"
decide Permit         "Sophie (DAGL) read on 500000 after restoring"          "$SOPHIE" read ttd testapp 500000
if [[ $HAVE_CONTAINER == 1 ]]; then echo "   store: $("$RT" exec "$CONTAINER" find /policy-store/metadata/ttd/testapp -maxdepth 1 -type f), $("$RT" exec "$CONTAINER" find /policy-store/metadata/ttd/testapp -path '*.versions/*' -type f | wc -l) versions"
else echo "   store: skipped (no container runtime or no PDP container)"; fi
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
echo "== Access Management authorized parties (what app-lib's GetPartyList reads), served by Access Management"
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
echo "GET $AP (Sophie's token) ->"; curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?includeRoles=true&includeAccessPackages=true&includeResources=true&includeInstances=true&includePartiesViaKeyRoles=true&includeSubParties=true&includeInactiveParties=true" -w ' HTTP %{http_code}' | summary
check() { local mark=ok; [[ "$2" == "$3" ]] || { mark=FAIL; FAIL=1; }; printf '%-4s %-62s -> %s (expected %s)\n' "$mark" "$1" "$2" "$3"; }
check "$AP for Sophie: party ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP" | python3 -I -c 'import sys,json; d=json.load(sys.stdin); d=d.get("data",d) if isinstance(d,dict) else d; print(" ".join(sorted(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in sorted(p.get("subunits") or p.get("childParties") or [], key=lambda c: c["partyId"])) for p in d)))')" "500000/500001/500002/500003 500004 500600/500800/500801/500802 500700 501337"
check "$AP?includeRoles=true for Sophie: 500000 type, orgnumber, dagl on it and its sub-unit" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?includeRoles=true" | python3 -I -c 'import sys,json; p=[p for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0]; print(p["type"], p["organizationNumber"], "dagl" in p["authorizedRoles"], "dagl" in p["subunits"][0]["authorizedRoles"], p["onlyHierarchyElementWithNoAccess"])')" "Organization 897069644 True True False"
check "$AP?includeRoles=false for Sophie: 500000 roles" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/$AP?includeRoles=false" | python3 -I -c 'import sys,json; print([p["authorizedRoles"] for p in json.load(sys.stdin)["data"] if p["partyId"]==500000][0])')" "[]"
check "$AP for Ola (12345): own party only" "$(curl -s -H "Authorization: Bearer $(token 12345)" "$PDP/$AP?includeRoles=true" | python3 -I -c 'import sys,json; print([(p["partyId"], p["type"], "dagl" in p["authorizedRoles"]) for p in json.load(sys.stdin)["data"]])')" "[(512345, 'Person', True)]"
check "$AP without a token" "$(curl -s -o /dev/null -w '%{http_code}' "$PDP/$AP")" "401"
check "POST authorization/api/localtest/refresh" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "$PDP/authorization/api/localtest/refresh")" "204"
echo "== localtest helper endpoint"
echo "GET authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read ->" \
  "$(curl -s "$PDP/authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read" | head -c 300)"
echo "== parties endpoint (JWT cookie validated against authentication's openid metadata)"
check "GET authorization/api/v1/parties?userid=1337: ids incl. sub-units" "$(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties?userid=1337" | python3 -I -c 'import sys,json; d=json.load(sys.stdin); d=d.get("data",d) if isinstance(d,dict) else d; print(" ".join(sorted(str(p["partyId"]) + "".join("/" + str(c["partyId"]) for c in sorted(p.get("subunits") or p.get("childParties") or [], key=lambda c: c["partyId"])) for p in d)))')" "500000/500001/500002/500003 500004 500600/500800/500801/500802 500700 501337"
echo "GET authorization/api/v1/parties?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties?userid=1337" -w ' HTTP %{http_code}' | python3 -I -c 'import sys,json; s=sys.stdin.read(); b,c=s.rsplit(" HTTP ",1); print("HTTP",c, [(p["partyId"],p["name"],[x["partyId"] for x in (p.get("childParties") or [])]) for p in json.loads(b)] if c=="200" else b[:200])')"
echo "GET authorization/api/v1/parties/500000/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties/500000/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties/500003/validate?userid=1337 -> $(curl -s -H "Authorization: Bearer $SOPHIE" "$PDP/authorization/api/v1/parties/500003/validate?userid=1337" -w ' HTTP %{http_code}')"
echo "GET authorization/api/v1/parties?userid=1337 without token -> $(curl -s -o /dev/null -w 'HTTP %{http_code}' "$PDP/authorization/api/v1/parties?userid=1337")"
echo "== PDP log errors since the start of this run (${CONTAINER:-no container})"
if [[ $HAVE_CONTAINER == 1 ]]; then
  "$RT" logs --since "$STARTED" "$CONTAINER" >"$LOGS/authorization.log" 2>&1
  # Log entries start at an unindented line. The two Indeterminate checks above provoke three fail: entries (the unknown
  # instance's 404 from Storage, logged twice; the policy not found); every other fail:/crit: entry is an error.
  cat >"$LOGS/errors.py" <<'PY'
import re, sys
entries, cur = [], []
for line in open(sys.argv[1], errors="replace"):
    if line[:1] not in (" ", "\t", "") and cur:
        entries.append(cur); cur = []
    cur.append(line.rstrip())
if cur:
    entries.append(cur)
provoked = ("Failed to lookup auth info from storage. Response StatusCode: 404",
            "Unable to find instance 500000/0f1e2d3c-4b5a-6978-8091-a2b3c4d5e6f7", "Policy not found for resource")
bad = [e for e in entries if re.match(r"(fail|crit):", e[0]) and not any(p in " ".join(e[:4]) for p in provoked)]
for e in bad[:5]:
    print("\n".join(l[:240] for l in e[:4]), file=sys.stderr)
print(len(bad))
PY
  check "error entries in the authorization log beyond the provoked ones" "$(python3 -I "$LOGS/errors.py" "$LOGS/authorization.log")" "0"
else
  echo "skip error lines in the authorization log (no container runtime or no PDP container)"
fi
exit $FAIL
