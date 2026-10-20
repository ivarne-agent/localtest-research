#!/usr/bin/env bash
# Smoke test for the resource-registry component (production Altinn Resource Registry on the shared Postgres) in the
# running stack, everything through the proxy. Pushes the ttd/testapp fixture the way the app runner does (Studio
# token, metadata to Storage, policy to the PDP, then the registry's subject reload as Designer calls it), and checks
# the endpoints Access Management, the PDP, Authentication and Dialogporten consume. Re-pushes the original policy at
# the end, so it can be run repeatedly. Requires the stack (`studioctl env up -r localtest-v2`, see ../../README.md).
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
FX="$HERE/../../smoke/apps/ttd/testapp"
BASE=${1:-http://local.altinn.cloud:8000}
RR="$BASE/resourceregistry/api/v1/resource"
FAIL=0
check() { # name python-expression-on-d (stdin is JSON)
  if python3 -c "import json,sys; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" 2>/dev/null; then echo "ok   $1"; else echo "FAIL $1"; FAIL=1; fi
}
expect() { # name expected actual
  if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1 (expected $2, got $3)"; FAIL=1; fi
}
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

for _ in $(seq 1 60); do curl -sf -o /dev/null "$BASE/resourceregistry/api/v1/resource/orgs" && break; sleep 1; done

STUDIO=$(curl -s "$BASE/Home/GetTestStudioToken" | tr -d '"')
push_metadata() { # app-name
  local body status
  body=$(sed "s#ttd/testapp#ttd/$1#g" "$FX/applicationmetadata.json")
  status=$(printf '%s' "$body" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X POST "$BASE/storage/api/v1/applications?appId=ttd/$1" --data-binary @-)
  [[ "$status" == 201 ]] || status=$(printf '%s' "$body" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/json' -X PUT "$BASE/storage/api/v1/applications/ttd/$1" --data-binary @-)
  echo "$status"
}
policy() { # app-name [sed-expression]: the fixture policy with Designer's placeholder replacement
  sed -e "s/\[ORG\]/ttd/g" -e "s/\[org\]/ttd/g" -e "s/\[APP\]/$1/g" -e "s/\[app\]/$1/g" -e "${2:-}" "$FX/policy.xml"
}
push_policy() { policy "$1" "${2:-}" | code -H "Authorization: Bearer $STUDIO" -H 'Content-Type: application/xml' -X POST "$BASE/authorization/api/v1/policies?org=ttd&app=$1" --data-binary @-; }
reload() { code "$RR/app_ttd_$1/policy/subjects?reloadFromXacml=true"; }
# The updated feed from the epoch, paged one row at a time to the end: one JSON array of every row.
feed() { python3 - "$RR/updated?since=1970-01-01T00:00:00Z&limit=$1" <<'PY'
import json, sys, urllib.request
url, rows = sys.argv[1], []
for _ in range(2000):
    d = json.load(urllib.request.urlopen(url))
    rows += d["data"]
    url = d.get("links", {}).get("next")
    if not url or not d["data"]:
        break
print(json.dumps(rows))
PY
}

echo "== app resources follow Storage at once (no application cache)"
curl -s "$RR/resourcelist?includeMigratedApps=true" > /dev/null   # would fill production's 10-minute cache
expect "push fresh app ttd/rrfresh metadata to Storage" 201 "$(push_metadata rrfresh)"
curl -s "$RR/resourcelist?includeMigratedApps=true" | check "resourcelist lists app_ttd_rrfresh right after the push" \
  'any(r["identifier"] == "app_ttd_rrfresh" for r in d)'
expect "resource/app_ttd_rrfresh 200 at once" 200 "$(code "$RR/app_ttd_rrfresh")"
code -H "Authorization: Bearer $STUDIO" -X DELETE "$BASE/storage/api/v1/applications/ttd/rrfresh" > /dev/null

echo "== the testapp fixture, pushed as the runner does"
expect "push ttd/testapp metadata to Storage" yes "$([[ "$(push_metadata testapp)" == 20? ]] && echo yes || echo no)"
expect "push ttd/testapp policy to the PDP" 200 "$(push_policy testapp)"
expect "registry reloads subjects from the pushed policy (Designer's call)" 200 "$(reload testapp)"

echo "== resources"
curl -s "$RR/resourcelist?includeMigratedApps=true" | check "resourcelist: app_ttd_testapp is an AltinnApp of ttd with Digdir's organisation number" \
  'any(r["identifier"] == "app_ttd_testapp" and r["resourceType"] == "AltinnApp" and r["hasCompetentAuthority"]["organization"] == "991825827" and r["hasCompetentAuthority"]["orgcode"] == "ttd" for r in d)'
curl -s "$RR/resourcelist?includeMigratedApps=true" | check "resourcelist: the seeded altinn_keyrole_access is there too" \
  'any(r["identifier"] == "altinn_keyrole_access" and r["resourceType"] == "GenericAccessResource" for r in d)'
curl -s "$RR/resourcelist?includeApps=false" | check "resourcelist?includeApps=false: no app resources" \
  'all(not r["identifier"].startswith("app_") for r in d) and len(d) >= 1'
expect "resource/app_ttd_testapp 200" 200 "$(code "$RR/app_ttd_testapp")"
expect "unknown resource id 404" 404 "$(code "$RR/does_not_exist")"
expect "unknown app 404" 404 "$(code "$RR/app_ttd_not_pushed")"
expect "seeded resource 200" 200 "$(code "$RR/altinn_keyrole_access")"

echo "== policy"
# The PDP's policy endpoint stores the text it read, so the fixture's UTF-8 byte order mark is not in the file.
EXPECTED=$(mktemp)
policy testapp | sed '1s/^\xEF\xBB\xBF//' > "$EXPECTED"
curl -s "$RR/app_ttd_testapp/policy" | cmp -s - "$EXPECTED" && echo "ok   resource/app_ttd_testapp/policy is byte-equal to the pushed policy (without its BOM)" || { echo "FAIL policy not byte-equal"; FAIL=1; }
rm -f "$EXPECTED"
curl -s "$RR/app_ttd_testapp/policy/subjects?reloadFromXacml=true" | check "policy/subjects?reloadFromXacml=true: dagl, regna and the org" \
  '{s["urn"] for s in d["data"]} == {"urn:altinn:rolecode:dagl", "urn:altinn:rolecode:regna", "urn:altinn:org:ttd"}'
curl -s "$RR/altinn_keyrole_access/policy/rights" | check "policy/rights of the seeded resource lists its subjects (REPR, DAGL, ...)" \
  '{a["value"].lower() for r in d for s in r["subjects"] for a in s["subjectAttributes"]} >= {"dagl", "lede", "repr", "bobe"}'
curl -s "$RR/altinn_keyrole_access/policy" | grep -q 'urn:altinn:resource:altinn_keyrole_access:policyid:1' && echo "ok   policy of the seeded resource is its XACML" || { echo "FAIL seeded policy"; FAIL=1; }

echo "== the updated feed"
ALL=$(feed 1000); ONE=$(feed 1)
echo "$ONE" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(len(d))' | { read -r n; echo "     ($n pairs paged one at a time)"; }
echo "$ONE" | check "limit=1 paging reaches every pair exactly once" \
  'len(d) > 3 and len({(r["resourceUrn"], r["subjectUrn"]) for r in d}) == len(d)'
python3 - "$ALL" "$ONE" <<'PY' && echo "ok   paged feed equals the unpaged feed" || { echo "FAIL paged feed differs from the unpaged feed"; FAIL=1; }
import json, sys
a, b = json.loads(sys.argv[1]), json.loads(sys.argv[2])
key = lambda r: (r["resourceUrn"], r["subjectUrn"])
sys.exit(0 if sorted(map(key, a)) == sorted(map(key, b)) else 1)
PY
echo "$ONE" | check "feed has the testapp pairs and the seeded resource's pairs" \
  '{r["subjectUrn"] for r in d if r["resourceUrn"] == "urn:altinn:resource:app_ttd_testapp" and not r["deleted"]} == {"urn:altinn:rolecode:dagl", "urn:altinn:rolecode:regna", "urn:altinn:org:ttd"} and any(r["resourceUrn"] == "urn:altinn:resource:altinn_keyrole_access" for r in d)'
BEFORE=$(echo "$ONE" | python3 -c 'import json,sys; print([r["updatedAt"] for r in json.load(sys.stdin) if r["subjectUrn"] == "urn:altinn:rolecode:regna" and r["resourceUrn"].endswith("app_ttd_testapp")][0])')
sleep 1
# The same policy without REGNA (every REGNA subject match becomes DAGL, which is already there).
expect "re-push the policy without regna" 200 "$(push_policy testapp 's/REGNA/DAGL/g')"
expect "reload subjects" 200 "$(reload testapp)"
feed 1000 | check "feed: regna is deleted with a newer updatedAt, dagl is not" \
  'any(r["subjectUrn"] == "urn:altinn:rolecode:regna" and r["resourceUrn"].endswith("app_ttd_testapp") and r["deleted"] and r["updatedAt"] > "'"$BEFORE"'" for r in d) and any(r["subjectUrn"] == "urn:altinn:rolecode:dagl" and r["resourceUrn"].endswith("app_ttd_testapp") and not r["deleted"] for r in d)'
curl -s "$RR/app_ttd_testapp/policy/subjects" | check "policy/subjects no longer lists regna" '"urn:altinn:rolecode:regna" not in {s["urn"] for s in d["data"]}'
expect "restore the original policy" 200 "$(push_policy testapp)"
expect "reload subjects" 200 "$(reload testapp)"
feed 1000 | check "feed: regna is back (deleted false, newer again)" \
  'any(r["subjectUrn"] == "urn:altinn:rolecode:regna" and r["resourceUrn"].endswith("app_ttd_testapp") and not r["deleted"] and r["updatedAt"] > "'"$BEFORE"'" for r in d)'

echo "== orgs and subject lookup"
curl -s "$RR/orgs" | check "resource/orgs returns the org list (ttd and digdir)" '"ttd" in d["orgs"] and "digdir" in d["orgs"] and d["orgs"]["digdir"]["orgnr"] == "991825827"'
curl -s -X POST -H 'Content-Type: application/json' -d '["urn:altinn:rolecode:dagl"]' "$RR/bysubjects" | check "bysubjects dagl: app_ttd_testapp and the seeded resource" \
  '{x["value"] for s in d["data"] for x in s["resources"]} >= {"app_ttd_testapp", "altinn_keyrole_access"}'

echo "== platform access tokens and writes"
expect "write endpoint without a token is refused" 401 "$(code -X POST -H 'Content-Type: application/json' -d '{}' "$RR")"
ADMIN=$(curl -s "$BASE/Home/GetTestOrgToken?org=ttd&orgNumber=991825827&scopes=altinn:resourceregistry/resource.admin" | tr -d '"')
expect "write endpoint with a resource.admin org token passes authentication (validation 400, not 401/403)" 400 "$(code -X POST -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' -d '{"identifier":"x"}' "$RR")"
PLATFORM=$("$HERE/../../certs/mint-platform-access-token.sh" platform authorization)
MEMBER="urn:altinn:party:uuid:$(cat /proc/sys/kernel/random/uuid)"
expect "access-lists/get-by-member with a platform access token (signed with the shared certificate): 200" 200 "$(code -H "PlatformAccessToken: $PLATFORM" "$BASE/resourceregistry/api/v1/access-lists/get-by-member?party=$MEMBER")"
curl -s -H "PlatformAccessToken: $PLATFORM" "$BASE/resourceregistry/api/v1/access-lists/get-by-member?party=$MEMBER" | check "  ... and no access lists (empty)" 'd == []'
expect "access-lists/get-by-member without a platform access token: 401" 401 "$(code "$BASE/resourceregistry/api/v1/access-lists/get-by-member?party=$MEMBER")"
expect "access-lists/get-by-member with a tampered platform access token: 401" 401 "$(code -H "PlatformAccessToken: ${PLATFORM%?}x" "$BASE/resourceregistry/api/v1/access-lists/get-by-member?party=$MEMBER")"

exit $FAIL
