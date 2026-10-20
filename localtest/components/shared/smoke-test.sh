#!/usr/bin/env bash
# Smoke test of the shared IMaskinportenService fake (fakes/Maskinporten/), compiled against Altinn.ApiClients.Maskinporten
# 10.1.0 into Maskinporten.Smoke and run against the running stack: a token from the localtest Maskinporten token endpoint,
# exchanged through production Authentication. UseAltinnTestOrg=true must give urn:altinn:org=ttd, false digdir.
# Needs the stack (Authentication reads altinncdn.no for the organisation code) and a NuGet cache or nuget.org.
#   ./smoke-test.sh [BASE]   (default http://local.altinn.cloud:8000)
set -uo pipefail
BASE=${1:-http://local.altinn.cloud:8000}
HERE=$(cd "$(dirname "$0")" && pwd)
FAIL=0
for flow in "true ttd" "false digdir"; do
  read -r test_org org <<<"$flow"
  if dotnet run --project "$HERE/Maskinporten.Smoke" -- "$BASE" "$test_org" "$org"; then
    echo "ok   fake with UseAltinnTestOrg=$test_org exchanges to urn:altinn:org=$org"
  else
    echo "FAIL fake with UseAltinnTestOrg=$test_org (expected urn:altinn:org=$org)"; FAIL=1
  fi
done
[[ $FAIL == 0 ]] && echo "all ok" || { echo "some checks failed"; exit 1; }
